using System.Text;
using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations;
using Api.Integrations.Blob;
using Api.Integrations.Email;
using Api.Integrations.Next3;
using Api.Integrations.Push;
using Api.Integrations.Sms;
using Api.Modules.Audit;
using Api.Modules.Broker;
using Api.Modules.Claims;
using Api.Modules.Declarations;
using Api.Modules.Expert;
using Api.Modules.Media;
using Api.Modules.Notifications;
using Api.Modules.PublicSurface;
using Api.Modules.Push;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Composition;

public static class ServiceRegistration
{
    public static IServiceCollection AddAxaMotorClaims(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is missing.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<CleanupWorker>();

        services.TryAddSingleton(TimeProvider.System);

        AddPorts(services, configuration);

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<PublicLinkOptions>(configuration.GetSection(PublicLinkOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<MediaOptions>(configuration.GetSection(MediaOptions.SectionName));
        services.Configure<ClarityOptions>(configuration.GetSection(ClarityOptions.SectionName));
        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.SectionName));
        services.Configure<Next3Options>(configuration.GetSection(Next3Options.SectionName));

        // §5.3's routing table (#13), type list (#14) and the BRD's upload kill-switch. **Validated in
        // every environment**, unlike the Next3 and Push validators below: the broker placeholders are
        // well-formed, so an always-on check passes today and turns a mistyped insurance type into a
        // container that will not start rather than a 500 the first broker sees.
        services.AddSingleton<IValidateOptions<BrokerOptions>, BrokerOptionsValidator>();
        services.AddOptions<BrokerOptions>()
            .Bind(configuration.GetSection(BrokerOptions.SectionName))
            .ValidateOnStart();

        // The §6.3 queue. The writer is scoped because it joins the caller's transaction; the
        // processor is a singleton that opens its own scope per pass (NotificationLog's shape).
        services.AddScoped<OutboxWriter>();
        services.AddScoped<OutboxDequeue>();
        services.AddScoped<OutboxSentQuery>();
        services.AddSingleton<OutboxProcessor>();

        // §7's media pipeline. The upload service is scoped so its document row, outbox row and audit
        // row all join one SaveChanges; the cleanup runner is a singleton opening its own scope, like
        // the outbox processor, and each sweep is registered by the module that owns its data.
        services.AddScoped<MediaUploadService>();
        // §7.1's table as configuration leaves it — every asker of "may this bucket take an uploaded
        // file?" must go through here, or the upload's answer and /api/config/media's can drift.
        services.AddScoped<BucketRules>();
        services.AddScoped<IBucketRuleOverride, BrokerUploadSwitch>();
        services.AddScoped<DocumentBlobSweeper>();
        services.AddSingleton<CleanupRunner>();
        services.AddScoped<ICleanupTask, MediaBlobCleanupTask>();
        services.AddScoped<ICleanupTask, BrokerMediaCleanupTask>();
        services.AddScoped<ICleanupTask, OtpChallengeCleanupTask>();
        // Slice 7.2's three. Registered here beside the others, but each lives in the module that
        // owns its data — the re-queue and the rejected-declaration sweep read declarations, the
        // prune reads §8's device tables — which is what ICleanupTask's own doc comment asks for.
        services.AddScoped<ICleanupTask, StrandedDeferredRequeueTask>();
        services.AddScoped<ICleanupTask, RejectedDeclarationBlobCleanupTask>();
        services.AddScoped<ICleanupTask, DeviceRegistryCleanupTask>();
        services.AddPublicRateLimiting();
        services.AddScoped<PublicLinkTokenService>();
        services.AddScoped<ClaimCache>();

        // §5.3's Option 1 (slice 5.2). No NEXT3 anywhere in it — that is the module's defining
        // property, and what keeps architecture rules 3 and 4 green without a new rule.
        services.AddScoped<BrokerRequestService>();
        services.AddScoped<BrokerRequestEmail>();

        // Slice 5.3. Registered with the broker module rather than the public one because that is
        // where it lives: §9.1's public endpoint calls it with a request id so no user type crosses
        // architecture rule 2's boundary.
        services.AddScoped<BrokerRequestNotifier>();

        // §5.2's transitions and the transactions they own (slice 4.1).
        services.AddScoped<DeclarationService>();
        services.AddScoped<AssignmentHandler>();
        // Subscribes the single idempotent handler to whichever assignment source is configured
        // (§6.2). Fails at startup rather than at first delivery if the source is unimplemented.
        services.AddHostedService<AssignmentIngestion>();
        services.AddScoped<OtpService>();
        services.AddScoped<TokenService>();
        services.AddScoped<InviteService>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<IAuthorizationHandler, ActiveUserHandler>();

        var jwt = configuration.GetSection("Auth:Jwt").Get<JwtOptions>()
            ?? throw new InvalidOperationException("Configuration section 'Auth:Jwt' is missing.");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // Keep the raw "sub"/"role" claim names — the default inbound mapping renames
                // them and silently breaks RequireRole. ClockSkew 0 so short TTLs mean what they say.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    RoleClaimType = "role",
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.Zero,
                };
            });

        // Lifetime is checked against the app's TimeProvider so tests can travel time.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<TimeProvider>((options, timeProvider) =>
                options.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = timeProvider.GetUtcNow().UtcDateTime;
                    return (notBefore is null || notBefore <= now) && expires is not null && now < expires;
                });

        var activeUser = new ActiveUserRequirement();
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Expert, p => p.RequireRole(UserRoles.Expert).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.Garage, p => p.RequireRole(UserRoles.Garage).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.ClaimOfficer, p => p.RequireRole(UserRoles.ClaimOfficer).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.Broker, p => p.RequireRole(UserRoles.Broker).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.Admin, p => p.RequireRole(UserRoles.Admin).AddRequirements(activeUser))
            .AddPolicy(AuthPolicies.ActiveUser, p => p.RequireAuthenticatedUser().AddRequirements(activeUser));

        return services;
    }

    /// <summary>
    /// The five ports of design.md §6.2/§3 and their implementations. This method is the only place
    /// in the codebase allowed to name a concrete NEXT3 implementation (arch rule 1) — everything
    /// else depends on the interface and cannot tell which one is live.
    /// </summary>
    private static void AddPorts(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FakeOptions>(configuration.GetSection(FakeOptions.SectionName));
        services.AddSingleton<FakeBehavior>();
        services.AddSingleton<NotificationLog>();

        // The sixth port (§3's Azure Blob, §7.3's transit buffer). Same shape as Next3:Mode: the fake
        // is the default so nothing — tests included — needs a storage emulator to be running.
        services.Configure<BlobOptions>(configuration.GetSection(BlobOptions.SectionName));
        services.AddSingleton<InMemoryBlobStore>();
        var blobMode = configuration[$"{BlobOptions.SectionName}:Mode"] ?? "fake";
        services.AddSingleton<IBlobStore>(sp => blobMode switch
        {
            "fake" => sp.GetRequiredService<InMemoryBlobStore>(),
            "azure" => ActivatorUtilities.CreateInstance<AzureBlobStore>(sp),
            _ => throw new InvalidOperationException(
                $"Unknown Blob:Mode '{blobMode}'. Expected 'fake' or 'azure' (see CLAUDE.md)."),
        });

        // Senders are singletons. SMS and email have no real implementation yet (#7/#40 for the SMS
        // gateway; the email provider is equally unanswered). Each fake logs to `notification` (§8).
        services.AddSingleton<ISmsSender, FakeSmsSender>();
        services.AddSingleton<IEmailSender, FakeEmailSender>();

        // Push is the first sender to get a real implementation (slice 3.4), so it gets the same mode
        // switch as Blob and NEXT3 — third outing. `fake` everywhere by default: web push needs a
        // VAPID key pair, and nothing (tests, a fresh clone, the week-4 demo) should require one.
        services.Configure<PushOptions>(configuration.GetSection(PushOptions.SectionName));
        services.AddSingleton<FakePushSender>();

        var pushMode = configuration[$"{PushOptions.SectionName}:Mode"] ?? PushModes.Fake;

        // Registered in both modes so the container's shape does not depend on configuration; in fake
        // mode nothing ever resolves it.
        services.AddHttpClient(PushHttpClient.Name, (sp, client) =>
        {
            var push = sp.GetRequiredService<IOptions<PushOptions>>().Value;
            if (push.TimeoutSeconds > 0)
            {
                client.Timeout = TimeSpan.FromSeconds(push.TimeoutSeconds);
            }
        });

        // The third named client, after `next3` and `webpush` (slice 6.3). Registered unconditionally
        // for the reason above, and carrying its own timeout because FCM's deadline is configured
        // separately from web push's — a hung Google must fail one handset, not hold the
        // assignment-ingestion path open for everybody.
        services.AddHttpClient(FcmHttpClient.Name, (sp, client) =>
        {
            var fcm = sp.GetRequiredService<IOptions<PushOptions>>().Value.Fcm;
            if (fcm.TimeoutSeconds > 0)
            {
                client.Timeout = TimeSpan.FromSeconds(fcm.TimeoutSeconds);
            }
        });

        // Read once, beside the mode, so the composite's shape and the validator's gate cannot
        // disagree about whether FCM is on.
        var fcmEnabled = configuration.GetValue($"{PushOptions.SectionName}:Fcm:Enabled", false);

        if (string.Equals(pushMode, PushModes.WebPush, StringComparison.Ordinal))
        {
            // Only in the live mode, for the reason PushOptionsValidator spells out: the placeholder
            // VAPID values are what every environment runs on today, so an always-on validator would
            // stop the application booting everywhere.
            services.AddSingleton<IValidateOptions<PushOptions>, PushOptionsValidator>();

            if (fcmEnabled)
            {
                // Gated **twice** — live mode and FCM switched on — because `Enabled` is false
                // everywhere and Appendix A's `Push:Fcm:*` are placeholders, so a validator that
                // fired on mode alone would stop every webpush deployment booting the moment this
                // slice landed. Same argument as the outer gate, one level in.
                services.AddSingleton<IValidateOptions<PushOptions>, FcmOptionsValidator>();
            }

            services.AddOptions<PushOptions>()
                .Bind(configuration.GetSection(PushOptions.SectionName))
                .ValidateOnStart();
        }

        // Singleton and shared by every FCM send: the OAuth exchange is a network round trip and the
        // token it returns lasts an hour, so minting one per push would add a second remote call to
        // the assignment path for nothing. Registered in both modes so the container's shape does not
        // depend on configuration; in fake mode nothing resolves it.
        services.AddSingleton<FcmAccessTokens>();

        services.AddSingleton<IPushSender>(sp => pushMode switch
        {
            PushModes.Fake => sp.GetRequiredService<FakePushSender>(),

            // **Web push and FCM are additive, never alternatives** (slice 6.3). A user legitimately
            // has a browser and a phone, so the live mode composes every channel this deployment has
            // rather than choosing one. With FCM off there is a single channel and the behaviour is
            // exactly what shipped in 3.4 — which is what makes turning it on a config change.
            PushModes.WebPush => ActivatorUtilities.CreateInstance<CompositePushSender>(
                sp,
                (IReadOnlyList<IPushSender>)(fcmEnabled
                    ? new IPushSender[]
                    {
                        ActivatorUtilities.CreateInstance<WebPushSender>(sp),
                        ActivatorUtilities.CreateInstance<FcmPushSender>(sp),
                    }
                    : [ActivatorUtilities.CreateInstance<WebPushSender>(sp)])),

            _ => throw new InvalidOperationException(
                $"Unknown Push:Mode '{pushMode}'. Expected '{PushModes.Fake}' or "
                + $"'{PushModes.WebPush}' (design.md §8)."),
        });

        // Singleton: the fake's seeded claims and its clientRef sent-log are state that must outlive
        // a request and be shared with the outbox worker.
        services.AddSingleton<FakeNext3Client>();

        var mode = configuration["Next3:Mode"] ?? "fake";

        // The app's first IHttpClientFactory (slice 3.3). Registered in both modes so the container
        // shape does not depend on configuration; in fake mode nothing ever resolves it.
        // Microsoft.Extensions.Http ships with the web SDK, so this needs no package reference.
        services.AddHttpClient(Next3HttpClient.Name, (sp, client) =>
        {
            var next3 = sp.GetRequiredService<IOptions<Next3Options>>().Value;

            // Only when it is usable: BaseAddress rejects a malformed URI, and every value in
            // Appendix A's Next3 section is a PLACEHOLDER until #1 answers. Real mode has already
            // been validated by then (Next3OptionsValidator); fake mode must still boot.
            if (Uri.TryCreate(next3.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                // Trailing slash, or Uri resolution silently drops the last path segment of BaseUrl —
                // "https://host/api" + "claims/X" would request "https://host/claims/X".
                client.BaseAddress = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
            }

            if (next3.TimeoutSeconds > 0)
            {
                client.Timeout = TimeSpan.FromSeconds(next3.TimeoutSeconds);
            }
        });

        // Singleton so the OAuth token is cached across outbox passes rather than re-fetched per push.
        services.AddSingleton<Next3TokenProvider>();

        if (string.Equals(mode, "real", StringComparison.Ordinal))
        {
            // Fail-fast, and **only in real mode** — see Next3OptionsValidator for why this is not
            // constructor validation, and why an always-on validator would stop the app booting on
            // the placeholder file that every other environment runs on today.
            services.AddSingleton<IValidateOptions<Next3Options>, Next3OptionsValidator>();
            services.AddOptions<Next3Options>()
                .Bind(configuration.GetSection(Next3Options.SectionName))
                .ValidateOnStart();
        }
        services.AddSingleton<INext3Client>(sp => mode switch
        {
            "fake" => sp.GetRequiredService<FakeNext3Client>(),
            "real" => ActivatorUtilities.CreateInstance<RealNext3Client>(sp),
            _ => throw new InvalidOperationException(
                $"Unknown Next3:Mode '{mode}'. Expected 'fake' or 'real' (design.md §6.2)."),
        });

        var assignmentSource = configuration["Next3:AssignmentSource"] ?? "fake";
        services.AddSingleton<FakeAssignmentSource>();
        services.AddSingleton<IAssignmentSource>(sp => assignmentSource switch
        {
            "fake" => sp.GetRequiredService<FakeAssignmentSource>(),
            "oracle-poll" => sp.GetRequiredService<OraclePollAssignmentSource>(),
            // #34 is resolved (2026-08-31): the client rules out a webhook and gives the app a
            // direct Oracle poll instead — 'oracle-poll' above. 'webhook' and bare 'poll' stay
            // deliberately unsupported rather than repurposed, so a stale config value fails loudly
            // instead of silently meaning something different than it used to.
            "webhook" or "poll" => throw new InvalidOperationException(
                $"Next3:AssignmentSource '{assignmentSource}' is not implemented (#34 chose "
                + "'oracle-poll' instead)."),
            _ => throw new InvalidOperationException(
                $"Unknown Next3:AssignmentSource '{assignmentSource}'. Expected 'webhook', 'poll', "
                + "'oracle-poll' or 'fake'."),
        });

        // Registered only in this mode, not unconditionally like OutboxWorker/CleanupWorker: those
        // two loops do real work in every environment, but Next3:AssignmentSource stays 'fake'
        // everywhere until real Oracle connection details land (#34), so an always-registered
        // OraclePollWorker would just be a permanently idle background loop — worse than not
        // registering it at all.
        if (string.Equals(assignmentSource, "oracle-poll", StringComparison.Ordinal))
        {
            services.AddSingleton<IValidateOptions<Next3Options>, Next3OracleOptionsValidator>();
            services.AddOptions<Next3Options>()
                .Bind(configuration.GetSection(Next3Options.SectionName))
                .ValidateOnStart();

            services.AddSingleton<OraclePollAssignmentSource>();
            services.AddSingleton<IAssignmentQuerySource, OracleAssignmentQuerySource>();
            services.AddSingleton<OraclePollRunner>();
            services.AddHostedService<OraclePollWorker>();
        }
    }
}
