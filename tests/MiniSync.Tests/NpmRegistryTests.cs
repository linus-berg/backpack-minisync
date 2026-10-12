// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Npm;

namespace MiniSync.Tests;

public class NpmRegistryTests {
  private static NpmRegistry Create(NpmFeed feed, NpmConfig? config = null) {
    config ??= new NpmConfig();
    config.registry = NpmFeed.C_REGISTRY;
    return new NpmRegistry(config, new Fetcher(4, feed.http));
  }

  private static async Task<string?> Pick(NpmRegistry registry, string name,
                                          string spec, bool root = true,
                                          params string[] selected) {
    PackageRequest request = new() {
      name = name,
      spec = spec,
      required_by = root ? null : "parent@1.0.0"
    };
    List<ResolvedPackage> chosen = selected.Select(
                                             v => new ResolvedPackage {
                                               name = name,
                                               version = v,
                                               url = "https://x/y.tgz",
                                               path = "npm/x/y.tgz"
                                             }
                                           )
                                           .ToList();
    ResolvedPackage? package =
      await registry.Resolve(request, chosen, CancellationToken.None);
    return package?.version;
  }

  [Fact]
  public async Task Partial_version_takes_the_highest_patch_not_latest() {
    NpmFeed feed = new();
    foreach (string v in new[] { "18.3.1", "19.0.0", "19.0.1-rc.1", "19.0.8", "19.1.0" }) {
      feed.Publish("react", v);
    }

    feed.Latest("react", "19.1.0");
    Assert.Equal("19.0.8", await Pick(Create(feed), "react", "19.0"));
    Assert.Equal("19.0.0", await Pick(Create(feed), "react", "19.0.0"));
    Assert.Equal("19.1.0", await Pick(Create(feed), "react", "latest"));
    Assert.Equal("19.1.0", await Pick(Create(feed), "react", "^19.0.0"));
  }

  [Fact]
  public async Task Latest_tag_wins_when_it_satisfies_the_range() {
    NpmFeed feed = new NpmFeed().Publish("lib", "1.0.0")
                                .Publish("lib", "1.2.0")
                                .Publish("lib", "1.3.0")
                                .Latest("lib", "1.2.0");
    Assert.Equal("1.2.0", await Pick(Create(feed), "lib", "^1.0.0", false));
    Assert.Equal("1.3.0", await Pick(Create(feed), "lib", "^1.3.0", false));
  }

  [Fact]
  public async Task Deprecated_versions_are_avoided_when_possible() {
    Dictionary<string, object> deprecated =
      new() { ["deprecated"] = "broken, use 1.1.0" };
    NpmFeed feed = new NpmFeed().Publish("lib", "1.1.0")
                                .Publish("lib", "1.2.0", deprecated)
                                .Publish("lib", "2.0.0", deprecated);
    Assert.Equal("1.1.0", await Pick(Create(feed), "lib", "^1.0.0", false));
    Assert.Equal("2.0.0", await Pick(Create(feed), "lib", "^2.0.0", false));
  }

  [Fact]
  public async Task Dependencies_reuse_a_selected_version_but_roots_do_not() {
    NpmFeed feed = new NpmFeed().Publish("lib", "1.1.0")
                                .Publish("lib", "1.5.0")
                                .Latest("lib", "1.5.0");
    Assert.Equal("1.1.0", await Pick(Create(feed), "lib", "^1.0.0", false, "1.1.0"));
    Assert.Equal("1.5.0", await Pick(Create(feed), "lib", "^1.0.0", true, "1.1.0"));
  }

  [Fact]
  public async Task Non_registry_specs_are_reported() {
    NpmFeed feed = new NpmFeed().Publish("lib", "1.0.0");
    ResolveException error = await Assert.ThrowsAsync<ResolveException>(
                               () => Pick(Create(feed), "lib", "git+https://github.com/a/b.git", false)
                             );
    Assert.Contains("only registry packages", error.Message);
    await Assert.ThrowsAsync<ResolveException>(
      () => Pick(Create(feed), "lib", "^9.0.0", false)
    );
    await Assert.ThrowsAsync<ResolveException>(
      () => Pick(Create(feed), "missing", "1.0.0")
    );
  }

  [Fact]
  public async Task Scoped_packages_are_requested_encoded_and_stored_like_backpack() {
    NpmFeed feed = new NpmFeed().Publish("@types/node", "20.0.0");
    ResolvedPackage package = (await Create(feed).Resolve(
                                 new PackageRequest { name = "@types/node", spec = "20.0.0" },
                                 Array.Empty<ResolvedPackage>(),
                                 CancellationToken.None
                               ))!;
    Assert.Contains($"{NpmFeed.C_REGISTRY}@types%2fnode", feed.http.requests);
    Assert.Equal("npm/registry.test/@types/node/node-20.0.0.tgz", package.path);
  }

