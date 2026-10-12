// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Nuget.Models;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace MiniSync.Nuget;

/// <summary>
///   Resolves NuGet packages the way restore does: a dependency gets the
///   lowest version that satisfies it, and only the dependency groups that
///   apply to the configured target frameworks are followed. Everything is
///   read from the feed's flat container, so each package version costs one
///   small <c>.nuspec</c> request instead of a full registration page.
/// </summary>
public class NugetRegistry : IRegistry {
  private const string C_BASE_ADDRESS_TYPE_ = "PackageBaseAddress/3.0.0";
  private readonly NugetConfig config_;
  private readonly Fetcher fetcher_;
  private readonly List<NuGetFramework> frameworks_;
  private readonly object lock_ = new();

  private readonly ConcurrentDictionary<string, Lazy<Task<NuspecReader>>>
    nuspecs_ = new();

  private readonly ConcurrentDictionary<string, Lazy<Task<List<NuGetVersion>?>>>
    versions_ = new();

  private Task<string>? base_address_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="NugetRegistry" /> class.
  /// </summary>
  /// <param name="config">The NuGet settings.</param>
  /// <param name="fetcher">The HTTP fetcher.</param>
  /// <exception cref="ConfigException">Thrown when a target framework is not recognised.</exception>
  public NugetRegistry(NugetConfig config, Fetcher fetcher) {
    config_ = config;
    fetcher_ = fetcher;
    frameworks_ = config.target_frameworks.Select(ParseFramework).ToList();
  }

  /// <inheritdoc />
  public string module => "nuget";

  /// <inheritdoc />
  public string NormalizeName(string name) {
    return name.ToLowerInvariant();
  }

  /// <inheritdoc />
  public void Prefetch(string name, CancellationToken token) {
    _ = GetVersions(name, token);
  }

