// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Configuration;

/// <summary>
///   Thrown when the config file is missing or invalid.
/// </summary>
public class ConfigException : Exception {
  /// <summary>
  ///   Initializes a new instance of the <see cref="ConfigException" /> class.
  /// </summary>
  /// <param name="message">What is wrong with the config.</param>
  public ConfigException(string message) : base(message) {
  }
}