  [Fact]
  public async Task Dependency_lists_follow_npm_install_semantics() {
    NpmFeed feed = new NpmFeed().Publish(
      "app",
      "1.0.0",
      new Dictionary<string, object> {
        ["dependencies"] = NpmFeed.Map(
          "plain", "^1.0.0",
          "inside", "^1.0.0",
          "native", "^1.0.0",
          "string-width-cjs", "npm:string-width@^4.2.0",
          "@scope/aliased", "npm:@scope/real@^2.0.0"
        ),
        ["optionalDependencies"] = NpmFeed.Map("native", "^1.0.0"),
        ["peerDependencies"] = NpmFeed.Map("react", "^19.0.0", "bufferutil", "^4.0.0"),
        ["peerDependenciesMeta"] = new Dictionary<string, object> {
          ["bufferutil"] = new Dictionary<string, bool> { ["optional"] = true }
        },
        ["bundleDependencies"] = new[] { "inside" }
      }
    );
    NpmRegistry registry = Create(feed);
    ResolvedPackage app = (await registry.Resolve(
                             new PackageRequest { name = "app", spec = "1.0.0" },
                             Array.Empty<ResolvedPackage>(),
                             CancellationToken.None
                           ))!;
    IReadOnlyList<PackageRequest> dependencies =
      await registry.GetDependencies(app, CancellationToken.None);

    Assert.Equal(
      new[] {
        "plain@^1.0.0", "string-width@^4.2.0", "@scope/real@^2.0.0",
        "native@^1.0.0", "react@^19.0.0"
      },
      dependencies.Select(d => $"{d.name}@{d.spec}")
    );
    Assert.True(dependencies.Single(d => d.name == "native").optional);
    Assert.All(dependencies, d => Assert.Equal("app@1.0.0", d.required_by));

    NpmRegistry lean = Create(
      feed,
      new NpmConfig { peer_dependencies = false, optional_dependencies = false }
    );
    Assert.Equal(
      new[] { "plain", "string-width", "@scope/real" },
      (await lean.GetDependencies(app, CancellationToken.None)).Select(d => d.name)
    );
  }

  [Theory]
  [InlineData("win32", "x64", true)]
  [InlineData("linux", "x64", false)]
  [InlineData("win32", "arm64", false)]
  public async Task Optional_platform_packages_are_filtered(string os, string cpu,
                                                            bool synced) {
    NpmFeed feed = new NpmFeed().Publish(
      "@esbuild/win32-x64",
      "0.24.0",
      new Dictionary<string, object> {
        ["os"] = new[] { "win32" },
        ["cpu"] = new[] { "x64" }
      }
    );
    NpmRegistry registry = Create(
      feed,
      new NpmConfig { os = new List<string> { os }, cpu = new List<string> { cpu } }
    );
    PackageRequest optional = new() {
      name = "@esbuild/win32-x64",
      spec = "0.24.0",
      required_by = "esbuild@0.24.0",
      optional = true
    };
    ResolvedPackage? package = await registry.Resolve(
                                 optional,
                                 Array.Empty<ResolvedPackage>(),
                                 CancellationToken.None
                               );
    Assert.Equal(synced, package != null);

    /* A required dependency is never filtered; npm would install it anyway. */
    Assert.NotNull(
      await registry.Resolve(
        optional with { optional = false },
        Array.Empty<ResolvedPackage>(),
        CancellationToken.None
      )
    );
  }

  [Fact]
  public async Task Negated_platforms_are_respected() {
    NpmFeed feed = new NpmFeed().Publish(
      "posix-only",
      "1.0.0",
      new Dictionary<string, object> { ["os"] = new[] { "!win32" } }
    );
    PackageRequest optional = new() {
      name = "posix-only", spec = "1.0.0", required_by = "x@1.0.0", optional = true
    };
    NpmRegistry windows = Create(feed, new NpmConfig { os = new List<string> { "win32" } });
    NpmRegistry both = Create(
      feed,
      new NpmConfig { os = new List<string> { "win32", "linux" } }
    );
    Assert.Null(await windows.Resolve(optional, Array.Empty<ResolvedPackage>(), CancellationToken.None));
    Assert.NotNull(await both.Resolve(optional, Array.Empty<ResolvedPackage>(), CancellationToken.None));
  }

  [Fact]
  public async Task Verify_checks_the_integrity_hash() {
    string file = Path.GetTempFileName();
    try {
      byte[] content = Encoding.UTF8.GetBytes("tarball");
      await File.WriteAllBytesAsync(file, content);
      NpmRegistry registry = Create(new NpmFeed());
      ResolvedPackage package = new() {
        name = "lib", version = "1.0.0", url = "https://x/lib.tgz", path = "npm/x/lib.tgz",
        integrity = $"sha1-AAAA {FakeHttp.Sri(content)}"
      };
      await registry.Verify(file, package, CancellationToken.None);

      ResolvedPackage tampered = new() {
        name = "lib", version = "1.0.0", url = "https://x/lib.tgz", path = "npm/x/lib.tgz",
        integrity = FakeHttp.Sri(Encoding.UTF8.GetBytes("other"))
      };
      await Assert.ThrowsAsync<InvalidDataException>(
        () => registry.Verify(file, tampered, CancellationToken.None)
      );
    } finally {
      File.Delete(file);
    }
  }
}

