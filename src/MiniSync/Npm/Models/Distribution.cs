// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace MiniSync.Npm.Models;

/// <summary>
///   Represents the distribution information of an npm package version.
/// </summary>
public class Distribution {
  /// <summary>
  ///   Gets or sets the URL to the tarball.
  /// </summary>
  [JsonPropertyName("tarball")]
  public string? tarball { get; set; }

  /// <summary>
  ///   Gets or sets the Subresource Integrity string of the tarball.
  /// </summary>
  [JsonPropertyName("integrity")]
  public string? integrity { get; set; }

  /// <summary>
  ///   Gets or sets the hex SHA-1 of the tarball, published by old packages
  ///   that predate <see cref="integrity" />.
  /// </summary>
  [JsonPropertyName("shasum")]
  public string? shasum { get; set; }
}
