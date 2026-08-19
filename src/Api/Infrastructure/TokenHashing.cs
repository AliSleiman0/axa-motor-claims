using System.Security.Cryptography;
using System.Text;

namespace Api.Infrastructure;

/// <summary>
/// The one way this application stores a bearer secret: SHA-256, hex-encoded, raw value never
/// persisted (design.md §9 / §9.1). Used by invites, refresh tokens and Option 2 public links —
/// three call sites that must not drift apart, since a mismatch shows up as "tokens silently stop
/// validating" rather than as a failing build.
/// </summary>
/// <remarks>
/// Deliberately unsalted and fast: these are 256-bit CSPRNG values, not passwords. A salt would
/// defeat the lookup-by-hash these tables are built on, and stretching would buy nothing against
/// an input that cannot be guessed or dictionary-attacked.
/// </remarks>
public static class TokenHashing
{
    /// <summary>Hex length of a SHA-256 digest — the <c>token_hash</c> column width.</summary>
    public const int HashLength = 64;

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
