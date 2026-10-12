// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   The outcome of downloading a set of packages.
/// </summary>
public class DownloadResult {
  /// <summary>
  ///   Gets or sets the number of files downloaded.
  /// </summary>
  public int downloaded { get; set; }

  /// <summary>
  ///   Gets or sets the number of bytes downloaded.
  /// </summary>
  public long bytes { get; set; }

  /// <summary>
  ///   Gets or sets the number of files that were already on disk.
  /// </summary>
  public int present { get; set; }

  /// <summary>
  ///   Gets the packages that failed to download, with the reason.
  /// </summary>
  public List<string> failed { get; } = new();
}
