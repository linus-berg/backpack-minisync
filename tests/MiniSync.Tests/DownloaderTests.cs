// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;
using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Npm;

namespace MiniSync.Tests;

public sealed class DownloaderTests : IDisposable {
  private readonly string root_ =
    Path.Combine(Path.GetTempPath(), $"minisync-test-{Guid.NewGuid():N}");

  public void Dispose() {
    if (Directory.Exists(root_)) {
      Directory.Delete(root_, true);
    }
  }

  private static async Task<(NpmRegistry registry, ResolvedPackage package)> Setup(
    NpmFeed feed, string name, string version) {
    NpmRegistry registry = new(
      new NpmConfig { registry = NpmFeed.C_REGISTRY },
      new Fetcher(4, feed.http)
    );
    ResolvedPackage package = (await registry.Resolve(
                                 new PackageRequest { name = name, spec = version },
                                 Array.Empty<ResolvedPackage>(),
                                 CancellationToken.None
                               ))!;
    return (registry, package);
  }

  [Fact]
  public async Task Downloads_once_into_the_backpack_layout_and_the_delta() {
    NpmFeed feed = new NpmFeed().Publish("@scope/lib", "1.0.0");
    (NpmRegistry registry, ResolvedPackage package) = await Setup(feed, "@scope/lib", "1.0.0");
    Downloader downloader = new(new Fetcher(4, feed.http), root_, 4, true);

    DownloadResult first = await downloader.Download(registry, new[] { package }, CancellationToken.None);
    Assert.Equal(1, first.downloaded);
    string stored = Path.Combine(root_, "npm", "registry.test", "@scope", "lib", "lib-1.0.0.tgz");
    Assert.Equal("@scope/lib@1.0.0", await File.ReadAllTextAsync(stored));
    string delta = Path.Combine(
      root_, "delta", "npm", DateTime.UtcNow.ToString("yyyy_MM_dd"),
      "registry.test", "@scope", "lib", "lib-1.0.0.tgz"
    );
    Assert.True(File.Exists(delta));

    DownloadResult second = await downloader.Download(registry, new[] { package }, CancellationToken.None);
    Assert.Equal(0, second.downloaded);
    Assert.Equal(1, second.present);
    Assert.Single(feed.http.requests, u => u.EndsWith(".tgz"));
  }

  [Fact]
  public async Task A_failed_delta_copy_is_retried_on_the_next_run() {
    NpmFeed feed = new NpmFeed().Publish("lib", "1.0.0");
    (NpmRegistry registry, ResolvedPackage package) = await Setup(feed, "lib", "1.0.0");
    Downloader downloader = new(new Fetcher(4, feed.http), root_, 4, true);
    string delta = Storage.GetFullPath(
      root_, Storage.GetDeltaPath("npm", package.url, DateTime.UtcNow)
    );
    /* A directory where the delta file belongs makes the copy fail. */
    Directory.CreateDirectory(delta);

    DownloadResult first = await downloader.Download(registry, new[] { package }, CancellationToken.None);
    Assert.Single(first.failed);
    Assert.False(downloader.Exists(package));

    Directory.Delete(delta);
    DownloadResult second = await downloader.Download(registry, new[] { package }, CancellationToken.None);
    Assert.Equal(1, second.downloaded);
    Assert.True(File.Exists(delta));
  }

  [Fact]
  public async Task Corrupt_downloads_leave_nothing_behind() {
    NpmFeed feed = new NpmFeed().Publish("lib", "1.0.0");
    (NpmRegistry registry, ResolvedPackage package) = await Setup(feed, "lib", "1.0.0");
    feed.http.Add(package.url, Encoding.UTF8.GetBytes("tampered"));
    Downloader downloader = new(new Fetcher(4, feed.http), root_, 4, true);

    DownloadResult result = await downloader.Download(registry, new[] { package }, CancellationToken.None);
    Assert.Single(result.failed);
    Assert.Contains("integrity", result.failed[0]);
    Assert.Empty(Directory.GetFiles(root_, "*", SearchOption.AllDirectories));
  }

  [Fact]
  public async Task Missing_files_are_reported_as_failures() {
    NpmFeed feed = new NpmFeed().Publish("lib", "1.0.0");
    (NpmRegistry registry, ResolvedPackage package) = await Setup(feed, "lib", "1.0.0");
    ResolvedPackage gone = new() {
      name = package.name, version = package.version, path = package.path,
      url = $"{NpmFeed.C_REGISTRY}lib/-/gone.tgz", integrity = package.integrity
    };
    Downloader downloader = new(new Fetcher(4, feed.http), root_, 4, false);
    DownloadResult result = await downloader.Download(registry, new[] { gone }, CancellationToken.None);
    Assert.Contains("404", Assert.Single(result.failed));
  }
}
