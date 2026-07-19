using System.Security.Cryptography;
using System.Text;

namespace QMSoft.Api.Features.Payments;

/// <summary>
/// Razorpay signature verification — the security-critical port.
///
/// Two distinct HMAC-SHA256 checks, both against the school's key secret (or the
/// platform webhook secret for webhooks):
///   • payment signature: HMAC(order_id + "|" + payment_id)   — client callback
///   • webhook signature:  HMAC(raw_request_body)             — server-to-server
///
/// Verification uses FIXED-TIME comparison (CryptographicOperations.
/// FixedTimeEquals) — a plain string == leaks timing and is a real bypass vector.
/// The Node code used === ; this is a deliberate hardening, not a behaviour change.
/// </summary>
public sealed class RazorpayService
{
    private readonly IConfiguration _cfg;
    public RazorpayService(IConfiguration cfg) => _cfg = cfg;

    private static string HmacHex(string key, byte[] message)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(h.ComputeHash(message)).ToLowerInvariant();
    }

    /// <summary>Client callback: HMAC(order|payment) == signature.</summary>
    public bool VerifyPaymentSignature(string keySecret, string orderId, string paymentId, string signature)
    {
        if (string.IsNullOrEmpty(keySecret)) return false;
        var expected = HmacHex(keySecret, Encoding.UTF8.GetBytes($"{orderId}|{paymentId}"));
        return FixedEquals(expected, signature);
    }

    /// <summary>Webhook: HMAC(rawBody) == X-Razorpay-Signature. Body must be the
    /// EXACT bytes received — any re-serialization breaks the hash.</summary>
    public bool VerifyWebhookSignature(byte[] rawBody, string signature)
    {
        var secret = _cfg["RAZORPAY_WEBHOOK_SECRET"] ?? "";
        if (string.IsNullOrEmpty(secret) || rawBody.Length == 0) return false;
        var expected = HmacHex(secret, rawBody);
        return FixedEquals(expected, signature);
    }

    private static bool FixedEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b ?? "");
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
