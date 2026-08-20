using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Api.Integrations.Blob;
using Api.Integrations.Next3;
using Api.Outbox;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests.Integrations;

/// <summary>
/// <c>RealNext3Client</c> against a scripted transport (design.md §6.1, §6.2, §6.3).
///
/// There is no sandbox (#1), so this is where the real client is actually proven. The load-bearing
/// half is not the happy path — it is the **exception classification**: §6.3's worker retries some
/// failures for 26 h 36 m and sends the rest to A2 on the first attempt, so getting the split wrong
/// either hides a bad payload for a day and a half or gives up on a NEXT3 restart.
/// </summary>
public sealed class RealNext3ClientTests
{
    private const string SeededVisa = "PLACEHOLDER-VISA-0001";
    private const string Zone = "Asia/Dubai";

    [Fact]
    public async Task ApiKeyMode_SendsTheKeyHeader()
    {
        var (client, handler, _) = Build();
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetClaim(SeededVisa, CancellationToken.None);

        Assert.Equal("PLACEHOLDER-next3-key", Assert.Single(handler.Requests).ApiKey);
    }

    [Fact]
    public async Task OAuthMode_SendsABearerToken()
    {
        var (client, handler, _) = Build(o => UseOAuth(o));
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-token-1", 3600));
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetClaim(SeededVisa, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("Bearer PLACEHOLDER-token-1", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task TheOAuthTokenIsCachedAndReusedAcrossCalls()
    {
        var (client, handler, _) = Build(o => UseOAuth(o));
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-token-1", 3600));
        handler.Respond(HttpStatusCode.NotFound);
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetClaim(SeededVisa, CancellationToken.None);
        await client.GetClaim(SeededVisa, CancellationToken.None);

        // Three requests, not four: one token and two claims. A token fetch per push would double the
        // call count against a legacy core system that has told us nothing about its rate limits (#1).
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests[1..], r => Assert.Equal("Bearer PLACEHOLDER-token-1", r.Authorization));
    }

    [Fact]
    public async Task TheOAuthTokenIsRefreshedBeforeItActuallyExpires()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        var (client, handler, _) = Build(o => UseOAuth(o), time);

        // A 120-second token with a 60-second safety margin is treated as good for 60 seconds.
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-token-1", 120));
        handler.Respond(HttpStatusCode.NotFound);
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-token-2", 120));
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetClaim(SeededVisa, CancellationToken.None);

        // Past the margin but *not* past the real expiry. A token fetched with a second of life left
        // is a 401 on arrival, so the margin is the point of the test — at 90s the raw token is still
        // valid and a naive cache would happily reuse it.
        time.Advance(TimeSpan.FromSeconds(90));
        await client.GetClaim(SeededVisa, CancellationToken.None);

        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("Bearer PLACEHOLDER-token-2", handler.Requests[3].Authorization);
    }

    [Fact]
    public async Task A401RefreshesTheTokenAndRetriesExactlyOnce()
    {
        var (client, handler, _) = Build(o => UseOAuth(o));
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-stale", 3600));
        handler.Respond(HttpStatusCode.Unauthorized);
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-fresh", 3600));
        handler.Respond(HttpStatusCode.NotFound);

        await client.GetClaim(SeededVisa, CancellationToken.None);

        // NEXT3 may expire a token early. That is not a rejection, so one retry with a fresh token —
        // and the retry really did carry the new one, which is the half a "did it retry?" count misses.
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("Bearer PLACEHOLDER-stale", handler.Requests[1].Authorization);
        Assert.Equal("Bearer PLACEHOLDER-fresh", handler.Requests[3].Authorization);
    }

    [Fact]
    public async Task ASecondUnauthorizedIsARejectionRatherThanAnEndlessRetry()
    {
        var (client, handler, _) = Build(o => UseOAuth(o));
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-token-1", 3600));
        handler.Respond(HttpStatusCode.Unauthorized);
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-token-2", 3600));
        handler.Respond(HttpStatusCode.Unauthorized);

        // A credential that is simply wrong answers 401 for ever. Bounded to one retry, and then it
        // goes to A2 where somebody can fix it — rather than being retried for 26 hours first.
        var ex = await Assert.ThrowsAsync<Next3RejectedException>(() =>
            client.GetClaim(SeededVisa, CancellationToken.None));

        Assert.False(OutboxProcessor.IsTransient(ex));
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task GetClaim_MapsA404ToNull()
    {
        var (client, handler, _) = Build();
        handler.Respond(HttpStatusCode.NotFound);

        // §4's cache shows a different screen for "no such claim" than for "NEXT3 is unreachable".
        // An exception here would collapse the two into one, and the expert would be told the claim
        // does not exist every time NEXT3 restarted.
        Assert.Null(await client.GetClaim("PLACEHOLDER-VISA-UNKNOWN", CancellationToken.None));
    }

    [Fact]
    public async Task GetClaim_ReadsTheClaimBack()
    {
        var (client, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, """
            {
              "visaNo": "PLACEHOLDER-VISA-0001",
              "policyNo": "PLACEHOLDER-POL-0001",
              "plateNo": "PLC-TEST-01",
              "insuredName": "PLACEHOLDER Insured One",
              "insuredPhone": "+999000001001",
              "carMakeModel": "PLACEHOLDER Make One",
              "city": "PLACEHOLDER City A",
              "accidentDate": "2026-08-03"
            }
            """);

        var claim = await client.GetClaim(SeededVisa, CancellationToken.None);

        Assert.NotNull(claim);
        Assert.Equal("PLC-TEST-01", claim.PlateNo);
        Assert.Equal(new DateOnly(2026, 8, 3), claim.AccidentDate);
    }

    [Theory]
    // Refused: no amount of waiting turns these into a success, so they belong on A2 now.
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Conflict, false)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    // NEXT3 having a bad moment: these are what the retry schedule exists for.
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    public async Task AStatusIsClassifiedAsTheOutboxWouldNeedIt(HttpStatusCode status, bool expectedTransient)
    {
        var (client, handler, _) = Build();
        handler.Respond(status, """{"code":"PLACEHOLDER","detail":"PLACEHOLDER detail."}""");

        var ex = await Record.ExceptionAsync(() =>
            client.RecordArrival(SeededVisa, Arrival(), "REF-1", CancellationToken.None));

        Assert.NotNull(ex);

        // Asked of the real classifier rather than of a copy of its list. This is the assertion that
        // matters: a 400 classified transient would be re-sent for 26 h 36 m before anyone saw it,
        // and a 503 classified permanent would give up on a NEXT3 restart that healed in a minute.
        Assert.Equal(expectedTransient, OutboxProcessor.IsTransient(ex));
    }

    [Fact]
    public async Task ARejectionKeepsEnoughDetailForSomeoneToActOnIt()
    {
        var (client, handler, blobs) = Build();
        await SeedBlob(blobs);
        handler.Respond(HttpStatusCode.BadRequest, """{"detail":"PLACEHOLDER unknown document type."}""");

        var ex = await Assert.ThrowsAsync<Next3RejectedException>(() =>
            client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None));

        // This string becomes next3_outbox.last_error, which is the whole of what A2 shows. "400 Bad
        // Request" tells the person looking at it nothing they can use.
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains(nameof(RealNext3Client.UploadDocument), ex.Message, StringComparison.Ordinal);
        Assert.Contains("PLACEHOLDER unknown document type.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClientTimeout_SurfacesAsTimeoutException()
    {
        var (client, handler, _) = Build();

        // What HttpClient throws when its own Timeout elapses: a TaskCanceledException, *not* a
        // TimeoutException.
        handler.Throws(new TaskCanceledException("Simulated HttpClient timeout."));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            client.GetClaim(SeededVisa, CancellationToken.None));

        // The trap: TaskCanceledException derives from OperationCanceledException, and
        // OutboxProcessor's catch filter excludes exactly that type — so left unmapped, a NEXT3
        // timeout escapes the worker entirely, abandons the rest of the batch, and strands its row in
        // `processing` until the lease expires: never retried on schedule, never listed on A2.
        Assert.True(OutboxProcessor.IsTransient(ex));
    }

    [Fact]
    public async Task ACallerCancellation_StaysACancellation()
    {
        var (client, handler, _) = Build();
        handler.Throws(new TaskCanceledException("Should not be reached."));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // The other half of the same guard, and the reason the discriminator is the token rather than
        // the exception type. A shutdown must stay a cancellation — OutboxProcessor deliberately lets
        // those through untouched so a stopping worker does not record a spurious failure on a row it
        // never really attempted.
        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetClaim(SeededVisa, cts.Token));

        Assert.IsNotType<TimeoutException>(ex);
    }

