using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users;

public sealed record ExpertCreateDto(string Phone, string DisplayName, string Email, string? Next3Id);

public sealed record ExpertUpdateDto(string DisplayName, string Email, string? Next3Id);

public sealed record ExpertDto(
    Guid Id, string Phone, string DisplayName, string Status,
    string Email, string? Next3Id, bool Active, DateTime? InactivatedAt);

public sealed record GarageCreateDto(
    string Phone, string DisplayName, string ContactName, string? ContactPhone, string? Mobile,
    string Email, string? Next3Id, string? Address, string? OpeningHours);

public sealed record GarageUpdateDto(
    string DisplayName, string ContactName, string? ContactPhone, string? Mobile,
    string Email, string? Next3Id, string? Address, string? OpeningHours);

public sealed record GarageDto(
    Guid Id, string Phone, string DisplayName, string Status,
    string ContactName, string? ContactPhone, string? Mobile, string Email,
    string? Next3Id, string? Address, string? OpeningHours, bool Active, DateTime? InactivatedAt);

public sealed record ClaimOfficerCreateDto(string Phone, string DisplayName, string Next3User, string Email);

public sealed record ClaimOfficerUpdateDto(string DisplayName, string Next3User, string Email);

public sealed record ClaimOfficerDto(
    Guid Id, string Phone, string DisplayName, string Status, string Next3User, string Email);

public sealed record BrokerCreateDto(string Phone, string DisplayName, string IrisCode, string Email);

public sealed record BrokerUpdateDto(string DisplayName, string IrisCode, string Email);

public sealed record BrokerDto(
    Guid Id, string Phone, string DisplayName, string Status, string IrisCode, string Email);

/// <summary>
/// A1 profile CRUD (design.md §5.4): one group per profile type with the §4 field sets.
/// Create issues an invite via slice 1.2's flow; there is no hard delete and no reactivate
/// (deactivation lives on /api/admin/users — both smaller interpretations, recorded).
/// Phone is immutable after create: it is the OTP login identity.
/// </summary>
public static class AdminProfileEndpoints
{
    public static IEndpointRouteBuilder MapAdminProfileEndpoints(this IEndpointRouteBuilder app)
    {
        MapExperts(app.MapGroup("/api/admin/experts").RequireAuthorization(AuthPolicies.Admin));
        MapGarages(app.MapGroup("/api/admin/garages").RequireAuthorization(AuthPolicies.Admin));
        MapClaimOfficers(app.MapGroup("/api/admin/claim-officers").RequireAuthorization(AuthPolicies.Admin));
        MapBrokers(app.MapGroup("/api/admin/brokers").RequireAuthorization(AuthPolicies.Admin));
        return app;
    }

    private static void MapExperts(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Ordered(await ListExperts(db).ToListAsync(ct), e => e.DisplayName));

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var dto = await ListExperts(db, id).SingleOrDefaultAsync(ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapPost("/", async (
            ExpertCreateDto dto, ClaimsPrincipal principal, AppDbContext db, InviteService invites,
            AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            if (dto.Next3Id is not null && await db.ExpertProfiles.AnyAsync(p => p.Next3Id == dto.Next3Id, ct))
            {
                return DuplicateNext3Id();
            }

            var (user, error) = await NewInvitedUser(db, time, dto.Phone, dto.DisplayName, UserRole.Expert, ct);
            if (error is not null)
            {
                return error;
            }

            db.ExpertProfiles.Add(new ExpertProfile
            {
                UserId = user!.Id,
                Next3Id = dto.Next3Id,
                Email = dto.Email,
                Active = true,
            });
            audit.Append(principal.GetUserId(), AuditActions.ProfileCreated,
                AuditEntityKinds.ExpertProfile, user.Id, dto);
            return await SaveThenInvite(db, invites, user.Id, $"/api/admin/experts/{user.Id}", ct);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, ExpertUpdateDto dto, ClaimsPrincipal principal, AppDbContext db,
            AuditWriter audit, CancellationToken ct) =>
        {
            var user = await FindUser(db, id, UserRole.Expert, ct);
            var profile = await db.ExpertProfiles.SingleOrDefaultAsync(p => p.UserId == id, ct);
            if (user is null || profile is null)
            {
                return Results.NotFound();
            }

            if (dto.Next3Id is not null
                && await db.ExpertProfiles.AnyAsync(p => p.Next3Id == dto.Next3Id && p.UserId != id, ct))
            {
                return DuplicateNext3Id();
            }

            user.DisplayName = dto.DisplayName;
            profile.Email = dto.Email;
            profile.Next3Id = dto.Next3Id;
            audit.Append(principal.GetUserId(), AuditActions.ProfileUpdated,
                AuditEntityKinds.ExpertProfile, id, dto);
            return await SaveUpdate(db, ct);
        });
    }

