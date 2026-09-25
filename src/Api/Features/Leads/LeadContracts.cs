using System.ComponentModel.DataAnnotations;
using Api.Domain;

namespace Api.Features.Leads;

/// <summary>What the contact form posts. Never the entity — the client cannot set status or id.</summary>
public record CreateLeadRequest
{
    [Required, StringLength(120, MinimumLength = 2)]
    public string Name { get; init; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";

    [StringLength(160)]
    public string? Company { get; init; }

    [Required, StringLength(4000, MinimumLength = 10)]
    public string Message { get; init; } = "";

    [StringLength(200)]
    public string? Source { get; init; }

    /// <summary>
    /// Hidden field, invisible to a person and left empty by a real browser.
    /// Bots fill every input they find, so anything here means the submission is automated.
    /// </summary>
    [StringLength(200)]
    public string? Website { get; init; }
}

public record LeadSummary(
    int Id,
    string Name,
    string Email,
    string? Company,
    LeadStatus Status,
    DateTime CreatedAtUtc);

public record LeadDetail(
    int Id,
    string Name,
    string Email,
    string? Company,
    string Message,
    string? Source,
    LeadStatus Status,
    DateTime CreatedAtUtc,
    string? Note);

public record UpdateLeadRequest
{
    public LeadStatus? Status { get; init; }

    [StringLength(2000)]
    public string? Note { get; init; }
}

public record Paged<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);

/// <summary>
/// Query string for the admin list, bound with [AsParameters].
///
/// Offset and Limit are nullable on purpose. [AsParameters] binding treats a non-nullable
/// value type as required and throws when it is missing from the query string — a property
/// initialiser is never consulted. Nullable here, defaulted below, so /api/admin/leads
/// works with no query string at all.
/// </summary>
public record LeadQuery
{
    public LeadStatus? Status { get; init; }

    [Range(0, int.MaxValue)]
    public int? Offset { get; init; }

    [Range(1, MaxPageSize)]
    public int? Limit { get; init; }

    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public int Skip => Offset ?? 0;

    public int Take => Math.Clamp(Limit ?? DefaultPageSize, 1, MaxPageSize);
}
