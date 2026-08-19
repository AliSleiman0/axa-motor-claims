using Api.Integrations.Next3;
using Api.Modules.Users;

namespace Api.Modules.Expert;

/// <summary>The three values NEXT3 sends when it assigns a claim (§6.2).</summary>
public sealed record InjectAssignmentDto(string? VisaNo, string? ExpertNext3Id, string? Next3AssignmentRef);

/// <summary>
/// Injects a synthetic assignment (design.md §6.2: the fake "injects synthetic assignments for
/// demo/UAT"). This is what drives the week-4 demo and any UAT run that happens before NEXT3 can
/// call us — without it, the only trigger is a debugger.
///
/// Admin-gated, and refused unless the fake source is the configured one, so it cannot be used to
/// forge an assignment in an environment wired to the real NEXT3.
/// </summary>
/// <remarks>
/// It calls <see cref="AssignmentHandler"/> directly rather than
/// <see cref="FakeAssignmentSource.Inject"/>, because <see cref="IAssignmentSource"/>'s handler
/// signature returns no result (a webhook receiver has nothing to report to) and this endpoint owes
/// its caller a typed outcome. The subscription path itself is covered by tests that call Inject
/// through DI — the same handler runs either way, which is §6.2's single-funnel guarantee.
/// </remarks>
public static class DevAssignmentEndpoints
{
    public static IEndpointRouteBuilder MapDevAssignmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/dev/assignments").RequireAuthorization(AuthPolicies.Admin);

        group.MapPost("/", async (
            InjectAssignmentDto dto, IAssignmentSource source, AssignmentHandler handler, CancellationToken ct) =>
        {
            if (source is not FakeAssignmentSource)
            {
                return Results.Conflict(new { error = "assignment_source_not_fake" });
            }

            if (string.IsNullOrWhiteSpace(dto.VisaNo)
                || string.IsNullOrWhiteSpace(dto.ExpertNext3Id)
                || string.IsNullOrWhiteSpace(dto.Next3AssignmentRef))
            {
                return Results.BadRequest(new { error = "incomplete_assignment" });
            }

            var result = await handler.Handle(
                new AssignmentReceived(dto.VisaNo, dto.ExpertNext3Id, dto.Next3AssignmentRef), ct);

            return result switch
            {
                AssignmentIngestionResult.Created => Results.Ok(new { created = true }),
                AssignmentIngestionResult.DuplicateIgnored => Results.Ok(new { created = false }),
                // 422, not 400: the request is well-formed and the caller cannot fix it — the
                // expert's NEXT3 id is not mapped to a profile yet (#8).
                _ => Results.UnprocessableEntity(new { error = "unknown_expert" }),
            };
        });

        return app;
    }
}
