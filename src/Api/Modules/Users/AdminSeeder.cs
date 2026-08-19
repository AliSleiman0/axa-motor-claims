using Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Users;

/// <summary>§5.4: the first admin user is seeded by deployment config — admins are never invited.</summary>
public static class AdminSeeder
{
    public static async Task Seed(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<AuthOptions>>().Value.SeedAdmin;
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        if (string.IsNullOrWhiteSpace(seed.Phone))
        {
            return;
        }

        if (await db.Users.AnyAsync(u => u.Phone == seed.Phone, ct))
        {
            return;
        }

        db.Users.Add(new AppUser
        {
            Id = Guid.CreateVersion7(),
            Phone = seed.Phone,
            Role = UserRole.Admin,
            DisplayName = seed.DisplayName,
            Status = UserStatus.Active,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);
    }
}
