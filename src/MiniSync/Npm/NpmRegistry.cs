// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using MiniSync.Configuration;
using MiniSync.Core;
using MiniSync.Npm.Models;
using Semver;

namespace MiniSync.Npm;

/// <summary>
///   Resolves npm packages the way the npm CLI picks versions, so the offline
///   registry ends up holding what <c>npm install</c> will ask it for.
/// </summary>
public class NpmRegistry : IRegistry {
  /* The abbreviated document; npm itself sends this exact header. */
  private const string C_ACCEPT_ =
    "application/vnd.npm.install-v1+json; q=1.0, application/json; q=0.8, */*";

  private static readonly string[] S_HASH_STRENGTH_ =
    { "sha512", "sha384", "sha256", "sha1" };

  private readonly ConcurrentDictionary<string, Lazy<Task<Metadata?>>> cache_ =
    new();

  private readonly NpmConfig config_;
  private readonly Fetcher fetcher_;
  private readonly string registry_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="NpmRegistry" /> class.
  /// </summary>
  /// <param name="config">The npm settings.</param>
  /// <param name="fetcher">The HTTP fetcher.</param>
  public NpmRegistry(NpmConfig config, Fetcher fetcher) {
    config_ = config;
    fetcher_ = fetcher;
    registry_ = config.registry.EndsWith('/')
                  ? config.registry
                  : $"{config.registry}/";
  }

  /// <inheritdoc />
  public string module => "npm";

  /// <inheritdoc />
  public string NormalizeName(string name) {
    return name;
  }

  /// <inheritdoc />
  public void Prefetch(string name, CancellationToken token) {
    _ = GetMetadata(name, token);
  }

