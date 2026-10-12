// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Configuration;

/// <summary>
///   npm settings.
/// </summary>
public class NpmConfig : EcosystemConfig {
  /// <summary>
  ///   Gets or sets the registry URL.
  /// </summary>
  public string registry { get; set; } = "https://registry.npmjs.org/";

  /// <summary>
  ///   Gets or sets a value indicating whether peer dependencies are synced.
  ///   npm 7 and later installs them automatically. Peers marked optional in
  ///   <c>peerDependenciesMeta</c> are never synced.
  /// </summary>
  public bool peer_dependencies { get; set; } = true;

  /// <summary>
  ///   Gets or sets a value indicating whether optional dependencies are synced.
  /// </summary>
  public bool optional_dependencies { get; set; } = true;

  /// <summary>
  ///   Gets or sets the operating systems (<c>process.platform</c> values such
  ///   as <c>win32</c>, <c>linux</c>, <c>darwin</c>) to sync optional,
  ///   platform specific packages for. Empty means all.
  /// </summary>
  public List<string> os { get; set; } = new();

  /// <summary>
  ///   Gets or sets the CPU architectures (<c>process.arch</c> values such as
  ///   <c>x64</c>, <c>arm64</c>) to sync optional, platform specific packages
  ///   for. Empty means all.
  /// </summary>
  public List<string> cpu { get; set; } = new();
}
