// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace MiniSync.Npm.Models;

/// <summary>
///   The abbreviated metadata ("corgi" document) of an npm package: just what
///   an installer needs, which is a fraction of the full document's size.
/// </summary>
public class Metadata {
  /// <summary>
  ///   Gets or sets the dist-tags, e.g. <c>latest</c>, mapped to versions.
  /// </summary>
  [JsonPropertyName("dist-tags")]
  public Dictionary<string, string> dist_tags { get; set; } = new();

  /// <summary>
  ///   Gets or sets the published versions.
  /// </summary>
  [JsonPropertyName("versions")]
  public Dictionary<string, Package> versions { get; set; } = new();
}
