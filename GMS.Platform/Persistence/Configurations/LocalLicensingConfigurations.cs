namespace GMS.Platform.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Platform.Entities;

public class LocalLicenseConfiguration : IEntityTypeConfiguration<LocalLicense>
{
    public void Configure(EntityTypeBuilder<LocalLicense> builder)
    {
        builder.ToTable("local_licenses", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_local_licenses_status",
                "[Status] IN ('created','pending_activation','active','suspended','revoked')");
            t.HasCheckConstraint(
                "CK_local_licenses_device_limit",
                "[DeviceLimit] > 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LicenseKey).HasMaxLength(40).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CustomerContact).HasMaxLength(200);
        builder.Property(x => x.DealReference).HasMaxLength(100);
        builder.Property(x => x.Product).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Edition).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.RevokedReason).HasMaxLength(500);
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.LicenseKey).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CustomerName);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.ContractId);

        builder.HasOne<PlatformCustomer>()
            .WithMany()
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<PlatformContract>()
            .WithMany()
            .HasForeignKey(x => x.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Changes)
            .WithOne(x => x.License!)
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Installations)
            .WithOne(x => x.License!)
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class LocalLicenseChangeConfiguration : IEntityTypeConfiguration<LocalLicenseChange>
{
    public void Configure(EntityTypeBuilder<LocalLicenseChange> builder)
    {
        builder.ToTable("local_license_changes", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_local_license_changes_initiated_by",
                "[InitiatedBy] IN ('platform_admin','system','customer')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChangeType).HasMaxLength(30).IsRequired();
        builder.Property(x => x.FromStatus).HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasMaxLength(20);
        builder.Property(x => x.InitiatedBy).HasMaxLength(15).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.HasIndex(x => x.LicenseId);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

public class LocalInstallationConfiguration : IEntityTypeConfiguration<LocalInstallation>
{
    public void Configure(EntityTypeBuilder<LocalInstallation> builder)
    {
        builder.ToTable("local_installations", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_local_installations_status",
                "[Status] IN ('active','deactivated')");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.InstallationId).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(15).IsRequired();
        builder.Property(x => x.MachineFingerprint).HasMaxLength(128);
        builder.Property(x => x.DeactivationReason).HasMaxLength(500);
        builder.Property(x => x.GymCode).HasMaxLength(40);
        builder.Property(x => x.GymName).HasMaxLength(200);
        builder.Property(x => x.AppVersion).HasMaxLength(40);

        // One license may only be actively bound to a given installation-id once — re-activating
        // the SAME installation (e.g. after a validate cycle) must update this row, never insert
        // a duplicate. A DIFFERENT installation-id trying to bind the same license while this one
        // is still active is the anti-resale case, rejected in LocalLicenseService, not by this
        // constraint (device limit is a business rule with a count check, not a uniqueness rule).
        builder.HasIndex(x => new { x.LicenseId, x.InstallationId }).IsUnique();
        builder.HasIndex(x => x.InstallationId);
        builder.HasIndex(x => x.Status);
    }
}

public class LocalActivationAttemptConfiguration : IEntityTypeConfiguration<LocalActivationAttempt>
{
    public void Configure(EntityTypeBuilder<LocalActivationAttempt> builder)
    {
        builder.ToTable("local_activation_attempts", "platform");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.LicenseKeyAttempted).HasMaxLength(40).IsRequired();
        builder.Property(x => x.InstallationId).HasMaxLength(40);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.Result).HasMaxLength(30).IsRequired();

        builder.HasIndex(x => x.LicenseId);
        builder.HasIndex(x => x.LicenseKeyAttempted);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.Result);
    }
}

public class LocalLifecycleEventConfiguration : IEntityTypeConfiguration<LocalLifecycleEvent>
{
    public void Configure(EntityTypeBuilder<LocalLifecycleEvent> builder)
    {
        builder.ToTable("local_lifecycle_events", "platform");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(80).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.InstallationId).HasMaxLength(40);
        builder.Property(x => x.GymCode).HasMaxLength(40);
        builder.Property(x => x.GymName).HasMaxLength(200);
        builder.Property(x => x.Message).HasMaxLength(500);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => x.OperationId);
        builder.HasIndex(x => x.LicenseId);
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

public class LocalOwnerRecoveryRequestConfiguration : IEntityTypeConfiguration<LocalOwnerRecoveryRequest>
{
    public void Configure(EntityTypeBuilder<LocalOwnerRecoveryRequest> builder)
    {
        builder.ToTable("local_owner_recoveries", "platform", t =>
        {
            t.HasCheckConstraint(
                "CK_local_owner_recoveries_status",
                "[Status] IN ('pending','approved','completed','rejected','expired','cancelled','revoked')");
            t.HasCheckConstraint(
                "CK_local_owner_recoveries_method",
                "[Method] IN ('online','offline')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InstallationId).HasMaxLength(40).IsRequired();
        builder.Property(x => x.GymCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.GymName).HasMaxLength(200);
        builder.Property(x => x.Nonce).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.Method).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DecisionReason).HasMaxLength(500);
        builder.Property(x => x.ApprovalBlob).HasMaxLength(2000);
        builder.Property(x => x.HistoryJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.HasIndex(x => x.InstallationId);
        builder.HasIndex(x => x.CustomerId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.ApprovalJti).IsUnique().HasFilter("[ApprovalJti] IS NOT NULL");
        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
