using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Modules.Audit;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

internal sealed record CreatedDto(Guid Id);

[Collection("api")]
public sealed class AdminProfileCrudTests(ApiFixture fixture)
{
    private static string NextNext3Id() => $"N3-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task CreateExpert_RoundTrip_CreateInviteActivateDeactivate()
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();
        var next3Id = NextNext3Id();

        var create = await admin.PostAsJsonAsync("/api/admin/experts", new
        {
            phone,
            displayName = "Expert One",
            email = "expert-one@example.invalid",
            next3Id,
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        await using (var db = fixture.CreateDbContext())
        {
            var profile = await db.ExpertProfiles.SingleAsync(p => p.UserId == id);
            Assert.Equal("expert-one@example.invalid", profile.Email);
            Assert.Equal(next3Id, profile.Next3Id);
            Assert.True(profile.Active);
            var user = await db.Users.SingleAsync(u => u.Id == id);
            Assert.Equal(UserStatus.Invited, user.Status);
        }

        var userClient = await ActivateViaInvite(phone);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/expert/ping")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == id);
            Assert.Equal(UserStatus.Inactive, user.Status);
            Assert.NotNull(user.InactivatedAt);
            var profile = await db.ExpertProfiles.SingleAsync(p => p.UserId == id);
            Assert.False(profile.Active);
            Assert.NotNull(profile.InactivatedAt);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/expert/ping")).StatusCode);
    }

    [Fact]
    public async Task CreateGarage_RoundTrip_CreateInviteActivateDeactivate()
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();
        var next3Id = NextNext3Id();

        var create = await admin.PostAsJsonAsync("/api/admin/garages", new
        {
            phone,
            displayName = "Garage One",
            contactName = "Contact One",
            contactPhone = "+999120000001",
            mobile = "+999120000002",
            email = "garage-one@example.invalid",
            next3Id,
            address = "1 Test Street",
            openingHours = "Sat-Thu 8-18",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        await using (var db = fixture.CreateDbContext())
        {
            var profile = await db.GarageProfiles.SingleAsync(p => p.UserId == id);
            Assert.Equal("Contact One", profile.ContactName);
            Assert.Equal("+999120000001", profile.Phone);
            Assert.Equal("+999120000002", profile.Mobile);
            Assert.Equal("garage-one@example.invalid", profile.Email);
            Assert.Equal(next3Id, profile.Next3Id);
            Assert.Equal("1 Test Street", profile.Address);
            Assert.Equal("Sat-Thu 8-18", profile.OpeningHours);
            Assert.True(profile.Active);
        }

        var userClient = await ActivateViaInvite(phone);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/garage/ping")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(UserStatus.Inactive, (await db.Users.SingleAsync(u => u.Id == id)).Status);
            var profile = await db.GarageProfiles.SingleAsync(p => p.UserId == id);
            Assert.False(profile.Active);
            Assert.NotNull(profile.InactivatedAt);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/garage/ping")).StatusCode);
    }

    [Fact]
    public async Task CreateClaimOfficer_RoundTrip_CreateInviteActivateDeactivate()
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();

        var create = await admin.PostAsJsonAsync("/api/admin/claim-officers", new
        {
            phone,
            displayName = "Officer One",
            next3User = "n3user-one",
            email = "officer-one@example.invalid",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        await using (var db = fixture.CreateDbContext())
        {
            var profile = await db.ClaimOfficerProfiles.SingleAsync(p => p.UserId == id);
            Assert.Equal("n3user-one", profile.Next3User);
            Assert.Equal("officer-one@example.invalid", profile.Email);
        }

        var userClient = await ActivateViaInvite(phone);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/officer/ping")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(UserStatus.Inactive, (await db.Users.SingleAsync(u => u.Id == id)).Status);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/officer/ping")).StatusCode);
    }

    [Fact]
    public async Task CreateBroker_RoundTrip_CreateInviteActivateDeactivate()
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();

