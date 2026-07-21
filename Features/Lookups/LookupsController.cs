using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Lookups;

// Ported verbatim from Node routes/lookups.js.
//
//   GET /api/lookups/pincode/{pincode}   → { pincode, city, state, country, postOffices[], source, cached }
//   GET /api/lookups/transliterate?text= → { hindi, cached? }
//
// Why proxy instead of letting the browser call the public APIs directly:
// rate-limiting (one egress IP + cache instead of a NAT'd school office),
// resilience (primary → fallback → offline-prefix), CSP/privacy, and auth.
//
// Deliberately self-contained: a static HttpClient and static in-memory
// caches, so this file needs NO Program.cs registration to work.
[ApiController]
[Route("api/lookups")]
[Authorize]
public sealed class LookupsController : ControllerBase
{
    private readonly ITenantContext _tenant;
    public LookupsController(ITenantContext tenant) { _tenant = tenant; }

    private static readonly HttpClient Http = CreateClient();
    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "QMSoft-School/1.0");
        return c;
    }

    private static readonly string[] AllowedRoles =
        { "superadmin", "school_admin", "principal", "teacher", "accountant" };

    private bool RoleAllowed() => AllowedRoles.Contains(_tenant.Role);

    // ─── Pincode cache (24h positive / 5min negative-or-partial) ─────────
    private sealed record CacheEntry(object? Value, DateTime ExpiresAt);
    private static readonly ConcurrentDictionary<string, CacheEntry> PinCache = new();
    private const int MaxPinCache = 5000;
    private static readonly TimeSpan PinTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromMinutes(5);

    private static object? FromCache(ConcurrentDictionary<string, CacheEntry> cache, string key, out bool hit)
    {
        hit = false;
        if (!cache.TryGetValue(key, out var e)) return null;
        if (DateTime.UtcNow > e.ExpiresAt) { cache.TryRemove(key, out _); return null; }
        hit = true;
        return e.Value;
    }

    private static void ToCache(ConcurrentDictionary<string, CacheEntry> cache, int max, string key, object? value, TimeSpan ttl)
    {
        if (cache.Count >= max)
        {
            // Cheap eviction: drop ~10% of entries (matches the Node behaviour).
            var drop = (int)Math.Ceiling(max * 0.1);
            foreach (var k in cache.Keys)
            {
                cache.TryRemove(k, out _);
                if (--drop <= 0) break;
            }
        }
        cache[key] = new CacheEntry(value, DateTime.UtcNow.Add(ttl));
    }

    // ─── Offline state resolver (postal-circle prefixes) ─────────────────
    private static readonly Dictionary<int, string> StateByPin2 = new()
    {
        [11] = "Delhi",
        [12] = "Haryana", [13] = "Haryana",
        [14] = "Punjab", [15] = "Punjab", [16] = "Punjab",
        [17] = "Himachal Pradesh",
        [18] = "Jammu & Kashmir", [19] = "Jammu & Kashmir",
        [20] = "Uttar Pradesh", [21] = "Uttar Pradesh", [22] = "Uttar Pradesh", [23] = "Uttar Pradesh",
        [24] = "Uttar Pradesh", [25] = "Uttar Pradesh", [26] = "Uttar Pradesh", [27] = "Uttar Pradesh", [28] = "Uttar Pradesh",
        [30] = "Rajasthan", [31] = "Rajasthan", [32] = "Rajasthan", [33] = "Rajasthan", [34] = "Rajasthan",
        [36] = "Gujarat", [37] = "Gujarat", [38] = "Gujarat", [39] = "Gujarat",
        [40] = "Maharashtra", [41] = "Maharashtra", [42] = "Maharashtra", [43] = "Maharashtra", [44] = "Maharashtra",
        [45] = "Madhya Pradesh", [46] = "Madhya Pradesh", [47] = "Madhya Pradesh", [48] = "Madhya Pradesh",
        [49] = "Chhattisgarh",
        [50] = "Telangana", [51] = "Andhra Pradesh", [52] = "Andhra Pradesh", [53] = "Andhra Pradesh",
        [56] = "Karnataka", [57] = "Karnataka", [58] = "Karnataka", [59] = "Karnataka",
        [60] = "Tamil Nadu", [61] = "Tamil Nadu", [62] = "Tamil Nadu", [63] = "Tamil Nadu", [64] = "Tamil Nadu",
        [67] = "Kerala", [68] = "Kerala", [69] = "Kerala",
        [70] = "West Bengal", [71] = "West Bengal", [72] = "West Bengal", [73] = "West Bengal", [74] = "West Bengal",
        [75] = "Odisha", [76] = "Odisha", [77] = "Odisha",
        [78] = "Assam", [79] = "North Eastern",
        [80] = "Bihar", [81] = "Jharkhand", [82] = "Jharkhand", [83] = "Jharkhand",
        [84] = "Bihar", [85] = "Bihar",
        [90] = "APO", [91] = "APO", [92] = "APO", [93] = "APO", [94] = "APO",
        [95] = "APO", [96] = "APO", [97] = "APO", [98] = "APO", [99] = "APO",
    };

    // 3-digit overrides for the well-known boundary splits.
    private static readonly Dictionary<int, string> StateByPin3 = new()
    {
        [248] = "Uttarakhand", [246] = "Uttarakhand", [249] = "Uttarakhand", [263] = "Uttarakhand", [262] = "Uttarakhand",
        [403] = "Goa",   // Goa sits inside the 40x Maharashtra block
    };

    private static string? StateFromPincode(string pin)
    {
        if (StateByPin3.TryGetValue(int.Parse(pin[..3]), out var s3)) return s3;
        return StateByPin2.TryGetValue(int.Parse(pin[..2]), out var s2) ? s2 : null;
    }

    // ─── GET /api/lookups/pincode/{pincode} ──────────────────────────────
    [HttpGet("pincode/{pincode}")]
    public async Task<IActionResult> Pincode(string pincode, CancellationToken ct)
    {
        if (!RoleAllowed())
            return StatusCode(403, new { error = "Forbidden", code = "INSUFFICIENT_PRIVILEGE" });

        var pin = (pincode ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(pin, @"^[1-9]\d{5}$"))
            return BadRequest(new { error = "INVALID_PINCODE", message = "Pincode must be 6 digits, starting 1-9." });

        var cached = FromCache(PinCache, pin, out var hit);
        if (hit && cached is not null)
            return Ok(Merge(cached, new Dictionary<string, object?> { ["cached"] = true }));
        if (hit && cached is null)
            return NotFound(new { error = "PINCODE_NOT_FOUND", message = "No record for this pincode — please enter city and state manually.", pincode = pin });

        object? result = null;
        try { result = await LookupViaIndiaPost(pin, ct); } catch { /* fall through */ }
        if (result is null)
        {
            try { result = await LookupViaFallback(pin, ct); } catch { /* fall through */ }
        }

        if (result is null)
        {
            // Offline-prefix partial: State always auto-fills, City stays manual.
            var offlineState = StateFromPincode(pin);
            if (offlineState is not null && offlineState != "APO" && offlineState != "North Eastern")
            {
                var partial = new Dictionary<string, object?>
                {
                    ["pincode"] = pin, ["city"] = "", ["state"] = offlineState, ["country"] = "India",
                    ["postOffices"] = Array.Empty<object>(),
                    ["source"] = "offline-prefix", ["cityNeedsManualEntry"] = true,
                };
                ToCache(PinCache, MaxPinCache, pin, partial, NegativeTtl);   // short TTL so online can enrich later
                return Ok(Merge(partial, new Dictionary<string, object?> { ["cached"] = false }));
            }

            ToCache(PinCache, MaxPinCache, pin, null, NegativeTtl);          // negative-cache
            return NotFound(new { error = "PINCODE_NOT_FOUND", message = "Could not auto-detect city — please enter city manually.", pincode = pin });
        }

        ToCache(PinCache, MaxPinCache, pin, result, PinTtl);
        return Ok(Merge(result, new Dictionary<string, object?> { ["cached"] = false }));
    }

    private static async Task<object?> LookupViaIndiaPost(string pin, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"https://api.postalpincode.in/pincode/{pin}", ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"india-post HTTP {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            throw new HttpRequestException("india-post empty");

        var row = doc.RootElement[0];
        if (row.TryGetProperty("Status", out var st) && st.GetString() != "Success") return null;
        if (!row.TryGetProperty("PostOffice", out var pos) || pos.ValueKind != JsonValueKind.Array || pos.GetArrayLength() == 0)
            return null;

        var primary = pos[0];
        string S(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var offices = new List<object>();
        var take = Math.Min(pos.GetArrayLength(), 10);
        for (var i = 0; i < take; i++)
        {
            var po = pos[i];
            offices.Add(new { name = S(po, "Name"), branch = S(po, "BranchType"), district = S(po, "District"), state = S(po, "State") });
        }

        var city = S(primary, "District");
        if (city == "") city = S(primary, "Block");
        if (city == "") city = S(primary, "Name");
        var country = S(primary, "Country");

        return new Dictionary<string, object?>
        {
            ["pincode"] = pin, ["city"] = city, ["state"] = S(primary, "State"),
            ["country"] = country == "" ? "India" : country,
            ["postOffices"] = offices, ["source"] = "india-post",
        };
    }

    private static async Task<object?> LookupViaFallback(string pin, CancellationToken ct)
    {
        using var res = await Http.GetAsync($"https://api.zippopotam.us/in/{pin}", ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"fallback HTTP {(int)res.StatusCode}");
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("places", out var places) || places.ValueKind != JsonValueKind.Array || places.GetArrayLength() == 0)
            return null;

        var place = places[0];
        string S(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        var name = S(place, "place name");
        var state = S(place, "state");
        var country = doc.RootElement.TryGetProperty("country", out var cn) && cn.ValueKind == JsonValueKind.String ? cn.GetString() : "India";

        return new Dictionary<string, object?>
        {
            ["pincode"] = pin, ["city"] = name, ["state"] = state, ["country"] = country ?? "India",
            ["postOffices"] = new object[] { new { name, district = name, state } },
            ["source"] = "fallback",
        };
    }

    // Shallow-merge helper: base payload (+ cached flag) without mutating the cache entry.
    private static Dictionary<string, object?> Merge(object payload, Dictionary<string, object?> extra)
    {
        var dict = payload is Dictionary<string, object?> d
            ? new Dictionary<string, object?>(d)
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(payload)) ?? new();
        foreach (var (k, v) in extra) dict[k] = v;
        return dict;
    }

    // ─── GET /api/lookups/transliterate?text= ────────────────────────────
    //
    // Google Input Tools has no CORS headers — the browser can't call it, so
    // the server proxies. Lowercase input transliterates reliably; capitals
    // often echo Latin back, so we always send lowercase upstream.
    private static readonly ConcurrentDictionary<string, CacheEntry> TranslitCache = new();
    private const int MaxTranslitCache = 10000;
    private static readonly TimeSpan TranslitTtl = TimeSpan.FromDays(7);

    [HttpGet("transliterate")]
    public async Task<IActionResult> Transliterate([FromQuery] string? text, CancellationToken ct)
    {
        if (!RoleAllowed())
            return StatusCode(403, new { error = "Forbidden", code = "INSUFFICIENT_PRIVILEGE" });

        var t = (text ?? "").Trim();
        if (t.Length > 200) t = t[..200];
        if (t.Length == 0) return Ok(new { hindi = "" });

        var key = t.ToLowerInvariant();
        var cached = FromCache(TranslitCache, key, out var hit);
        if (hit) return Ok(new { hindi = cached as string ?? "", cached = true });

        try
        {
            var url = $"https://inputtools.google.com/request?text={Uri.EscapeDataString(key)}&itc=hi-t-i0-und&num=1&cp=0&cs=1&ie=utf-8&oe=utf-8&app=demopage";
            using var res = await Http.GetAsync(url, ct);
            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                // Shape: ["SUCCESS", [["rahul", ["राहुल", ...]]]]
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2
                    && root[0].GetString() == "SUCCESS"
                    && root[1].ValueKind == JsonValueKind.Array && root[1].GetArrayLength() > 0
                    && root[1][0].ValueKind == JsonValueKind.Array && root[1][0].GetArrayLength() >= 2
                    && root[1][0][1].ValueKind == JsonValueKind.Array && root[1][0][1].GetArrayLength() > 0)
                {
                    var hindi = root[1][0][1][0].GetString() ?? "";
                    ToCache(TranslitCache, MaxTranslitCache, key, hindi, TranslitTtl);
                    return Ok(new { hindi, cached = false });
                }
            }
        }
        catch { /* graceful empty below */ }

        return Ok(new { hindi = "" });   // never break the form over a lookup
    }
}
