// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   Minimal console logger. Downloads run in parallel, so every write takes a
///   lock to keep lines and colours from interleaving.
/// </summary>
public static class Log {
  private static readonly object S_LOCK_ = new();

  /// <summary>
  ///   Gets or sets a value indicating whether debug messages are written.
  /// </summary>
  public static bool verbose { get; set; }

  /// <summary>
  ///   Writes an informational message.
  /// </summary>
  /// <param name="message">The message.</param>
  public static void Info(string message) {
    Write(Console.Out, message, null);
  }

  /// <summary>
  ///   Writes a success message.
  /// </summary>
  /// <param name="message">The message.</param>
  public static void Success(string message) {
    Write(Console.Out, message, ConsoleColor.Green);
  }

  /// <summary>
  ///   Writes a warning.
  /// </summary>
  /// <param name="message">The message.</param>
  public static void Warn(string message) {
    Write(Console.Out, $"warning: {message}", ConsoleColor.Yellow);
  }

  /// <summary>
  ///   Writes an error to standard error.
  /// </summary>
  /// <param name="message">The message.</param>
  public static void Error(string message) {
    Write(Console.Error, $"error: {message}", ConsoleColor.Red);
  }

  /// <summary>
  ///   Writes a debug message when <see cref="verbose" /> is enabled.
  /// </summary>
  /// <param name="message">The message.</param>
  public static void Debug(string message) {
    if (verbose) {
      Write(Console.Out, message, ConsoleColor.DarkGray);
    }
  }

  private static void Write(TextWriter writer, string message,
                            ConsoleColor? color) {
    lock (S_LOCK_) {
      if (color != null) {
        Console.ForegroundColor = color.Value;
      }

      writer.WriteLine(message);
      if (color != null) {
        Console.ResetColor();
      }
    }
  }
}
