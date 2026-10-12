// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   Thrown when a package request cannot be satisfied.
/// </summary>
public class ResolveException : Exception {
  /// <summary>
  ///   Initializes a new instance of the <see cref="ResolveException" /> class.
  /// </summary>
  /// <param name="message">Why the request cannot be satisfied.</param>
  public ResolveException(string message) : base(message) {
  }
}