  /// <inheritdoc />
  public async Task<ResolvedPackage?> Resolve(PackageRequest request,
                                              IReadOnlyList<ResolvedPackage>
                                                selected,
                                              CancellationToken token) {
    /* The id ends up in a URL and a path, so only real ids get that far. */
    if (!PackageIdValidator.IsValidPackageId(request.name)) {
      throw new ResolveException($"'{request.name}' is not a valid package id");
    }

    List<NuGetVersion> available = await GetVersions(request.name, token) ??
                                   throw new ResolveException(
                                     $"not found in {config_.source}"
                                   );
    NuGetVersion version = request.root
                             ? SelectRoot(request.spec, available)
                             : SelectDependency(request.spec, selected, available);
    string url = GetPackageUrl(
      await GetBaseAddress(token),
      request.name,
      version.ToNormalizedString()
    );
    return new ResolvedPackage {
      name = request.name,
      version = version.ToNormalizedString(),
      url = url,
      path = Storage.GetArtifactPath(module, url),
      required_by = request.required_by
    };
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<PackageRequest>> GetDependencies(
    ResolvedPackage package, CancellationToken token) {
    NuspecReader nuspec = await GetNuspec(package, token);
    List<PackageDependencyGroup> groups =
      nuspec.GetDependencyGroups().ToList();
    IEnumerable<PackageDependencyGroup> followed =
      frameworks_.Count == 0 ? groups : GetNearestGroups(groups);
    string parent = package.ToString();
    return followed.SelectMany(g => g.Packages)
                   .Select(
                     d => new PackageRequest {
                       name = d.Id,
                       spec = d.VersionRange.ToNormalizedString(),
                       required_by = parent
                     }
                   )
                   .ToList();
  }

  /// <inheritdoc />
  public async Task Verify(string file, ResolvedPackage package,
                           CancellationToken token) {
    /* Opening the archive proves it is a complete zip, and its nuspec has to
       name the package that was asked for. The stream is ours so the handle
       is released even when the archive is corrupt; on Windows a leaked
       handle would stop the download from being cleaned up. */
    await using FileStream stream = File.OpenRead(file);
    using PackageArchiveReader reader = new(stream, true);
    PackageIdentity identity = await reader.GetIdentityAsync(token);
    if (!identity.Id.Equals(package.name, StringComparison.OrdinalIgnoreCase) ||
        !identity.Version.Equals(NuGetVersion.Parse(package.version))) {
      throw new InvalidDataException(
        $"the download contains {identity.Id} {identity.Version}"
      );
    }
  }

  /// <summary>
  ///   Selects the version of a root. A bare version is exact, a floating
  ///   version (<c>13.*</c>) or range (<c>[13.0,14.0)</c>) takes the highest
  ///   match, and <c>latest</c> or <c>*</c> the highest stable version.
  /// </summary>
  private static NuGetVersion SelectRoot(string spec,
                                         List<NuGetVersion> available) {
    string trimmed = spec.Trim();
    NuGetVersion? latest =
      available.Where(v => !v.IsPrerelease).Max() ?? available.Max();
    if (trimmed is "" or "*" or "latest") {
      return latest ?? throw new ResolveException("no versions are published");
    }

    if (NuGetVersion.TryParse(trimmed, out NuGetVersion? exact)) {
      return available.FirstOrDefault(v => v.Equals(exact)) ??
             throw new ResolveException(
               $"version {trimmed} is not published (latest is {latest}); " +
               $"use a floating version such as {exact.Major}.{exact.Minor}.* for the newest match"
             );
    }

    if (!VersionRange.TryParse(trimmed, true, out VersionRange? range)) {
      throw new ResolveException($"'{spec}' is not a version or version range");
    }

    NuGetVersion? match = range.IsFloating
                            ? range.FindBestMatch(available)
                            : available.Where(
                                         v => range.Satisfies(v) &&
                                              (AllowsPrerelease(range) ||
                                               !v.IsPrerelease)
                                       )
                                       .Max();
    return match ??
           throw new ResolveException($"no published version matches {spec}");
  }

  /// <summary>
  ///   Selects the version of a dependency: a version that is already
  ///   selected if one fits, otherwise the lowest that does. This is the
  ///   version restore will pick, and it never changes once published, so
  ///   syncs stay small and repeatable.
  /// </summary>
  private static NuGetVersion SelectDependency(string spec,
                                               IReadOnlyList<ResolvedPackage>
                                                 selected,
                                               List<NuGetVersion> available) {
    if (!VersionRange.TryParse(spec, out VersionRange? range)) {
      throw new ResolveException($"'{spec}' is not a version range");
    }

    bool prerelease = AllowsPrerelease(range);
    NuGetVersion? reused = selected.Select(p => NuGetVersion.Parse(p.version))
                                   .Where(
                                     v => range.Satisfies(v) &&
                                          (prerelease || !v.IsPrerelease)
                                   )
                                   .Min();
    if (reused != null) {
      return reused;
    }

    return range.FindBestMatch(
             available.Where(v => prerelease || !v.IsPrerelease)
           ) ??
           throw new ResolveException(
             $"no published version matches {range.PrettyPrint()}"
           );
  }

  /// <summary>
  ///   NuGet only resolves a dependency to a prerelease when the range itself
  ///   names one.
  /// </summary>
  private static bool AllowsPrerelease(VersionRange range) {
    return range.MinVersion?.IsPrerelease == true ||
           range.MaxVersion?.IsPrerelease == true;
  }

  /// <summary>
  ///   Picks, for each target framework, the dependency group restore would
  ///   use. A package without a compatible group contributes nothing.
  /// </summary>
  private IEnumerable<PackageDependencyGroup> GetNearestGroups(
    List<PackageDependencyGroup> groups) {
    HashSet<PackageDependencyGroup> nearest = new(ReferenceEqualityComparer.Instance);
    foreach (NuGetFramework framework in frameworks_) {
      PackageDependencyGroup? group = NuGetFrameworkUtility.GetNearest(
        groups,
        framework,
        g => g.TargetFramework
      );
      if (group != null) {
        nearest.Add(group);
      }
    }

    return groups.Where(nearest.Contains);
  }

  private Task<string> GetBaseAddress(CancellationToken token) {
    lock (lock_) {
      return base_address_ ??= DiscoverBaseAddress(token);
    }
  }

  private async Task<string> DiscoverBaseAddress(CancellationToken token) {
    ServiceIndex index =
      await fetcher_.GetJson<ServiceIndex>(config_.source, null, token) ??
      throw new ResolveException($"{config_.source} not found");
    string address =
      index.resources.FirstOrDefault(r => r.IsType(C_BASE_ADDRESS_TYPE_))?.id ??
      throw new ResolveException(
        $"{config_.source} is not a NuGet v3 feed with a flat container"
      );
    return address.EndsWith('/') ? address : $"{address}/";
  }

  private Task<List<NuGetVersion>?> GetVersions(string id,
                                                CancellationToken token) {
    return versions_.GetOrAdd(
                      NormalizeName(id),
                      key => new Lazy<Task<List<NuGetVersion>?>>(
                        () => FetchVersions(key, token)
                      )
                    )
                    .Value;
  }

  private async Task<List<NuGetVersion>?> FetchVersions(string id,
    CancellationToken token) {
    string base_address = await GetBaseAddress(token);
    VersionIndex? index = await fetcher_.GetJson<VersionIndex>(
                            $"{base_address}{id}/index.json",
                            null,
                            token
                          );
    if (index == null) {
      return null;
    }

    List<NuGetVersion> versions = new();
    foreach (string version in index.versions) {
      if (NuGetVersion.TryParse(version, out NuGetVersion? parsed)) {
        versions.Add(parsed);
      }
    }

    return versions;
  }

  private Task<NuspecReader> GetNuspec(ResolvedPackage package,
                                       CancellationToken token) {
    string id = NormalizeName(package.name);
    string version = package.version.ToLowerInvariant();
    return nuspecs_.GetOrAdd(
                     $"{id}/{version}",
                     _ => new Lazy<Task<NuspecReader>>(
                       () => FetchNuspec(id, version, token)
                     )
                   )
                   .Value;
  }

  private async Task<NuspecReader> FetchNuspec(string id, string version,
                                               CancellationToken token) {
    string base_address = await GetBaseAddress(token);
    byte[] xml = await fetcher_.GetBytes(
                   $"{base_address}{id}/{version}/{id}.nuspec",
                   token
                 ) ??
                 throw new ResolveException($"{id} {version} has no nuspec");
    /* NuspecReader's stream constructor parses with DTDs prohibited. */
    return new NuspecReader(new MemoryStream(xml));
  }

  /// <summary>
  ///   Builds a flat container URL. Like backpack, the whole path is
  ///   lowercase, which is also how it ends up on disk.
  /// </summary>
  private static string GetPackageUrl(string base_address, string id,
                                      string version) {
    string path = $"{id}/{version}/{id}.{version}.nupkg".ToLowerInvariant();
    return $"{base_address}{path}";
  }

  private static NuGetFramework ParseFramework(string framework) {
    NuGetFramework parsed = NuGetFramework.Parse(framework);
    if (parsed.IsUnsupported) {
      throw new ConfigException(
        $"nuget.target_frameworks: '{framework}' is not a target framework"
      );
    }

    if (!parsed.HasPlatform ||
        parsed.PlatformVersion != FrameworkConstants.EmptyVersion) {
      return parsed;
    }

    /* A project's net8.0-windows is restored as net8.0-windows7.0. Taken
       literally it would match no windows dependency group at all. */
    if (parsed.Platform.Equals("windows", StringComparison.OrdinalIgnoreCase)) {
      return new NuGetFramework(
        parsed.Framework,
        parsed.Version,
        parsed.Platform,
        new Version(7, 0)
      );
    }

    Log.Warn(
      $"nuget.target_frameworks: '{framework}' has no platform version, so " +
      $"no {parsed.Platform} specific dependency group can match; write it " +
      "the way restore sees it, e.g. net8.0-android34.0"
    );
    return parsed;
  }
}
