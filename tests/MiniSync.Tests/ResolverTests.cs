// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Npm;

namespace MiniSync.Tests;

public class ResolverTests {
  private static Task<ResolveResult> Resolve(NpmFeed feed, PackageFilter filter,
                                             params string[] roots) {
    NpmRegistry registry = new(
      new NpmConfig { registry = NpmFeed.C_REGISTRY },
      new Fetcher(4, feed.http)
    );
    List<PackageRequest> requests = roots.Select(
                                           r => new PackageRequest {
                                             name = r.Split('@')[0],
                                             spec = r.Split('@')[1]
                                           }
                                         )
                                         .ToList();
    return new Resolver(registry, filter).Resolve(requests, CancellationToken.None);
  }

  private static PackageFilter All() {
    return new PackageFilter(Array.Empty<string>(), Array.Empty<string>());
  }

  /* app -> a, b; a -> shared ^1.0; b -> shared ^1.2 (one copy satisfies
     both); a -> deep -> leaf. Only what the roots need is selected. */
  private static NpmFeed Graph() {
    return new NpmFeed().Publish("app", "1.0.0", NpmFeed.Deps("a", "^1.0.0", "b", "^1.0.0"))
                        .Publish("app", "2.0.0", NpmFeed.Deps("unused", "^1.0.0"))
                        .Publish("a", "1.0.0", NpmFeed.Deps("shared", "^1.0.0", "deep", "1.0.0"))
                        .Publish("b", "1.0.0", NpmFeed.Deps("shared", "^1.2.0"))
                        .Publish("shared", "1.0.0")
                        .Publish("shared", "1.2.0")
                        .Publish("shared", "2.0.0")
                        .Latest("shared", "1.2.0")
                        .Publish("deep", "1.0.0", NpmFeed.Deps("leaf", "~1.0.0"))
                        .Publish("leaf", "1.0.0")
                        .Publish("leaf", "1.0.5")
                        .Publish("unused", "1.0.0");
  }

  [Fact]
  public async Task Selects_one_version_per_need_and_nothing_else() {
    ResolveResult result = await Resolve(Graph(), All(), "app@1.0.0");
    Assert.Empty(result.errors);
    Assert.Equal(
      new[] { "app@1.0.0", "a@1.0.0", "b@1.0.0", "shared@1.2.0", "deep@1.0.0", "leaf@1.0.5" },
      result.packages.Select(p => p.ToString())
    );
    Assert.Equal("a@1.0.0", result.packages.Single(p => p.name == "deep").required_by);
  }

  [Fact]
  public async Task Each_root_version_is_resolved_separately() {
    ResolveResult result = await Resolve(Graph(), All(), "shared@1.0.0", "shared@2.0.0");
    Assert.Equal(
      new[] { "shared@1.0.0", "shared@2.0.0" },
      result.packages.Select(p => p.ToString())
    );
  }

  [Fact]
  public async Task Denied_packages_are_not_synced_or_traversed() {
    PackageFilter filter = new(Array.Empty<string>(), new[] { "de*" });
    ResolveResult result = await Resolve(Graph(), filter, "app@1.0.0");
    Assert.DoesNotContain(result.packages, p => p.name is "deep" or "leaf");
    Assert.Equal("deep@1.0.0 (required by a@1.0.0)", result.blocked.Single().ToString());
  }

  [Fact]
  public async Task Allow_list_limits_dependencies_but_not_roots() {
    PackageFilter filter = new(new[] { "a", "shared" }, Array.Empty<string>());
    ResolveResult result = await Resolve(Graph(), filter, "app@1.0.0");
    Assert.Equal(
      new[] { "app@1.0.0", "a@1.0.0", "shared@1.2.0" },
      result.packages.Select(p => p.ToString())
    );
    Assert.Equal(new[] { "b", "deep" }, result.blocked.Select(r => r.name));
  }

  [Fact]
  public async Task Failures_are_collected_and_the_rest_still_resolves() {
    NpmFeed feed = Graph().Publish("broken", "1.0.0", NpmFeed.Deps("ghost", "^1.0.0", "leaf", "^1.0.0"));
    ResolveResult result = await Resolve(feed, All(), "broken@1.0.0", "nope@1.0.0");
    Assert.Equal(new[] { "broken@1.0.0", "leaf@1.0.5" }, result.packages.Select(p => p.ToString()));
    Assert.Equal(2, result.errors.Count);
    Assert.Contains(result.errors, e => e.StartsWith("ghost@^1.0.0 (required by broken@1.0.0)"));
    Assert.Contains(result.errors, e => e.StartsWith("nope@1.0.0"));
  }

  [Fact]
  public async Task Metadata_is_fetched_once_per_package() {
    NpmFeed feed = Graph();
    await Resolve(feed, All(), "app@1.0.0", "shared@1.0.0");
    Assert.Single(feed.http.requests, u => u == $"{NpmFeed.C_REGISTRY}shared");
  }
}
