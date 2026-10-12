// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace MiniSync.Core;

/// <summary>
///   Maps artifact URLs to paths on disk. The layout is the same as the keys
///   backpack writes to S3 (<c>&lt;module&gt;/&lt;host&gt;&lt;path&gt;</c>), so
///   anything that consumes a backpack bucket can consume this directory.
/// </summary>
public static class Storage {
  private static readonly char[] S_INVALID_ = {
    '\\', ':', '*', '?', '"', '<', '>', '|'
  };

  /// <summary>
  ///   Gets the storage path of an artifact relative to the output directory.
  /// </summary>
  /// <param name="module">The module, e.g. <c>npm</c> or <c>nuget</c>.</param>
  /// <param name="url">The URL the artifact is downloaded from.</param>
  /// <returns>A relative path using forward slashes.</returns>
  /// <exception cref="InvalidDataException">Thrown when the URL cannot be stored safely.</exception>
  public static string GetArtifactPath(string module, string url) {
    return $"{module}/{GetLocation(url)}";
  }

  /// <summary>
  ///   Gets the path of an artifact inside the delta deposit for a given day,
  ///   matching backpack's <c>delta/&lt;module&gt;/&lt;yyyy_MM_dd&gt;/&lt;location&gt;</c>.
  /// </summary>
  /// <param name="module">The module.</param>
  /// <param name="url">The URL the artifact is downloaded from.</param>
  /// <param name="date">The day of the deposit.</param>
  /// <returns>A relative path using forward slashes.</returns>
  public static string GetDeltaPath(string module, string url, DateTime date) {
    return $"delta/{module}/{date:yyyy_MM_dd}/{GetLocation(url)}";
  }

  /// <summary>
  ///   Resolves a relative storage path against the output directory and
  ///   refuses anything that would land outside it. URLs come from registry
  ///   metadata, which is not something to trust with the file system.
  /// </summary>
  /// <param name="root">The output directory.</param>
  /// <param name="relative">The relative storage path.</param>
  /// <returns>The absolute path.</returns>
  /// <exception cref="InvalidOperationException">Thrown when the path escapes the root.</exception>
  public static string GetFullPath(string root, string relative) {
    string full_root = Path.GetFullPath(root);
    string full = Path.GetFullPath(
      Path.Combine(full_root, relative.Replace('/', Path.DirectorySeparatorChar))
    );
    string prefix = Path.EndsInDirectorySeparator(full_root)
                      ? full_root
                      : full_root + Path.DirectorySeparatorChar;
    StringComparison comparison = OperatingSystem.IsWindows()
                                    ? StringComparison.OrdinalIgnoreCase
                                    : StringComparison.Ordinal;
    if (!full.StartsWith(prefix, comparison)) {
      throw new InvalidOperationException(
        $"{relative} resolves outside of {full_root}"
      );
    }

    return full;
  }

  private static string GetLocation(string url) {
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
        (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) {
      throw new InvalidDataException($"'{url}' is not an http(s) URL");
    }

    /* npm tarballs live under /<name>/-/<file>; backpack drops the "-". */
    string path = Regex.Replace(uri.LocalPath, @"\/-\/", "/");

    /* LocalPath is decoded, so %5C and %3A arrive as \ and :, which Windows
       would read as a separator and an alternate data stream. */
    foreach (string segment in path.Split('/')[1..]) {
      if (segment is "" or "." or ".." ||
          segment.IndexOfAny(S_INVALID_) >= 0 ||
          segment.Any(char.IsControl)) {
        throw new InvalidDataException($"'{url}' cannot be stored safely");
      }
    }

    return $"{uri.Host}{path}";
  }
}
