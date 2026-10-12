// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace MiniSync.Tests;

/// <summary>
///   Builds a fake npm registry on top of <see cref="FakeHttp" />.
/// </summary>
public class NpmFeed {
  public const string C_REGISTRY = "https://registry.test/";
  private readonly Dictionary<string, Dictionary<string, object>> packages_ = new();
  private readonly Dictionary<string, string> latest_ = new();

  public FakeHttp http { get; } = new();

  /// <summary>
  ///   Publishes a version. <paramref name="extra" /> adds manifest fields
  ///   such as dependencies, os or deprecated.
  /// </summary>
  public NpmFeed Publish(string name, string version,
                         Dictionary<string, object>? extra = null,
                         byte[]? content = null) {
    string file = name.Contains('/') ? name.Split('/')[1] : name;
    string tarball = $"{C_REGISTRY}{name}/-/{file}-{version}.tgz";
    content ??= Encoding.UTF8.GetBytes($"{name}@{version}");
    Dictionary<string, object> manifest = new(extra ?? new()) {
      ["name"] = name,
      ["version"] = version,
      ["dist"] = new Dictionary<string, string> {
        ["tarball"] = tarball,
        ["integrity"] = FakeHttp.Sri(content)
      }
    };
    if (!packages_.TryGetValue(name, out Dictionary<string, object>? versions)) {
      versions = new Dictionary<string, object>();
      packages_[name] = versions;
    }

    versions[version] = manifest;
    http.Add(tarball, content);
    Serve(name);
    return this;
  }

  /// <summary>
  ///   Points the latest tag at a version.
  /// </summary>
  public NpmFeed Latest(string name, string version) {
    latest_[name] = version;
    Serve(name);
    return this;
  }

  private void Serve(string name) {
    Dictionary<string, string> tags = new();
    if (latest_.TryGetValue(name, out string? latest)) {
      tags["latest"] = latest;
    }

    http.AddJson(
      $"{C_REGISTRY}{name.Replace("/", "%2f")}",
      new Dictionary<string, object> {
        ["name"] = name,
        ["dist-tags"] = tags,
        ["versions"] = packages_[name]
      }
    );
  }

  /// <summary>
  ///   Builds a gzipped tarball holding the given files.
  /// </summary>
  public static byte[] Tarball(Dictionary<string, string> files) {
    using MemoryStream output = new();
    using (GZipStream gzip = new(output, CompressionLevel.Fastest, true))
    using (TarWriter tar = new(gzip, TarEntryFormat.Ustar, false)) {
      foreach (KeyValuePair<string, string> file in files) {
        tar.WriteEntry(
          new UstarTarEntry(TarEntryType.RegularFile, file.Key) {
            DataStream = new MemoryStream(Encoding.UTF8.GetBytes(file.Value))
          }
        );
      }
    }

    return output.ToArray();
  }

  public static Dictionary<string, object> Deps(params string[] pairs) {
    return new Dictionary<string, object> { ["dependencies"] = Map(pairs) };
  }

  public static Dictionary<string, string> Map(params string[] pairs) {
    Dictionary<string, string> map = new();
    for (int i = 0; i < pairs.Length; i += 2) {
      map[pairs[i]] = pairs[i + 1];
    }

    return map;
  }
}
