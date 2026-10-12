// Copyright (c) 2026 Linus Berg. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Text;
using MiniSync.Core;

namespace MiniSync.Tests;

public class FetcherTests {
  /// <summary>
  ///   Answers each request with the next scripted response.
  /// </summary>
  private class Scripted : HttpMessageHandler {
    private readonly Queue<Func<HttpResponseMessage>> responses_;

    public Scripted(params Func<HttpResponseMessage>[] responses) {
      responses_ = new Queue<Func<HttpResponseMessage>>(responses);
    }

    public int calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken token) {
      calls++;
      return Task.FromResult(responses_.Dequeue()());
    }
  }

  /// <summary>
  ///   A body that never produces a byte.
  /// </summary>
  private class Stalled : Stream {
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get; set; }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) {
      await Task.Delay(Timeout.Infinite, token);
      return 0;
    }
  }

  private static HttpResponseMessage Status(HttpStatusCode status, string body = "") {
    return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
  }

  private static Fetcher Create(HttpMessageHandler handler) {
    return new Fetcher(2, handler) {
      retry_delay = TimeSpan.FromMilliseconds(1),
      idle_timeout = TimeSpan.FromMilliseconds(50)
    };
  }

  [Fact]
  public async Task Transient_failures_are_retried() {
    Scripted handler = new(
      () => Status(HttpStatusCode.ServiceUnavailable),
      () => throw new HttpRequestException("connection reset"),
      () => Status(HttpStatusCode.OK, "hello")
    );
    Assert.Equal("hello"u8.ToArray(), await Create(handler).GetBytes("https://x/a", CancellationToken.None));
    Assert.Equal(3, handler.calls);
  }

  [Fact]
  public async Task Not_found_is_null_for_metadata_and_an_error_for_downloads() {
    Scripted handler = new(() => Status(HttpStatusCode.NotFound), () => Status(HttpStatusCode.NotFound));
    Fetcher fetcher = Create(handler);
    Assert.Null(await fetcher.GetBytes("https://x/a", CancellationToken.None));
    string file = Path.GetTempFileName();
    try {
      HttpRequestException error = await Assert.ThrowsAsync<HttpRequestException>(
                                     () => fetcher.Download("https://x/a", file, CancellationToken.None)
                                   );
      Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
      Assert.Equal(2, handler.calls);
    } finally {
      File.Delete(file);
    }
  }

  [Fact]
  public async Task A_stalled_body_is_retried_and_then_reported_as_a_timeout() {
    Func<HttpResponseMessage> stall =
      () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new Stalled()) };
    Scripted handler = new(stall, stall, stall, stall);
    /* Not an OperationCanceledException: that would be mistaken for Ctrl+C. */
    await Assert.ThrowsAsync<TimeoutException>(
      () => Create(handler).GetBytes("https://x/a", CancellationToken.None)
    );
    Assert.Equal(4, handler.calls);
  }

  [Fact]
  public async Task Cancellation_is_not_retried() {
    using CancellationTokenSource cancel = new();
    cancel.Cancel();
    Scripted handler = new(() => Status(HttpStatusCode.OK, "x"));
    await Assert.ThrowsAnyAsync<OperationCanceledException>(
      () => Create(handler).GetBytes("https://x/a", cancel.Token)
    );
    Assert.Equal(0, handler.calls);
  }
}
