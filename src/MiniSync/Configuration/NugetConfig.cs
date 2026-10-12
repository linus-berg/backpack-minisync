// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Configuration;

/// <summary>
///   NuGet settings.
/// </summary>
public class NugetConfig : EcosystemConfig {
  /// <summary>
  ///   Gets or sets the NuGet v3 service index URL.
  /// </summary>
  public string source { get; set; } = "https://api.nuget.org/v3/index.json";

  /// <summary>
  ///   Gets or sets the target frameworks (e.g. <c>net8.0</c>, <c>net48</c>)
  ///   whose dependency groups are followed. Empty means every group, which
  ///   is always complete but can pull in a lot of legacy packages.
  /// </summary>
  public List<string> target_frameworks { get; set; } = new();
}
