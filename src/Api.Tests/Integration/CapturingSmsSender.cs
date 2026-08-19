using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Api.Integrations.Sms;

namespace Api.Tests.Integration;

/// <summary>Test double replacing the fake sender: captures messages so tests can read OTP codes.</summary>
public sealed partial class CapturingSmsSender : ISmsSender
{
    private readonly ConcurrentQueue<(string Phone, string Message)> _sent = new();

    public Task Send(string phone, string message, CancellationToken ct)
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

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigits();
}
