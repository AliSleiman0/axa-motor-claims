using System.Collections.Concurrent;
using Api.Integrations.Email;

namespace Api.Tests.Integration;

/// <summary>One captured attachment, with the bytes the sender actually read.</summary>
public sealed record SentAttachment(string FileName, string ContentType, long SizeBytes, byte[] Content);

public sealed record SentEmail(
    string Recipient,
    string Subject,
    string Body,
    string Template,
    IReadOnlyList<SentAttachment> Attachments);

/// <summary>
/// <c>CapturingSmsSender</c>'s shape for email (slice 5.2), and it exists for one assertion the
/// `notification` log deliberately cannot make.
///
/// §4 keeps attachment **bytes** out of the notification payload on purpose — a broker's email carries
/// a customer's identity documents, and copying them into a table nothing sweeps would undo §7.3. So
/// the payload can prove the names and sizes and nothing else, and "the attachments were read from
/// blob storage rather than from whatever the upload held on to" needs the bytes.
///
/// It **delegates to the real <see cref="FakeEmailSender"/>** rather than replacing it, so every
/// existing assertion about `notification` rows — the officer fan-out's email fallback, the failure
/// injection — is untouched.
/// </summary>
public sealed class CapturingEmailSender(FakeEmailSender inner) : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    /// <summary>
    /// The sender this one wraps, so `SenderNotificationTests`' "the app boots fully on fakes" can
    /// still assert what it is rather than being excluded the way `ISmsSender` is.
    /// </summary>
    public IEmailSender Inner => inner;

    public IReadOnlyList<SentEmail> Sent => [.. _sent];

    public SentEmail? LastTo(string recipient) =>
        _sent.LastOrDefault(e => string.Equals(e.Recipient, recipient, StringComparison.Ordinal));

    public IReadOnlyList<SentEmail> AllTo(string recipient) =>
        [.. _sent.Where(e => string.Equals(e.Recipient, recipient, StringComparison.Ordinal))];

    public async Task Send(
        string recipient,
        string subject,
        string body,
        string templateName,
        Guid? recipientUserId,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        // Delegated first, so a send the fake refuses is not recorded as one that happened — the
        // failure path's whole point is that `emailed_at` stays null and nothing went out.
        await inner.Send(recipient, subject, body, templateName, recipientUserId, attachments, ct);

        var captured = new List<SentAttachment>();

        foreach (var attachment in attachments)
        {
            await using var content = await attachment.Open(ct);
            using var buffer = new MemoryStream();

            if (content is not null)
            {
                await content.CopyToAsync(buffer, ct);
            }

            captured.Add(new SentAttachment(
                attachment.FileName, attachment.ContentType, attachment.SizeBytes, buffer.ToArray()));
        }

        _sent.Enqueue(new SentEmail(recipient, subject, body, templateName, captured));
    }
}
