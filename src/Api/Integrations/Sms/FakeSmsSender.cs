namespace Api.Integrations.Sms;

/// <summary>Dev/demo sender: the OTP code shows up in the console instead of a phone (#7/#40).</summary>
public sealed partial class FakeSmsSender(ILogger<FakeSmsSender> logger) : ISmsSender
{
    [LoggerMessage(Level = LogLevel.Information, Message = "FAKE SMS to {Phone}: {Message}")]
    private static partial void LogSms(ILogger logger, string phone, string message);

    public Task Send(string phone, string message, CancellationToken ct)
    {
        LogSms(logger, phone, message);
        return Task.CompletedTask;
    }
}
