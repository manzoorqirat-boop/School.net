using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace QMSoft.Api.Common;

/// <summary>
/// CONTRACT — port of utils/paginate.js output shape:
///   { items: [...], pagination: { total, page, limit, pages, hasNext, hasPrev } }
///
/// Empty results still return a full pagination object. Never a bare array,
/// never null — contracts.ts declares pagination as required.
/// </summary>
public sealed class PageInfo
{
    public const int DefaultLimit = 50;

    [JsonPropertyName("total")]   public int Total { get; init; }
    [JsonPropertyName("page")]    public int Page { get; init; }
    [JsonPropertyName("limit")]   public int Limit { get; init; }
    [JsonPropertyName("pages")]   public int Pages { get; init; }
    [JsonPropertyName("hasNext")] public bool HasNext { get; init; }
    [JsonPropertyName("hasPrev")] public bool HasPrev { get; init; }

    public static PageInfo Create(int total, int page, int limit)
    {
        var pages = limit <= 0 ? 0 : (int)Math.Ceiling(total / (double)limit);
        return new PageInfo
        {
            Total = total,
            Page = page,
            Limit = limit,
            Pages = pages,
            HasNext = page < pages,
            HasPrev = page > 1,
        };
    }

    public static PageInfo Empty(int page = 1, int limit = DefaultLimit)
        => Create(0, page, limit);
}

public class Paged<T>
{
    public const int DefaultLimit = PageInfo.DefaultLimit;

    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = [];

    [JsonPropertyName("pagination")]
    public PageInfo Pagination { get; init; } = PageInfo.Empty();

    public static Paged<T> Empty(int page = 1, int limit = DefaultLimit)
        => new() { Items = [], Pagination = PageInfo.Empty(page, limit) };
}

/// <summary>
/// For the endpoints that ALSO emit the legacy aliases.
///
/// RESOLVED (API-CONTRACT §0.5): keep them. attendanceController.js:335-336
/// sends both envelopes, and payroll/page.tsx:704 reads `leaves.records`.
/// Dropping either breaks a live page.
///
/// Only attendance/student/:id and the leaves endpoints need this — do not
/// blanket-apply it, or every list response grows a duplicate array.
/// </summary>
public sealed class PagedWithAliases<T> : Paged<T>
{
    [JsonPropertyName("records")]
    public IReadOnlyList<T> Records => Items;

    [JsonPropertyName("count")]
    public int Count => Pagination.Total;
}

public static class PagedExtensions
{
    /// <summary>
    /// NOTE: two round-trips (COUNT + page). That matches paginate.js and is fine
    /// for these table sizes. Do not "optimise" to a window function without
    /// measuring — COUNT over a filtered index is cheap here.
    /// </summary>
    public static async Task<Paged<T>> ToPagedAsync<T>(
        this IQueryable<T> query,
        int? page,
        int? limit,
        CancellationToken ct = default)
    {
        var p = Math.Max(1, page ?? 1);
        var l = Math.Clamp(limit ?? Paged<T>.DefaultLimit, 1, 500);

        var total = await query.CountAsync(ct);

        // Short-circuit: skip the second query when the page is provably empty.
        if (total == 0)
            return Paged<T>.Empty(p, l);

        var items = await query.Skip((p - 1) * l).Take(l).ToListAsync(ct);

        return new Paged<T>
        {
            Items = items,
            Pagination = PageInfo.Create(total, p, l),
        };
    }
}
