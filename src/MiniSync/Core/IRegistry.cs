// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   The ecosystem specific half of a sync: how versions are chosen, which
///   dependencies a version has and how a downloaded file is verified. The
///   <see cref="Resolver" /> drives it and owns everything else.
/// </summary>
public interface IRegistry {
  /// <summary>
  ///   Gets the module name, used as the top level storage directory.
  /// </summary>
  public string module { get; }

  /// <summary>
  ///   Normalizes a package name for comparison.
  /// </summary>
  /// <param name="name">The package name.</param>
  /// <returns>The normalized name.</returns>
  public string NormalizeName(string name);

  /// <summary>
  ///   Starts fetching metadata for a package that is about to be resolved.
  /// </summary>
  /// <param name="name">The package name.</param>
  /// <param name="token">The cancellation token.</param>
  public void Prefetch(string name, CancellationToken token);

  /// <summary>
  ///   Selects the version that satisfies a request. Dependency requests
  ///   should prefer a version that is already selected, which is what keeps
  ///   the synced set small.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <param name="selected">The versions of this package selected so far.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>The selected package, or null when an optional dependency does not apply.</returns>
  /// <exception cref="ResolveException">Thrown when no version satisfies the request.</exception>
  public Task<ResolvedPackage?> Resolve(PackageRequest request,
                                        IReadOnlyList<ResolvedPackage> selected,
                                        CancellationToken token);

  /// <summary>
  ///   Gets the dependencies of a selected package that have to be synced.
  /// </summary>
  /// <param name="package">The package.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>The dependency requests.</returns>
  public Task<IReadOnlyList<PackageRequest>> GetDependencies(
    ResolvedPackage package, CancellationToken token);

  /// <summary>
  ///   Verifies a downloaded file before it is committed to storage.
  /// </summary>
  /// <param name="file">The downloaded file.</param>
  /// <param name="package">The package it should contain.</param>
  /// <param name="token">The cancellation token.</param>
  /// <exception cref="InvalidDataException">Thrown when the file is not the expected package.</exception>
  public Task Verify(string file, ResolvedPackage package,
                     CancellationToken token);
}