  /// <inheritdoc />
  public async Task<ResolvedPackage?> Resolve(PackageRequest request,
                                              IReadOnlyList<ResolvedPackage>
                                                selected,
                                              CancellationToken token) {
    Metadata metadata = await GetMetadata(request.name, token) ??
                        throw new ResolveException("not found in the registry");
    string version = SelectVersion(metadata, request, selected);
    Package package = metadata.versions[version];
    if (request.optional && !IsPlatformSupported(package)) {
      Log.Debug($"  skipped {request.name}@{version}, not for the configured platforms");
      return null;
    }

    string tarball = package.dist?.tarball ??
                     throw new ResolveException($"{version} has no tarball");
    return new ResolvedPackage {
      name = request.name,
      version = version,
      url = tarball,
      path = Storage.GetArtifactPath(module, tarball),
      integrity = GetIntegrity(package.dist),
      required_by = request.required_by
    };
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<PackageRequest>> GetDependencies(
    ResolvedPackage package, CancellationToken token) {
    Metadata metadata = await GetMetadata(package.name, token) ??
                        throw new ResolveException("not found in the registry");
    Package manifest = metadata.versions[package.version];
    string parent = package.ToString();
    List<PackageRequest> requests =
      manifest.has_shrinkwrap.ValueKind == JsonValueKind.True
        ? await GetLockedDependencies(package, token) ??
          GetDeclaredDependencies(manifest, parent)
        : GetDeclaredDependencies(manifest, parent);

    if (config_.peer_dependencies) {
      foreach ((string name, string spec) in GetSpecs(manifest.peer_dependencies)) {
        /* npm never installs an optional peer on its own. */
        if (!IsOptionalPeer(manifest, name)) {
          requests.Add(CreateRequest(name, spec, parent, false));
        }
      }
    }

    return requests;
  }

  /// <inheritdoc />
  public async Task Verify(string file, ResolvedPackage package,
                           CancellationToken token) {
    await using FileStream stream = File.OpenRead(file);
    await VerifyIntegrity(stream, package, token);
  }

  private List<PackageRequest> GetDeclaredDependencies(Package manifest,
                                                       string parent) {
    HashSet<string> bundled = GetBundled(manifest);
    Dictionary<string, string> optional =
      GetSpecs(manifest.optional_dependencies);
    List<PackageRequest> requests = new();

    foreach ((string name, string spec) in GetSpecs(manifest.dependencies)) {
      /* Optional dependencies are duplicated into dependencies on publish;
         they are handled below so they keep their optional flag. */
      if (!bundled.Contains(name) && !optional.ContainsKey(name)) {
        requests.Add(CreateRequest(name, spec, parent, false));
      }
    }

    if (config_.optional_dependencies) {
      foreach ((string name, string spec) in optional) {
        if (!bundled.Contains(name)) {
          requests.Add(CreateRequest(name, spec, parent, true));
        }
      }
    }

    return requests;
  }

  /// <summary>
  ///   Gets the dependencies locked by the npm-shrinkwrap.json in a package's
  ///   tarball. npm installs those exact versions rather than resolving the
  ///   declared ranges, so they are what the offline side will ask for. The
  ///   tarball is fetched here and again by the download; such packages are
  ///   rare enough for that not to matter.
  /// </summary>
  /// <returns>The locked dependencies, or null if the tarball has no shrinkwrap after all.</returns>
  private async Task<List<PackageRequest>?> GetLockedDependencies(
    ResolvedPackage package, CancellationToken token) {
    byte[] tarball = await fetcher_.GetBytes(package.url, token) ??
                     throw new ResolveException($"{package.url} not found");
    using (MemoryStream stream = new(tarball)) {
      await VerifyIntegrity(stream, package, token);
    }

    using JsonDocument? shrinkwrap = Shrinkwrap.Read(tarball);
    if (shrinkwrap == null) {
      Log.Warn($"{package} is marked as shrinkwrapped but ships no npm-shrinkwrap.json");
      return null;
    }

    string parent = package.ToString();
    List<PackageRequest> locked = Shrinkwrap.GetLocked(shrinkwrap.RootElement)
                                            .Select(
                                              l => CreateRequest(
                                                l.name,
                                                l.spec,
                                                parent,
                                                l.optional
                                              )
                                            )
                                            .ToList();
    Log.Debug($"  {package} ships a shrinkwrap locking {locked.Count} packages");
    return locked;
  }

  private static async Task VerifyIntegrity(Stream stream,
                                            ResolvedPackage package,
                                            CancellationToken token) {
    if (string.IsNullOrEmpty(package.integrity)) {
      Log.Debug($"  {package} publishes no integrity, not verified");
      return;
    }

    (string algorithm, string expected) = GetStrongestHash(package.integrity);
    byte[] actual = algorithm switch {
      "sha512" => await SHA512.HashDataAsync(stream, token),
      "sha384" => await SHA384.HashDataAsync(stream, token),
      "sha256" => await SHA256.HashDataAsync(stream, token),
      _ => await SHA1.HashDataAsync(stream, token)
    };
    if (Convert.ToBase64String(actual) != expected) {
      throw new InvalidDataException(
        $"{algorithm} integrity check failed, the download is corrupt"
      );
    }
  }

  /// <summary>
  ///   Picks a version the way npm-pick-manifest does: the <c>latest</c> tag
  ///   if it satisfies the spec, otherwise the highest satisfying version that
  ///   is not deprecated. A dependency first reuses any version already
  ///   selected that satisfies it, the same deduplication npm performs.
  /// </summary>
  private static string SelectVersion(Metadata metadata,
                                      PackageRequest request,
                                      IReadOnlyList<ResolvedPackage> selected) {
    string spec = request.spec.Trim();
    if (metadata.dist_tags.TryGetValue(spec, out string? tagged)) {
      return metadata.versions.ContainsKey(tagged)
               ? tagged
               : throw new ResolveException(
                 $"tag '{spec}' points at {tagged}, which is not published"
               );
    }

    if (!SemVersionRange.TryParseNpm(
          spec.Length == 0 ? "*" : spec,
          out SemVersionRange? range
        )) {
      throw new ResolveException(
        $"'{request.spec}' is not a version, range or tag; only registry packages can be synced"
      );
    }

    if (!request.root) {
      string? reused = Matching(selected.Select(p => p.version), range)
                       .OrderByDescending(
                         v => v.semver,
                         SemVersion.PrecedenceComparer
                       )
                       .Select(v => v.version)
                       .FirstOrDefault();
      if (reused != null) {
        return reused;
      }
    }

    if (metadata.dist_tags.TryGetValue("latest", out string? latest) &&
        metadata.versions.TryGetValue(latest, out Package? latest_package) &&
        ParseVersion(latest) is { } latest_version &&
        range.Contains(latest_version) &&
        !IsDeprecated(latest_package)) {
      return latest;
    }

    return Matching(metadata.versions.Keys, range)
           .OrderBy(v => IsDeprecated(metadata.versions[v.version]))
           .ThenByDescending(v => v.semver, SemVersion.PrecedenceComparer)
           .Select(v => v.version)
           .FirstOrDefault() ??
           throw new ResolveException($"no published version matches '{request.spec}'");
  }

  private static IEnumerable<(string version, SemVersion semver)> Matching(
    IEnumerable<string> versions, SemVersionRange range) {
    foreach (string version in versions) {
      SemVersion? semver = ParseVersion(version);
      if (semver != null && range.Contains(semver)) {
        yield return (version, semver);
      }
    }
  }

  private Task<Metadata?> GetMetadata(string name, CancellationToken token) {
    /* Scoped names are requested as @scope%2fname, like the npm CLI does. */
    return cache_.GetOrAdd(
                   name,
                   n => new Lazy<Task<Metadata?>>(
                     () => fetcher_.GetJson<Metadata>(
                       $"{registry_}{n.Replace("/", "%2f")}",
                       C_ACCEPT_,
                       token
                     )
                   )
                 )
                 .Value;
  }

  private bool IsPlatformSupported(Package package) {
    return Matches(GetStrings(package.os), config_.os) &&
           Matches(GetStrings(package.cpu), config_.cpu);
  }

  /// <summary>
  ///   Checks a package's <c>os</c> or <c>cpu</c> list, which may contain
  ///   negations such as <c>!win32</c>, against the configured targets.
  /// </summary>
  private static bool Matches(List<string> declared, List<string> targets) {
    if (declared.Count == 0 || targets.Count == 0) {
      return true;
    }

    List<string> allowed = declared.Where(d => !d.StartsWith('!')).ToList();
    return targets.Any(
      target =>
        !declared.Contains($"!{target}", StringComparer.OrdinalIgnoreCase) &&
        (allowed.Count == 0 ||
         allowed.Contains(target, StringComparer.OrdinalIgnoreCase))
    );
  }

  /// <summary>
  ///   Turns an <c>npm:</c> alias (<c>"string-width-cjs": "npm:string-width@^4.2.0"</c>)
  ///   into a request for the real package.
  /// </summary>
  private static PackageRequest CreateRequest(string name, string spec,
                                              string parent, bool optional) {
    if (spec.StartsWith("npm:", StringComparison.Ordinal)) {
      string target = spec[4..];
      int at = target.LastIndexOf('@');
      name = at > 0 ? target[..at] : target;
      spec = at > 0 ? target[(at + 1)..] : "*";
    }

    return new PackageRequest {
      name = name,
      spec = spec,
      required_by = parent,
      optional = optional
    };
  }

  private static HashSet<string> GetBundled(Package package) {
    JsonElement bundle =
      package.bundle_dependencies.ValueKind != JsonValueKind.Undefined
        ? package.bundle_dependencies
        : package.bundled_dependencies;
    if (bundle.ValueKind == JsonValueKind.True) {
      return GetSpecs(package.dependencies).Keys.ToHashSet();
    }

    return GetStrings(bundle).ToHashSet();
  }

  private static bool IsOptionalPeer(Package package, string name) {
    return package.peer_dependencies_meta != null &&
           package.peer_dependencies_meta.TryGetValue(
             name,
             out JsonElement meta
           ) &&
           meta.ValueKind == JsonValueKind.Object &&
           meta.TryGetProperty("optional", out JsonElement optional) &&
           optional.ValueKind == JsonValueKind.True;
  }

  private static bool IsDeprecated(Package package) {
    return package.deprecated.ValueKind == JsonValueKind.String &&
           package.deprecated.GetString()!.Length > 0;
  }

  private static SemVersion? ParseVersion(string version) {
    return SemVersion.TryParse(
             version,
             SemVersionStyles.Strict,
             out SemVersion? parsed
           )
             ? parsed
             : null;
  }

  private static Dictionary<string, string> GetSpecs(
    Dictionary<string, JsonElement>? dependencies) {
    Dictionary<string, string> specs = new();
    if (dependencies == null) {
      return specs;
    }

    foreach (KeyValuePair<string, JsonElement> dependency in dependencies) {
      if (dependency.Value.ValueKind == JsonValueKind.String) {
        specs[dependency.Key] = dependency.Value.GetString()!;
      }
    }

    return specs;
  }

  private static List<string> GetStrings(JsonElement element) {
    return element.ValueKind switch {
      JsonValueKind.String => new List<string> { element.GetString()! },
      JsonValueKind.Array => element.EnumerateArray()
                                    .Where(e => e.ValueKind == JsonValueKind.String)
                                    .Select(e => e.GetString()!)
                                    .ToList(),
      _ => new List<string>()
    };
  }

  private static string? GetIntegrity(Distribution? dist) {
    if (!string.IsNullOrEmpty(dist?.integrity)) {
      return dist.integrity;
    }

    if (string.IsNullOrEmpty(dist?.shasum)) {
      return null;
    }

    return $"sha1-{Convert.ToBase64String(Convert.FromHexString(dist.shasum))}";
  }

  /// <summary>
  ///   Picks the strongest hash out of a Subresource Integrity string, which
  ///   may list several (<c>sha512-... sha1-...</c>).
  /// </summary>
  private static (string algorithm, string digest) GetStrongestHash(
    string integrity) {
    List<(string algorithm, string digest)> hashes = new();
    foreach (string entry in integrity.Split(
               ' ',
               StringSplitOptions.RemoveEmptyEntries
             )) {
      int dash = entry.IndexOf('-');
      if (dash <= 0) {
        continue;
      }

      string algorithm = entry[..dash].ToLowerInvariant();
      /* SRI allows options after a '?', none of which matter here. */
      string digest = entry[(dash + 1)..].Split('?')[0];
      if (S_HASH_STRENGTH_.Contains(algorithm)) {
        hashes.Add((algorithm, digest));
      }
    }

    if (hashes.Count == 0) {
      throw new InvalidDataException(
        $"unsupported integrity '{integrity}'"
      );
    }

    return hashes.OrderBy(h => Array.IndexOf(S_HASH_STRENGTH_, h.algorithm))
                 .First();
  }
}
