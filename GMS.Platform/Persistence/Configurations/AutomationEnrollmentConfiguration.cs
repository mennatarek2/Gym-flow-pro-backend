namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class AutomationEnrollmentConfiguration : IEntityTypeConfiguration<AutomationEnrollment>
{
    public void Configure(EntityTypeBuilder<AutomationEnrollment> builder)
    {
        builder.ToTable("automation_enrollments", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_automation_enrollments_subject_type",
                "[SubjectType] IN ('member','platform_invoice')");
            t.HasCheckConstraint(
                "CK_automation_enrollments_step",
                "[Step] >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.SequenceKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SubjectType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.HaltedReason).HasMaxLength(64);

        builder.HasIndex(x => new { x.SequenceKey, x.SubjectType, x.SubjectId })
            .IsUnique()
            .HasFilter("[HaltedReason] IS NULL")
            .HasDatabaseName("UX_automation_enrollments_active_subject");

        builder.HasIndex(x => new { x.NextRunAtUtc, x.HaltedReason })
            .HasDatabaseName("IX_automation_enrollments_due");
    }
}
