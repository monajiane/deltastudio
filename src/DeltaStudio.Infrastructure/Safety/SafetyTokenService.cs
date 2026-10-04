using System.Security.Cryptography;
using System.Text;

namespace DeltaStudio.Infrastructure.Safety;

/// <summary>Classification of an operation's blast radius. Enforced by the MCP layer.</summary>
public enum SafetyClass
{
    /// <summary>Cannot change anything (reads, validation, exports).</summary>
    Safe,
    /// <summary>Changes the offline project only (never touches a PLC).</summary>
    ProjectWrite,
    /// <summary>Touches a connected PLC, recoverable (write device/register).</summary>
    Dangerous,
    /// <summary>Can change machine behaviour irreversibly or take control (download, run/stop, force).</summary>
    Critical,
}

/// <summary>
/// Two-step confirmation for every Dangerous/Critical operation:
///   1. caller performs `prepare` → gets a single-use token bound to a digest of the exact operation;
///   2. caller performs the operation with the token; token must match digest and expiry (120 s).
/// No token → no execution, and no implicit grants. This is what "never download silently"
/// looks like in code.
/// </summary>
public sealed class SafetyTokenService
{
    private sealed record PendingToken(string Token, string Digest, DateTimeOffset ExpiresUtc, SafetyClass Class, string Description);

    private readonly List<PendingToken> _tokens = new();
    private readonly object _sync = new();

    /// <summary>Token lifetime. Short on purpose.</summary>
    public TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(2);

    /// <summary>Issues a confirmation token for a canonical operation digest.</summary>
    public string Issue(string digest, SafetyClass cls, string description)
    {
        var t = new PendingToken(
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            digest,
            DateTimeOffset.UtcNow.Add(Lifetime),
            cls,
            description);

        lock (_sync)
        {
            _tokens.RemoveAll(x => x.ExpiresUtc < DateTimeOffset.UtcNow);
            _tokens.Add(t);
        }

        return t.Token;
    }

    /// <summary>
    /// Validates and consumes a token. true = accepted. On any mismatch (unknown, expired, digest mismatch,
    /// reused) returns false with the reason in <paramref name="error"/>. Tokens are strictly one-shot.
    /// </summary>
    public bool ValidateAndConsume(string? token, string expectedDigest, SafetyClass expectedClass, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            error = "confirmation_token is required for this operation class (" + expectedClass + "). " +
                    "Call the matching *_prepare tool first, show the plan to the human operator, obtain approval, " +
                    "then retry with the token. Silent writes are refused by design (see docs/safety.md).";
            return false;
        }

        lock (_sync)
        {
            PendingToken? match = _tokens.FirstOrDefault(x => x.Token == token);
            if (match is null)
            {
                error = "Unknown or already-consumed confirmation_token.";
                return false;
            }

            if (match.ExpiresUtc < DateTimeOffset.UtcNow)
            {
                _tokens.Remove(match);
                error = "confirmation_token expired (2 minute lifetime). Re-prepare the operation.";
                return false;
            }

            if (!string.Equals(match.Digest, expectedDigest, StringComparison.Ordinal))
            {
                error = "confirmation_token does not match this operation's digest (parameters changed after approval?). " +
                        "Re-prepare and confirm the new plan.";
                return false;
            }

            if (match.Class < expectedClass)
            {
                error = $"confirmation_token was issued for '{match.Class}', insufficient for '{expectedClass}'.";
                return false;
            }

            _tokens.Remove(match); // one-shot
            return true;
        }
    }

    /// <summary>Number of outstanding tokens (diagnostics).</summary>
    public int OutstandingCount
    {
        get
        {
            lock (_sync)
            {
                _tokens.RemoveAll(x => x.ExpiresUtc < DateTimeOffset.UtcNow);
                return _tokens.Count;
            }
        }
    }

    /// <summary>
    /// Canonical digest: sha256 over normalized "class|operation|args" — stable for identical calls,
    /// different for any changed argument (device, value, station…).
    /// </summary>
    public static string Digest(string operation, string canonicalArgs)
    {
        string input = $"{operation}\n{canonicalArgs}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}
