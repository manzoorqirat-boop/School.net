using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QMSoft.Api.Middleware;

/// <summary>
/// Strips HTML tags from free-text fields in JSON request bodies.
///
/// Port of the Node middleware/sanitize.js, which ran globally as
/// `app.use(sanitizeBody)`. The .NET port dropped it, so a payload like
///
///     { "firstName": "&lt;script&gt;alert(1)&lt;/script&gt;Aarav" }
///
/// was stored verbatim. React and React Native escape on render, so the apps
/// themselves never executed it — but an exported HTML report opened in a
/// browser did. Defence in depth: the export helper now escapes too, and this
/// stops the payload reaching the database in the first place.
///
/// Schools enter plain text only — names, addresses, notes. There is no
/// legitimate reason for a student's name to contain markup.
///
/// SKIP list mirrors the Node original: fields whose value is legitimately
/// non-text (opaque tokens, data: URIs, secrets) or where stripping would
/// corrupt the value (passwords with special characters).
/// </summary>
public sealed class SanitizeBodyMiddleware
{
    private readonly RequestDelegate _next;
    public SanitizeBodyMiddleware(RequestDelegate next) => _next = next;

    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "newPassword", "oldPassword", "currentPassword",
        "razorpayKeySecret", "razorpayKeyId",
        "logoUrl",                              // may be a data: URI
        "token", "refreshToken", "accessToken",
        "signature", "hmac",
        "aadhar", "aadharNumber",
        "bankAccount", "bankAccountNumber", "ifsc", "bankIfsc",
        "chequeNo", "transactionRef",
        "upiUri",
    };

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ShouldInspect(ctx)) { await _next(ctx); return; }

        ctx.Request.EnableBuffering();
        string raw;
        using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true))
            raw = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(raw)) { await _next(ctx); return; }

        JsonNode? root;
        try { root = JsonNode.Parse(raw); }
        catch { await _next(ctx); return; }   // not JSON — model binding will reject it
        if (root is null) { await _next(ctx); return; }

        if (!Walk(root)) { await _next(ctx); return; }   // nothing changed

        var cleaned = Encoding.UTF8.GetBytes(root.ToJsonString());
        ctx.Request.Body = new MemoryStream(cleaned);
        ctx.Request.ContentLength = cleaned.Length;

        await _next(ctx);
    }

    private static bool ShouldInspect(HttpContext ctx)
    {
        var m = ctx.Request.Method;
        if (m != HttpMethods.Post && m != HttpMethods.Put && m != HttpMethods.Patch) return false;

        var ct = ctx.Request.ContentType;
        if (ct is null || !ct.Contains("application/json", StringComparison.OrdinalIgnoreCase)) return false;

        // The Razorpay webhook signature is an HMAC over the EXACT bytes
        // received. Rewriting the body — even harmlessly — breaks verification.
        if (ctx.Request.Path.StartsWithSegments("/api/webhooks")) return false;

        return true;
    }

    /// <summary>Rewrites string values in place. Returns true if anything changed.</summary>
    private static bool Walk(JsonNode node)
    {
        var changed = false;

        switch (node)
        {
            // A JsonNode already attached to a parent cannot be re-attached
            // elsewhere — System.Text.Json throws InvalidOperationException.
            // So for the only case that produces a NEW node (a cleaned string)
            // we detach first by assigning the raw value, never the node.
            case JsonObject obj:
                foreach (var prop in obj.ToList())
                {
                    if (prop.Value is null) continue;

                    if (prop.Value is JsonValue pv && pv.TryGetValue<string>(out var ps))
                    {
                        if (Skip.Contains(prop.Key)) continue;
                        var cleanedProp = Strip(ps);
                        if (cleanedProp != ps) { obj[prop.Key] = cleanedProp; changed = true; }
                        continue;
                    }

                    var child = prop.Value;
                    if (Walk(child)) changed = true;
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is null) continue;

                    if (arr[i] is JsonValue av && av.TryGetValue<string>(out var asv))
                    {
                        var cleanedItem = Strip(asv);
                        if (cleanedItem != asv) { arr[i] = cleanedItem; changed = true; }
                        continue;
                    }

                    var child = arr[i]!;
                    if (Walk(child)) changed = true;
                }
                break;

            // Strings are handled by the parent (object/array) branches above so
            // the new value is written through the parent, not re-parented. A
            // bare top-level string body is not something any endpoint accepts.
            case JsonValue:
                break;
        }

        return changed;
    }

    /// <summary>
    /// Removes tags and the content of script/style blocks. Deliberately not a
    /// full HTML parser: the goal is "no markup survives", not "render safe
    /// HTML" — the Node version used sanitize-html with an empty allow-list,
    /// which is the same outcome.
    /// </summary>
    private static string Strip(string s)
    {
        // Fast path — the overwhelming majority of fields have no angle brackets.
        if (s.IndexOf('<') < 0 && s.IndexOf('>') < 0) return s;

        // Drop script/style bodies entirely, then any remaining tag.
        var noBlocks = System.Text.RegularExpressions.Regex.Replace(
            s, @"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.Singleline);

        var noTags = System.Text.RegularExpressions.Regex.Replace(noBlocks, @"<[^>]*>", string.Empty);

        return noTags.Trim();
    }
}

public static class SanitizeBodyMiddlewareExtensions
{
    public static IApplicationBuilder UseSanitizeBody(this IApplicationBuilder app)
        => app.UseMiddleware<SanitizeBodyMiddleware>();
}
