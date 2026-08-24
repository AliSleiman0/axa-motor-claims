using System.Net;
using System.Net.Http.Json;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §9.1: "Invalid, expired, and locked tokens all return the same uniform 404 — no
/// distinguishable states to enumerate." Asserted byte-for-byte, because a difference of one
/// header or one character is all an enumeration script needs.
/// </summary>
[Collection("api")]
public sealed class PublicLinkNotFoundUniformityTests(ApiFixture fixture)
{
    private sealed record Probe(string Name, string Body, HttpStatusCode Status, string[] Headers);

    [Fact]
    public async Task Unknown_Malformed_Expired_And_Locked_AreIndistinguishable()
    {
        var probes = new List<Probe>
        {
            await Get("unknown", RandomToken()),
            await Get("malformed", "not a base64url token!!"),
            await Get("empty-ish", "-"),
            await Get("expired", await ExpiredToken()),
            await Get("locked", await LockedToken()),
        };

        var reference = probes[0];
        foreach (var probe in probes)
        {
            Assert.Equal(HttpStatusCode.NotFound, probe.Status);
            Assert.Equal(reference.Body, probe.Body);
            Assert.Equal(reference.Headers, probe.Headers);
        }
    }

    [Fact]
    public async Task TheSameHoldsForSubmit()
    {
        var probes = new List<Probe>
        {
            await Submit("unknown", RandomToken()),
            await Submit("expired", await ExpiredToken()),
            await Submit("locked", await LockedToken()),
        };

        var reference = probes[0];
        foreach (var probe in probes)
        {
            Assert.Equal(HttpStatusCode.NotFound, probe.Status);
            Assert.Equal(reference.Body, probe.Body);
            Assert.Equal(reference.Headers, probe.Headers);
        }
    }

    /// <summary>
    /// Slice 5.3's two new routes. The surface grew, so the guarantee has to grow with it: a document
    /// route that leaked the difference between "no such link" and "that link is used up" would hand
    /// an enumeration script exactly what the submit route refuses it.
    ///
    /// The upload probe deliberately carries a real multipart body, so a 404 here is the token being
    /// refused rather than the request being malformed — the two must not be distinguishable either.
    /// </summary>
    [Fact]
    public async Task TheSameHoldsForTheDocumentRoutes()
    {
        var probes = new List<Probe>
        {
            await ListDocuments("unknown", RandomToken()),
            await ListDocuments("expired", await ExpiredToken()),
            await ListDocuments("locked", await LockedToken()),
        };

        var uploads = new List<Probe>
        {
            await UploadDocument("unknown", RandomToken()),
            await UploadDocument("expired", await ExpiredToken()),
            await UploadDocument("locked", await LockedToken()),
        };

        AssertUniform(probes);
        AssertUniform(uploads);
    }

    private static void AssertUniform(IReadOnlyList<Probe> probes)
    {
        var reference = probes[0];
        foreach (var probe in probes)
        {
            Assert.Equal(HttpStatusCode.NotFound, probe.Status);
            Assert.Equal(reference.Body, probe.Body);
            Assert.Equal(reference.Headers, probe.Headers);
        }
    }

    private async Task<Probe> ListDocuments(string name, string token)
    {
        using var client = fixture.CreatePublicClient();
        return await Describe(name, await client.GetAsync(PublicLinkFlows.DocumentsPath(token)));
    }

    private async Task<Probe> UploadDocument(string name, string token)
    {
        using var client = fixture.CreatePublicClient();
        return await Describe(name, await PublicLinkFlows.UploadPublicDocument(client, token));
    }

    private async Task<Probe> Get(string name, string token)
    {
        using var client = fixture.CreatePublicClient();
        var response = await client.GetAsync($"/public/{token}");
        return await Describe(name, response);
    }

    private async Task<Probe> Submit(string name, string token)
    {
        using var client = fixture.CreatePublicClient();
        var response = await client.PostAsJsonAsync(
            $"/public/{token}/submit", PublicLinkFlows.CompleteSubmission());
        return await Describe(name, response);
    }

    private static async Task<Probe> Describe(string name, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        // Content headers describe the response shape; Date/Server vary by clock and are not signal.
        var headers = response.Content.Headers
            .Select(h => $"{h.Key}: {string.Join(",", h.Value)}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new Probe(name, body, response.StatusCode, headers);
    }

    private static string RandomToken() => Convert.ToHexString(Guid.NewGuid().ToByteArray());

    private async Task<string> ExpiredToken()
    {
        var link = await fixture.IssueLink();
        fixture.Time.Advance(TimeSpan.FromDays(fixture.PublicLink.CurrentValue.ValidityDays + 1));
        return link.Token;
    }

    private async Task<string> LockedToken()
    {
        var link = await fixture.IssueLink();
        using var client = fixture.CreatePublicClient();

        // Slice 5.3 made a supporting document a precondition of the submit, so locking a token now
        // means walking the customer's whole journey rather than posting the six fields.
        await PublicLinkFlows.OpenAndAttach(client, link.Token);

        var submit = await client.PostAsJsonAsync(
            $"/public/{link.Token}/submit", PublicLinkFlows.CompleteSubmission());
        submit.EnsureSuccessStatusCode();
        return link.Token;
    }
}
