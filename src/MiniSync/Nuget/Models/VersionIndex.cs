// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace MiniSync.Nuget.Models;

/// <summary>
///   The version list of a package in a flat container
///   (<c>{base}/{id}/index.json</c>).
/// </summary>
public class VersionIndex {
  /// <summary>
  ///   Gets or sets the normalized, lowercase versions.
  /// </summary>
  [JsonPropertyName("versions")]
  public List<string> versions { get; set; } = new();
}
