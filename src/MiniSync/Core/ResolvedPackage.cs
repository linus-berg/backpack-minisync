// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   A package version selected for syncing and the file that backs it.
/// </summary>
public class ResolvedPackage {
  /// <summary>
  ///   Gets the package name.
  /// </summary>
  public required string name { get; init; }

  /// <summary>
  ///   Gets the selected version.
  /// </summary>
  public required string version { get; init; }

  /// <summary>
  ///   Gets the URL of the package file.
  /// </summary>
  public required string url { get; init; }

  /// <summary>
  ///   Gets the storage path relative to the output directory.
  /// </summary>
  public required string path { get; init; }

  /// <summary>
  ///   Gets the expected Subresource Integrity string, when the registry
  ///   publishes one.
  /// </summary>
  public string? integrity { get; init; }

  /// <summary>
  ///   Gets the package whose dependency first pulled this one in, or null for
  ///   a root.
  /// </summary>
  public string? required_by { get; init; }

  /// <inheritdoc />
  public override string ToString() {
    return $"{name}@{version}";
  }
}
