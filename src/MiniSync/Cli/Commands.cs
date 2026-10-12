// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Npm;
using MiniSync.Nuget;

namespace MiniSync.Cli;

/// <summary>
///   The command implementations.
/// </summary>
public static class Commands {
  private static readonly string[] S_ECOSYSTEMS_ = { "npm", "nuget" };

  private const string C_USAGE_ = """
    minisync - sync exact npm and NuGet package versions, and only the
    dependencies they need, to disk for transfer to an offline network.

    usage: minisync <command> [options]

    commands:
      init                                   create minisync.json
      add <npm|nuget> <name>[@<version>]...  add packages (no version = pin latest)
      remove <npm|nuget> <name>[@<version>]... remove packages or single versions
      list                                   show the packages in the config
      sync                                   resolve and download everything

    options:
      -c, --config <file>   config file (default: minisync.json)
      -o, --output <dir>    override the output directory (sync)
          --only <npm|nuget> sync one ecosystem only (sync)
      -n, --dry-run         resolve and show what would be downloaded (sync)
          --force           overwrite an existing config (init)
      -v, --verbose         show debug output
          --version         show the version
      -h, --help            show this help

    examples:
      minisync init
      minisync add npm react@19.0 react-dom@19.0
      minisync add nuget Newtonsoft.Json@13.0.3 Serilog@4.*
      minisync sync --dry-run
      minisync sync
    """;

  /// <summary>
  ///   Runs the command line.
  /// </summary>
  /// <param name="args">The arguments.</param>
  /// <returns>The process exit code.</returns>
  public static async Task<int> Run(string[] args) {
    using CancellationTokenSource cancel = new();
    Console.CancelKeyPress += (_, e) => {
      /* The first Ctrl+C stops cleanly, a second one kills the process. */
      if (!cancel.IsCancellationRequested) {
        e.Cancel = true;
        Log.Warn("cancelling, press Ctrl+C again to force");
        cancel.Cancel();
      }
    };

    try {
      CommandLine cli = CommandLine.Parse(args);
      Log.verbose = cli.HasFlag("--verbose");
      if (cli.HasFlag("--version")) {
        Log.Info(
          $"minisync {typeof(Commands).Assembly.GetName().Version?.ToString(3)}"
        );
        return 0;
      }

      if (cli.HasFlag("--help") || cli.command is null or "help") {
        Console.WriteLine(C_USAGE_);
        return 0;
      }

      return cli.command switch {
        "init" => Init(cli),
        "add" => await Add(cli, cancel.Token),
        "remove" => Remove(cli),
        "list" => List(cli),
        "sync" => await Sync(cli, cancel.Token),
        _ => throw new UsageException($"unknown command '{cli.command}'")
      };
    } catch (UsageException e) {
      Log.Error(e.Message);
      Log.Info("run 'minisync --help' for usage");
      return 2;
    } catch (ConfigException e) {
      Log.Error(e.Message);
      return 1;
    } catch (OperationCanceledException) when (cancel.IsCancellationRequested) {
      Log.Error("cancelled");
      return 130;
    }
  }

  private static int Init(CommandLine cli) {
    string path = GetConfigPath(cli);
    if (File.Exists(path) && !cli.HasFlag("--force")) {
      throw new UsageException(
        $"{path} already exists, use --force to overwrite it"
      );
    }

    new SyncConfig().Save(path);
    Log.Success($"created {path}");
    Log.Info("add packages with e.g. 'minisync add npm react@19.0', then run 'minisync sync'");
    return 0;
  }

  private static async Task<int> Add(CommandLine cli, CancellationToken token) {
    (string ecosystem, List<string> specs) = GetPackageArguments(cli);
    string path = GetConfigPath(cli);
    SyncConfig config = SyncConfig.Load(path);
    EcosystemConfig settings = GetSettings(config, ecosystem);
    using Fetcher fetcher = new(config.concurrency);
    IRegistry registry = CreateRegistry(ecosystem, config, fetcher);

    int failures = 0;
    bool changed = false;
    foreach (string spec in specs) {
      (string name, string? version) = ParseSpec(spec);
      try {
        /* Resolving up front catches typos before they reach the config,
           and gives a version to pin when none was asked for. */
        ResolvedPackage package = (await registry.Resolve(
                                    new PackageRequest {
                                      name = name,
                                      spec = version ?? "latest"
                                    },
                                    Array.Empty<ResolvedPackage>(),
                                    token
                                  ))!;
        string pinned = version ?? package.version;
        string key = FindPackage(settings, ecosystem, name) ?? name;
        List<string> versions = settings.packages.GetValueOrDefault(key) ?? new();
        if (versions.Contains(pinned)) {
          Log.Info($"{key}@{pinned} is already in the config");
          continue;
        }

        versions.Add(pinned);
        settings.packages[key] = versions;
        changed = true;
        Log.Success(
          pinned == package.version
            ? $"added {key}@{pinned}"
            : $"added {key}@{pinned} (currently resolves to {package.version})"
        );
      } catch (Exception e) when (e is not OperationCanceledException) {
        Log.Error($"{name}@{version ?? "latest"}: {e.Message}");
        failures++;
      }
    }

    if (changed) {
      config.Save(path);
    }

    return failures == 0 ? 0 : 1;
  }

