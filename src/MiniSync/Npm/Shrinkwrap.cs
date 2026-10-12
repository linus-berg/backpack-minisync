// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace MiniSync.Npm;

/// <summary>
///   Reads the <c>npm-shrinkwrap.json</c> a package can ship in its tarball.
///   npm installs exactly the versions locked there instead of resolving the
///   package's dependency ranges.
/// </summary>
public static class Shrinkwrap {
  private const string C_FILE_ = "npm-shrinkwrap.json";
  private const string C_NODE_MODULES_ = "node_modules/";

  /// <summary>
  ///   Extracts the shrinkwrap from a package tarball.
  /// </summary>
  /// <param name="tarball">The gzipped tarball.</param>
  /// <returns>The parsed shrinkwrap, or null if the tarball has none.</returns>
  public static JsonDocument? Read(byte[] tarball) {
    using MemoryStream compressed = new(tarball);
    using GZipStream gzip = new(compressed, CompressionMode.Decompress);
    using TarReader reader = new(gzip);
    while (reader.GetNextEntry() is { } entry) {
      /* The top directory is "package" in practice, but not by rule. */
      string[] parts =
        entry.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
      if (parts.Length == 2 && parts[1] == C_FILE_ && entry.DataStream != null) {
        return JsonDocument.Parse(entry.DataStream);
      }
    }

    return null;
  }

  /// <summary>
  ///   Lists what a shrinkwrap locks for a production install: everything
  ///   except development and bundled packages. Specs are exact versions, an
  ///   <c>npm:</c> alias, or for git and file dependencies the resolved
  ///   location, which cannot be synced and gets reported.
  /// </summary>
  /// <param name="shrinkwrap">The shrinkwrap root element.</param>
  /// <returns>The locked packages.</returns>
  public static List<(string name, string spec, bool optional)> GetLocked(
    JsonElement shrinkwrap) {
    List<(string name, string spec, bool optional)> locked = new();
    /* lockfileVersion 2 and 3 have a flat "packages" map keyed by install
       path; version 1 only has the nested "dependencies" tree. */
    if (shrinkwrap.TryGetProperty("packages", out JsonElement packages) &&
        packages.ValueKind == JsonValueKind.Object) {
      foreach (JsonProperty entry in packages.EnumerateObject()) {
        int index = entry.Name.LastIndexOf(C_NODE_MODULES_, StringComparison.Ordinal);
        JsonElement value = entry.Value;
        /* "" is the package itself, paths without node_modules are links. */
        if (index < 0 ||
            value.ValueKind != JsonValueKind.Object ||
            IsTrue(value, "dev") ||
            IsTrue(value, "inBundle") ||
            IsTrue(value, "link") ||
            GetSpec(value) is not { } spec) {
          continue;
        }

        string name = GetString(value, "name") ??
                      entry.Name[(index + C_NODE_MODULES_.Length)..];
        locked.Add(
          (name, spec, IsTrue(value, "optional") || IsTrue(value, "devOptional"))
        );
      }

      return locked;
    }

    if (shrinkwrap.TryGetProperty("dependencies", out JsonElement dependencies)) {
      AddVersion1(dependencies, locked);
    }

    return locked;
  }

  private static void AddVersion1(JsonElement dependencies,
                                  List<(string name, string spec, bool optional)>
                                    locked) {
    if (dependencies.ValueKind != JsonValueKind.Object) {
      return;
    }

    foreach (JsonProperty entry in dependencies.EnumerateObject()) {
      JsonElement value = entry.Value;
      /* Everything below a development or bundled package is one too. */
      if (value.ValueKind != JsonValueKind.Object ||
          IsTrue(value, "dev") ||
          IsTrue(value, "bundled")) {
        continue;
      }

      if (GetSpec(value) is { } spec) {
        locked.Add((entry.Name, spec, IsTrue(value, "optional")));
      }

      if (value.TryGetProperty("dependencies", out JsonElement nested)) {
        AddVersion1(nested, locked);
      }
    }
  }

  private static string? GetSpec(JsonElement entry) {
    string? version = GetString(entry, "version");
    string? resolved = GetString(entry, "resolved");
    if (resolved != null &&
        !resolved.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !resolved.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
      return resolved;
    }

    return version;
  }

  private static string? GetString(JsonElement element, string property) {
    return element.TryGetProperty(property, out JsonElement value) &&
           value.ValueKind == JsonValueKind.String
             ? value.GetString()
             : null;
  }

  private static bool IsTrue(JsonElement element, string property) {
    return element.TryGetProperty(property, out JsonElement value) &&
           value.ValueKind == JsonValueKind.True;
  }
}
