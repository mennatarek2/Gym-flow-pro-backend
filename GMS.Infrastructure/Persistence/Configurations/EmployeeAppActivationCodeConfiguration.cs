namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class EmployeeAppActivationCodeConfiguration : IEntityTypeConfiguration<EmployeeAppActivationCode>
{
    public void Configure(EntityTypeBuilder<EmployeeAppActivationCode> builder)
    {
        builder.ToTable("employee_app_activation_codes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.EmployeeId).IsRequired();

        builder.Property(x => x.CodeHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.ConsumedAtUtc);
        builder.Property(x => x.RevokedAtUtc);
        builder.Property(x => x.CreatedByUserId);

        builder.Property(x => x.RowVersion)
            .IsRowVersion();

        builder.HasIndex(x => new { x.TenantId, x.CodeHash });
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ConsumedAtUtc, x.RevokedAtUtc });

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Employee)
            .WithMany()
            .HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