    private static void MapGarages(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Ordered(await ListGarages(db).ToListAsync(ct), g => g.DisplayName));

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var dto = await ListGarages(db, id).SingleOrDefaultAsync(ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapPost("/", async (
            GarageCreateDto dto, ClaimsPrincipal principal, AppDbContext db, InviteService invites,
            AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            if (dto.Next3Id is not null && await db.GarageProfiles.AnyAsync(p => p.Next3Id == dto.Next3Id, ct))
            {
                return DuplicateNext3Id();
            }

            var (user, error) = await NewInvitedUser(db, time, dto.Phone, dto.DisplayName, UserRole.Garage, ct);
            if (error is not null)
            {
                return error;
            }

            db.GarageProfiles.Add(new GarageProfile
            {
                UserId = user!.Id,
                ContactName = dto.ContactName,
                Phone = dto.ContactPhone,
                Mobile = dto.Mobile,
                Email = dto.Email,
                Next3Id = dto.Next3Id,
                Address = dto.Address,
                OpeningHours = dto.OpeningHours,
                Active = true,
            });
            audit.Append(principal.GetUserId(), AuditActions.ProfileCreated,
                AuditEntityKinds.GarageProfile, user.Id, dto);
            return await SaveThenInvite(db, invites, user.Id, $"/api/admin/garages/{user.Id}", ct);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, GarageUpdateDto dto, ClaimsPrincipal principal, AppDbContext db,
            AuditWriter audit, CancellationToken ct) =>
        {
            var user = await FindUser(db, id, UserRole.Garage, ct);
            var profile = await db.GarageProfiles.SingleOrDefaultAsync(p => p.UserId == id, ct);
            if (user is null || profile is null)
            {
                return Results.NotFound();
            }

            if (dto.Next3Id is not null
                && await db.GarageProfiles.AnyAsync(p => p.Next3Id == dto.Next3Id && p.UserId != id, ct))
            {
                return DuplicateNext3Id();
            }

            user.DisplayName = dto.DisplayName;
            profile.ContactName = dto.ContactName;
            profile.Phone = dto.ContactPhone;
            profile.Mobile = dto.Mobile;
            profile.Email = dto.Email;
            profile.Next3Id = dto.Next3Id;
            profile.Address = dto.Address;
            profile.OpeningHours = dto.OpeningHours;
            audit.Append(principal.GetUserId(), AuditActions.ProfileUpdated,
                AuditEntityKinds.GarageProfile, id, dto);
            return await SaveUpdate(db, ct);
        });
    }

    private static void MapClaimOfficers(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Ordered(await ListClaimOfficers(db).ToListAsync(ct), o => o.DisplayName));

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var dto = await ListClaimOfficers(db, id).SingleOrDefaultAsync(ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapPost("/", async (
            ClaimOfficerCreateDto dto, ClaimsPrincipal principal, AppDbContext db, InviteService invites,
            AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            var (user, error) = await NewInvitedUser(
                db, time, dto.Phone, dto.DisplayName, UserRole.ClaimOfficer, ct);
            if (error is not null)
            {
                return error;
            }

            db.ClaimOfficerProfiles.Add(new ClaimOfficerProfile
            {
                UserId = user!.Id,
                Next3User = dto.Next3User,
                Email = dto.Email,
            });
            audit.Append(principal.GetUserId(), AuditActions.ProfileCreated,
                AuditEntityKinds.ClaimOfficerProfile, user.Id, dto);
            return await SaveThenInvite(db, invites, user.Id, $"/api/admin/claim-officers/{user.Id}", ct);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, ClaimOfficerUpdateDto dto, ClaimsPrincipal principal, AppDbContext db,
            AuditWriter audit, CancellationToken ct) =>
        {
            var user = await FindUser(db, id, UserRole.ClaimOfficer, ct);
            var profile = await db.ClaimOfficerProfiles.SingleOrDefaultAsync(p => p.UserId == id, ct);
            if (user is null || profile is null)
            {
                return Results.NotFound();
            }

            user.DisplayName = dto.DisplayName;
            profile.Next3User = dto.Next3User;
            profile.Email = dto.Email;
            audit.Append(principal.GetUserId(), AuditActions.ProfileUpdated,
                AuditEntityKinds.ClaimOfficerProfile, id, dto);
            return await SaveUpdate(db, ct);
        });
    }

