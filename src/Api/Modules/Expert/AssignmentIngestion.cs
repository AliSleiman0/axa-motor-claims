using Api.Integrations.Next3;

namespace Api.Modules.Expert;

/// <summary>
/// Wires the inbound half of the NEXT3 boundary to its handler at startup (design.md §6.2). Which
/// source is live — webhook, poller or fake — is #34 and is a config value; this subscription is
/// the same either way, which is the whole point of the interface.
/// </summary>
/// <remarks>
/// <see cref="IAssignmentSource"/> is a singleton and <c>AppDbContext</c> is scoped, so the handler
/// runs inside a scope created per delivery — the NotificationLog pattern. Capturing a DbContext in
/// a singleton is the standard way to get "a second operation was started on this context".
/// </remarks>
public sealed class AssignmentIngestion(IAssignmentSource source, IServiceScopeFactory scopes) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        source.Subscribe(async (assignment, ct) =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<AssignmentHandler>();
            await handler.Handle(assignment, ct);
        });

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
