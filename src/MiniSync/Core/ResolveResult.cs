// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   The outcome of resolving a set of root packages.
/// </summary>
public class ResolveResult {
  /// <summary>
  ///   Gets the selected packages, roots first, then in breadth-first order.
  /// </summary>
  public List<ResolvedPackage> packages { get; } = new();

  /// <summary>
  ///   Gets the requests refused by the allow/deny lists.
  /// </summary>
  public List<PackageRequest> blocked { get; } = new();

  /// <summary>
  ///   Gets the optional dependencies left out because they do not fit the
  ///   configured platforms.
  /// </summary>
  public List<PackageRequest> skipped { get; } = new();

  /// <summary>
  ///   Gets the requests that could not be resolved.
  /// </summary>
  public List<string> errors { get; } = new();
}
