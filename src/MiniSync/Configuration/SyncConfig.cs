// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniSync.Configuration;

/// <summary>
///   The sync definition file: where packages are stored and which packages,
///   at which versions, are synced.
/// </summary>
public class SyncConfig {
  /// <summary>
  ///   The file name used when no config is given on the command line.
  /// </summary>
  public const string C_DEFAULT_FILE = "minisync.json";

  private static readonly JsonSerializerOptions S_OPTIONS_ = new() {
    AllowTrailingCommas = true,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
  };

  /// <summary>
  ///   Gets or sets the output directory. Relative paths are relative to the
  ///   config file.
  /// </summary>
  public string output { get; set; } = "data";

  /// <summary>
  ///   Gets or sets the number of parallel HTTP requests.
  /// </summary>
  public int concurrency { get; set; } = 8;

  /// <summary>
  ///   Gets or sets a value indicating whether newly downloaded files are also
  ///   copied to <c>delta/&lt;module&gt;/&lt;yyyy_MM_dd&gt;/</c>, so only what
  ///   is new has to be carried across to the offline side.
  /// </summary>
  public bool delta { get; set; }

  /// <summary>
  ///   Gets or sets the npm settings.
  /// </summary>
  public NpmConfig npm { get; set; } = new();

  /// <summary>
  ///   Gets or sets the NuGet settings.
  /// </summary>
  public NugetConfig nuget { get; set; } = new();

  /// <summary>
  ///   Loads and validates a config file.
  /// </summary>
  /// <param name="path">The file path.</param>
  /// <returns>The config.</returns>
  /// <exception cref="ConfigException">Thrown when the file is missing or invalid.</exception>
  public static SyncConfig Load(string path) {
    if (!File.Exists(path)) {
      throw new ConfigException(
        $"{path} not found, create one with 'minisync init'"
      );
    }

    SyncConfig? config;
    try {
      config = JsonSerializer.Deserialize<SyncConfig>(
        File.ReadAllText(path),
        S_OPTIONS_
      );
    } catch (JsonException e) {
      throw new ConfigException($"{path}: {e.Message}");
    }

    if (config == null) {
      throw new ConfigException($"{path} is empty");
    }

    config.Validate(path);
    return config;
  }

  /// <summary>
  ///   Writes the config to a file.
  /// </summary>
  /// <param name="path">The file path.</param>
  public void Save(string path) {
    File.WriteAllText(path, JsonSerializer.Serialize(this, S_OPTIONS_) + "\n");
  }

  /// <summary>
  ///   Gets the absolute output directory.
  /// </summary>
  /// <param name="config_path">The path the config was loaded from.</param>
  /// <returns>The absolute output directory.</returns>
  public string GetOutputDirectory(string config_path) {
    string directory =
      Path.GetDirectoryName(Path.GetFullPath(config_path)) ?? ".";
    return Path.GetFullPath(Path.Combine(directory, output));
  }

  private void Validate(string path) {
    if (string.IsNullOrWhiteSpace(output)) {
      throw new ConfigException($"{path}: output must not be empty");
    }

    if (concurrency < 1) {
      throw new ConfigException($"{path}: concurrency must be at least 1");
    }

    ValidateUrl(path, "npm.registry", npm.registry);
    ValidateUrl(path, "nuget.source", nuget.source);
    ValidatePackages(path, "npm", npm);
    ValidatePackages(path, "nuget", nuget);
  }

  private static void ValidateUrl(string path, string key, string url) {
    if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
        (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) {
      throw new ConfigException($"{path}: {key} must be an http(s) URL");
    }
  }

  private static void ValidatePackages(string path, string ecosystem,
                                       EcosystemConfig settings) {
    foreach (KeyValuePair<string, List<string>> package in settings.packages) {
      if (package.Value.Count == 0) {
        throw new ConfigException(
          $"{path}: {ecosystem}.packages.{package.Key} has no versions"
        );
      }
    }
  }
}
