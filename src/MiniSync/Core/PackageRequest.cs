// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   A request for one version of a package, either from the config file
///   (a root) or from another package's dependency list.
/// </summary>
public record PackageRequest {
  /// <summary>
  ///   Gets the package name.
  /// </summary>
  public required string name { get; init; }

  /// <summary>
  ///   Gets the version spec: an exact version, a range or a tag.
  /// </summary>
  public required string spec { get; init; }

  /// <summary>
  ///   Gets the package that asked for this one, or null for a root.
  /// </summary>
  public string? required_by { get; init; }

  /// <summary>
  ///   Gets a value indicating whether the dependency is optional, which allows
  ///   it to be skipped when it does not fit the configured platforms.
  /// </summary>
  public bool optional { get; init; }

  /// <summary>
  ///   Gets a value indicating whether this request came from the config file.
  /// </summary>
  public bool root => required_by == null;

  /// <inheritdoc />
  public override string ToString() {
    return required_by == null
             ? $"{name}@{spec}"
             : $"{name}@{spec} (required by {required_by})";
  }
}