    [Fact]
    public async Task RecordArrival_SplitsTheInstantInTheConfiguredZone()
    {
        var (client, handler, _) = Build();
        handler.Respond(HttpStatusCode.NoContent);

        // 2026-08-20 21:30 UTC is 2026-08-21 01:30 in Asia/Dubai — slice 2.4's case, with the two
        // clocks on *different calendar days*. That is the whole point of the fixture: a split that
        // silently fell back to UTC would report the expert as arriving the day before, on the one
        // field a claims dispute turns on (§9), in a row that cannot be repaired from its own payload.
        var instant = new DateTimeOffset(2026, 8, 20, 21, 30, 0, TimeSpan.Zero);

        await client.RecordArrival(
            SeededVisa, new ArrivalInfo(instant, 25.2048, 55.2708), "REF-1", CancellationToken.None);

        var body = JsonDocument.Parse(Assert.Single(handler.Requests).Body).RootElement;

        Assert.Equal("2026-08-21", body.GetProperty("date").GetString());
        Assert.Equal("01:30:00", body.GetProperty("time").GetString());
        Assert.Equal(Zone, body.GetProperty("timeZone").GetString());

        // And the instant travels too, so an answer to #6 of "UTC, actually" is a config change rather
        // than a re-push of rows whose offset has already been thrown away.
        Assert.Equal(
            instant,
            DateTimeOffset.Parse(
                body.GetProperty("occurredAt").GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public async Task ClientRefIsSentOnBothWrites()
    {
        var (client, handler, blobs) = Build();
        await SeedBlob(blobs);
        handler.Respond(HttpStatusCode.NoContent);
        handler.Respond(HttpStatusCode.Created, """{"documentId":"PLACEHOLDER-DOCUMENT-ID"}""");

        await client.RecordArrival(SeededVisa, Arrival(), "REF-ARRIVAL", CancellationToken.None);
        await client.UploadDocument(SeededVisa, Document(), "REF-DOCUMENT", CancellationToken.None);

        // #32: without this on the wire, every retry after a dropped connection adds a duplicate
        // photograph to the claim file, and nothing on our side can tell that it happened.
        Assert.Contains("REF-ARRIVAL", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("REF-DOCUMENT", handler.Requests[1].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadDocument_SendsTheBlobAsAStreamedMultipartPart()
    {
        var (client, handler, blobs) = Build();
        await SeedBlob(blobs);
        handler.Respond(HttpStatusCode.Created);

        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);

        var request = Assert.Single(handler.Requests);

        // Multipart rather than a materialised byte array: this runs in the outbox worker, a batch is
        // ten rows, and Media:MaxFileMb allows 15 MB each.
        Assert.Equal(nameof(MultipartFormDataContent), request.ContentTypeName);
        Assert.Contains("PLACEHOLDER-bytes", request.Body, StringComparison.Ordinal);
        Assert.Contains("PLACEHOLDER-DOC-01", request.Body, StringComparison.Ordinal);
        Assert.Contains(Next3Folders.ExpertDocuments, request.Body, StringComparison.Ordinal);
        Assert.Equal(1, blobs.OpenCount);
    }

    [Fact]
    public async Task ARetriedUploadReopensTheBlob()
    {
        var (client, handler, blobs) = Build(o => UseOAuth(o));
        await SeedBlob(blobs);

        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-stale", 3600));
        handler.Respond(HttpStatusCode.Unauthorized);
        handler.Respond(HttpStatusCode.OK, Token("PLACEHOLDER-fresh", 3600));
        handler.Respond(HttpStatusCode.Created);

        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);

        // The retry rebuilds the request, and rebuilding it re-opens the blob. It has to: the first
        // attempt consumed the stream, so a resent request would carry an empty file — a photograph
        // that reaches NEXT3 as zero bytes, under the right visa, reported as sent.
        Assert.Equal(2, blobs.OpenCount);
        Assert.Contains("PLACEHOLDER-bytes", handler.Requests[3].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadDocument_WhenTheBlobIsAlreadyGone_IsNotWorthRetrying()
    {
        var (client, _, _) = Build();

        var ex = await Assert.ThrowsAsync<Next3RejectedException>(() =>
            client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None));

        // §7.3 deletes a blob once its push is confirmed. If the bytes are gone while the row is still
        // pending, no retry brings them back — so it goes to A2 immediately rather than eight times
        // over 26 hours, and the error says which key was missing.
        Assert.False(OutboxProcessor.IsTransient(ex));
        Assert.Contains("blob/photo.jpg", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchClaims_WithNeitherTerm_NeverReachesNext3()
    {
        var (client, handler, _) = Build();

        Assert.Empty(await client.SearchClaims(null, null, CancellationToken.None));
        Assert.Empty(await client.SearchClaims("  ", "  ", CancellationToken.None));

        // "Give me everything you have" is not a question this application asks a claims core system,
        // and the guard is here rather than only in the caller so the port keeps the property.
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchClaims_PassesOnlyTheTermsItWasGiven()
    {
        var (client, handler, _) = Build();
        handler.Respond(HttpStatusCode.OK, "[]");

        await client.SearchClaims("PLC-TEST-01", null, CancellationToken.None);

        var uri = Assert.Single(handler.Requests).Uri!;
        Assert.Contains("plateNo=PLC-TEST-01", uri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("visaNo=", uri.Query, StringComparison.Ordinal);
    }

    /// <summary>The bytes an upload sends. Absent unless a test asks for them — see the blob-gone test.</summary>
    private static Task SeedBlob(IBlobStore blobs) =>
        blobs.Put(
            "blob/photo.jpg",
            new MemoryStream(Encoding.UTF8.GetBytes("PLACEHOLDER-bytes")),
            "image/jpeg",
            CancellationToken.None);

    private static ArrivalInfo Arrival() =>
        new(new DateTimeOffset(2026, 8, 21, 9, 30, 0, TimeSpan.Zero), 25.2048, 55.2708);

    private static DocumentPush Document() =>
        new(Next3Folders.ExpertDocuments, "PLACEHOLDER-DOC-01", "photo.jpg", "image/jpeg", "blob/photo.jpg");

    private static string Token(string value, int expiresIn) =>
        $$"""{"access_token":"{{value}}","token_type":"Bearer","expires_in":{{expiresIn}}}""";

    private static void UseOAuth(Next3Options options)
    {
        options.AuthMode = Next3AuthModes.OAuth;
        options.OAuth.TokenUrl = "https://PLACEHOLDER-next3.example/api/auth/token";
        options.OAuth.ClientId = "PLACEHOLDER-client-id";
        options.OAuth.ClientSecret = "PLACEHOLDER-client-secret";
    }

    private static (RealNext3Client Client, StubHttpMessageHandler Handler, CountingBlobStore Blobs) Build(
        Action<Next3Options>? configure = null, FakeTimeProvider? time = null)
    {
        var options = new Next3Options
        {
            BaseUrl = "https://PLACEHOLDER-next3.example/api",
            AuthMode = Next3AuthModes.ApiKey,
            ApiKey = "PLACEHOLDER-next3-key",
            ArrivalTimeZone = Zone,
            TimeoutSeconds = 30,
        };

        configure?.Invoke(options);

        var handler = new StubHttpMessageHandler();
        var factory = new StubHttpClientFactory(handler, new Uri(options.BaseUrl.TrimEnd('/') + "/"));
        var clock = time ?? new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        var blobs = new CountingBlobStore(new InMemoryBlobStore(clock));
        var tokens = new Next3TokenProvider(factory, Options.Create(options), clock);

        return (new RealNext3Client(factory, Options.Create(options), blobs, tokens), handler, blobs);
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }

    /// <summary>Counts <c>Open</c> calls, so "the retry re-opened the blob" is assertable.</summary>
    private sealed class CountingBlobStore(InMemoryBlobStore inner) : IBlobStore
    {
        public int OpenCount { get; private set; }

        public Task Put(string key, Stream content, string contentType, CancellationToken ct) =>
            inner.Put(key, content, contentType, ct);

        public Task<bool> Exists(string key, CancellationToken ct) => inner.Exists(key, ct);

        public Task<Stream?> Open(string key, CancellationToken ct)
        {
            OpenCount++;
            return inner.Open(key, ct);
        }

        public Task<bool> Delete(string key, CancellationToken ct) => inner.Delete(key, ct);

        public Task<IReadOnlyList<BlobItem>> List(string prefix, CancellationToken ct) =>
            inner.List(prefix, ct);
    }
}
