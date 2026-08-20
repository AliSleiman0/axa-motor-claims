using System.Net.Sockets;

namespace Api.Tests.Media;

/// <summary>
/// A fact that runs only when Azurite is listening, and reports itself skipped when it is not.
///
/// The suite must stay green on a machine with no Docker — `Blob:Mode` is `fake` everywhere for
/// exactly that reason — but the real adapter then has no test at all, which is how a thin wrapper
/// quietly stops working. This is the compromise: the contract is asserted against the emulator
/// whenever the developer has it up (and in CI once §10's pipeline runs one), and skipped otherwise.
/// The same shape slice 3.3 will want for fake-vs-sandbox NEXT3 contract tests.
/// </summary>
public sealed class AzuriteFactAttribute : FactAttribute
{
    public const int BlobPort = 10_000;

    private static readonly Lazy<bool> Available = new(Probe, isThreadSafe: true);

    public AzuriteFactAttribute()
    {
        if (!Available.Value)
        {
            Skip = $"Azurite is not listening on localhost:{BlobPort} (see CLAUDE.md for the command).";
        }
    }

    private static bool Probe()
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", BlobPort).Wait(TimeSpan.FromMilliseconds(500))
                && client.Connected;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (AggregateException)
        {
            return false;
        }
    }
}
