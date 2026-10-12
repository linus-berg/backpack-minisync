// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MiniSync.Tests;

/// <summary>
///   An in-memory web server: URLs map to canned bodies, everything else is a
///   404, and every request is recorded.
/// </summary>
public class FakeHttp : HttpMessageHandler {
  private readonly ConcurrentDictionary<string, byte[]> routes_ = new();

  /// <summary>
  ///   Gets the URLs requested so far.
  /// </summary>
  public ConcurrentQueue<string> requests { get; } = new();

  /// <summary>
  ///   Serves a body at a URL.
  /// </summary>
  public FakeHttp Add(string url, byte[] body) {
    routes_[url] = body;
    return this;
  }

  /// <summary>
  ///   Serves a string at a URL.
  /// </summary>
  public FakeHttp Add(string url, string body) {
    return Add(url, Encoding.UTF8.GetBytes(body));
  }

  /// <summary>
  ///   Serves an object as JSON at a URL.
  /// </summary>
  public FakeHttp AddJson(string url, object body) {
    return Add(url, JsonSerializer.Serialize(body));
  }

  /// <inheritdoc />
  protected override Task<HttpResponseMessage> SendAsync(
    HttpRequestMessage request, CancellationToken token) {
    string url = request.RequestUri!.OriginalString;
    requests.Enqueue(url);
    HttpResponseMessage response = routes_.TryGetValue(url, out byte[]? body)
                                     ? new HttpResponseMessage(HttpStatusCode.OK) {
                                       Content = new ByteArrayContent(body)
                                     }
                                     : new HttpResponseMessage(
                                       HttpStatusCode.NotFound
                                     );
    return Task.FromResult(response);
  }

  /// <summary>
  ///   Computes the SRI sha512 string of some bytes.
  /// </summary>
  public static string Sri(byte[] data) {
    return $"sha512-{Convert.ToBase64String(SHA512.HashData(data))}";
  }

  /// <summary>
  ///   Builds a minimal .nupkg: a zip holding just a nuspec.
  /// </summary>
  public static byte[] Nupkg(string id, string version) {
    using MemoryStream stream = new();
    using (ZipArchive zip = new(stream, ZipArchiveMode.Create, true)) {
      ZipArchiveEntry entry = zip.CreateEntry($"{id}.nuspec");
      using StreamWriter writer = new(entry.Open());
      writer.Write(Nuspec(id, version, ""));
    }

    return stream.ToArray();
  }

  /// <summary>
  ///   Builds a nuspec with the given dependencies element content.
  /// </summary>
  public static string Nuspec(string id, string version, string dependencies) {
    return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>test</authors>
                <description>test</description>
                <dependencies>{dependencies}</dependencies>
              </metadata>
            </package>
            """;
  }
}