public class ShrinkwrapTests {
  private static async Task<List<string>> Locked(string shrinkwrap) {
    byte[] tarball = NpmFeed.Tarball(
      new Dictionary<string, string> {
        ["package/package.json"] = """{ "name": "cli", "version": "1.0.0" }""",
        ["package/npm-shrinkwrap.json"] = shrinkwrap
      }
    );
    NpmFeed feed = new NpmFeed().Publish(
      "cli",
      "1.0.0",
      new Dictionary<string, object> {
        ["_hasShrinkwrap"] = true,
        ["dependencies"] = NpmFeed.Map("dep", "^1.0.0"),
        ["peerDependencies"] = NpmFeed.Map("peer", "^2.0.0")
      },
      tarball
    );
    NpmRegistry registry = new(
      new NpmConfig { registry = NpmFeed.C_REGISTRY },
      new Fetcher(4, feed.http)
    );
    ResolvedPackage cli = (await registry.Resolve(
                             new PackageRequest { name = "cli", spec = "1.0.0" },
                             Array.Empty<ResolvedPackage>(),
                             CancellationToken.None
                           ))!;
    return (await registry.GetDependencies(cli, CancellationToken.None))
           .Select(d => $"{d.name}@{d.spec}{(d.optional ? "?" : "")}")
           .ToList();
  }

  [Fact]
  public async Task Lockfile_v2_versions_replace_the_declared_ranges() {
    List<string> locked = await Locked(
      """
      {
        "lockfileVersion": 2,
        "packages": {
          "": { "name": "cli", "version": "1.0.0" },
          "node_modules/dep": { "version": "1.0.3" },
          "node_modules/dep/node_modules/inner": { "version": "0.1.0" },
          "node_modules/string-width-cjs": { "name": "string-width", "version": "4.2.3" },
          "node_modules/fsevents": { "version": "2.3.3", "optional": true },
          "node_modules/jest": { "version": "29.0.0", "dev": true },
          "node_modules/vendored": { "version": "1.0.0", "inBundle": true },
          "node_modules/from-git": { "version": "1.0.0", "resolved": "git+ssh://git@github.com/a/b.git#abc" }
        },
        "dependencies": { "ignored": { "version": "9.9.9" } }
      }
      """
    );
    Assert.Equal(
      new[] {
        "dep@1.0.3", "inner@0.1.0", "string-width@4.2.3", "fsevents@2.3.3?",
        "from-git@git+ssh://git@github.com/a/b.git#abc", "peer@^2.0.0"
      },
      locked
    );
  }

  [Fact]
  public async Task Lockfile_v1_trees_are_walked() {
    List<string> locked = await Locked(
      """
      {
        "lockfileVersion": 1,
        "dependencies": {
          "dep": { "version": "1.0.3", "dependencies": { "inner": { "version": "0.1.0" } } },
          "alias": { "version": "npm:real@2.0.0" },
          "opt": { "version": "3.0.0", "optional": true },
          "jest": { "version": "29.0.0", "dev": true, "dependencies": { "jest-dep": { "version": "1.0.0" } } }
        }
      }
      """
    );
    Assert.Equal(
      new[] { "dep@1.0.3", "inner@0.1.0", "real@2.0.0", "opt@3.0.0?", "peer@^2.0.0" },
      locked
    );
  }

  [Fact]
  public async Task Hostile_tarball_urls_are_rejected_not_stored() {
    foreach (string url in new[] {
               "https://registry.test/a/..%5c..%5cx.tgz",
               "https://registry.test/a/x.tgz%3Astream",
               "file:///etc/passwd"
             }) {
      NpmFeed feed = new();
      feed.http.AddJson(
        $"{NpmFeed.C_REGISTRY}evil",
        new Dictionary<string, object> {
          ["versions"] = new Dictionary<string, object> {
            ["1.0.0"] = new { dist = new { tarball = url } }
          }
        }
      );
      NpmRegistry registry = new(
        new NpmConfig { registry = NpmFeed.C_REGISTRY },
        new Fetcher(4, feed.http)
      );
      ResolveResult result = await new Resolver(
                               registry,
                               new PackageFilter(Array.Empty<string>(), Array.Empty<string>())
                             ).Resolve(
                               new[] { new PackageRequest { name = "evil", spec = "1.0.0" } },
                               CancellationToken.None
                             );
      Assert.Empty(result.packages);
      Assert.Single(result.errors);
    }
  }
}
