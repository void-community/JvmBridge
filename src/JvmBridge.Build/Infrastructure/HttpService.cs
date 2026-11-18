using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace JvmBridge.Build.Infrastructure;

internal sealed class HttpService : IDisposable
{
    // With 2, 4, 8 and 16 seconds between them, so half a minute without name resolution or with connections reset doesn't fail a download.
    private const int Attempts = 5;

    private readonly ConcurrentDictionary<string, JsonNode> _cache = new();
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Func<TimeSpan, CancellationToken, Task> _pause;

    public HttpService()
        : this(new HttpClient(), ownsClient: true)
    {
    }

    internal HttpService(HttpClient client, bool ownsClient = false, Func<TimeSpan, CancellationToken, Task>? pause = null)
    {
        _client = client;
        _ownsClient = ownsClient;
        _pause = pause ?? Task.Delay;
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(productName: "JvmBridge.Build", productVersion: "1.0"));
    }

    public void Dispose()
    {
        if (_ownsClient)
            _client.Dispose();
    }

    /// <summary>Downloads a file, asking again when a request fails without an answer, the server answers with a transient status or the connection is reset while the content is read. The file is deleted when the download fails.</summary>
    public async Task DownloadFileAsync(string url, string path, CancellationToken cancellationToken)
    {
        // Opened before the first attempt, so a path that cannot be written fails at once instead of being asked again.
        FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true);

        try
        {
            await using (output.ConfigureAwait(continueOnCapturedContext: false))
            {
                await SendAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        TimeSpan.FromMinutes(minutes: 10),
                        async (response, token) =>
                        {
                            // Each attempt writes the file from its beginning, dropping what a broken one wrote.
                            output.SetLength(value: 0);

                            Stream input = await Succeeded(response, url).Content.ReadAsStreamAsync(token).ConfigureAwait(continueOnCapturedContext: false);

                            await using (input.ConfigureAwait(continueOnCapturedContext: false))
                                await input.CopyToAsync(output, token).ConfigureAwait(continueOnCapturedContext: false);
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(continueOnCapturedContext: false);
            }
        }
        catch
        {
            File.Delete(path);

            throw;
        }
    }

    public async Task<JsonNode?> GetJsonAsync(string url, bool missing = false, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(url, out JsonNode? cached))
            return cached;

        string? payload = await GetStringAsync(url, missing, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (payload is null)
            return null;

        JsonNode value = JsonNode.Parse(payload) ?? throw new InvalidOperationException($"Empty JSON response: {url}");
        _cache[url] = value;

        return value;
    }

    /// <summary>Reads a text, asking again as <see cref="DownloadFileAsync"/> does.</summary>
    public async Task<string?> GetStringAsync(string url, bool missing = false, CancellationToken cancellationToken = default)
    {
        string? text = null;

        await SendAsync(
                url,
                HttpCompletionOption.ResponseContentRead,
                TimeSpan.FromSeconds(seconds: 90),
                async (response, token) =>
                {
                    if (!missing || response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.Gone))
                        text = await Succeeded(response, url).Content.ReadAsStringAsync(token).ConfigureAwait(continueOnCapturedContext: false);
                },
                cancellationToken
            )
            .ConfigureAwait(continueOnCapturedContext: false);

        return text;
    }

    private static HttpRequestMessage CreateRequest(string url)
    {
        HttpRequestMessage request = new(HttpMethod.Get, url);
        Uri uri = request.RequestUri ?? throw new InvalidOperationException(message: "HTTP request URI is missing.");
        string? token = Environment.GetEnvironmentVariable(variable: "GH_TOKEN") ?? Environment.GetEnvironmentVariable(variable: "GITHUB_TOKEN");

        if (!string.IsNullOrEmpty(token) && string.Equals(uri.Host, b: "api.github.com", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", token);

        return request;
    }

    private static HttpResponseMessage Succeeded(HttpResponseMessage response, string url)
    {
        return response.IsSuccessStatusCode ? response : throw new InvalidOperationException($"Download failed HTTP {(int)response.StatusCode}: {url}");
    }

    // A failure without an answer, a transient status, a connection reset while the content is read and a request that outlasts its time are asked again; the caller's cancellation is not.
    private async Task SendAsync(
        string url,
        HttpCompletionOption completion,
        TimeSpan timeout,
        Func<HttpResponseMessage, CancellationToken, Task> read,
        CancellationToken cancellationToken
    )
    {
        Exception? failure = null;

        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (failure is not null)
                await _pause(TimeSpan.FromSeconds(1 << (attempt - 1)), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            limit.CancelAfter(timeout);

            using HttpRequestMessage request = CreateRequest(url);

            try
            {
                using HttpResponseMessage response = await _client.SendAsync(request, completion, limit.Token).ConfigureAwait(continueOnCapturedContext: false);

                HttpStatusCode status = response.StatusCode;

                if (status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)status >= 500)
                    throw new HttpRequestException($"The server answered HTTP {(int)status}.", inner: null, status);

                await read(response, limit.Token).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }
            catch (Exception transient) when (transient is HttpRequestException or IOException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                failure = transient;
            }
        }

        throw new InvalidOperationException($"Could not download {url} in {Attempts} attempts: {failure?.Message}", failure);
    }
}
