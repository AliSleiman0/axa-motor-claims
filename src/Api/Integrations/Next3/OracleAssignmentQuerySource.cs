using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace Api.Integrations.Next3;

/// <summary>
/// The real half of the Oracle-poll adapter (#34, resolved 2026-08-31). Built the same way slice
/// 3.3 built <c>RealNext3Client</c> ahead of the NEXT3 sandbox: wired for real, registered only
/// behind its mode switch, and **completely unexercised by any test in this slice** — Oracle
/// credentials are further out than NEXT3's own sandbox, so unlike <c>Next3ClientContractTests</c>
/// there is no `[SandboxFact]`-gated live test to write yet. Same treatment as
/// <see cref="Next3Options.ClientCertificatePath"/>: config-ready, not exercised, said so rather
/// than hidden. The day real connection details land, this needs a live-connection pass before it
/// is trusted — nothing here proves the SQL below actually runs against NEXT3's schema.
///
/// The query is a parameterized narrowing of
/// `docs/client-answers-2026-08-31/uat-visa-event-trigger.sql` — the join shape is the client's
/// own reference (`CARS_NOTIFICATION` through `CARS_POLICY`, `CAR_SEQUENCE = 0`), narrowed to the
/// three columns <see cref="AssignmentReceived"/> needs, with the reference's `SYSDATE - 1` literal
/// replaced by a bound parameter driven from <see cref="TimeProvider"/> — this codebase reads time
/// from the app's clock everywhere else that matters (design.md §6.3), not the database's.
/// `NOTIFICATION_ID` is used verbatim as the dedupe ref: the client's own answer to #6 already
/// treats it as the app-side assignment identifier ("`NOTIFICATION_ID` = the one in mobile
/// application").
/// </summary>
public sealed class OracleAssignmentQuerySource(
    IOptions<Next3Options> options,
    TimeProvider time) : IAssignmentQuerySource
{
    private const string PollQuery =
        """
        SELECT N.NOTIFICATION_ID, N.NOTIFICATION_VISA, E.SUPPLIER_ID AS EXPERT_ID
        FROM CARS_NOTIFICATION N, CARS_LOSS_TOWING T, CARS_SUPPLIER E, CARS_LOSS_CAR C,
             CARS_TOWN O, CARS_POLICY_CAR CPC, CARS_POLICY CP
        WHERE N.NOTIFICATION_ID = T.NOTIFICATION_ID
          AND T.LOSS_TOW_EXPERT_ID = E.SUPPLIER_ID
          AND T.LOSS_TOW_ID = C.CAR_CLAIM_ID
          AND C.CAR_SEQUENCE = 0
          AND O.TOWN_ID = T.DISTRIBUTION_LOSS_TOWN_ID
          AND CPC.CAR_ID = N.NOTIFICATION_POL_CAR_ID
          AND CPC.POLICY_ID = CP.POLICY_ID
          AND T.SYS_CREATED_DATE >= :since
        """;

    public async Task<IReadOnlyList<AssignmentReceived>> PollAsync(CancellationToken ct)
    {
        var oracle = options.Value.Oracle;
        var connectionString =
            $"Data Source={oracle.Host}:{oracle.Port}/{oracle.ServiceName};"
            + $"User Id={oracle.Username};Password={oracle.Password};";

        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = PollQuery;
        command.Parameters.Add(new OracleParameter(
            "since", OracleDbType.Date, time.GetUtcNow().UtcDateTime.AddDays(-1), System.Data.ParameterDirection.Input));

        var rows = new List<AssignmentReceived>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new AssignmentReceived(
                VisaNo: reader.GetString(reader.GetOrdinal("NOTIFICATION_VISA")),
                ExpertNext3Id: reader.GetString(reader.GetOrdinal("EXPERT_ID")),
                Next3AssignmentRef: reader.GetString(reader.GetOrdinal("NOTIFICATION_ID"))));
        }

        return rows;
    }
}
