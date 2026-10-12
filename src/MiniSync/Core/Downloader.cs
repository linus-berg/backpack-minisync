// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   Downloads resolved packages into the output directory.
/// </summary>
public class Downloader {
  private readonly int concurrency_;
  private readonly bool delta_;
  private readonly Fetcher fetcher_;
  private readonly string root_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="Downloader" /> class.
  /// </summary>
  /// <param name="fetcher">The HTTP fetcher.</param>
  /// <param name="root">The output directory.</param>
  /// <param name="concurrency">The number of parallel downloads.</param>
  /// <param name="delta">Whether new files are also copied into today's delta deposit.</param>
  public Downloader(Fetcher fetcher, string root, int concurrency, bool delta) {
    fetcher_ = fetcher;
    root_ = root;
    concurrency_ = concurrency;
    delta_ = delta;
  }

  /// <summary>
  ///   Checks whether a package is already in storage. Like backpack, a file
  ///   that exists is trusted: it only ever gets there after verification.
  /// </summary>
  /// <param name="package">The package.</param>
  /// <returns>True if the file exists; otherwise, false.</returns>
  public bool Exists(ResolvedPackage package) {
    return File.Exists(Storage.GetFullPath(root_, package.path));
  }

  /// <summary>
  ///   Downloads every package that is not already in storage.
  /// </summary>
  /// <param name="registry">The registry used to verify downloads.</param>
  /// <param name="packages">The packages.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>What was downloaded, skipped and failed.</returns>
  public async Task<DownloadResult> Download(IRegistry registry,
                                             IReadOnlyList<ResolvedPackage>
                                               packages,
                                             CancellationToken token) {
    DownloadResult result = new();
    DateTime today = DateTime.UtcNow;
    ParallelOptions options = new() {
      MaxDegreeOfParallelism = concurrency_,
      CancellationToken = token
    };
    await Parallel.ForEachAsync(
      packages,
      options,
      async (package, t) => {
        try {
          if (Exists(package)) {
            lock (result) {
              result.present++;
            }

            return;
          }

          long bytes = await DownloadOne(registry, package, today, t);
          lock (result) {
            result.downloaded++;
            result.bytes += bytes;
          }

          Log.Info($"  + {package} ({FormatBytes(bytes)})");
        } catch (Exception e) when (e is not OperationCanceledException ||
                                    !t.IsCancellationRequested) {
          lock (result) {
            result.failed.Add($"{package}: {e.Message}");
          }

          Log.Error($"{package}: {e.Message}");
        }
      }
    );
    return result;
  }

  private async Task<long> DownloadOne(IRegistry registry,
                                       ResolvedPackage package, DateTime today,
                                       CancellationToken token) {
    string target = Storage.GetFullPath(root_, package.path);
    string partial = $"{target}.part";
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    try {
      long bytes = await fetcher_.Download(package.url, partial, token);
      await registry.Verify(partial, package, token);
      /* The delta copy is made first. Once the file is in place it counts as
         synced and is never looked at again, so a delta copy that failed
         after that point would be missing from the deposit for good. */
      if (delta_) {
        string delta = Storage.GetFullPath(
          root_,
          Storage.GetDeltaPath(registry.module, package.url, today)
        );
        Directory.CreateDirectory(Path.GetDirectoryName(delta)!);
        File.Copy(partial, delta, true);
      }

      File.Move(partial, target, true);
      return bytes;
    } finally {
      /* Only a verified file is ever moved into place, so a failed or
         cancelled download leaves nothing behind but this. */
      DeletePartial(partial);
    }
  }

  /// <summary>
  ///   Removes a partial download. This runs while an exception may be in
  ///   flight, so it must not throw: on Windows a virus scanner can briefly
  ///   hold a fresh file open, and that error would replace the real one. A
  ///   leftover is harmless because the next attempt truncates it.
  /// </summary>
  private static void DeletePartial(string partial) {
    try {
      if (File.Exists(partial)) {
        File.Delete(partial);
      }
    } catch (Exception e) when (e is IOException or UnauthorizedAccessException) {
      Log.Debug($"  could not remove {partial}: {e.Message}");
    }
  }

  /// <summary>
  ///   Formats a byte count for display.
  /// </summary>
  /// <param name="bytes">The byte count.</param>
  /// <returns>A human readable size.</returns>
  public static string FormatBytes(long bytes) {
    string[] units = { "B", "KB", "MB", "GB", "TB" };
    double size = bytes;
    int unit = 0;
    while (size >= 1024 && unit < units.Length - 1) {
      size /= 1024;
      unit++;
    }

    return unit == 0 ? $"{bytes} B" : $"{size:0.0} {units[unit]}";
  }
}
