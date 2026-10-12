// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniSync.Nuget.Models;

/// <summary>
///   One endpoint in a NuGet v3 service index.
/// </summary>
public class ServiceResource {
  /// <summary>
  ///   Gets or sets the endpoint URL.
  /// </summary>
  [JsonPropertyName("@id")]
  public string id { get; set; } = "";

  /// <summary>
  ///   Gets or sets the resource type, a string or, on some feeds, an array.
  /// </summary>
  [JsonPropertyName("@type")]
  public JsonElement type { get; set; }

  /// <summary>
  ///   Checks whether the resource is of a given type.
  /// </summary>
  /// <param name="name">The type, e.g. <c>PackageBaseAddress/3.0.0</c>.</param>
  /// <returns>True if the resource has that type; otherwise, false.</returns>
  public bool IsType(string name) {
    return type.ValueKind switch {
      JsonValueKind.String => type.GetString() == name,
      JsonValueKind.Array => type.EnumerateArray()
                                 .Any(t => t.ValueKind == JsonValueKind.String &&
                                           t.GetString() == name),
      _ => false
    };
  }
}
