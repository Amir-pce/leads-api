using System.Security.Cryptography;
using System.Text;
using Api.Data;
using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Features.Leads;

public static class LeadEndpoints
{
    /// <summary>A second enquiry from the same sender inside this window is treated as a repeat.</summary>
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(2);

    public static IEndpointRouteBuilder MapLeadEndpoints(this IEndpointRouteBuilder app)
    {
        var publicGroup = app.MapGroup("/api/leads")
            .WithTags("Leads")
            .RequireRateLimiting("contact-form");

        publicGroup.MapPost("/", Create)
            .WithName("CreateLead")
            .WithSummary("Submit the contact form.");

        var adminGroup = app.MapGroup("/api/admin/leads")
            .WithTags("Leads (admin)")
            .AddEndpointFilter<AdminKeyFilter>();

        adminGroup.MapGet("/", List).WithName("ListLeads");
        adminGroup.MapGet("/{id:int}", GetOne).WithName("GetLead");
        adminGroup.MapPatch("/{id:int}", Update).WithName("UpdateLead");

        return app;
    }

    // ---------------------------------------------------------------- public

    private static async Task<IResult> Create(
        CreateLeadRequest request,
        AppDbContext db,
        HttpContext http,
        ILogger<Lead> log,
        CancellationToken ct)
    {
        // A filled honeypot is a bot. Answer 202 rather than an error: a bot that is told
        // it failed simply retries with the field cleared.
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            log.LogInformation("Contact form honeypot tripped; submission dropped.");
            return TypedResults.Accepted((string?)null);
        }

        var senderHash = HashSender(http);

        var since = DateTime.UtcNow - DuplicateWindow;
        var isRepeat = senderHash is not null && await db.Leads
            .AsNoTracking()
            .AnyAsync(l => l.SenderHash == senderHash && l.CreatedAtUtc >= since, ct);

        if (isRepeat)
        {
            return TypedResults.Problem(
                title: "Already received",
                detail: "We already have your message. Give it a moment before sending another.",
                statusCode: StatusCodes.Status429TooManyRequests);
        }

        var lead = new Lead
        {
            Name = request.Name.Trim(),
            Email = request.Email.Trim().ToLowerInvariant(),
            Company = Blank(request.Company),
            Message = request.Message.Trim(),
            Source = Blank(request.Source),
            SenderHash = senderHash,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);

        log.LogInformation("Lead {LeadId} received from {Email}.", lead.Id, lead.Email);

        // The form only needs to know it worked; the record itself is not public.
        return TypedResults.Created($"/api/admin/leads/{lead.Id}", new { lead.Id });
    }

    // ----------------------------------------------------------------- admin

    private static async Task<IResult> List(
        [AsParameters] LeadQuery query,
        AppDbContext db,
        CancellationToken ct)
    {
        var leads = db.Leads
            .AsNoTracking()
            .Where(l => query.Status == null || l.Status == query.Status);

        var total = await leads.CountAsync(ct);

        var items = await leads
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip(query.Skip)
            .Take(query.Take)
            .Select(l => new LeadSummary(
                l.Id, l.Name, l.Email, l.Company, l.Status, l.CreatedAtUtc))
            .ToListAsync(ct);

        return TypedResults.Ok(new Paged<LeadSummary>(items, total, query.Skip, query.Take));
    }

    private static async Task<IResult> GetOne(int id, AppDbContext db, CancellationToken ct)
    {
        var lead = await db.Leads
            .AsNoTracking()
            .Where(l => l.Id == id)
            .Select(l => new LeadDetail(
                l.Id, l.Name, l.Email, l.Company, l.Message,
                l.Source, l.Status, l.CreatedAtUtc, l.Note))
            .FirstOrDefaultAsync(ct);

        return lead is null ? TypedResults.NotFound() : TypedResults.Ok(lead);
    }

    private static async Task<IResult> Update(
        int id,
        UpdateLeadRequest request,
        AppDbContext db,
        CancellationToken ct)
    {
        var lead = await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (lead is null) return TypedResults.NotFound();

        if (request.Status is { } status) lead.Status = status;
        if (request.Note is not null) lead.Note = Blank(request.Note);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new LeadDetail(
            lead.Id, lead.Name, lead.Email, lead.Company, lead.Message,
            lead.Source, lead.Status, lead.CreatedAtUtc, lead.Note));
    }

    // ---------------------------------------------------------------- helpers

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? HashSender(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ip)) return null;

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip));
        return Convert.ToHexStringLower(bytes);
    }
}
