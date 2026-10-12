// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Text.Json;

namespace MiniSync.Core;

/// <summary>
///   HTTP access shared by every registry. Caps the number of requests in
///   flight and retries transient failures with exponential backoff.
/// </summary>
public sealed class Fetcher : IDisposable {
  private const int C_ATTEMPTS_ = 4;
  private const int C_BUFFER_SIZE_ = 81920;
  private readonly HttpClient client_;
  private readonly SemaphoreSlim gate_;

  /// <summary>
  ///   Initializes a new instance of the <see cref="Fetcher" /> class.
  /// </summary>
  /// <param name="concurrency">The maximum number of concurrent requests.</param>
  /// <param name="handler">An optional handler, used by tests to fake the network.</param>
  public Fetcher(int concurrency, HttpMessageHandler? handler = null) {
    client_ = new HttpClient(handler ?? CreateHandler()) {
      Timeout = Timeout.InfiniteTimeSpan
    };
    client_.DefaultRequestHeaders.UserAgent.ParseAdd(
      $"backpack-minisync/{typeof(Fetcher).Assembly.GetName().Version?.ToString(3)}"
    );
    gate_ = new SemaphoreSlim(concurrency);
  }

  /* There is deliberately no limit on how long a transfer may take, only on
     how long the server may stay silent, so large packages over slow links
     still complete while a stalled connection is retried. Tests shorten these. */
  internal TimeSpan headers_timeout { get; init; } = TimeSpan.FromSeconds(100);
  internal TimeSpan idle_timeout { get; init; } = TimeSpan.FromSeconds(60);
  internal TimeSpan retry_delay { get; init; } = TimeSpan.FromSeconds(1);

  /// <inheritdoc />
  public void Dispose() {
    client_.Dispose();
    gate_.Dispose();
  }

  /// <summary>
  ///   Gets a JSON document and deserializes it.
  /// </summary>
  /// <typeparam name="T">The type to deserialize into.</typeparam>
  /// <param name="url">The URL to fetch.</param>
  /// <param name="accept">An optional Accept header.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>The deserialized document, or null when the server answers 404.</returns>
  public async Task<T?> GetJson<T>(string url, string? accept,
                                   CancellationToken token) where T : class {
    return await Send(
             url,
             accept,
             async (response, t) => {
               using MemoryStream body = await ReadBody(response, t);
               return JsonSerializer.Deserialize<T>(body);
             },
             true,
             token
           );
  }

  /// <summary>
  ///   Gets a document as raw bytes, leaving decoding to the caller (an XML
  ///   parser has to see the encoding the document declares).
  /// </summary>
  /// <param name="url">The URL to fetch.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>The body, or null when the server answers 404.</returns>
  public async Task<byte[]?> GetBytes(string url, CancellationToken token) {
    return await Send(
             url,
             null,
             async (response, t) => {
               using MemoryStream body = await ReadBody(response, t);
               return body.ToArray();
             },
             true,
             token
           );
  }

  /// <summary>
  ///   Downloads a file. The destination is truncated on every attempt so a
  ///   retry never appends to a partial body.
  /// </summary>
  /// <param name="url">The URL to fetch.</param>
  /// <param name="destination">The file to write.</param>
  /// <param name="token">The cancellation token.</param>
  /// <returns>The number of bytes written.</returns>
  public async Task<long> Download(string url, string destination,
                                   CancellationToken token) {
    long? bytes = await Send<long?>(
                    url,
                    null,
                    async (response, t) => {
                      await using Stream body =
                        await response.Content.ReadAsStreamAsync(t);
                      await using FileStream file = new(
                        destination,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        C_BUFFER_SIZE_,
                        true
                      );
                      return await CopyWithIdleTimeout(body, file, t);
                    },
                    false,
                    token
                  );
    return bytes!.Value;
  }

  private async Task<MemoryStream> ReadBody(HttpResponseMessage response,
                                           CancellationToken token) {
    await using Stream body = await response.Content.ReadAsStreamAsync(token);
    MemoryStream memory = new();
    await CopyWithIdleTimeout(body, memory, token);
    memory.Position = 0;
    return memory;
  }

