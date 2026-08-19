using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Api.Integrations.Sms;

namespace Api.Tests.Integration;

/// <summary>Test double replacing the fake sender: captures messages so tests can read OTP codes.</summary>
public sealed partial class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    // Writes no `notification` rows — tests asserting the send log must exercise the real
    // FakeSmsSender, not this double (see SenderNotificationTests).
    public Task Send(
        string phone,
        string message,
        string templateName,
        Guid? recipientUserId,
        CancellationToken ct)
    {
        _sent.Enqueue((phone, message));
        return Task.CompletedTask;
    }

    public int CountFor(string phone) => _sent.Count(m => m.Phone == phone);

    public string LastOtpFor(string phone)
    {
        var message = _sent.Where(m => m.Phone == phone).Last().Message;
        return SixDigits().Match(message).Value;
    }

    public string LastInviteTokenFor(string phone)
    {
        var message = _sent.Where(m => m.Phone == phone && m.Message.Contains("invite token")).Last().Message;
        return InviteToken().Match(message).Groups[1].Value;
    }

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigits();

    [GeneratedRegex(@"invite token is ([A-Za-z0-9_-]+)")]
    private static partial Regex InviteToken();
}
