// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Cli;

/// <summary>
///   A parsed command line: a command, positional arguments and options.
///   Options can be written <c>--name value</c> or <c>--name=value</c>.
/// </summary>
public class CommandLine {
  private static readonly Dictionary<string, string> S_ALIASES_ = new() {
    { "-c", "--config" },
    { "-o", "--output" },
    { "-n", "--dry-run" },
    { "-v", "--verbose" },
    { "-h", "--help" },
    { "/?", "--help" }
  };

  private static readonly HashSet<string> S_FLAGS_ = new() {
    "--dry-run", "--verbose", "--help", "--version", "--force"
  };

  private static readonly HashSet<string> S_VALUES_ = new() {
    "--config", "--output", "--only"
  };

  private readonly Dictionary<string, string?> options_ = new();

  private CommandLine() {
  }

  /// <summary>
  ///   Gets the command, or null if none was given.
  /// </summary>
  public string? command { get; private set; }

  /// <summary>
  ///   Gets the positional arguments after the command.
  /// </summary>
  public List<string> arguments { get; } = new();

  /// <summary>
  ///   Parses command line arguments.
  /// </summary>
  /// <param name="args">The arguments.</param>
  /// <returns>The parsed command line.</returns>
  /// <exception cref="UsageException">Thrown for unknown or incomplete options.</exception>
  public static CommandLine Parse(string[] args) {
    CommandLine cli = new();
    for (int i = 0; i < args.Length; i++) {
      string arg = args[i];
      if (!arg.StartsWith('-') && arg != "/?") {
        if (cli.command == null) {
          cli.command = arg.ToLowerInvariant();
        } else {
          cli.arguments.Add(arg);
        }

        continue;
      }

      string name = arg;
      string? value = null;
      int equals = arg.IndexOf('=');
      if (equals > 0) {
        name = arg[..equals];
        value = arg[(equals + 1)..];
      }

      name = S_ALIASES_.GetValueOrDefault(name, name);
      if (S_FLAGS_.Contains(name) && value == null) {
        cli.options_[name] = null;
      } else if (S_VALUES_.Contains(name)) {
        if (value == null) {
          if (i + 1 >= args.Length) {
            throw new UsageException($"{name} needs a value");
          }

          value = args[++i];
        }

        cli.options_[name] = value;
      } else {
        throw new UsageException($"unknown option '{arg}'");
      }
    }

    return cli;
  }

  /// <summary>
  ///   Checks whether a flag was given.
  /// </summary>
  /// <param name="name">The long flag name, e.g. <c>--dry-run</c>.</param>
  /// <returns>True if the flag was given; otherwise, false.</returns>
  public bool HasFlag(string name) {
    return options_.ContainsKey(name);
  }

  /// <summary>
  ///   Gets the value of an option.
  /// </summary>
  /// <param name="name">The long option name, e.g. <c>--config</c>.</param>
  /// <returns>The value, or null if the option was not given.</returns>
  public string? GetOption(string name) {
    return options_.GetValueOrDefault(name);
  }
}
