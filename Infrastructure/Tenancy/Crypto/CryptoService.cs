using System.Security.Cryptography;
using System.Text;

namespace QMSoft.Api.Infrastructure.Crypto;

public interface ICryptoService
{
    string? Encrypt(string? plaintext);
    string? Decrypt(string? stored);
    string Sha256Hex(string input);
    bool IsEncrypted(string? value);
}

/// <summary>
/// Port of utils/crypto.js — AES-256-GCM.
///
/// Format (MUST match the Node implementation byte-for-byte):
///   enc:v1:&lt;iv_hex&gt;:&lt;tag_hex&gt;:&lt;ciphertext_hex&gt;
///     iv  = 12 bytes (96-bit, GCM recommendation)
///     tag = 16 bytes (128-bit auth tag)
///     key = 32 bytes from ENCRYPTION_KEY (64 hex chars)
///
/// Both encrypt() and decrypt() are idempotent and null-tolerant, exactly like
/// the Node original: encrypting an already-encrypted value returns it
/// unchanged; decrypting a plaintext value returns it unchanged. That tolerance
/// is what lets a value round-trip through EF's ValueConverter without
/// double-encrypting.
/// </summary>
public sealed class CryptoService : ICryptoService
{
    private const string Prefix = "enc:v1:";
    private const int IvLen = 12;
    private const int TagLen = 16;
    private const int KeyLen = 32;

    private readonly byte[]? _key;
    private readonly ILogger<CryptoService> _log;
    private bool _plaintextWarned;

    public CryptoService(IConfiguration cfg, IHostEnvironment env, ILogger<CryptoService> log)
    {
        _log = log;
        var hex = cfg["ENCRYPTION_KEY"];

        if (string.IsNullOrWhiteSpace(hex))
        {
            // assertEncryptionKeyAtBoot() semantics: fatal in prod, tolerated in dev.
            // Program.cs calls AssertKeyAtBoot() before the host starts; this branch
            // only survives in Development/Test.
            _key = null;
            return;
        }

        byte[] key;
        try
        {
            key = Convert.FromHexString(hex.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "ENCRYPTION_KEY must be hex. Generate with: " +
                "openssl rand -hex 32");
        }

        if (key.Length != KeyLen)
        {
            throw new InvalidOperationException(
                $"ENCRYPTION_KEY must be {KeyLen} bytes ({KeyLen * 2} hex chars). " +
                $"Got {key.Length} bytes.");
        }

        _key = key;
    }

    /// <summary>
    /// Call from Program.cs BEFORE the host starts.
    /// Production: missing key is FATAL — silent plaintext fallback would let bank
    /// account / Razorpay / cheque data go unencrypted with no visible warning.
    /// Development: warn and allow plaintext so local work needs no key.
    /// </summary>
    public static void AssertKeyAtBoot(IConfiguration cfg, IHostEnvironment env)
    {
        var hex = cfg["ENCRYPTION_KEY"];
        var ok = !string.IsNullOrWhiteSpace(hex)
                 && hex.Trim().Length == KeyLen * 2;

        if (ok) return;

        var msg = $"ENCRYPTION_KEY is not set or is not {KeyLen * 2} hex chars. " +
                  "Generate one with: openssl rand -hex 32";

        if (env.IsProduction())
        {
            Console.Error.WriteLine($"[FATAL] {msg}");
            Console.Error.WriteLine(
                "[FATAL] Refusing to start without ENCRYPTION_KEY in production.");
            Environment.Exit(1);
        }

        Console.WriteLine($"[crypto] {msg}");
        Console.WriteLine(
            "[crypto] Non-production — encrypted fields fall back to plaintext.");
    }

    public bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;
        if (IsEncrypted(plaintext)) return plaintext;   // idempotent

        if (_key is null)
        {
            WarnPlaintextOnce();
            return plaintext;
        }

        var iv = RandomNumberGenerator.GetBytes(IvLen);
        var pt = Encoding.UTF8.GetBytes(plaintext);
        var ct = new byte[pt.Length];
        var tag = new byte[TagLen];

        using var gcm = new AesGcm(_key, TagLen);
        gcm.Encrypt(iv, pt, ct, tag);

        return string.Concat(
            Prefix,
            Convert.ToHexString(iv).ToLowerInvariant(), ":",
            Convert.ToHexString(tag).ToLowerInvariant(), ":",
            Convert.ToHexString(ct).ToLowerInvariant());
    }

    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return stored;
        if (!IsEncrypted(stored)) return stored;        // tolerate plaintext

        if (_key is null)
        {
            // Encrypted data but no key: cannot recover. Returning the ciphertext
            // would render "enc:v1:..." in the UI; null is the honest answer.
            _log.LogError("Encrypted value present but ENCRYPTION_KEY is not set.");
            return null;
        }

        try
        {
            var body = stored.AsSpan(Prefix.Length);

            var c1 = body.IndexOf(':');
            if (c1 < 0) return null;
            var rest = body[(c1 + 1)..];
            var c2 = rest.IndexOf(':');
            if (c2 < 0) return null;

            var iv  = Convert.FromHexString(body[..c1]);
            var tag = Convert.FromHexString(rest[..c2]);
            var ct  = Convert.FromHexString(rest[(c2 + 1)..]);

            if (iv.Length != IvLen || tag.Length != TagLen) return null;

            var pt = new byte[ct.Length];
            using var gcm = new AesGcm(_key, TagLen);
            gcm.Decrypt(iv, ct, tag, pt);   // throws CryptographicException on tamper

            return Encoding.UTF8.GetString(pt);
        }
        catch (CryptographicException)
        {
            // Auth tag mismatch — tampered, or wrong key. Never surface ciphertext.
            _log.LogError("Failed to decrypt value (auth tag mismatch or wrong key).");
            return null;
        }
        catch (FormatException)
        {
            _log.LogError("Malformed encrypted value.");
            return null;
        }
    }

    public string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
               .ToLowerInvariant();

    private void WarnPlaintextOnce()
    {
        if (_plaintextWarned) return;
        _plaintextWarned = true;
        _log.LogWarning("ENCRYPTION_KEY not set — storing sensitive fields as PLAINTEXT.");
    }
}
