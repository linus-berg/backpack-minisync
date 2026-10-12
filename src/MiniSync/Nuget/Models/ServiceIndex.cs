// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json.Serialization;

namespace MiniSync.Nuget.Models;

/// <summary>
///   A NuGet v3 service index (<c>index.json</c>), which lists the endpoints
///   of a feed.
/// </summary>
public class ServiceIndex {
  /// <summary>
  ///   Gets or sets the feed's resources.
  /// </summary>
  [JsonPropertyName("resources")]
  public List<ServiceResource> resources { get; set; } = new();
}
