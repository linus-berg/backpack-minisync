// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Nuget;

namespace MiniSync.Tests;

/// <summary>
///   Builds a fake NuGet v3 feed on top of <see cref="FakeHttp" />.
/// </summary>
public static class NugetFeed {
  public const string C_SOURCE = "https://nuget.test/v3/index.json";
  public const string C_FLAT = "https://nuget.test/flat/";

  public static FakeHttp Feed() {
    return new FakeHttp().AddJson(
      C_SOURCE,
      new {
        version = "3.0.0",
        resources = new object[] {
          new Dictionary<string, string> { ["@id"] = "https://nuget.test/reg/", ["@type"] = "RegistrationsBaseUrl" },
          new Dictionary<string, string> { ["@id"] = "https://nuget.test/flat", ["@type"] = "PackageBaseAddress/3.0.0" }
        }
      }
    );
  }

  public static FakeHttp Versions(this FakeHttp http, string id,
                                  params string[] versions) {
    return http.AddJson($"{C_FLAT}{id.ToLowerInvariant()}/index.json", new { versions });
  }

  public static FakeHttp Nuspec(this FakeHttp http, string id, string version,
                                string dependencies) {
    string lower = id.ToLowerInvariant();
    return http.Add(
      $"{C_FLAT}{lower}/{version}/{lower}.nuspec",
      FakeHttp.Nuspec(id, version, dependencies)
    );
  }
}

public class NugetRegistryTests {
  private static NugetRegistry Create(FakeHttp http, params string[] frameworks) {
    NugetConfig config = new() {
      source = NugetFeed.C_SOURCE,
      target_frameworks = frameworks.ToList()
    };
    return new NugetRegistry(config, new Fetcher(4, http));
  }

  private static async Task<ResolvedPackage> Pick(NugetRegistry registry,
                                                  string id, string spec,
                                                  bool root = true,
                                                  params string[] selected) {
    return (await registry.Resolve(
              new PackageRequest {
                name = id,
                spec = spec,
                required_by = root ? null : "Parent@1.0.0"
              },
              selected.Select(
                        v => new ResolvedPackage {
                          name = id, version = v, url = "https://x/y.nupkg", path = "nuget/x/y.nupkg"
                        }
                      )
                      .ToList(),
              CancellationToken.None
            ))!;
  }

  [Fact]
  public async Task Roots_are_exact_unless_floating() {
    FakeHttp http = NugetFeed.Feed().Versions("Newtonsoft.Json", "12.0.3", "13.0.1", "13.0.3", "13.0.4-beta1");
    NugetRegistry registry = Create(http);
    Assert.Equal("13.0.1", (await Pick(registry, "Newtonsoft.Json", "13.0.1")).version);
    Assert.Equal("13.0.3", (await Pick(registry, "Newtonsoft.Json", "13.*")).version);
    Assert.Equal("13.0.3", (await Pick(registry, "Newtonsoft.Json", "latest")).version);
    Assert.Equal("13.0.3", (await Pick(registry, "Newtonsoft.Json", "[13.0,14.0)")).version);
    Assert.Equal("13.0.4-beta1", (await Pick(registry, "Newtonsoft.Json", "13.0.4-beta1")).version);

    ResolveException missing = await Assert.ThrowsAsync<ResolveException>(
                                 () => Pick(registry, "Newtonsoft.Json", "13.0")
                               );
    Assert.Contains("13.0.*", missing.Message);
  }

  [Fact]
  public async Task Dependencies_take_the_lowest_applicable_version() {
    FakeHttp http = NugetFeed.Feed().Versions("Lib", "0.9.0", "1.0.0-rc1", "1.0.0", "1.1.0", "2.0.0");
    NugetRegistry registry = Create(http);
    Assert.Equal("1.0.0", (await Pick(registry, "Lib", "[1.0.0, )", false)).version);
    Assert.Equal("1.0.0-rc1", (await Pick(registry, "Lib", "[1.0.0-rc1, )", false)).version);
    Assert.Equal("1.1.0", (await Pick(registry, "Lib", "[1.0.0, )", false, "1.1.0")).version);
    Assert.Equal("2.0.0", (await Pick(registry, "Lib", "[2.0.0, )", false, "1.1.0")).version);
    await Assert.ThrowsAsync<ResolveException>(() => Pick(registry, "Lib", "[3.0.0, )", false));
  }

  [Fact]
  public async Task Package_urls_and_paths_match_backpack() {
    FakeHttp http = NugetFeed.Feed().Versions("Serilog", "3.1.1");
    ResolvedPackage package = await Pick(Create(http), "Serilog", "3.1.1");
    Assert.Equal($"{NugetFeed.C_FLAT}serilog/3.1.1/serilog.3.1.1.nupkg", package.url);
    Assert.Equal("nuget/nuget.test/flat/serilog/3.1.1/serilog.3.1.1.nupkg", package.path);
  }