  private static int Remove(CommandLine cli) {
    (string ecosystem, List<string> specs) = GetPackageArguments(cli);
    string path = GetConfigPath(cli);
    SyncConfig config = SyncConfig.Load(path);
    EcosystemConfig settings = GetSettings(config, ecosystem);

    int failures = 0;
    foreach (string spec in specs) {
      (string name, string? version) = ParseSpec(spec);
      string? key = FindPackage(settings, ecosystem, name);
      List<string>? versions =
        key == null ? null : settings.packages.GetValueOrDefault(key);
      if (key == null || versions == null ||
          (version != null && !versions.Contains(version))) {
        Log.Error($"{spec} is not in the config");
        failures++;
        continue;
      }

      if (version != null) {
        versions.Remove(version);
      }

      if (version == null || versions.Count == 0) {
        settings.packages.Remove(key);
      }

      Log.Success($"removed {(version == null ? key : $"{key}@{version}")}");
    }

    config.Save(path);
    return failures == 0 ? 0 : 1;
  }

  private static int List(CommandLine cli) {
    string path = GetConfigPath(cli);
    SyncConfig config = SyncConfig.Load(path);
    Log.Info($"output: {config.GetOutputDirectory(path)}");
    foreach (string ecosystem in S_ECOSYSTEMS_) {
      EcosystemConfig settings = GetSettings(config, ecosystem);
      Log.Info("");
      Log.Info($"{ecosystem} ({settings.packages.Count} packages)");
      foreach (KeyValuePair<string, List<string>> package in settings.packages) {
        Log.Info($"  {package.Key}  {string.Join(", ", package.Value)}");
      }

      if (settings.allow.Count > 0) {
        Log.Info($"  allow: {string.Join(", ", settings.allow)}");
      }

      if (settings.deny.Count > 0) {
        Log.Info($"  deny: {string.Join(", ", settings.deny)}");
      }
    }

    return 0;
  }

  private static async Task<int> Sync(CommandLine cli, CancellationToken token) {
    string path = GetConfigPath(cli);
    SyncConfig config = SyncConfig.Load(path);
    string? only = cli.GetOption("--only")?.ToLowerInvariant();
    if (only != null && !S_ECOSYSTEMS_.Contains(only)) {
      throw new UsageException("--only must be npm or nuget");
    }

    string output = cli.GetOption("--output") is { } directory
                      ? Path.GetFullPath(directory)
                      : config.GetOutputDirectory(path);
    bool dry_run = cli.HasFlag("--dry-run");
    using Fetcher fetcher = new(config.concurrency);
    Downloader downloader = new(fetcher, output, config.concurrency, config.delta);
    Stopwatch watch = Stopwatch.StartNew();

    bool ok = true;
    bool any = false;
    foreach (string ecosystem in S_ECOSYSTEMS_) {
      EcosystemConfig settings = GetSettings(config, ecosystem);
      if ((only != null && only != ecosystem) || settings.packages.Count == 0) {
        continue;
      }

      any = true;
      IRegistry registry = CreateRegistry(ecosystem, config, fetcher);
      ok &= await SyncEcosystem(
              registry,
              settings,
              downloader,
              dry_run,
              token
            );
    }

    if (!any) {
      Log.Warn("nothing to sync, add packages with 'minisync add'");
      return 0;
    }

    Log.Info("");
    Log.Info($"finished in {watch.Elapsed.TotalSeconds:0.0}s, output in {output}");
    return ok ? 0 : 1;
  }

