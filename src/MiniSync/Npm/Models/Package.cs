// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniSync.Npm.Models;

/// <summary>
///   The metadata of one version of an npm package. Fields that real-world
///   packages publish in more than one shape are kept as <see cref="JsonElement" />.
/// </summary>
public class Package {
  /// <summary>
  ///   Gets or sets the dependencies.
  /// </summary>
  [JsonPropertyName("dependencies")]
  public Dictionary<string, JsonElement>? dependencies { get; set; }

  /// <summary>
  ///   Gets or sets the optional dependencies. npm also copies these into
  ///   <see cref="dependencies" /> when a package is published.
  /// </summary>
  [JsonPropertyName("optionalDependencies")]
  public Dictionary<string, JsonElement>? optional_dependencies { get; set; }

  /// <summary>
  ///   Gets or sets the peer dependencies.
  /// </summary>
  [JsonPropertyName("peerDependencies")]
  public Dictionary<string, JsonElement>? peer_dependencies { get; set; }

  /// <summary>
  ///   Gets or sets the peer dependency metadata, which marks peers optional.
  /// </summary>
  [JsonPropertyName("peerDependenciesMeta")]
  public Dictionary<string, JsonElement>? peer_dependencies_meta { get; set; }

  /// <summary>
  ///   Gets or sets the dependencies shipped inside the tarball: an array of
  ///   names, or true for all of them.
  /// </summary>
  [JsonPropertyName("bundleDependencies")]
  public JsonElement bundle_dependencies { get; set; }

  /// <summary>
  ///   Gets or sets the legacy spelling of <see cref="bundle_dependencies" />.
  /// </summary>
  [JsonPropertyName("bundledDependencies")]
  public JsonElement bundled_dependencies { get; set; }

  /// <summary>
  ///   Gets or sets the supported operating systems.
  /// </summary>
  [JsonPropertyName("os")]
  public JsonElement os { get; set; }

  /// <summary>
  ///   Gets or sets the supported CPU architectures.
  /// </summary>
  [JsonPropertyName("cpu")]
  public JsonElement cpu { get; set; }

  /// <summary>
  ///   Gets or sets the deprecation message, if the version is deprecated.
  /// </summary>
  [JsonPropertyName("deprecated")]
  public JsonElement deprecated { get; set; }

  /// <summary>
  ///   Gets or sets whether the tarball ships an npm-shrinkwrap.json.
  /// </summary>
  [JsonPropertyName("_hasShrinkwrap")]
  public JsonElement has_shrinkwrap { get; set; }

  /// <summary>
  ///   Gets or sets the distribution information.
  /// </summary>
  [JsonPropertyName("dist")]
  public Distribution? dist { get; set; }
}