        var create = await admin.PostAsJsonAsync("/api/admin/brokers", new
        {
            phone,
            displayName = "Broker One",
            irisCode = "IRIS-TEST-1",
            email = "broker-one@example.invalid",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        await using (var db = fixture.CreateDbContext())
        {
            var profile = await db.BrokerProfiles.SingleAsync(p => p.UserId == id);
            Assert.Equal("IRIS-TEST-1", profile.IrisCode);
            Assert.Equal("broker-one@example.invalid", profile.Email);
        }

        var userClient = await ActivateViaInvite(phone);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/broker/ping")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            Assert.Equal(UserStatus.Inactive, (await db.Users.SingleAsync(u => u.Id == id)).Status);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/broker/ping")).StatusCode);
    }

    // Regression: GET-by-id composed a Where over the projected DTO, which EF cannot translate
    // past the client-evaluated ToDbValue() — every detail request 500'd (found in manual testing;
    // the round-trip tests never called GET).
    [Theory]
    [InlineData("/api/admin/experts")]
    [InlineData("/api/admin/garages")]
    [InlineData("/api/admin/claim-officers")]
    [InlineData("/api/admin/brokers")]
    public async Task GetListAndDetail_ReturnDto_AndUnknownIdIs404(string basePath)
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();
        var create = await admin.PostAsJsonAsync(basePath, Payload(basePath, phone, NextNext3Id()));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        var detail = await admin.GetFromJsonAsync<JsonElement>($"{basePath}/{id}");
        Assert.Equal(phone, detail.GetProperty("phone").GetString());
        Assert.Equal("invited", detail.GetProperty("status").GetString());

        var list = await admin.GetFromJsonAsync<JsonElement>(basePath);
        Assert.Contains(list.EnumerateArray(), e => e.GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{basePath}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task CreateProfile_DuplicatePhoneAcrossRoles_Conflict()
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();

        var expert = await admin.PostAsJsonAsync("/api/admin/experts", new
        {
            phone,
            displayName = "Expert Dup",
            email = "expert-dup@example.invalid",
            next3Id = (string?)null,
        });
        Assert.Equal(HttpStatusCode.Created, expert.StatusCode);

        var broker = await admin.PostAsJsonAsync("/api/admin/brokers", new
        {
            phone,
            displayName = "Broker Dup",
            irisCode = "IRIS-DUP",
            email = "broker-dup@example.invalid",
        });
        Assert.Equal(HttpStatusCode.Conflict, broker.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Phone == phone)); // no orphan user
        Assert.Equal(0, await db.BrokerProfiles.CountAsync(p => p.IrisCode == "IRIS-DUP")); // no orphan profile
    }

    [Theory]
    [InlineData("/api/admin/experts")]
    [InlineData("/api/admin/garages")]
    public async Task Create_DuplicateNext3Id_Conflict(string basePath)
    {
        using var admin = await fixture.CreateAdminClient();
        var next3Id = NextNext3Id();

        var first = await admin.PostAsJsonAsync(basePath, Payload(basePath, TestPhones.Next(), next3Id));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await admin.PostAsJsonAsync(basePath, Payload(basePath, TestPhones.Next(), next3Id));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateExpert_TwoProfilesWithNullNext3Id_Allowed()
    {
        using var admin = await fixture.CreateAdminClient();

        var first = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), null));
        var second = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode); // filtered unique index admits multiple NULLs
    }

    [Fact]
    public async Task UpdateGarage_ChangesFields_WritesAuditRow()
    {
        using var admin = await fixture.CreateAdminClient();
        var create = await admin.PostAsJsonAsync("/api/admin/garages", Payload("/api/admin/garages", TestPhones.Next(), NextNext3Id()));
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
        var newNext3Id = NextNext3Id();

        var update = await admin.PutAsJsonAsync($"/api/admin/garages/{id}", new
        {
            displayName = "Garage Renamed",
            contactName = "New Contact",
            contactPhone = (string?)null,
            mobile = (string?)null,
            email = "garage-renamed@example.invalid",
            next3Id = newNext3Id,
            address = (string?)null,
            openingHours = (string?)null,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        await using var db = fixture.CreateDbContext();
        var user = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal("Garage Renamed", user.DisplayName);
        var profile = await db.GarageProfiles.SingleAsync(p => p.UserId == id);
        Assert.Equal("New Contact", profile.ContactName);
        Assert.Equal(newNext3Id, profile.Next3Id);
        Assert.Null(profile.Address);

        var audit = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.ProfileUpdated && a.EntityId == id);
        Assert.NotNull(audit.ActorUserId);
        Assert.Equal(AuditEntityKinds.GarageProfile, audit.EntityKind);
        Assert.Contains("Garage Renamed", audit.Detail);
    }

    [Fact]
    public async Task UpdateExpert_DuplicateNext3Id_Conflict()
    {
        using var admin = await fixture.CreateAdminClient();
        var taken = NextNext3Id();
        var own = NextNext3Id();
        (await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), taken)))
            .EnsureSuccessStatusCode();
        var create = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), own));
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        var update = await admin.PutAsJsonAsync($"/api/admin/experts/{id}", new
        {
            displayName = "Expert Two",
            email = "expert-two@example.invalid",
            next3Id = taken,
        });
        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(own, (await db.ExpertProfiles.SingleAsync(p => p.UserId == id)).Next3Id); // original intact
    }

    [Fact]
    public async Task Create_WritesProfileCreatedAuditRow_WithActor()
    {
        var adminUser = await fixture.CreateUser(UserRole.Admin, UserStatus.Active);
        var admin = fixture.CreateClient();
        admin.WithBearer((await fixture.Login(admin, adminUser.Phone)).AccessToken);

        var create = await admin.PostAsJsonAsync("/api/admin/brokers", new
        {
            phone = TestPhones.Next(),
            displayName = "Broker Audited",
            irisCode = "IRIS-AUDIT",
            email = "broker-audited@example.invalid",
        });
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        await using var db = fixture.CreateDbContext();
        var audit = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.ProfileCreated && a.EntityId == id);
        Assert.Equal(adminUser.Id, audit.ActorUserId);
        Assert.Equal(AuditEntityKinds.BrokerProfile, audit.EntityKind);
        Assert.Contains("IRIS-AUDIT", audit.Detail);
    }

    [Fact]
    public async Task Deactivate_LeavesInFlightDataUntouched()
    {
        using var admin = await fixture.CreateAdminClient();

        // Victim mid-invite-flow: created, invited, never activated.
        var next3Id = NextNext3Id();
        var create = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), next3Id));
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        // Unrelated bystander whose rows must not move.
        var otherCreate = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), NextNext3Id()));
        var otherId = (await otherCreate.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        AppUser userBefore;
        ExpertProfile profileBefore;
        AppUser otherBefore;
        ExpertProfile otherProfileBefore;
        Invite inviteBefore;
        await using (var db = fixture.CreateDbContext())
        {
            userBefore = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
            profileBefore = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.UserId == id);
            otherBefore = await db.Users.AsNoTracking().SingleAsync(u => u.Id == otherId);
            otherProfileBefore = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.UserId == otherId);
            inviteBefore = await db.Invites.AsNoTracking().SingleAsync(i => i.UserId == id);
        }

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
            Assert.Equal(userBefore.Phone, user.Phone);
            Assert.Equal(userBefore.DisplayName, user.DisplayName);
            Assert.Equal(userBefore.CreatedAt, user.CreatedAt);
            Assert.Equal(UserStatus.Inactive, user.Status); // the only user change (+ inactivated_at)

            var profile = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.UserId == id);
            Assert.Equal(profileBefore.Email, profile.Email);
            Assert.Equal(profileBefore.Next3Id, profile.Next3Id);
            Assert.False(profile.Active); // the only profile change (+ inactivated_at)

            var invite = await db.Invites.AsNoTracking().SingleAsync(i => i.UserId == id);
            Assert.Equal(inviteBefore.TokenHash, invite.TokenHash);
            Assert.Equal(inviteBefore.ExpiresAt, invite.ExpiresAt);
            Assert.Null(invite.UsedAt);

            var other = await db.Users.AsNoTracking().SingleAsync(u => u.Id == otherId);
            Assert.Equal(otherBefore.Status, other.Status);
            Assert.Null(other.InactivatedAt);
            var otherProfile = await db.ExpertProfiles.AsNoTracking().SingleAsync(p => p.UserId == otherId);
            Assert.True(otherProfile.Active);
            Assert.Equal(otherProfileBefore.Next3Id, otherProfile.Next3Id);
        }
    }

    [Fact]
    public async Task Deactivate_Twice_SecondIsNoOp_NoSecondAuditRow()
    {
        using var admin = await fixture.CreateAdminClient();
        var create = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), null));
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);
        DateTime? firstStamp;
        await using (var db = fixture.CreateDbContext())
        {
            firstStamp = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == id)).InactivatedAt;
        }

        fixture.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/users/{id}/deactivate", null)).StatusCode);

        await using (var db2 = fixture.CreateDbContext())
        {
            Assert.Equal(firstStamp, (await db2.Users.AsNoTracking().SingleAsync(u => u.Id == id)).InactivatedAt);
            Assert.Equal(1, await db2.Set<AuditLog>()
                .CountAsync(a => a.Action == AuditActions.UserDeactivated && a.EntityId == id));
        }
    }

    [Fact]
    public async Task Deactivate_UnknownUser_NotFound()
    {
        using var admin = await fixture.CreateAdminClient();
        var response = await admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/deactivate", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReissueInvite_OnInvitedUser_SendsNewToken()
    {
        using var admin = await fixture.CreateAdminClient();
        var phone = TestPhones.Next();
        var create = await admin.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", phone, null));
        var id = (await create.Content.ReadFromJsonAsync<CreatedDto>())!.Id;
        var firstToken = fixture.Sms.LastInviteTokenFor(phone);

        var reissue = await admin.PostAsync($"/api/admin/users/{id}/invite", null);
        Assert.Equal(HttpStatusCode.OK, reissue.StatusCode);
        var secondToken = fixture.Sms.LastInviteTokenFor(phone);
        Assert.NotEqual(firstToken, secondToken);

        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, await db.Invites.CountAsync(i => i.UserId == id));
        Assert.Equal(1, await db.Set<AuditLog>()
            .CountAsync(a => a.Action == AuditActions.InviteIssued && a.EntityId == id));
    }

    [Fact]
    public async Task ReissueInvite_OnActiveUser_Conflict()
    {
        using var admin = await fixture.CreateAdminClient();
        var active = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var response = await admin.PostAsync($"/api/admin/users/{active.Id}/invite", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AdminCrud_ByNonAdmin_IsForbidden()
    {
        var garage = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, garage.Phone)).AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/experts")).StatusCode);
        var create = await client.PostAsJsonAsync("/api/admin/experts", Payload("/api/admin/experts", TestPhones.Next(), null));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    /// <summary>Minimal valid create payload per §4 field set, keyed by endpoint.</summary>
    private static object Payload(string basePath, string phone, string? next3Id) => basePath switch
    {
        "/api/admin/experts" => new
        {
            phone,
            displayName = "Expert Payload",
            email = "expert-payload@example.invalid",
            next3Id,
        },
        "/api/admin/garages" => new
        {
            phone,
            displayName = "Garage Payload",
            contactName = "Garage Contact",
            contactPhone = (string?)null,
            mobile = (string?)null,
            email = "garage-payload@example.invalid",
            next3Id,
            address = (string?)null,
            openingHours = (string?)null,
        },
        "/api/admin/claim-officers" => new
        {
            phone,
            displayName = "Officer Payload",
            next3User = "n3user-payload",
            email = "officer-payload@example.invalid",
        },
        "/api/admin/brokers" => new
        {
            phone,
            displayName = "Broker Payload",
            irisCode = "IRIS-PAYLOAD",
            email = "broker-payload@example.invalid",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(basePath), basePath, null),
    };

    private async Task<HttpClient> ActivateViaInvite(string phone)
    {
        var token = fixture.Sms.LastInviteTokenFor(phone);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/invite/accept", new { token })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(phone);
        var verify = await client.PostAsJsonAsync("/auth/invite/verify", new { token, code });
        verify.EnsureSuccessStatusCode();
        var tokens = (await verify.Content.ReadFromJsonAsync<TokenPairDto>())!;
        return client.WithBearer(tokens.AccessToken);
    }
}
