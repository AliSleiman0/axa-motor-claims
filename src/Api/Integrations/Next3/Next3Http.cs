namespace Api.Integrations.Next3;

/// <summary>The named <c>HttpClient</c> both NEXT3 callers resolve — the app's first (slice 3.3).</summary>
public static class Next3HttpClient
{
    public const string Name = "next3";
}

/// <summary>Shared HTTP handling for the NEXT3 edge — the parts the client and the token provider agree on.</summary>
public static class Next3Http
{
    /// <summary>
    /// How much of a rejection body is worth keeping. It ends up in `next3_outbox.last_error`, which
    /// A2 renders, so it has to say something — and it is capped because the column is read by a human
    /// on a screen, and a legacy core system answering an error with an HTML page is not unusual.
    /// </summary>
    private const int SnippetLength = 500;

    /// <summary>
    /// The start of a failed response's body, flattened to one line, or null when it has nothing to
    /// say. Never throws: this only ever runs while another failure is already being reported, and
    /// losing the real error to a formatting problem would be a poor trade.
    /// </summary>
    public static async Task<string?> ReadSnippet(HttpResponseMessage response, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(response);

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            var flattened = body.ReplaceLineEndings(" ").Trim();
            return flattened.Length <= SnippetLength ? flattened : flattened[..SnippetLength];
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
