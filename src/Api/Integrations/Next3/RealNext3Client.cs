using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Integrations.Blob;
using Microsoft.Extensions.Options;

namespace Api.Integrations.Next3;

/// <summary>
/// The real NEXT3 client (design.md §6.1, §6.2). Only <c>Api.Composition</c> may name this type
/// (architecture rule 1); everything else depends on <see cref="INext3Client"/> and cannot tell which
/// implementation is live.
///
/// **Built against our own contract.** #1 is unanswered — there is no sandbox, no credentials and no
/// specification — so this is written to <c>docs/next3-openapi.yaml</c>, the proposal AXA is being
/// sent, and `Next3:Mode` stays `fake` everywhere until a sandbox exists. Paths and field names are
/// therefore *ours*: if NEXT3's real API differs, this file and that YAML are the only two places
/// that change, which is the entire reason the boundary exists.
///
/// **What this class is really for is throwing the right exception.** §6.3's worker retries
/// <c>HttpRequestException</c> and <c>TimeoutException</c> on a schedule that takes 26 h 36 m to
/// exhaust, and sends everything else to `failed` on the first attempt. So the mapping below is not
/// bookkeeping: it decides whether a NEXT3 hiccup heals by itself overnight, or whether a bad payload
/// waits a day and a half before a human sees it on A2.
///
/// Singleton-safe by construction — <c>OutboxProcessor</c> is a singleton and takes
/// <see cref="INext3Client"/> directly, so nothing here may hold a scoped dependency such as
/// <c>AppDbContext</c>. A *named* <c>HttpClient</c> rather than a typed one, for the same reason.
/// </summary>
public sealed class RealNext3Client(
    IHttpClientFactory httpClients,
    IOptions<Next3Options> options,
    IBlobStore blobs,
    Next3TokenProvider tokens) : INext3Client
{
    /// <summary>
    /// The API-key header. Our proposal's choice, defined in <c>docs/next3-openapi.yaml</c> — not
    /// invented client data, which is why it is a constant here rather than an Appendix A key. If #1
    /// answers with a different header, it becomes a placeholder then.
    /// </summary>
    private const string ApiKeyHeader = "X-Api-Key";

    private static readonly JsonSerializerOptions JsonFormat = new(JsonSerializerDefaults.Web);

    public async Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct)
    {
        using var response = await Send(
            () => Plain(HttpMethod.Get, $"claims/{Uri.EscapeDataString(visaNo)}"),
            ct);

        // §6.1: a visa NEXT3 does not know is a 404, and the caller wants null rather than an
        // exception — §4's claim cache treats "not found" and "NEXT3 is down" as different states,
        // and only this line keeps them apart. Checked before the 4xx rejection below, deliberately.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureAccepted(response, nameof(GetClaim), ct);

        var claim = await response.Content.ReadFromJsonAsync<Next3ClaimDto>(JsonFormat, ct);
        return claim?.ToClaimDetail();
    }

    public async Task<IReadOnlyList<ClaimSummary>> SearchClaims(
        string? plateNo, string? visaNo, CancellationToken ct)
    {
        // Neither term given returns nothing without calling NEXT3 at all — the same rule the fake
        // holds (§6.2) and the YAML states. The officer's lookup is a deliberate search; "everything
        // you have" is not a question this application is entitled to ask a claims core system.
        if (string.IsNullOrWhiteSpace(plateNo) && string.IsNullOrWhiteSpace(visaNo))
        {
            return [];
        }

        var query = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(plateNo))
        {
            query.Add($"plateNo={Uri.EscapeDataString(plateNo)}");
        }

        if (!string.IsNullOrWhiteSpace(visaNo))
        {
            query.Add($"visaNo={Uri.EscapeDataString(visaNo)}");
        }

        using var response = await Send(
            () => Plain(HttpMethod.Get, $"claims/search?{string.Join("&", query)}"),
            ct);

        await EnsureAccepted(response, nameof(SearchClaims), ct);

        var results = await response.Content.ReadFromJsonAsync<List<Next3ClaimSummaryDto>>(JsonFormat, ct);
        return results is null ? [] : [.. results.Select(r => r.ToClaimSummary())];
    }

    public async Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);

        // **The zone split happens here and nowhere earlier.** ArrivalInfo carries an instant on
        // purpose (see its doc comment): an expert arriving at 01:30 GST collapsed into a bare date
        // upstream would be reported to NEXT3 as arriving the previous calendar day, in an outbox row
        // that can sit in the queue for 26 hours and cannot be repaired from its own payload. This is
        // the edge that knows NEXT3's format, so this is where the lossy conversion belongs.
        //
        // The zone is validated at startup (Next3OptionsValidator), so there is no silent UTC
        // fallback here to re-create that bug.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.ArrivalTimeZone);
        var local = TimeZoneInfo.ConvertTime(info.OccurredAt, zone);

        var payload = new ArrivalRequestDto(
            clientRef,
            info.OccurredAt,
            local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            options.Value.ArrivalTimeZone,
            info.Latitude,
            info.Longitude);

        using var response = await Send(
            () => WithJson(HttpMethod.Post, $"claims/{Uri.EscapeDataString(visaNo)}/arrival", payload),
            ct);

        await EnsureAccepted(response, nameof(RecordArrival), ct);
    }

    public async Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);

        using var response = await Send(
            () => Multipart($"claims/{Uri.EscapeDataString(visaNo)}/documents", doc, clientRef, ct),
            ct);

        await EnsureAccepted(response, nameof(UploadDocument), ct);
    }

    public async Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct)
    {
        using var response = await Send(() => Plain(HttpMethod.Get, "experts"), ct);

        await EnsureAccepted(response, nameof(GetExperts), ct);

        var experts = await response.Content.ReadFromJsonAsync<List<Next3ExpertDto>>(JsonFormat, ct);
        return experts is null ? [] : [.. experts.Select(e => e.ToExpert())];
    }

    public async Task<IReadOnlyList<Next3Garage>> GetGarages(CancellationToken ct)
    {
        using var response = await Send(() => Plain(HttpMethod.Get, "garages"), ct);

        await EnsureAccepted(response, nameof(GetGarages), ct);

        var garages = await response.Content.ReadFromJsonAsync<List<Next3GarageDto>>(JsonFormat, ct);
        return garages is null ? [] : [.. garages.Select(g => g.ToGarage())];
    }

    private static Task<HttpRequestMessage> Plain(HttpMethod method, string path) =>
        Task.FromResult(new HttpRequestMessage(method, path));

    private static Task<HttpRequestMessage> WithJson<T>(HttpMethod method, string path, T payload) =>
        Task.FromResult(new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(payload, options: JsonFormat),
        });

    /// <summary>
    /// §6.1's document upload — "the core of the whole application".
    ///
    /// The blob is **streamed** into the body rather than read into an array: this runs in the outbox
    /// worker, `Media:MaxFileMb` allows 15 MB, and a batch of ten would otherwise be held in memory at
    /// once. The stream and the content are owned by the returned request, which the caller disposes.
    /// </summary>
    private async Task<HttpRequestMessage> Multipart(
        string path, DocumentPush doc, string clientRef, CancellationToken ct)
    {
        var blob = await blobs.Open(doc.BlobKey, ct)
            // Non-transient, and the reason matters: §7.3 deletes a blob only once its outbox row
            // reads `sent`, so if the bytes are gone while the row is still pending, no amount of
            // retrying brings them back. Straight to A2, where a person can see it.
            ?? throw new Next3RejectedException(
                $"Blob '{doc.BlobKey}' no longer exists, so there is nothing to upload. The bytes "
                + "cannot be recovered by retrying (design.md §7.3).");

        var content = new MultipartFormDataContent
        {
            // clientRef first, and required by the YAML: it is what makes a retry after a timeout
            // safe (#32). Metadata before the file, mirroring the ordering contract our own upload
            // endpoint enforces for exactly the same reason (slice 2.3).
            { new StringContent(clientRef), "clientRef" },
            { new StringContent(doc.DocType), "docType" },
            { new StringContent(doc.Folder), "folder" },
        };

        var file = new StreamContent(blob);
        file.Headers.ContentType = new MediaTypeHeaderValue(doc.ContentType);
        content.Add(file, "file", doc.FileName);

        return new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
    }

    /// <summary>
    /// Sends the request, retrying **once** on a 401 in OAuth mode with a fresh token.
    ///
    /// A token can expire between being cached and being used, and that is not a rejection — but a
    /// genuinely wrong credential answers 401 for ever, so the retry is bounded to one and the second
    /// 401 becomes a <see cref="Next3RejectedException"/> on A2. The request is rebuilt rather than
    /// resent because an <c>HttpRequestMessage</c> cannot be sent twice — which also re-opens the
    /// blob, since the first attempt consumed its stream.
    /// </summary>
    private async Task<HttpResponseMessage> Send(
        Func<Task<HttpRequestMessage>> createRequest, CancellationToken ct)
    {
        var response = await SendOnce(createRequest, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized
            && string.Equals(options.Value.AuthMode, Next3AuthModes.OAuth, StringComparison.Ordinal))
        {
            response.Dispose();
            tokens.Invalidate();
            response = await SendOnce(createRequest, ct);
        }

        return response;
    }

    private async Task<HttpResponseMessage> SendOnce(
        Func<Task<HttpRequestMessage>> createRequest, CancellationToken ct)
    {
        using var request = await createRequest();
        await Authorize(request, ct);

        try
        {
            return await httpClients.CreateClient(Next3HttpClient.Name).SendAsync(request, ct);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // **The trap this method exists to close.** HttpClient reports its own timeout as a
            // TaskCanceledException, which derives from OperationCanceledException — and
            // OutboxProcessor's catch filter excludes exactly that type, so a timeout would escape the
            // worker, abandon the rest of the batch, and leave this row stranded in `processing` until
            // its lease expired: never retried on schedule, never listed on A2.
            //
            // `ct` is the discriminator, not the exception type. A caller who really did cancel gets
            // their cancellation back untouched, because this filter does not match.
            throw new TimeoutException("The NEXT3 request timed out.", ex);
        }
    }

    private async Task Authorize(HttpRequestMessage request, CancellationToken ct)
    {
        var settings = options.Value;

        switch (settings.AuthMode)
        {
            case Next3AuthModes.OAuth:
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", await tokens.GetToken(ct));
                break;

            case Next3AuthModes.ApiKey:
                request.Headers.TryAddWithoutValidation(ApiKeyHeader, settings.ApiKey);
                break;

            default:
                // Unreachable when the host validated its options, and non-transient when it did not:
                // no amount of retrying fixes a misconfigured auth mode.
                throw new Next3RejectedException(
                    $"Next3:AuthMode '{settings.AuthMode}' is not supported. Expected "
                    + $"'{Next3AuthModes.ApiKey}' or '{Next3AuthModes.OAuth}' (design.md §6.1, #1).");
        }
    }

    /// <summary>
    /// Turns a response into the outcome §6.3 wants.
    ///
    /// 4xx is intercepted **before** <c>EnsureSuccessStatusCode</c>, which is the load-bearing order:
    /// that method raises <c>HttpRequestException</c> for every failure alike, and the outbox retries
    /// that one — so without this, a 400 would spend 26 h 36 m being resent before anybody saw it.
    /// 5xx falls through to it on purpose, because <c>HttpRequestException</c> is precisely the
    /// classification a server-side fault deserves.
    /// </summary>
    private static async Task EnsureAccepted(
        HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var status = (int)response.StatusCode;

        if (status is >= 400 and < 500)
        {
            var snippet = await Next3Http.ReadSnippet(response, ct);
            throw new Next3RejectedException(status, operation, snippet);
        }

        response.EnsureSuccessStatusCode();
    }
}
