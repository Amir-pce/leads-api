using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Lead> Leads => Set<Lead>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Lead>(lead =>
        {
            lead.Property(l => l.Name).HasMaxLength(120).IsRequired();
            lead.Property(l => l.Email).HasMaxLength(254).IsRequired();
            lead.Property(l => l.Company).HasMaxLength(160);
            lead.Property(l => l.Message).HasMaxLength(4000).IsRequired();
            lead.Property(l => l.Source).HasMaxLength(200);
            lead.Property(l => l.SenderHash).HasMaxLength(64);
            lead.Property(l => l.Note).HasMaxLength(2000);

            lead.Property(l => l.Status).HasConversion<int>();

            lead.Property(l => l.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            // The admin list is always "newest first", optionally filtered by status.
            // This covers both the unfiltered and the filtered read without a sort.
            lead.HasIndex(l => new { l.Status, l.CreatedAtUtc })
                .HasDatabaseName("IX_Leads_Status_CreatedAtUtc")
                .IsDescending(false, true);

            // Used by the duplicate check on submit, which looks at one sender in a time window.
            lead.HasIndex(l => new { l.SenderHash, l.CreatedAtUtc })
                .HasDatabaseName("IX_Leads_SenderHash_CreatedAtUtc")
                .IsDescending(false, true);
        });
    }
}
