namespace Api.Integrations.Sms;

public interface ISmsSender
{
    Task Send(string phone, string message, CancellationToken ct);
}
