using Api.Integrations;
using Api.Integrations.Email;
using Api.Integrations.Push;
using Api.Integrations.Sms;
using Api.Modules.Notifications;
using Api.Modules.Users;
using Api.Tests.Integrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §8: every send is logged to `notification`. These go through the real fakes rather than
/// the booted app's ISmsSender, which ApiFixture replaces with a capturing double that writes no rows.
/// </summary>
[Collection("api")]
public sealed class SenderNotificationTests(ApiFixture fixture)
{
    [Fact]
    public async Task Sms_Send_LogsSentRow_WithoutStoringTheCredential()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var sender = new FakeSmsSender(NullLogger<FakeSmsSender>.Instance, NotificationLog(), FakeBehavior());

        await sender.Send(user.Phone, "Your verification code is 123456", "test_sms", user.Id, CancellationToken.None);

        var row = await SingleRow(n => n.RecipientAddress == user.Phone);
        Assert.Equal(NotificationChannels.Sms, row.Channel);
        Assert.Equal(NotificationStatuses.Sent, row.Status);
        Assert.Equal("test_sms", row.Template);
        Assert.Equal(user.Id, row.RecipientUserId);
        Assert.NotNull(row.SentAt);
        Assert.Null(row.Error);

        // §9 hashes OTP codes and invite tokens so a DB leak hands out no working logins; copying
        // the message body into the send log would give exactly that back.
        Assert.Null(row.Payload);
    }

    [Fact]
    public async Task Email_Send_LogsSentRow_WithPayload()
    {
        var recipient = $"PLACEHOLDER-{Guid.NewGuid():N}@example.invalid";
        var sender = new FakeEmailSender(NullLogger<FakeEmailSender>.Instance, NotificationLog(), FakeBehavior());

        await sender.Send(recipient, "PLACEHOLDER subject", "PLACEHOLDER body", "test_email", null, CancellationToken.None);

        var row = await SingleRow(n => n.RecipientAddress == recipient);
        Assert.Equal(NotificationChannels.Email, row.Channel);
        Assert.Equal(NotificationStatuses.Sent, row.Status);
        Assert.Null(row.RecipientUserId);
        Assert.Contains("PLACEHOLDER body", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_Send_LogsSentRow_AgainstTheRecipientUser()
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var sender = new FakePushSender(NullLogger<FakePushSender>.Instance, NotificationLog(), FakeBehavior());

        await sender.Send(
            user.Id,
            new PushMessage("PLACEHOLDER title", "PLACEHOLDER body", "/expert/PLACEHOLDER"),
            "test_push",
            CancellationToken.None);

        var row = await SingleRow(n => n.RecipientUserId == user.Id && n.Channel == NotificationChannels.Push);
        Assert.Equal(NotificationStatuses.Sent, row.Status);
        Assert.Equal(user.Id.ToString(), row.RecipientAddress);
        Assert.Contains("PLACEHOLDER title", row.Payload, StringComparison.Ordinal);
        // The payload is the JSON the service worker reads (slice 3.4), so the click target is part
        // of what gets logged — "the popup arrived but went nowhere" is otherwise unanswerable.
        Assert.Contains("/expert/PLACEHOLDER", row.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_WhenFailureInjected_LogsFailedRowAndThrows()
    {
        var recipient = $"PLACEHOLDER-{Guid.NewGuid():N}@example.invalid";
        var sender = new FakeEmailSender(
            NullLogger<FakeEmailSender>.Instance, NotificationLog(), FakeBehavior(failureRate: 1));

        await Assert.ThrowsAsync<FakeTransientException>(() =>
            sender.Send(recipient, "PLACEHOLDER subject", "PLACEHOLDER body", "test_email", null, CancellationToken.None));

        var row = await SingleRow(n => n.RecipientAddress == recipient);
        Assert.Equal(NotificationStatuses.Failed, row.Status);
        Assert.Null(row.SentAt);
        Assert.NotNull(row.Error);
        Assert.NotEqual(default, row.CreatedAt);
    }

    [Fact]
    public async Task OtpRequest_ThroughTheBootedApp_ResolvesAllFivePortsAsFakes()
    {
        // The DoD's "app boots fully on fakes", asserted rather than eyeballed. ISmsSender is
        // excluded: ApiFixture deliberately replaces it with the capturing double.
        var services = fixture.Services;

        Assert.IsType<Api.Integrations.Next3.FakeNext3Client>(
            services.GetRequiredService<Api.Integrations.Next3.INext3Client>());
        Assert.IsType<Api.Integrations.Next3.FakeAssignmentSource>(
            services.GetRequiredService<Api.Integrations.Next3.IAssignmentSource>());
        Assert.IsType<FakeEmailSender>(services.GetRequiredService<IEmailSender>());
        Assert.IsType<FakePushSender>(services.GetRequiredService<IPushSender>());
        Assert.NotNull(services.GetRequiredService<ISmsSender>());
    }

    private NotificationLog NotificationLog() =>
        new(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Time);

    private static FakeBehavior FakeBehavior(double failureRate = 0) =>
        FakeTestHarness.Build(failureRate).Behavior;

    private async Task<Notification> SingleRow(
        System.Linq.Expressions.Expression<Func<Notification, bool>> predicate)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Notification>().AsNoTracking().SingleAsync(predicate);
    }
}
