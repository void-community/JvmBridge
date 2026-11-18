using System.Net;
using System.Security.Cryptography;
using System.Text;

using Xunit;

using JvmBridge.Build.Infrastructure;

namespace JvmBridge.Build.Tests;

/// <summary>
/// Verifies that a download, and a text read, is asked again when a request gets no answer, the server answers with a transient status or the connection is reset while the content is read, and that a download that fails leaves no file behind.
/// </summary>
public sealed class DownloadTests
{
    private const string Address = "https://example.invalid/app.jar";

    private static readonly byte[] Payload = [.. Enumerable.Range(start: 0, count: 300_000).Select(static index => (byte)(index % 251))];

    private static readonly TimeSpan[] Pauses = [TimeSpan.FromSeconds(seconds: 2), TimeSpan.FromSeconds(seconds: 4), TimeSpan.FromSeconds(seconds: 8), TimeSpan.FromSeconds(seconds: 16)];

    /// <summary>Requests that get no answer, as when a name does not resolve, and a transient status are asked again through all four pauses, half a minute in all.</summary>
    [Fact]
    public async Task ARequestWithoutAnAnswerIsAskedThroughEveryPauseAsync()
    {
        List<TimeSpan> pauses = [];

        using ScriptedHandler handler = new(Unresolved, Unresolved, Unresolved, Unavailable, Whole);

        using HttpClient client = new(handler);

        using HttpService http = new(client, pause: Recorder(pauses));

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-download-test-");

        string path = Path.Combine(temporary.Path, path2: "app.jar");

        await http.DownloadFileAsync(Address, path, CancellationToken.None);

        Assert.Equal(Payload, await File.ReadAllBytesAsync(path, CancellationToken.None));
        Assert.Equal(expected: 5, handler.Requests);
        Assert.Equal(Pauses, pauses);
        Assert.Equal(TimeSpan.FromSeconds(seconds: 30), TimeSpan.FromTicks(pauses.Sum(static pause => pause.Ticks)));
    }

    /// <summary>A connection reset while the content is read is asked again, and the file holds only what the attempt that succeeded wrote.</summary>
    [Fact]
    public async Task AResetDownloadStartsTheFileAgainAsync()
    {
        List<TimeSpan> pauses = [];

        using ScriptedHandler handler = new(Reset, Whole);

        using HttpClient client = new(handler);

        using HttpService http = new(client, pause: Recorder(pauses));

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-download-test-");

        string path = Path.Combine(temporary.Path, path2: "app.jar");

        await http.DownloadFileAsync(Address, path, CancellationToken.None);

        Assert.Equal(SHA256.HashData(Payload), SHA256.HashData(await File.ReadAllBytesAsync(path, CancellationToken.None)));
        Assert.Equal(expected: 2, handler.Requests);
        Assert.Equal([Pauses[0]], pauses);
    }

    /// <summary>A connection reset on every attempt fails after the fifth with the address and the reset in the message, and deletes the part that was written.</summary>
    [Fact]
    public async Task AResetOnEveryAttemptFailsWithTheAddressAsync()
    {
        List<TimeSpan> pauses = [];

        using ScriptedHandler handler = new(Reset, Reset, Reset, Reset, Reset);

        using HttpClient client = new(handler);

        using HttpService http = new(client, pause: Recorder(pauses));

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-download-test-");

        string path = Path.Combine(temporary.Path, path2: "app.jar");

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => http.DownloadFileAsync(Address, path, CancellationToken.None));

        IOException reset = Assert.IsType<IOException>(failure.InnerException);

        Assert.StartsWith("Could not download " + Address + " in 5 attempts: ", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(reset.Message, failure.Message, StringComparison.Ordinal);
        Assert.Contains(expectedSubstring: "Connection reset by peer", reset.Message, StringComparison.Ordinal);
        Assert.Equal(expected: 5, handler.Requests);
        Assert.Equal(Pauses, pauses);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.Path));
    }

    /// <summary>A text whose connection is reset while it is read is asked again.</summary>
    [Fact]
    public async Task ATextIsReadAgainAfterAResetAsync()
    {
        List<TimeSpan> pauses = [];

        using ScriptedHandler handler = new(Reset, Text);

        using HttpClient client = new(handler);

        using HttpService http = new(client, pause: Recorder(pauses));

        string? text = await http.GetStringAsync(Address, cancellationToken: CancellationToken.None);

        Assert.Equal(expected: "pinned", text);
        Assert.Equal(expected: 2, handler.Requests);
        Assert.Equal([Pauses[0]], pauses);
    }

    /// <summary>A status that is not transient, such as 404, fails at once, names the address and leaves no file; a text that may be missing is read as none.</summary>
    [Fact]
    public async Task AnAnswerThatIsNotTransientFailsAtOnceAsync()
    {
        List<TimeSpan> pauses = [];

        using ScriptedHandler handler = new(Missing, Missing);

        using HttpClient client = new(handler);

        using HttpService http = new(client, pause: Recorder(pauses));

        using TemporaryDirectory temporary = new(prefix: "jvmbridge-download-test-");

        string path = Path.Combine(temporary.Path, path2: "app.jar");

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => http.DownloadFileAsync(Address, path, CancellationToken.None));
        string? text = await http.GetStringAsync(Address, missing: true, CancellationToken.None);

        Assert.Equal("Download failed HTTP 404: " + Address, failure.Message);
        Assert.Null(text);
        Assert.Equal(expected: 2, handler.Requests);
        Assert.Empty(pauses);
        Assert.False(File.Exists(path));
    }

    private static HttpResponseMessage Missing()
    {
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static Func<TimeSpan, CancellationToken, Task> Recorder(List<TimeSpan> pauses)
    {
        return (pause, cancellationToken) =>
        {
            pauses.Add(pause);

            return Task.CompletedTask;
        };
    }

    // The server sends a third of the file, then the connection is reset.
    private static HttpResponseMessage Reset()
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ResetStream(Payload[..100_000])) };
    }

    private static HttpResponseMessage Text()
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content: "pinned", Encoding.UTF8) };
    }

    private static HttpResponseMessage Unavailable()
    {
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }

    private static HttpResponseMessage Unresolved()
    {
        throw new HttpRequestException(message: "nodename nor servname provided, or not known (example.invalid:443)");
    }

    private static HttpResponseMessage Whole()
    {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
    }

    // Fails as a connection reset by the peer does, once the part it was given has been read.
    private sealed class ResetStream(byte[] part) : MemoryStream(part)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            return read > 0 ? read : throw new IOException(message: "Unable to read data from the transport connection: Connection reset by peer.");
        }
    }

    // Answers each request with the next of its answers.
    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage>[] _answers = answers;
        private int _requests;

        internal int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpResponseMessage response = _answers[Interlocked.Increment(ref _requests) - 1]();

            response.RequestMessage = request;

            return Task.FromResult(response);
        }
    }
}
