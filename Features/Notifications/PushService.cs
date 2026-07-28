using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using QMSoft.Api.Data;
using QMSoft.Api.Domain.Entities;
using QMSoft.Api.Infrastructure.Tenancy;

namespace QMSoft.Api.Features.Notifications;

/// <summary>
/// Sends push notifications through Expo's relay.
///
/// Expo forwards to APNs and FCM on our behalf, so this service never touches
/// Apple or Google credentials directly — those live in the Expo project. That
/// is the whole reason for choosing Expo push over raw FCM here: a single
/// HTTP endpoint, one token format, no per-platform certificate handling on a
/// server that already has enough moving parts.
///
/// Runs UNFILTERED. It is invoked from a Hangfire job with no JWT and no tenant
/// context, and it resolves audiences across whichever school triggered it, so
/// it builds its own AppDbContext the way LateFeeJob and the seeder do.
/// </summary>
public sealed class PushService
{
    private const string ExpoSendUrl = "https://exp.host/--/api/v2/push/send";

    /// <summary>Expo's documented cap. Larger payloads are rejected outright.</summary>
    private const int BatchSize = 100;

    private readonly IServiceProvider _sp;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<PushService> _log;

    public PushService(IServiceProvider sp, IHttpClientFactory http, ILogger<PushService> log)
    { _sp = sp; _http = http; _log = log; }

