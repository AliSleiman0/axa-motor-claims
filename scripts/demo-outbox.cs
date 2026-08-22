#:package Microsoft.Data.SqlClient@6.*

// Read-only view of the transactional outbox, for the week-4 demo (slice 4.3).
//
// design.md 5.4's A2 screen — the failed-push queue with its Retry button — is slice 6.2. Until it
// exists there is nothing in the product that shows the queue, and beat 3 of the demo is *about* the
// queue: it fills while NEXT3 is down and drains when NEXT3 comes back. This prints what A2 will
// print, from a terminal, so the beat can be shown rather than asserted.
//
// It writes nothing. Every statement here is a SELECT, deliberately: CLAUDE.md's rule is that demo
// state is seeded through the API and never through SQL, and observing is the only thing SQL is
// allowed to do here.
//
// A .NET 10 file-based app rather than a project, so it adds nothing to the solution, and rather
// than PowerShell because Microsoft.Data.SqlClient refuses to load into pwsh ("not supported on this
// platform" — the netstandard facade is what resolves there).
//
// Usage: dotnet run scripts/demo-outbox.cs [--connection "<connection string>"]

using System.Globalization;
using Microsoft.Data.SqlClient;

var connectionString = @"Server=(localdb)\MSSQLLocalDB;Database=AxaMotorClaims;Integrated Security=true;TrustServerCertificate=true";
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] is "--connection" or "-c")
    {
        connectionString = args[i + 1];
    }
}

await using var connection = new SqlConnection(connectionString);
try
{
    await connection.OpenAsync();
}
catch (SqlException ex)
{
    Console.Error.WriteLine($"Could not open the demo database: {ex.Message}");
    Console.Error.WriteLine(@"Run .\scripts\demo-reset.ps1 first.");
    return 1;
}

await Print(
    "QUEUE  (design.md 6.3 - A2 is slice 6.2; this is what it will show)",
    """
    SELECT  o.status,
            COUNT(*)                        AS rows_,
            MAX(o.attempts)                 AS max_attempts,
            -- Only for rows still in play: on a `sent` row this column holds the lease the worker
            -- pushed out when it claimed it, and a sent row showing a future "retry" reads, on a
            -- projector, as though it were going to be pushed twice.
            MIN(CASE WHEN o.status IN ('pending', 'processing') THEN o.next_retry_at END) AS next_retry_at
    FROM    dbo.next3_outbox o
    GROUP BY o.status
    ORDER BY o.status
    """);

await Print(
    "QUEUE ROWS  (newest last)",
    """
    SELECT TOP (40)
            CONVERT(varchar(8), o.created_at, 108) AS created,
            o.operation,
            o.visa_no,
            o.status,
            o.attempts,
            CASE WHEN o.status IN ('pending', 'processing')
                 THEN CONVERT(varchar(8), o.next_retry_at, 108) END AS retry_at,
            CONVERT(varchar(8), o.sent_at, 108)       AS sent_at,
            LEFT(ISNULL(o.last_error, ''), 44)        AS last_error
    FROM    dbo.next3_outbox o
    ORDER BY o.created_at, o.id
    """);

await Print(
    "DOCUMENTS  (7.1 buckets; deferred = waiting for an officer's approval)",
    """
    SELECT TOP (40)
            d.bucket,
            d.push_status,
            d.origin,
            d.clarity_result,
            ISNULL(d.file_name, '')  AS file_name,
            d.content_type,
            d.size_bytes,
            CASE WHEN d.blob_deleted_at IS NULL THEN 'yes' ELSE 'no' END AS blob
    FROM    dbo.document d
    ORDER BY d.created_at, d.id
    """);

await Print(
    "NOTIFICATIONS  (section 8 - one row per attempt, per device)",
    """
    SELECT TOP (15)
            CONVERT(varchar(8), n.created_at, 108) AS created,
            n.channel,
            n.template,
            n.status,
            LEFT(n.recipient_address, 34)     AS recipient,
            LEFT(ISNULL(n.error, ''), 30)     AS error
    FROM    dbo.notification n
    ORDER BY n.created_at DESC, n.id DESC
    """);

return 0;

async Task Print(string heading, string sql)
{
    Console.WriteLine();
    Console.WriteLine(heading);
    Console.WriteLine(new string('-', heading.Length));

    await using var command = new SqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    var rows = new List<string[]>();
    while (await reader.ReadAsync())
    {
        rows.Add([.. Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "")]);
    }

    if (rows.Count == 0)
    {
        Console.WriteLine("(none)");
        return;
    }

    var widths = columns
        .Select((name, i) => Math.Max(name.Length, rows.Max(r => r[i].Length)))
        .ToArray();

    Console.WriteLine(string.Join("  ", columns.Select((name, i) => name.PadRight(widths[i]))));
    Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
    foreach (var row in rows)
    {
        Console.WriteLine(string.Join("  ", row.Select((value, i) => value.PadRight(widths[i]))));
    }
}
