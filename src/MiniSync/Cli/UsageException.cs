// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Cli;

/// <summary>
///   Thrown when the command line is invalid.
/// </summary>
public class UsageException : Exception {
  /// <summary>
  ///   Initializes a new instance of the <see cref="UsageException" /> class.
  /// </summary>
  /// <param name="message">What is wrong with the command line.</param>
  public UsageException(string message) : base(message) {
  }
}