    private AppDbContext Unfiltered()
    {
        var scope = _sp.CreateScope();
        return new AppDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<AppDbContext>>(),
            FixedTenantContext.Unfiltered(),
            scope.ServiceProvider.GetRequiredService<Infrastructure.Crypto.ICryptoService>());
    }

    // ── Public entry points ───────────────────────────────────────────────

    /// <summary>
    /// Fan a published notice out to exactly the people the notice targets.
    ///
    /// The audience rules are NOT reimplemented here — they are read off the
    /// notice and applied the same way NoticesController applies them, because
    /// two copies of "who can see this" WILL diverge and the failure mode is a
    /// parent being pushed a notice they cannot then open.
    /// </summary>
    public async Task SendNoticeAsync(Guid noticeId, CancellationToken ct = default)
    {
        await using var db = Unfiltered();

        var notice = await db.Notices.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == noticeId && !n.IsDeleted, ct);
        if (notice is null) { _log.LogWarning("Push skipped: notice {Id} not found.", noticeId); return; }

        // A scheduled notice must not announce itself early. The job is
        // enqueued at publish time, but the row can have moved since.
        if (!notice.IsLive(DateTime.UtcNow))
        {
            _log.LogInformation("Push skipped: notice {Id} is not live yet.", noticeId);
            return;
        }

        var userIds = await ResolveNoticeAudienceAsync(db, notice, ct);
        if (userIds.Count == 0) { _log.LogInformation("Push skipped: notice {Id} targets nobody.", noticeId); return; }

        var body = notice.Body.Length > 140 ? notice.Body[..140].TrimEnd() + "…" : notice.Body;

        await SendToUsersAsync(
            db, notice.SchoolId, userIds,
            title: notice.Title,
            body: body,
            data: new Dictionary<string, string> { ["type"] = "notice", ["id"] = notice.Id.ToString() },
            // Only 'urgent' interrupts. Everything else arrives quietly — a
            // school that buzzes every phone for a stationery reminder gets
            // its notifications turned off within a week.
            highPriority: notice.Priority == NoticePriority.Urgent,
            ct: ct);
    }

    // ── Audience ──────────────────────────────────────────────────────────

    private static async Task<List<Guid>> ResolveNoticeAudienceAsync(
        AppDbContext db, Notice notice, CancellationToken ct)
    {
        var roles = notice.TargetRoles;
        var classes = notice.TargetClasses;

        var users = db.Users.AsNoTracking().Where(u => u.SchoolId == notice.SchoolId && u.IsActive);

        if (roles.Count > 0)
        {
            var wanted = roles.Select(RoleFromWire).Where(r => r != null).Select(r => r!.Value).ToList();
            users = users.Where(u => wanted.Contains(u.Role));
        }

        var candidates = await users.Select(u => new { u.Id, u.Role, u.StudentId }).ToListAsync(ct);
        if (classes.Count == 0) return candidates.Select(c => c.Id).ToList();

        // Class targeting constrains only the roles that belong to a class.
        // Staff targeted by role receive it regardless — same rule the read
        // path applies, and for the same reason: "Class 5 parents' meeting,
        // teachers please attend" must reach the teachers running it.
        var classBound = candidates.Where(c => c.Role is UserRole.Student or UserRole.Parent).ToList();
        var staff = candidates.Except(classBound).Select(c => c.Id).ToList();

        var studentIds = classBound.Where(c => c.StudentId != null).Select(c => c.StudentId!.Value).ToList();
        var parentIds = classBound.Where(c => c.Role == UserRole.Parent).Select(c => c.Id).ToList();

        var matched = new HashSet<Guid>(staff);

        if (studentIds.Count > 0)
        {
            var ok = await db.Students.AsNoTracking()
                .Where(s => studentIds.Contains(s.Id) && classes.Contains(s.Class))
                .Select(s => s.Id).ToListAsync(ct);
            foreach (var c in classBound.Where(c => c.StudentId != null && ok.Contains(c.StudentId!.Value)))
                matched.Add(c.Id);
        }

        if (parentIds.Count > 0)
        {
            var hits = await db.Users.AsNoTracking()
                .Where(u => parentIds.Contains(u.Id))
                .Select(u => new { u.Id, Classes = u.ParentOf.Select(s => s.Class) })
                .ToListAsync(ct);
            foreach (var h in hits.Where(h => h.Classes.Any(classes.Contains)))
                matched.Add(h.Id);
        }

        return matched.ToList();
    }

    /// <summary>
    /// Wire label → UserRole. EnumWireBridge only goes the other way, and the
    /// labels stored in Notice.TargetRoles are the wire form. An unrecognised
    /// label returns null and is dropped rather than throwing — a bad row must
    /// not take down the send for everyone else on the notice.
    /// </summary>
    private static UserRole? RoleFromWire(string wire) => wire switch
    {
        "superadmin"   => UserRole.SuperAdmin,
        "school_admin" => UserRole.SchoolAdmin,
        "principal"    => UserRole.Principal,
        "accountant"   => UserRole.Accountant,
        "teacher"      => UserRole.Teacher,
        "parent"       => UserRole.Parent,
        "student"      => UserRole.Student,
        _              => null,
    };

    // ── Delivery ──────────────────────────────────────────────────────────

    private async Task SendToUsersAsync(
        AppDbContext db, Guid schoolId, List<Guid> userIds,
        string title, string body, Dictionary<string, string> data,
        bool highPriority, CancellationToken ct)
    {
        var tokens = await db.Set<DeviceToken>().AsNoTracking()
            .Where(d => d.SchoolId == schoolId && userIds.Contains(d.UserId) && !d.IsRevoked)
            .Select(d => d.Token)
            .Distinct()
            .ToListAsync(ct);

        if (tokens.Count == 0)
        {
            _log.LogInformation("Push: {Users} user(s) targeted but no registered devices.", userIds.Count);
            return;
        }

        var client = _http.CreateClient(nameof(PushService));
        var dead = new List<string>();

        for (var i = 0; i < tokens.Count; i += BatchSize)
        {
            var slice = tokens.Skip(i).Take(BatchSize).ToList();
            var messages = slice.Select(t => new ExpoMessage
            {
                To = t, Title = title, Body = body, Data = data,
                Priority = highPriority ? "high" : "normal",
                Sound = highPriority ? "default" : null,
                ChannelId = highPriority ? "urgent" : "default",
            }).ToList();

            try
            {
                using var res = await client.PostAsJsonAsync(ExpoSendUrl, messages, ct);
                if (!res.IsSuccessStatusCode)
                {
                    // Never throw. A failed push must not fail the job that
                    // triggered it — the notice is already saved and visible.
                    _log.LogError("Expo push returned {Status} for {Count} token(s).",
                        (int)res.StatusCode, slice.Count);
                    continue;
                }

                var payload = await res.Content.ReadFromJsonAsync<ExpoSendResponse>(cancellationToken: ct);
                var tickets = payload?.Data ?? [];

                // Tickets come back positionally, so index alignment is the only
                // way to know WHICH token failed.
                for (var k = 0; k < tickets.Count && k < slice.Count; k++)
                {
                    if (tickets[k].Status == "ok") continue;
                    var code = tickets[k].Details?.Error;
                    if (code == "DeviceNotRegistered") dead.Add(slice[k]);
                    else _log.LogWarning("Push ticket error {Error} for one device.", code ?? tickets[k].Message);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Expo push batch failed for {Count} token(s).", slice.Count);
            }
        }

        if (dead.Count > 0) await RevokeAsync(dead, ct);
    }

    /// <summary>
    /// Retire tokens Expo says are gone — the app was uninstalled, or the OS
    /// rotated the token. Left in place they are re-sent on every notice
    /// forever, and Expo eventually rate-limits a sender that keeps pushing to
    /// dead devices.
    /// </summary>
    private async Task RevokeAsync(List<string> tokens, CancellationToken ct)
    {
        await using var db = Unfiltered();
        var rows = await db.Set<DeviceToken>().Where(d => tokens.Contains(d.Token)).ToListAsync(ct);
        foreach (var r in rows) { r.IsRevoked = true; r.RevokedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync(ct);
        _log.LogInformation("Push: revoked {Count} dead device token(s).", rows.Count);
    }

    // ── Expo wire types ───────────────────────────────────────────────────

    private sealed class ExpoMessage
    {
        [JsonPropertyName("to")]        public string To { get; set; } = "";
        [JsonPropertyName("title")]     public string Title { get; set; } = "";
        [JsonPropertyName("body")]      public string Body { get; set; } = "";
        [JsonPropertyName("data")]      public Dictionary<string, string>? Data { get; set; }
        [JsonPropertyName("priority")]  public string? Priority { get; set; }
        [JsonPropertyName("sound")]     public string? Sound { get; set; }
        [JsonPropertyName("channelId")] public string? ChannelId { get; set; }
    }

    private sealed class ExpoSendResponse
    {
        [JsonPropertyName("data")] public List<ExpoTicket> Data { get; set; } = [];
    }

    private sealed class ExpoTicket
    {
        [JsonPropertyName("status")]  public string Status { get; set; } = "";
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("details")] public ExpoTicketDetails? Details { get; set; }
    }

    private sealed class ExpoTicketDetails
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
