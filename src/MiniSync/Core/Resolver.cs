// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace MiniSync.Core;

/// <summary>
///   Walks the dependency graph of the root packages breadth first and selects
///   exactly one version per distinct request. Unlike backpack, which collects
///   every version of every package it meets, only the versions needed to
///   install the roots are selected.
/// </summary>
public class Resolver {
  private readonly PackageFilter filter_;
  private readonly IRegistry registry_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="Resolver" /> class.
  /// </summary>
  /// <param name="registry">The registry to resolve against.</param>
  /// <param name="filter">The allow/deny filter.</param>
  public Resolver(IRegistry registry, PackageFilter filter) {
    registry_ = registry;
    filter_ = filter;
  }

  /// <summary>
  ///   Resolves the roots and all of their dependencies.
  /// </summary>
  /// <param name="roots">The root requests.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>The selected packages and everything that was left out.</returns>
  public async Task<ResolveResult> Resolve(IReadOnlyList<PackageRequest> roots,
                                           CancellationToken token) {
    ResolveResult result = new();
    Dictionary<string, List<ResolvedPackage>> selected = new();
    HashSet<string> seen = new();
    Queue<(ResolvedPackage package, Task<IReadOnlyList<PackageRequest>>
      dependencies)> pending = new();

    /* Selection is sequential so the outcome never depends on which response
       arrives first. The network work runs ahead of it instead: a package's
       dependency list, and the metadata of everything on it, starts loading
       the moment the package is selected. */
    async Task Visit(IReadOnlyList<PackageRequest> requests) {
      foreach (PackageRequest request in requests) {
        if (filter_.IsAllowed(request)) {
          registry_.Prefetch(request.name, token);
        }
      }

      foreach (PackageRequest request in requests) {
        string key =
          $"{registry_.NormalizeName(request.name)}@{request.spec}{(request.optional ? "?" : "")}";
        if (!seen.Add(key)) {
          continue;
        }

        if (!filter_.IsAllowed(request)) {
          result.blocked.Add(request);
          continue;
        }

        ResolvedPackage? package =
          await Select(request, selected, result, token);
        if (package != null) {
          result.packages.Add(package);
          pending.Enqueue((package, LoadDependencies(package, token)));
        }
      }
    }

    await Visit(roots);
    while (pending.Count > 0) {
      (ResolvedPackage package,
        Task<IReadOnlyList<PackageRequest>> loading) = pending.Dequeue();
      IReadOnlyList<PackageRequest> dependencies;
      try {
        dependencies = await loading;
      } catch (Exception e) when (e is not OperationCanceledException) {
        result.errors.Add(
          $"{package}: could not read dependencies: {e.Message}"
        );
        continue;
      }

      await Visit(dependencies);
    }

    return result;
  }

  private async Task<IReadOnlyList<PackageRequest>> LoadDependencies(
    ResolvedPackage package, CancellationToken token) {
    IReadOnlyList<PackageRequest> dependencies =
      await registry_.GetDependencies(package, token);
    foreach (PackageRequest dependency in dependencies) {
      if (filter_.IsAllowed(dependency)) {
        registry_.Prefetch(dependency.name, token);
      }
    }

    return dependencies;
  }

  /// <summary>
  ///   Selects a version for a request.
  /// </summary>
  /// <returns>The package if it was newly selected; null if it was reused, skipped or failed.</returns>
  private async Task<ResolvedPackage?> Select(PackageRequest request,
    Dictionary<string, List<ResolvedPackage>> selected, ResolveResult result,
    CancellationToken token) {
    string name = registry_.NormalizeName(request.name);
    if (!selected.TryGetValue(name, out List<ResolvedPackage>? versions)) {
      versions = new List<ResolvedPackage>();
      selected[name] = versions;
    }

    ResolvedPackage? package;
    try {
      package = await registry_.Resolve(request, versions, token);
    } catch (Exception e) when (e is not OperationCanceledException) {
      result.errors.Add($"{request}: {e.Message}");
      return null;
    }

    if (package == null) {
      result.skipped.Add(request);
      return null;
    }

    if (versions.Any(v => v.version == package.version)) {
      return null;
    }

    versions.Add(package);
    return package;
  }
}
