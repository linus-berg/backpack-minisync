// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MiniSync.Cli;
using MiniSync.Configuration;
using MiniSync.Core;

namespace MiniSync.Tests;

public class StorageTests {
  [Theory]
  [InlineData("https://registry.npmjs.org/react/-/react-19.0.0.tgz", "npm/registry.npmjs.org/react/react-19.0.0.tgz")]
  [InlineData("https://registry.npmjs.org/@types/node/-/node-20.0.0.tgz", "npm/registry.npmjs.org/@types/node/node-20.0.0.tgz")]
  public void Npm_paths_match_backpack(string url, string expected) {
    Assert.Equal(expected, Storage.GetArtifactPath("npm", url));
  }

  [Fact]
  public void Delta_paths_match_backpack() {
    Assert.Equal(
      "delta/npm/2026_01_31/registry.npmjs.org/react/react-19.0.0.tgz",
      Storage.GetDeltaPath("npm", "https://registry.npmjs.org/react/-/react-19.0.0.tgz", new DateTime(2026, 1, 31))
    );
  }

  [Fact]
  public void Paths_cannot_escape_the_output_directory() {
    string root = Path.Combine(Path.GetTempPath(), "minisync-root");
    Assert.StartsWith(root, Storage.GetFullPath(root, "npm/a/b.tgz"));
    Assert.Throws<InvalidOperationException>(() => Storage.GetFullPath(root, "../outside.tgz"));
    Assert.Throws<InvalidOperationException>(() => Storage.GetFullPath(root, "npm/../../outside.tgz"));
  }
}

public class PackageFilterTests {
  [Theory]
  [InlineData("@types/node", false)]
  [InlineData("@TYPES/react", false)]
  [InlineData("lodash", true)]
  [InlineData("lodash.merge", false)]
  [InlineData("left-pad", false)]
  public void Globs_match_whole_names(string name, bool allowed) {
    PackageFilter filter = new(new[] { "lodash", "@types/*", "left-pad" }, new[] { "@types/*", "left-?ad" });
    Assert.Equal(allowed, filter.IsAllowed(new PackageRequest { name = name, spec = "1", required_by = "x" }));
  }
}

public sealed class SyncConfigTests : IDisposable {
  private readonly string path_ = Path.Combine(Path.GetTempPath(), $"minisync-{Guid.NewGuid():N}.json");

  public void Dispose() {
    File.Delete(path_);
  }

  [Fact]
  public void Versions_can_be_a_string_or_an_array_and_round_trip() {
    File.WriteAllText(
      path_,
      """
      {
        "npm": { "packages": { "react": "19.0", "typescript": ["5.6.3", "5.7.2"], }, },
        "nuget": { "target_frameworks": ["net8.0"] }
      }
      """
    );
    SyncConfig config = SyncConfig.Load(path_);
    Assert.Equal(new[] { "19.0" }, config.npm.packages["react"]);
    Assert.Equal(new[] { "5.6.3", "5.7.2" }, config.npm.packages["typescript"]);

    config.Save(path_);
    string saved = File.ReadAllText(path_);
    Assert.Contains("\"react\": \"19.0\"", saved);
    Assert.Equal(config.npm.packages["typescript"], SyncConfig.Load(path_).npm.packages["typescript"]);
  }

  [Fact]
  public void Typos_and_bad_values_are_errors() {
    File.WriteAllText(path_, """{ "nuget": { "target_framework": ["net8.0"] } }""");
    Assert.Contains("target_framework", Assert.Throws<ConfigException>(() => SyncConfig.Load(path_)).Message);

    File.WriteAllText(path_, """{ "npm": { "packages": { "react": 19 } } }""");
    Assert.Throws<ConfigException>(() => SyncConfig.Load(path_));

    File.WriteAllText(path_, """{ "concurrency": 0 }""");
    Assert.Throws<ConfigException>(() => SyncConfig.Load(path_));

    Assert.Contains("minisync init", Assert.Throws<ConfigException>(() => SyncConfig.Load(path_ + ".missing")).Message);
  }

  [Fact]
  public void Output_is_relative_to_the_config_file() {
    SyncConfig config = new() { output = "data" };
    string expected = Path.Combine(Path.GetDirectoryName(path_)!, "data");
    Assert.Equal(expected, config.GetOutputDirectory(path_));
  }
}

public class CommandLineTests {
  [Fact]
  public void Parses_commands_arguments_flags_and_values() {
    CommandLine cli = CommandLine.Parse(
      new[] { "add", "npm", "-c", "my.json", "react@19.0", "--dry-run", "--output=out", "@types/node@20" }
    );
    Assert.Equal("add", cli.command);
    Assert.Equal(new[] { "npm", "react@19.0", "@types/node@20" }, cli.arguments);
    Assert.Equal("my.json", cli.GetOption("--config"));
    Assert.Equal("out", cli.GetOption("--output"));
    Assert.True(cli.HasFlag("--dry-run"));
    Assert.False(cli.HasFlag("--verbose"));
  }

  [Fact]
  public void Rejects_unknown_and_incomplete_options() {
    Assert.Throws<UsageException>(() => CommandLine.Parse(new[] { "sync", "--bogus" }));
    Assert.Throws<UsageException>(() => CommandLine.Parse(new[] { "sync", "--config" }));
  }
}
