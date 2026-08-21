namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Year).IsRequired();
        builder.Property(s => s.Type).IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)");
        builder.Property(s => s.LastNumber).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.Year, s.Type }).IsUnique();

        builder.ToTable("invoice_sequences");
    }
}