  /// <summary>
  ///   Copies a response body, giving up if no data arrives for a while, so a
  ///   stalled connection is retried instead of hanging the sync forever.
  /// </summary>
  private async Task<long> CopyWithIdleTimeout(Stream source,
    Stream destination, CancellationToken token) {
    byte[] buffer = new byte[C_BUFFER_SIZE_];
    long total = 0;
    using CancellationTokenSource idle =
      CancellationTokenSource.CreateLinkedTokenSource(token);
    while (true) {
      idle.CancelAfter(idle_timeout);
      int read = await source.ReadAsync(buffer, idle.Token);
      if (read == 0) {
        return total;
      }

      await destination.WriteAsync(buffer.AsMemory(0, read), token);
      total += read;
    }
  }

  private async Task<T?> Send<T>(string url, string? accept,
                                 Func<HttpResponseMessage, CancellationToken,
                                   Task<T>> read, bool null_on_not_found,
                                 CancellationToken token) {
    for (int attempt = 1;; attempt++) {
      string failure;
      await gate_.WaitAsync(token);
      try {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        if (accept != null) {
          request.Headers.TryAddWithoutValidation("Accept", accept);
        }

        using CancellationTokenSource headers =
          CancellationTokenSource.CreateLinkedTokenSource(token);
        headers.CancelAfter(headers_timeout);
        using HttpResponseMessage response = await client_.SendAsync(
                                               request,
                                               HttpCompletionOption
                                                 .ResponseHeadersRead,
                                               headers.Token
                                             );
        headers.CancelAfter(Timeout.InfiniteTimeSpan);
        if (response.StatusCode == HttpStatusCode.NotFound &&
            null_on_not_found) {
          return default;
        }

        if (!IsTransient(response.StatusCode) || attempt == C_ATTEMPTS_) {
          if (!response.IsSuccessStatusCode) {
            throw new HttpRequestException(
              $"GET {url} returned {(int)response.StatusCode} {response.ReasonPhrase}",
              null,
              response.StatusCode
            );
          }

          return await read(response, token);
        }

        failure = $"{(int)response.StatusCode} {response.ReasonPhrase}";
      } catch (Exception e) when (attempt < C_ATTEMPTS_ &&
                                  IsTransient(e, token)) {
        failure = e.Message;
      } catch (OperationCanceledException e) when (!token.IsCancellationRequested) {
        /* Out of attempts. Callers treat a cancellation as Ctrl+C, so a
           timeout must not look like one. */
        throw new TimeoutException($"GET {url} timed out", e);
      } finally {
        gate_.Release();
      }

      TimeSpan delay = retry_delay * Math.Pow(2, attempt - 1);
      Log.Debug($"GET {url} failed ({failure}), retrying in {delay.TotalSeconds:0.#}s");
      await Task.Delay(delay, token);
    }
  }

  private static bool IsTransient(HttpStatusCode status) {
    return status == HttpStatusCode.RequestTimeout ||
           status == HttpStatusCode.TooManyRequests ||
           (int)status >= 500;
  }

  private static bool IsTransient(Exception e, CancellationToken token) {
    return e switch {
      HttpRequestException { StatusCode: not null } => false,
      HttpRequestException => true,
      IOException => true,
      /* A cancellation that is not ours is a timeout. */
      OperationCanceledException => !token.IsCancellationRequested,
      _ => false
    };
  }

  private static HttpMessageHandler CreateHandler() {
    /* Proxy settings come from the system (or HTTP(S)_PROXY), and the logged
       in user's credentials are offered to authenticating proxies, which is
       what a domain joined Windows server usually sits behind. */
    return new SocketsHttpHandler {
      AutomaticDecompression = DecompressionMethods.All,
      PooledConnectionLifetime = TimeSpan.FromMinutes(5),
      DefaultProxyCredentials = CredentialCache.DefaultCredentials
    };
  }
}
