// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;

namespace MiniSync.Core;

/// <summary>
///   Decides which packages may be synced, from <c>allow</c> and <c>deny</c>
///   glob lists (<c>*</c> matches anything, <c>?</c> one character). Matching
///   is case-insensitive.
/// </summary>
public class PackageFilter {
  private readonly List<Regex> allow_;
  private readonly List<Regex> deny_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="PackageFilter" /> class.
  /// </summary>
  /// <param name="allow">If not empty, only dependencies matching one of these are synced.</param>
  /// <param name="deny">Packages matching any of these are never synced.</param>
  public PackageFilter(IEnumerable<string> allow, IEnumerable<string> deny) {
    allow_ = allow.Select(ToRegex).ToList();
    deny_ = deny.Select(ToRegex).ToList();
  }

  /// <summary>
  ///   Checks whether a request may be synced. Roots are listed in the config
  ///   on purpose, so only <c>deny</c> applies to them.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <returns>True if the package may be synced; otherwise, false.</returns>
  public bool IsAllowed(PackageRequest request) {
    if (deny_.Any(r => r.IsMatch(request.name))) {
      return false;
    }

    return request.root ||
           allow_.Count == 0 ||
           allow_.Any(r => r.IsMatch(request.name));
  }

  private static Regex ToRegex(string glob) {
    string pattern = Regex.Escape(glob.Trim())
                          .Replace(@"\*", ".*")
                          .Replace(@"\?", ".");
    return new Regex(
      $"^{pattern}$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );
  }
}
