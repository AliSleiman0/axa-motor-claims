using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class AdminSeedTests(ApiFixture fixture)
{
    [Fact]
    public async Task SeededAdmin_Exists_AndReseedingIsIdempotent()
    {
        var seed = fixture.AuthOptions().SeedAdmin;

        await using (var db = fixture.CreateDbContext())
        {
            var admin = await db.Users.SingleAsync(u => u.Phone == seed.Phone);
            Assert.Equal(UserRole.Admin, admin.Role);
            Assert.Equal(UserStatus.Active, admin.Status);
        }

        await AdminSeeder.Seed(fixture.Services);

        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == seed.Phone));
        }
    }
}