  [Theory]
  [InlineData(new[] { "net8.0" }, new[] { "Modern" })]
  [InlineData(new[] { "net48" }, new[] { "Legacy", "Shared" })]
  [InlineData(new[] { "net6.0" }, new[] { "Standard", "Shared" })]
  [InlineData(new[] { "net8.0", "net48" }, new[] { "Legacy", "Shared", "Modern" })]
  [InlineData(new string[0], new[] { "Legacy", "Shared", "Standard", "Shared", "Modern" })]
  public async Task Only_the_nearest_dependency_group_is_followed(string[] frameworks,
                                                                  string[] expected) {
    FakeHttp http = NugetFeed.Feed().Versions("Root", "1.0.0")
                          .Nuspec(
                            "Root",
                            "1.0.0",
                            """
                            <group targetFramework=".NETFramework4.6.2">
                              <dependency id="Legacy" version="1.0.0" />
                              <dependency id="Shared" version="1.0.0" />
                            </group>
                            <group targetFramework=".NETStandard2.0">
                              <dependency id="Standard" version="1.0.0" />
                              <dependency id="Shared" version="1.0.0" />
                            </group>
                            <group targetFramework="net8.0">
                              <dependency id="Modern" version="[2.0.0, 3.0.0)" />
                            </group>
                            """
                          );
    NugetRegistry registry = Create(http, frameworks);
    ResolvedPackage root = await Pick(registry, "Root", "1.0.0");
    IReadOnlyList<PackageRequest> dependencies =
      await registry.GetDependencies(root, CancellationToken.None);
    Assert.Equal(expected, dependencies.Select(d => d.name));
    Assert.All(dependencies, d => Assert.Equal("Root@1.0.0", d.required_by));
  }

  [Fact]
  public async Task Ungrouped_dependencies_apply_to_every_framework() {
    FakeHttp http = NugetFeed.Feed().Versions("Old", "1.0.0")
                          .Nuspec("Old", "1.0.0", """<dependency id="Dep" version="1.2.0" />""");
    NugetRegistry registry = Create(http, "net8.0");
    ResolvedPackage old = await Pick(registry, "Old", "1.0.0");
    PackageRequest dependency =
      (await registry.GetDependencies(old, CancellationToken.None)).Single();
    Assert.Equal("Dep", dependency.name);

    /* The spec written into the request has to parse back as a range. */
    http.Versions("Dep", "1.1.0", "1.2.0", "1.3.0");
    Assert.Equal("1.2.0", (await Pick(registry, "Dep", dependency.spec, false)).version);
  }

  [Fact]
  public async Task Verify_checks_the_package_identity() {
    string file = Path.GetTempFileName();
    try {
      await File.WriteAllBytesAsync(file, FakeHttp.Nupkg("Serilog", "3.1.1"));
      NugetRegistry registry = Create(NugetFeed.Feed());
      ResolvedPackage serilog = new() {
        name = "serilog", version = "3.1.1", url = "https://x/s.nupkg", path = "nuget/x/s.nupkg"
      };
      await registry.Verify(file, serilog, CancellationToken.None);

      ResolvedPackage other = new() {
        name = "Other", version = "3.1.1", url = "https://x/s.nupkg", path = "nuget/x/s.nupkg"
      };
      await Assert.ThrowsAsync<InvalidDataException>(
        () => registry.Verify(file, other, CancellationToken.None)
      );

      await File.WriteAllTextAsync(file, "<html>captive portal</html>");
      await Assert.ThrowsAnyAsync<Exception>(
        () => registry.Verify(file, serilog, CancellationToken.None)
      );
    } finally {
      File.Delete(file);
    }
  }

  [Fact]
  public async Task Windows_frameworks_match_windows_groups_like_restore_does() {
    FakeHttp http = NugetFeed.Feed().Versions("Ui", "1.0.0")
                             .Nuspec(
                               "Ui",
                               "1.0.0",
                               """
                               <group targetFramework="net8.0"><dependency id="Portable" version="1.0.0" /></group>
                               <group targetFramework="net8.0-windows7.0"><dependency id="Desktop" version="1.0.0" /></group>
                               """
                             );
    NugetRegistry registry = Create(http, "net8.0-windows");
    ResolvedPackage ui = await Pick(registry, "Ui", "1.0.0");
    PackageRequest dependency =
      (await registry.GetDependencies(ui, CancellationToken.None)).Single();
    Assert.Equal("Desktop", dependency.name);
  }

  [Fact]
  public async Task Utf16_nuspecs_are_read() {
    string xml = FakeHttp.Nuspec("Wide", "1.0.0", """<dependency id="Dep" version="1.0.0" />""")
                         .Replace("encoding=\"utf-8\"", "encoding=\"utf-16\"");
    FakeHttp http = NugetFeed.Feed().Versions("Wide", "1.0.0");
    http.Add(
      $"{NugetFeed.C_FLAT}wide/1.0.0/wide.nuspec",
      Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray()
    );
    NugetRegistry registry = Create(http, "net8.0");
    ResolvedPackage wide = await Pick(registry, "Wide", "1.0.0");
    Assert.Equal("Dep", (await registry.GetDependencies(wide, CancellationToken.None)).Single().name);
  }

  [Fact]
  public async Task Invalid_ids_are_rejected_before_they_reach_a_url() {
    NugetRegistry registry = Create(NugetFeed.Feed());
    await Assert.ThrowsAsync<ResolveException>(() => Pick(registry, "..\\evil", "1.0.0", false));
  }

  [Fact]
  public void Unknown_target_frameworks_are_rejected() {
    Assert.Throws<ConfigException>(() => Create(NugetFeed.Feed(), "net8.0", "notaframework"));
  }
}
