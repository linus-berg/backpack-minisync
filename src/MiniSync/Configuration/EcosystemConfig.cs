// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace MiniSync.Configuration;

/// <summary>
///   Settings shared by every ecosystem.
/// </summary>
public abstract class EcosystemConfig {
  /// <summary>
  ///   Gets or sets the packages to sync, keyed by name. Each entry holds one
  ///   or more version specs; in the file a single spec can be a plain string.
  /// </summary>
  [JsonConverter(typeof(PackageVersionsConverter))]
  [JsonPropertyOrder(1)]
  public Dictionary<string, List<string>> packages { get; set; } = new();

  /// <summary>
  ///   Gets or sets glob patterns of the dependencies that may be synced. An
  ///   empty list allows everything. Packages listed in
  ///   <see cref="packages" /> are always allowed.
  /// </summary>
  public List<string> allow { get; set; } = new();

  /// <summary>
  ///   Gets or sets glob patterns of packages that are never synced, not even
  ///   when listed in <see cref="packages" />.
  /// </summary>
  public List<string> deny { get; set; } = new();
}
