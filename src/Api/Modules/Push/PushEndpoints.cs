using System.Buffers.Text;
using System.Security.Claims;
using Api.Infrastructure;
using Api.Integrations.Push;
using Api.Modules.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Push;

/// <summary>What the browser hands us after <c>pushManager.subscribe()</c>.</summary>
public sealed record SubscribeRequest(string? Endpoint, string? P256dh, string? Auth);

/// <summary>What the browser sends to stop being notified on this device.</summary>
public sealed record UnsubscribeRequest(string? Endpoint);

/// <summary>What the Capacitor shell hands us after FCM's `registration` event (slice 6.3).</summary>
public sealed record RegisterDeviceTokenRequest(string? Token, string? Platform);

/// <summary>What the shell sends to stop being notified on this handset.</summary>
public sealed record UnregisterDeviceTokenRequest(string? Token);

/// <summary>The public half of the VAPID pair, which the browser needs in order to subscribe at all.</summary>
public sealed record VapidPublicKeyDto(string PublicKey);

/// <summary>
/// Device registration for web push (design.md §8, slice 3.4).
///
/// Under <c>ActiveUser</c> rather than the Expert policy: the expert is the first role to need a
/// popup, but §8 has pushes going to claim officers, garages and brokers, and none of them should
/// need a second copy of this endpoint. Every row is scoped to the caller, so a wider policy grants
/// no wider access.
/// </summary>
public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/push").RequireAuthorization(AuthPolicies.ActiveUser);

        group.MapGet("/vapid-public-key", (IOptions<PushOptions> options) =>
            Results.Ok(new VapidPublicKeyDto(options.Value.Vapid.PublicKey)));

        group.MapPost("/subscriptions", async (
            SubscribeRequest? request, ClaimsPrincipal principal, AppDbContext db,
            IOptions<PushOptions> options, TimeProvider time, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request?.Endpoint)
                || string.IsNullOrWhiteSpace(request.P256dh)
                || string.IsNullOrWhiteSpace(request.Auth))
            {
                return Results.BadRequest(new { error = "subscription_incomplete" });
            }

            // Checked rather than truncated: an endpoint we shortened would be an endpoint no push
            // service recognises, and the failure would appear later as "this expert stopped getting
            // popups" rather than here as a 400.
            if (request.Endpoint.Length > PushSubscriptionLimits.EndpointLength)
            {
                return Results.BadRequest(new { error = "endpoint_too_long" });
            }

            if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpointUri)
                || endpointUri.Scheme != Uri.UriSchemeHttps)
            {
                return Results.BadRequest(new { error = "endpoint_not_https" });
            }

            // **The API will make an outbound HTTPS POST to whatever is stored here.** Without this
            // check, any authenticated user of any role could point it at an arbitrary host —
            // including one reachable only from inside the deployment — and read the outcome from the
            // `notification` log. That is a blind SSRF out of a service §9.1/#21 expects to survive an
            // AXA Group InfoSec reading, and slice 1.5's rate limiting covers `/public/*` only.
            if (!IsKnownPushService(endpointUri, options.Value.AllowedEndpointHosts))
            {
                return Results.BadRequest(new { error = "endpoint_host_not_allowed" });
            }

            // Exact sizes, not upper bounds. A `p256dh` is the 65-byte P-256 point and `auth` is a
            // 16-byte secret; anything else is not a key. This matters because the failure is
            // otherwise silent and total: a short key is stored happily, then throws out of the
            // sender's key-setting call at send time, and before this slice hardened that loop one
            // malformed row would have silenced every other device the expert owns. Same reasoning
            // as PushOptionsValidator checking the VAPID public key to the character.
            if (!IsKeyOf(request.P256dh, PushKeySizes.P256dhBytes))
            {
                return Results.BadRequest(new { error = "invalid_p256dh" });
            }

            if (!IsKeyOf(request.Auth, PushKeySizes.AuthBytes))
            {
                return Results.BadRequest(new { error = "invalid_auth" });
            }

            var now = time.GetUtcNow().UtcDateTime;
            var hash = TokenHashing.Hash(request.Endpoint);

            var existing = await db.Set<PushSubscription>()
                .SingleOrDefaultAsync(s => s.UserId == userId.Value && s.EndpointHash == hash, ct);

            if (existing is not null)
            {
                Refresh(existing, request);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = existing.Id });
            }

            // The sender loops over every live row serially, so an unbounded set is an unbounded
            // stall on the assignment-ingestion path — one user's accumulated dead browsers would
            // delay notifications for everyone. Counted after the upsert branch, so re-registering an
            // existing browser is never refused.
            var live = await db.Set<PushSubscription>()
                .CountAsync(s => s.UserId == userId.Value && s.RevokedAt == null, ct);

            if (live >= options.Value.MaxSubscriptionsPerUser)
            {
                return Results.BadRequest(new { error = "too_many_subscriptions" });
            }

            var subscription = new PushSubscription
            {
                Id = Guid.CreateVersion7(),
                UserId = userId.Value,
                Endpoint = request.Endpoint,
                EndpointHash = hash,
                P256dh = request.P256dh,
                Auth = request.Auth,
                CreatedAt = now,
            };

            db.Set<PushSubscription>().Add(subscription);

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = subscription.Id });
            }
            catch (DbUpdateException)
            {
                // The read above lost a race — two tabs, a double-click, or a retry. The unique index
                // on (user_id, endpoint_hash) is what actually enforces "one row per browser per
                // user"; this catch is how that enforcement is reported as success rather than as a
                // 500, because subscribing twice is not an error the user can act on.
                //
                // 1.5's lesson, fifth slice running: a read-then-write on a uniqueness rule is not a
                // guarantee. Remove the index and this handler silently starts creating duplicates,
                // which is the failure mode the concurrency test pins.
                //
                // **Careful if anything is ever added before the save above.** AuditWriter joins the
                // caller's transaction, so an audit row staged before this point would be discarded
                // here and the endpoint would still answer 200 — the shape of bug slice 2.3 found in
                // the retention sweep. Anything of that kind belongs after the re-read, not before.
                db.ChangeTracker.Clear();

                var winner = await db.Set<PushSubscription>()
                    .SingleOrDefaultAsync(s => s.UserId == userId.Value && s.EndpointHash == hash, ct);

                if (winner is null)
                {
                    // Not the race after all — an FK violation for a deleted user, say. Rethrow the
                    // original with its stack rather than substituting a confusing one.
                    throw;
                }

                Refresh(winner, request);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = winner.Id });
            }
        });

        // `[FromBody]` is not decoration: minimal APIs refuse to *infer* a body parameter on DELETE,
        // and without it the application fails at startup with "Body was inferred but the method does
        // not allow inferred body parameters" — every endpoint down, not just this one.
        //
        // Recorded caveat: some proxies strip a DELETE body. If that ever bites in deployment the fix
        // is a POST route or a query parameter, not a client-side workaround — but the endpoint is up
        // to 2048 characters, which is why it is not a query parameter already.
        group.MapDelete("/subscriptions", async (
            [FromBody] UnsubscribeRequest? request, ClaimsPrincipal principal, AppDbContext db,
            CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request?.Endpoint))
            {
                return Results.BadRequest(new { error = "endpoint_required" });
            }

            var hash = TokenHashing.Hash(request.Endpoint);

            // Scoped to the caller, so knowing someone else's endpoint buys nothing. Deleting rather
            // than revoking: the user asked to stop, which is different from the push service telling
            // us the browser is gone, and re-subscribing later should look like a fresh registration.
            await db.Set<PushSubscription>()
                .Where(s => s.UserId == userId.Value && s.EndpointHash == hash)
                .ExecuteDeleteAsync(ct);

            // Always 204: whether a row was there is not the caller's business, and telling them would
            // make this endpoint answer "does this endpoint belong to someone" for any endpoint.
            return Results.NoContent();
        });

        MapDeviceTokens(group);

        return app;
    }

    /// <summary>
    /// The native half of §8's device registry (slice 6.3): FCM registration tokens from the Android
    /// Capacitor shell, which has no <c>PushManager</c> and therefore cannot use the two routes above
    /// at all (research-capacitor.md §3, observed on the handset).
    ///
    /// Siblings in the same group rather than a module of their own, and under the same
    /// <c>ActiveUser</c> policy: this is the same question — "which devices should this user's popups
    /// go to" — answered for a different device kind. Every row is scoped to the caller, so the wider
    /// policy grants no wider access, exactly as the subscription routes reasoned.
    /// </summary>
    private static void MapDeviceTokens(RouteGroupBuilder group)
    {
        group.MapPost("/device-tokens", async (
            RegisterDeviceTokenRequest? request, ClaimsPrincipal principal, AppDbContext db,
            IOptions<PushOptions> options, TimeProvider time, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request?.Token))
            {
                return Results.BadRequest(new { error = "token_required" });
            }

            // Checked rather than truncated, for the endpoint route's reason one rule up: a token we
            // shortened is a token FCM does not recognise, and the failure would appear later as
            // "this expert stopped getting popups" rather than here as a 400.
            if (request.Token.Length > DeviceTokenLimits.TokenLength)
            {
                return Results.BadRequest(new { error = "token_too_long" });
            }

            // Validated against the same list the check constraint mirrors, so a platform the
            // database would refuse is a 400 here rather than a 500 at SaveChanges. Only `android`
            // today: iOS ships as the installed PWA and arrives through push_subscription (§11's
            // platform split).
            //
            // **Accept loosely, store canonically** (3.1's rule): matched case-insensitively, and
            // what is persisted is the constant, not what the caller sent. SQL Server's default
            // collation makes `[platform] IN ('android')` accept `'Android'` too, so an Ordinal
            // check here and a case-insensitive constraint there disagreed about what is storable —
            // and a seed or repair script could have written a value the database allows and no code
            // recognises. Raised by the db-review.
            var platform = DevicePlatforms.All.FirstOrDefault(known =>
                string.Equals(known, request.Platform, StringComparison.OrdinalIgnoreCase));

            if (platform is null)
            {
                return Results.BadRequest(new { error = "platform_not_supported" });
            }

            var now = time.GetUtcNow().UtcDateTime;
            var hash = TokenHashing.Hash(request.Token);

            // **Registering claims the handset, and anybody else holding it loses it.**
            //
            // This is the one place the push_subscription precedent deliberately does NOT carry, and
            // the db-review caught the copy. An FCM registration token identifies the *app install*,
            // not the person: a browser profile is plausibly one person's, but a field handset is
            // handed over, pooled and re-issued, and signing out does not delete anything. Without
            // this, expert A signs out, garage user B signs in on the same phone, both rows are live
            // on the same token, and every claim assigned to A pops up on B's screen — with the visa
            // number in the body and a deep link into A's assignment. It never self-heals either:
            // FCM only reports UNREGISTERED for a token that is *dead*, and this one is alive.
            //
            // Revoked rather than deleted, so `notification` rows naming the displaced row still
            // resolve and "this expert stopped getting popups on the 14th" stays answerable. Run on
            // every registration, so the handset always belongs to whoever signed in last — and A
            // getting it back is just A registering again.
            await db.Set<DeviceToken>()
                .Where(t => t.TokenHash == hash && t.UserId != userId.Value && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

            var existing = await db.Set<DeviceToken>()
                .SingleOrDefaultAsync(t => t.UserId == userId.Value && t.TokenHash == hash, ct);

            if (existing is not null)
            {
                Refresh(existing, request, platform);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = existing.Id });
            }

            // Counted after the upsert branch, so re-registering a handset the user already has is
            // never refused by the cap — which matters more here than for browsers, because the
            // shell re-registers on launch and FCM rotates tokens on its own schedule.
            var live = await db.Set<DeviceToken>()
                .CountAsync(t => t.UserId == userId.Value && t.RevokedAt == null, ct);

            if (live >= options.Value.MaxDeviceTokensPerUser)
            {
                return Results.BadRequest(new { error = "too_many_device_tokens" });
            }

            var device = new DeviceToken
            {
                Id = Guid.CreateVersion7(),
                UserId = userId.Value,
                Token = request.Token,
                TokenHash = hash,
                Platform = platform,
                CreatedAt = now,
            };

            db.Set<DeviceToken>().Add(device);

            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = device.Id });
            }
            catch (DbUpdateException)
            {
                // The read above lost a race. The unique index on (user_id, token_hash) is what
                // actually enforces "one row per handset per user"; this catch is how that
                // enforcement is reported as success rather than as a 500, because registering twice
                // is not an error the user can act on. 1.5's lesson, sixth slice running.
                //
                // The same caveat the subscriptions route carries applies here: AuditWriter joins the
                // caller's transaction, so anything staged before this save would be discarded here
                // while the endpoint still answered 200. Additions belong after the re-read.
                db.ChangeTracker.Clear();

                var winner = await db.Set<DeviceToken>()
                    .SingleOrDefaultAsync(t => t.UserId == userId.Value && t.TokenHash == hash, ct);

                if (winner is null)
                {
                    // Not the race after all — an FK violation for a deleted user, say. Rethrow the
                    // original with its stack rather than substituting a confusing one.
                    throw;
                }

                Refresh(winner, request, platform);
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { id = winner.Id });
            }
        });

        // `[FromBody]` for the reason spelled out on the DELETE above: minimal APIs refuse to *infer*
        // a body parameter on DELETE, and without it the whole application fails at startup rather
        // than this one route failing at request time.
        group.MapDelete("/device-tokens", async (
            [FromBody] UnregisterDeviceTokenRequest? request, ClaimsPrincipal principal,
            AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(request?.Token))
            {
                return Results.BadRequest(new { error = "token_required" });
            }

            var hash = TokenHashing.Hash(request.Token);

            // Scoped to the caller, so knowing someone else's token buys nothing. Deleting rather
            // than revoking: the user asked to stop, which is different from FCM telling us the
            // install is gone, and re-enabling later should look like a fresh registration.
            await db.Set<DeviceToken>()
                .Where(t => t.UserId == userId.Value && t.TokenHash == hash)
                .ExecuteDeleteAsync(ct);

            // Always 204, for the subscription route's reason: whether a row was there is not the
            // caller's business, and saying would make this endpoint answer "does this token belong
            // to someone" for any token.
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Re-registering refreshes the platform and un-revokes.
    ///
    /// `CreatedAt` is deliberately left alone, for <see cref="Refresh(PushSubscription,
    /// SubscribeRequest)"/>'s reason: the shell registers on every launch, so rewriting it would turn
    /// "when this handset first registered" into "the last time the app was opened" and make every
    /// row look permanently new to any future retention sweep. `LastUsedAt` is the moving value and
    /// the sender owns it.
    /// </summary>
    private static void Refresh(DeviceToken device, RegisterDeviceTokenRequest request, string platform)
    {
        device.Token = request.Token!;
        device.Platform = platform;
        device.RevokedAt = null;
    }

    /// <summary>
    /// Re-subscribing refreshes the keys and un-revokes. (Widths come from
    /// <see cref="PushSubscriptionLimits"/>, beside the entity, so this validation and the column
    /// definitions cannot drift into disagreeing about what fits.) The browser rotates `p256dh`/`auth` on its
    /// own schedule, and a stale pair does not fail loudly — the push is accepted by the service and
    /// then silently fails to decrypt on the device, which is the worst shape a bug can have here.
    /// </summary>
    private static void Refresh(PushSubscription subscription, SubscribeRequest request)
    {
        subscription.Endpoint = request.Endpoint!;
        subscription.P256dh = request.P256dh!;
        subscription.Auth = request.Auth!;
        subscription.RevokedAt = null;

        // `CreatedAt` is deliberately left alone. The panel offers the button again on every fresh
        // session, so this method runs routinely — rewriting it would turn "when this device first
        // registered" into "the last time somebody pressed the button", which is not what the column
        // means on any other table in §4, and would make every row look permanently new to any
        // future retention sweep. `LastUsedAt` is the moving value, and the sender owns it.
    }

    /// <summary>
    /// Whether the endpoint belongs to a push service we are willing to call.
    ///
    /// An empty allow-list means "any https host", which is an explicit opt-out rather than an
    /// accident: the shipped configuration lists the four real services, and a deployment that
    /// deliberately clears it has chosen to accept the SSRF surface described at the call site.
    /// Suffix matching, because these services shard across subdomains.
    /// </summary>
    private static bool IsKnownPushService(Uri endpoint, IReadOnlyList<string> allowedHosts)
    {
        if (allowedHosts.Count == 0)
        {
            return true;
        }

        return allowedHosts.Any(allowed =>
            endpoint.Host.Equals(allowed, StringComparison.OrdinalIgnoreCase)
            || endpoint.Host.EndsWith($".{allowed}", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a value is base64url that decodes to exactly <paramref name="bytes"/> bytes.</summary>
    private static bool IsKeyOf(string value, int bytes)
    {
        // `IsValid` first, and it is load-bearing rather than belt-and-braces: despite the name,
        // `TryDecodeFromChars` **throws** `FormatException` on a non-base64url character — it returns
        // false only when the destination is too small. Without this line a junk key is a 500 instead
        // of the 400 the next few lines exist to produce, which is how this was found.
        if (!Base64Url.IsValid(value))
        {
            return false;
        }

        // Slack on the buffer so an over-long value decodes rather than failing for want of room —
        // the length check is what rejects it, and "wrong size" is the answer we want to give.
        Span<byte> buffer = stackalloc byte[bytes + 8];
        return Base64Url.TryDecodeFromChars(value, buffer, out var written) && written == bytes;
    }
}

/// <summary>
/// The sizes the Push API fixes for a subscription's keys. Bytes rather than characters, because the
/// encoded length depends on padding and the byte count does not.
/// </summary>
public static class PushKeySizes
{
    /// <summary>The uncompressed P-256 point the browser encrypts to.</summary>
    public const int P256dhBytes = 65;

    /// <summary>The shared auth secret.</summary>
    public const int AuthBytes = 16;
}
