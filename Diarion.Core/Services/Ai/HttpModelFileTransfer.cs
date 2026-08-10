using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Diarion.Services.Ai;

/// <summary>
/// The ordinary way: one <c>GET</c>, resumed with a <c>Range</c> header, streamed into the partial
/// file under a watchdog. Used everywhere except iOS.
/// </summary>
/// <remarks>
/// This is the code that used to sit inside <see cref="ModelDownloadService"/>, moved rather than
/// rewritten — the resume rules and the two clocks are the part of the feature that took the
/// longest to get right, and they are covered by the tests that go through the service.
/// </remarks>
public sealed class HttpModelFileTransfer : IModelFileTransfer
{
    private const int CopyBufferBytes = 128 * 1024;

    /// <summary>
    /// How long a socket may deliver nothing before the download is declared dead.
    /// </summary>
    /// <remarks>
    /// A phone that goes through a tunnel, or an app that iOS suspended and resumed, leaves a
    /// connection that is open and silent. Without this the read simply waits — and the user
    /// watches a progress bar that will never move again, with no error and nothing to press.
    /// Sixty seconds is long enough to survive a bad minute of mobile signal and short enough that
    /// nobody sits through it twice.
    /// </remarks>
    private static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Separate and shorter: a server that has not sent headers yet is not slow, it is absent.</summary>
    private static readonly TimeSpan DefaultResponseHeadersTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Report at most once per megabyte. Per 128 KB chunk would be some nine thousand marshalled
    /// callbacks for the 1.1 GB model, all of them to move a progress bar by a hair.
    /// </summary>
    private const long ProgressReportIntervalBytes = 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _stallTimeout;
    private readonly TimeSpan _responseHeadersTimeout;

    /// <param name="stallTimeout">Overridable so a test can prove the watchdog fires without waiting a minute for it.</param>
    /// <param name="responseHeadersTimeout">Likewise.</param>
    public HttpModelFileTransfer(
        HttpClient httpClient,
        TimeSpan? stallTimeout = null,
        TimeSpan? responseHeadersTimeout = null)
    {
        _httpClient = httpClient;
        _stallTimeout = stallTimeout ?? DefaultStallTimeout;
        _responseHeadersTimeout = responseHeadersTimeout ?? DefaultResponseHeadersTimeout;
    }

    public async Task<bool> FetchAsync(
        ModelFileTransferRequest request,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reportBytes);

        var resumeFrom = File.Exists(request.PartialPath)
            ? new FileInfo(request.PartialPath).Length
            : 0L;

        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);
        if (resumeFrom > 0)
        {
            message.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null);
        }

        using var headers = new CancellationTokenSource(_responseHeadersTimeout);
        using var headersLinked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, headers.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, headersLinked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our clock ran out, not the user's patience. A failure, and it has to read as one.
            return false;
        }

        using (response)
        {
            // A server that ignores Range answers 200 with the whole file; honouring the resume
            // offset then would splice the beginning of the file onto itself.
            if (resumeFrom > 0 && response.StatusCode == HttpStatusCode.OK)
            {
                resumeFrom = 0;
            }
            else if (resumeFrom > 0 && response.StatusCode != HttpStatusCode.PartialContent)
            {
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            return await CopyToPartialAsync(
                response,
                request.PartialPath,
                resumeFrom,
                _stallTimeout,
                reportBytes,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Streams the body into the partial file under a watchdog, returning false if the connection
    /// went quiet. Bytes already written stay put — that is what the next resume is built on.
    /// </summary>
    private static async Task<bool> CopyToPartialAsync(
        HttpResponseMessage response,
        string partialPath,
        long resumeFrom,
        TimeSpan stallTimeout,
        Action<long> reportBytes,
        CancellationToken cancellationToken)
    {
        using var stall = new CancellationTokenSource(stallTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stall.Token);

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            await using var destination = new FileStream(
                partialPath,
                resumeFrom > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                CopyBufferBytes,
                useAsync: true);

            var buffer = new byte[CopyBufferBytes];
            var written = resumeFrom;
            var lastReported = resumeFrom;
            int read;

            while ((read = await source.ReadAsync(buffer, linked.Token).ConfigureAwait(false)) > 0)
            {
                // Bytes arrived, so the connection is alive: give it another full window.
                stall.CancelAfter(stallTimeout);

                await destination.WriteAsync(buffer.AsMemory(0, read), linked.Token).ConfigureAwait(false);
                written += read;

                if (written - lastReported >= ProgressReportIntervalBytes)
                {
                    lastReported = written;
                    reportBytes(written);
                }
            }

            reportBytes(written);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
