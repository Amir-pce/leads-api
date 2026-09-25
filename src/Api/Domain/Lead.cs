namespace Api.Domain;

/// <summary>
/// An enquiry submitted through the contact form on the site.
/// </summary>
public class Lead
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public required string Email { get; set; }

    public string? Company { get; set; }

    public required string Message { get; set; }

    /// <summary>Which page or campaign the enquiry came from, when the form reports it.</summary>
    public string? Source { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.New;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// SHA-256 of the caller's IP address. Stored as a hash rather than the address itself:
    /// it is enough to spot one sender flooding the form, and it is not personal data at rest.
    /// </summary>
    public string? SenderHash { get; set; }

    /// <summary>Free-text note added from the admin side after reading the enquiry.</summary>
    public string? Note { get; set; }
}
