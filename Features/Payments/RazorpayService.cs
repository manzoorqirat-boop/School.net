using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QMSoft.Api.Common;
using QMSoft.Api.Domain.Entities;

namespace QMSoft.Api.Features.Payments;

/// <summary>One leg of a Razorpay Payout Batch request.</summary>
public sealed record PayoutTransfer(
    string AccountNumber,
    string Ifsc,
    long AmountPaise,
    string Mode,
    string Purpose,
    string? Description,
    IReadOnlyDictionary<string, string>? Notes);

/// <summary>
/// The fields callers actually need out of a Razorpay order. The API returns
/// more (created_at, attempts, notes echo…) but nothing in this codebase reads
/// them — see feeController.js createRazorpayOrder, which only ever touches
/// order.id / .amount / .currency / .keyId.
/// </summary>
public sealed record RazorpayOrder(string Id, long AmountPaise, string Currency, string? Receipt, string Status, string KeyId);

public interface IRazorpayService
{
    Task<RazorpayOrder> CreateOrderAsync(
        School? school, long amountPaise, string? receipt,
        IReadOnlyDictionary<string, string>? notes, CancellationToken ct, string currency = "INR");

    bool VerifyPaymentSignature(School? school, string orderId, string paymentId, string signature);

    Task<string> InitiatePayoutBatchAsync(
        string keyId, string keySecret, IReadOnlyList<PayoutTransfer> transfers, CancellationToken ct);
}

/// <summary>
/// Port of services/razorpay.js.
///
/// The Node original skipped the razorpay npm SDK and hit the REST API
/// directly via fetch — same approach here via a typed HttpClient, so there's
/// no new SDK dependency to add to the csproj.
///
/// Register with builder.Services.AddHttpClient&lt;IRazorpayService, RazorpayService&gt;()
/// (NOT AddScoped) when this is wired back into Program.cs — a typed client is
/// what gives us pooled HttpClientHandler / DNS-refresh behaviour for free.
/// </summary>
public sealed class RazorpayService : IRazorpayService
{
    private const string RzpApi = "https://api.razorpay.com/v1";

    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public RazorpayService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    /// <summary>
    /// Prefer per-school keys, fall back to platform-level env vars.
    /// RazorpayKeySecret decrypts automatically via the EF ValueConverter on
    /// School — see SchoolConfiguration.cs — so it's plaintext by the time it
    /// reaches here, same as the Mongoose getter did in the Node original.
    /// </summary>
    private (string KeyId, string KeySecret) GetCredentials(School? school)
    {
        var keyId = school?.RazorpayKeyId is { Length: > 0 } sid ? sid : _cfg["RAZORPAY_KEY_ID"];
        var keySecret = school?.RazorpayKeySecret is { Length: > 0 } ssec ? ssec : _cfg["RAZORPAY_KEY_SECRET"];

        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(keySecret))
        {
            throw new AppException(
                "Online payment is not set up for this school yet. Please pay at the school office or contact the administrator.",
                400, ErrorCodes.RazorpayNotConfigured);
        }

        return (keyId, keySecret);
    }

    private static string BasicAuth(string keyId, string keySecret) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}"));

    public async Task<RazorpayOrder> CreateOrderAsync(
        School? school, long amountPaise, string? receipt,
        IReadOnlyDictionary<string, string>? notes, CancellationToken ct, string currency = "INR")
    {
        var (keyId, keySecret) = GetCredentials(school);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{RzpApi}/orders");
        req.Headers.Authorization = new("Basic", BasicAuth(keyId, keySecret));
        req.Content = JsonContent(new { amount = amountPaise, currency, receipt, notes });

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var doc = ParseOrEmpty(body);

        if (!resp.IsSuccessStatusCode)
        {
            var description = doc.RootElement.TryGetProperty("error", out var errEl) &&
                               errEl.TryGetProperty("description", out var descEl)
                ? descEl.GetString()
                : null;

            throw new AppException(
                description ?? "Razorpay order creation failed",
                (int)resp.StatusCode, ErrorCodes.RazorpayOrderFailed,
                new Dictionary<string, object?> { ["razorpay"] = body });
        }

        var root = doc.RootElement;
        return new RazorpayOrder(
            Id: root.GetProperty("id").GetString()!,
            AmountPaise: root.GetProperty("amount").GetInt64(),
            Currency: root.GetProperty("currency").GetString()!,
            Receipt: root.TryGetProperty("receipt", out var r) ? r.GetString() : null,
            Status: root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "",
            KeyId: keyId);
    }

    /// <summary>
    /// signature = HMAC_SHA256(order_id + '|' + payment_id, key_secret).
    /// Uses a fixed-time comparison — the Node original used plain `===`, which
    /// this tightens without changing behaviour for any legitimate caller.
    /// </summary>
    public bool VerifyPaymentSignature(School? school, string orderId, string paymentId, string signature)
    {
        var (_, keySecret) = GetCredentials(school);

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(keySecret),
            Encoding.UTF8.GetBytes($"{orderId}|{paymentId}")));

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());

        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public async Task<string> InitiatePayoutBatchAsync(
        string keyId, string keySecret, IReadOnlyList<PayoutTransfer> transfers, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(keySecret))
            throw new AppException("Razorpay credentials missing", 400, ErrorCodes.RazorpayNotConfigured);

        var payload = new
        {
            transfers = transfers.Select(t => new
            {
                account_number = t.AccountNumber,
                ifsc = t.Ifsc,
                amount = t.AmountPaise,
                mode = t.Mode,
                purpose = t.Purpose,
                description = t.Description,
                notes = t.Notes,
            }),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{RzpApi}/payouts/batch");
        req.Headers.Authorization = new("Basic", BasicAuth(keyId, keySecret));
        req.Content = JsonContent(payload);

        using var resp = await _http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var doc = ParseOrEmpty(body);

        if (!resp.IsSuccessStatusCode)
        {
            var description = doc.RootElement.TryGetProperty("error", out var errEl) &&
                               errEl.TryGetProperty("description", out var descEl)
                ? descEl.GetString()
                : null;

            throw new AppException(
                description ?? "Razorpay batch payout failed",
                (int)resp.StatusCode, ErrorCodes.RazorpayPayoutFailed,
                new Dictionary<string, object?> { ["razorpay"] = body });
        }

        var root = doc.RootElement;
        var batchId = root.TryGetProperty("batch_id", out var b) ? b.GetString()
                    : root.TryGetProperty("id", out var i) ? i.GetString()
                    : null;

        return batchId ?? throw new AppException(
            "Razorpay batch payout succeeded but returned no batch id", 502, ErrorCodes.RazorpayPayoutFailed);
    }

    private static JsonContent JsonContent(object payload) =>
        System.Net.Http.Json.JsonContent.Create(payload);

    /// <summary>Razorpay error bodies are always JSON, but guard the parse anyway
    /// so a malformed/empty response surfaces as a clean AppException rather
    /// than an unhandled JsonException.</summary>
    private static JsonDocument ParseOrEmpty(string body)
    {
        try { return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body); }
        catch (JsonException) { return JsonDocument.Parse("{}"); }
    }
}