  private static async Task<bool> SyncEcosystem(IRegistry registry,
                                                EcosystemConfig settings,
                                                Downloader downloader,
                                                bool dry_run,
                                                CancellationToken token) {
    string module = registry.module;
    List<PackageRequest> roots = settings.packages
                                         .SelectMany(
                                           p => p.Value.Select(
                                             v => new PackageRequest {
                                               name = p.Key,
                                               spec = v
                                             }
                                           )
                                         )
                                         .ToList();
    Log.Info("");
    Log.Info($"{module}: resolving {roots.Count} requested package(s)");
    if (settings is NugetConfig { target_frameworks.Count: 0 }) {
      Log.Warn(
        "nuget.target_frameworks is empty, so every dependency group is followed; " +
        "list your frameworks (e.g. \"net8.0\") to sync far less"
      );
    }

    ResolveResult resolved = await new Resolver(
                               registry,
                               new PackageFilter(settings.allow, settings.deny)
                             ).Resolve(roots, token);

    foreach (PackageRequest blocked in resolved.blocked) {
      Log.Warn($"{module}: {blocked} is blocked by allow/deny, not synced");
    }

    foreach (PackageRequest skipped in resolved.skipped) {
      Log.Debug($"{module}: {skipped} skipped, not for the configured platforms");
    }

    foreach (string error in resolved.errors) {
      Log.Error($"{module}: {error}");
    }

    int present = resolved.packages.Count(downloader.Exists);
    Log.Info(
      $"{module}: {resolved.packages.Count} package(s) resolved, " +
      $"{resolved.packages.Count - present} to download, {present} already on disk" +
      (resolved.skipped.Count > 0
         ? $", {resolved.skipped.Count} optional platform package(s) skipped"
         : "")
    );

    if (dry_run) {
      foreach (ResolvedPackage package in resolved.packages) {
        string state = downloader.Exists(package) ? "have" : "new ";
        string reason =
          package.required_by == null ? "" : $"  <- {package.required_by}";
        Log.Info($"  {state} {package}{reason}");
      }

      return resolved.errors.Count == 0;
    }

    DownloadResult downloaded =
      await downloader.Download(registry, resolved.packages, token);
    string summary =
      $"{module}: {downloaded.downloaded} downloaded ({Downloader.FormatBytes(downloaded.bytes)}), " +
      $"{downloaded.present} already on disk, {downloaded.failed.Count} failed";
    bool ok = resolved.errors.Count == 0 && downloaded.failed.Count == 0;
    if (ok) {
      Log.Success(summary);
    } else {
      Log.Error(summary);
    }

    return ok;
  }

  private static IRegistry CreateRegistry(string ecosystem, SyncConfig config,
                                          Fetcher fetcher) {
    return ecosystem == "npm"
             ? new NpmRegistry(config.npm, fetcher)
             : new NugetRegistry(config.nuget, fetcher);
  }

  private static EcosystemConfig GetSettings(SyncConfig config,
                                             string ecosystem) {
    return ecosystem == "npm" ? config.npm : config.nuget;
  }

  private static string GetConfigPath(CommandLine cli) {
    return cli.GetOption("--config") ?? SyncConfig.C_DEFAULT_FILE;
  }

  private static (string ecosystem, List<string> specs) GetPackageArguments(
    CommandLine cli) {
    if (cli.arguments.Count < 2 ||
        !S_ECOSYSTEMS_.Contains(cli.arguments[0].ToLowerInvariant())) {
      throw new UsageException(
        $"usage: minisync {cli.command} <npm|nuget> <name>[@<version>]..."
      );
    }

    return (cli.arguments[0].ToLowerInvariant(), cli.arguments.Skip(1).ToList());
  }

  /// <summary>
  ///   Splits <c>name@version</c>. The search starts after the first character
  ///   so scoped npm names (<c>@types/node@20.0.0</c>) work.
  /// </summary>
  private static (string name, string? version) ParseSpec(string spec) {
    int at = spec.LastIndexOf('@');
    if (at <= 0) {
      return (spec, null);
    }

    string version = spec[(at + 1)..];
    if (version.Length == 0) {
      throw new UsageException($"'{spec}' has an empty version");
    }

    return (spec[..at], version);
  }

  /// <summary>
  ///   Finds the config key of a package. NuGet ids are case-insensitive.
  /// </summary>
  private static string? FindPackage(EcosystemConfig settings, string ecosystem,
                                     string name) {
    StringComparison comparison = ecosystem == "nuget"
                                    ? StringComparison.OrdinalIgnoreCase
                                    : StringComparison.Ordinal;
    return settings.packages.Keys.FirstOrDefault(k => k.Equals(name, comparison));
  }
}