    private static void MapBrokers(RouteGroupBuilder group)
    {
        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Ordered(await ListBrokers(db).ToListAsync(ct), b => b.DisplayName));

        group.MapGet("/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var dto = await ListBrokers(db, id).SingleOrDefaultAsync(ct);
            return dto is null ? Results.NotFound() : Results.Ok(dto);
        });

        group.MapPost("/", async (
            BrokerCreateDto dto, ClaimsPrincipal principal, AppDbContext db, InviteService invites,
            AuditWriter audit, TimeProvider time, CancellationToken ct) =>
        {
            var (user, error) = await NewInvitedUser(db, time, dto.Phone, dto.DisplayName, UserRole.Broker, ct);
            if (error is not null)
            {
                return error;
            }

            db.BrokerProfiles.Add(new BrokerProfile
            {
                UserId = user!.Id,
                IrisCode = dto.IrisCode,
                Email = dto.Email,
            });
            audit.Append(principal.GetUserId(), AuditActions.ProfileCreated,
                AuditEntityKinds.BrokerProfile, user.Id, dto);
            return await SaveThenInvite(db, invites, user.Id, $"/api/admin/brokers/{user.Id}", ct);
        });

        group.MapPut("/{id:guid}", async (
            Guid id, BrokerUpdateDto dto, ClaimsPrincipal principal, AppDbContext db,
            AuditWriter audit, CancellationToken ct) =>
        {
            var user = await FindUser(db, id, UserRole.Broker, ct);
            var profile = await db.BrokerProfiles.SingleOrDefaultAsync(p => p.UserId == id, ct);
            if (user is null || profile is null)
            {
                return Results.NotFound();
            }

            user.DisplayName = dto.DisplayName;
            profile.IrisCode = dto.IrisCode;
            profile.Email = dto.Email;
            audit.Append(principal.GetUserId(), AuditActions.ProfileUpdated,
                AuditEntityKinds.BrokerProfile, id, dto);
            return await SaveUpdate(db, ct);
        });
    }

    // The id filter is applied BEFORE the projection: ToDbValue() is client-evaluated, which EF
    // allows only in a final projection — filtering the projected DTO afterwards throws at runtime.
    private static IQueryable<ExpertDto> ListExperts(AppDbContext db, Guid? id = null) =>
        Users(db, UserRole.Expert, id)
            .Join(db.ExpertProfiles, u => u.Id, p => p.UserId, (u, p) => new ExpertDto(
                u.Id, u.Phone, u.DisplayName, u.Status.ToDbValue(),
                p.Email, p.Next3Id, p.Active, p.InactivatedAt));

    private static IQueryable<GarageDto> ListGarages(AppDbContext db, Guid? id = null) =>
        Users(db, UserRole.Garage, id)
            .Join(db.GarageProfiles, u => u.Id, p => p.UserId, (u, p) => new GarageDto(
                u.Id, u.Phone, u.DisplayName, u.Status.ToDbValue(),
                p.ContactName, p.Phone, p.Mobile, p.Email,
                p.Next3Id, p.Address, p.OpeningHours, p.Active, p.InactivatedAt));

    private static IQueryable<ClaimOfficerDto> ListClaimOfficers(AppDbContext db, Guid? id = null) =>
        Users(db, UserRole.ClaimOfficer, id)
            .Join(db.ClaimOfficerProfiles, u => u.Id, p => p.UserId, (u, p) => new ClaimOfficerDto(
                u.Id, u.Phone, u.DisplayName, u.Status.ToDbValue(), p.Next3User, p.Email));

    private static IQueryable<BrokerDto> ListBrokers(AppDbContext db, Guid? id = null) =>
        Users(db, UserRole.Broker, id)
            .Join(db.BrokerProfiles, u => u.Id, p => p.UserId, (u, p) => new BrokerDto(
                u.Id, u.Phone, u.DisplayName, u.Status.ToDbValue(), p.IrisCode, p.Email));

    /// <summary>
    /// The four profile CRUDs' shared source. The list path is ordered and capped since slice 7.2;
    /// the by-id path is neither, because it returns one row.
    /// </summary>
    /// <remarks>
    /// The ordering here is not the one A1 displays — it is what makes *which* two hundred rows the
    /// cap keeps deterministic, which an unordered <c>Take</c> leaves to the query plan. The display
    /// order is applied by <see cref="Ordered{T}"/> after materialisation, because these DTOs project
    /// <c>Status.ToDbValue()</c>, which is client-evaluated: EF allows that only in a final
    /// projection, so nothing can be ordered on the server *after* it.
    /// </remarks>
    private static IQueryable<AppUser> Users(AppDbContext db, UserRole role, Guid? id)
    {
        var users = db.Users.AsNoTracking().Where(u => u.Role == role);

        return id is null
            ? users.OrderBy(u => u.DisplayName).Take(ListLimits.MaxRows)
            : users.Where(u => u.Id == id);
    }

    /// <summary>
    /// A1's display order: by name, case-insensitively, over at most <see cref="ListLimits.MaxRows"/>
    /// rows (slice 7.2).
    /// </summary>
    /// <remarks>
    /// These four lists had no <c>ORDER BY</c> at all — whatever SQL Server returned was the order an
    /// admin saw, which is tolerable while it is also the whole table and not once a cap can drop the
    /// tail. Alphabetical because A1 is a list somebody scans for a person by name; the four other
    /// worklists in the product are newest-first because they are queues, and this one is not.
    /// Sorted in memory rather than in SQL so the comparison is a stated one rather than whatever
    /// collation the database happens to carry — nothing in this codebase configures one.
    /// </remarks>
    private static List<T> Ordered<T>(List<T> rows, Func<T, string> name) =>
        rows.OrderBy(name, StringComparer.OrdinalIgnoreCase).ToList();

    private static Task<AppUser?> FindUser(AppDbContext db, Guid id, UserRole role, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.Id == id && u.Role == role, ct);

    private static async Task<(AppUser? User, IResult? Error)> NewInvitedUser(
        AppDbContext db, TimeProvider time, string phone, string displayName, UserRole role, CancellationToken ct)
    {
        if (!Phone.IsValidE164(phone))
        {
            return (null, Results.BadRequest(new { error = "invalid_phone" }));
        }

        if (await db.Users.AnyAsync(u => u.Phone == phone, ct))
        {
            return (null, Results.Conflict(new { error = "duplicate_phone" }));
        }

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            Phone = phone,
            Role = role,
            DisplayName = displayName,
            Status = UserStatus.Invited,
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        db.Users.Add(user);
        return (user, null);
    }

    /// <summary>
    /// Commits user + profile + audit in one transaction, then issues the invite (§5.4
    /// "create issues an invite") as its own save + SMS — a failed invite is recoverable
    /// via POST /api/admin/users/{id}/invite.
    /// </summary>
    private static async Task<IResult> SaveThenInvite(
        AppDbContext db, InviteService invites, Guid userId, string location, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race backstop: the unique indexes (phone, next3_id) are the real guard.
            return Results.Conflict(new { error = "duplicate" });
        }

        await invites.Issue(userId, ct);
        return Results.Created(location, new { id = userId });
    }

    private static async Task<IResult> SaveUpdate(AppDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { error = "duplicate" });
        }
    }

    private static IResult DuplicateNext3Id() => Results.Conflict(new { error = "duplicate_next3_id" });
}
