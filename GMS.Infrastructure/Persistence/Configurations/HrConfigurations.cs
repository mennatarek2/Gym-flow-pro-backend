namespace GMS.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GMS.Core.Entities;

public class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.Name).IsRequired().HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(d => d.NameAr).HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(d => d.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.UpdatedAtUtc);
        builder.Property(d => d.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(d => new { d.TenantId, d.Name }).IsUnique();

        builder.HasOne(d => d.Tenant).WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("positions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(p => p.TenantId).IsRequired();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(p => p.NameAr).HasMaxLength(80).HasColumnType("NVARCHAR(80)");
        builder.Property(p => p.Description).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(p => p.DefaultBasicSalary).HasColumnType("DECIMAL(14,2)");
        builder.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(p => p.CreatedAtUtc).IsRequired();
        builder.Property(p => p.UpdatedAtUtc);
        builder.Property(p => p.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(p => new { p.TenantId, p.DepartmentId });

        builder.HasOne(p => p.Tenant).WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Department).WithMany(d => d.Positions).HasForeignKey(p => p.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeNumber).IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)");
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100).HasColumnType("NVARCHAR(100)");
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100).HasColumnType("NVARCHAR(100)");
        builder.Property(e => e.Phone).HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(e => e.Email).HasMaxLength(256).HasColumnType("NVARCHAR(256)");
        builder.Property(e => e.NationalId).HasMaxLength(30).HasColumnType("VARCHAR(30)");
        builder.Property(e => e.DateOfBirth).HasColumnType("DATE");
        builder.Property(e => e.Address).HasMaxLength(300).HasColumnType("NVARCHAR(300)");
        builder.Property(e => e.PhotoUrl).HasMaxLength(500).HasColumnType("NVARCHAR(500)");
        builder.Property(e => e.HireDate).IsRequired().HasColumnType("DATE");
        builder.Property(e => e.TerminationDate).HasColumnType("DATE");
        builder.Property(e => e.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");

        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.Property(e => e.UpdatedAtUtc);
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeNumber })
            .IsUnique()
            .HasFilter("[EmployeeNumber] IS NOT NULL");

        builder.HasIndex(e => e.AppUserId)
            .IsUnique()
            .HasFilter("[AppUserId] IS NOT NULL");

        builder.HasIndex(e => e.EmployeeAppUserId)
            .IsUnique()
            .HasFilter("[EmployeeAppUserId] IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.HasIndex(e => new { e.TenantId, e.DepartmentId });

        builder.HasOne(e => e.Tenant).WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Department).WithMany(d => d.Employees).HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Position).WithMany(p => p.Employees).HasForeignKey(e => e.PositionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AppUser).WithMany().HasForeignKey(e => e.AppUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.EmployeeAppUser).WithMany().HasForeignKey(e => e.EmployeeAppUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class EmployeeContractConfiguration : IEntityTypeConfiguration<EmployeeContract>
{
    public void Configure(EntityTypeBuilder<EmployeeContract> builder)
    {
        builder.ToTable("employee_contracts");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedOnAdd();

        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.EmployeeId).IsRequired();
        builder.Property(c => c.ContractNumber).IsRequired().HasMaxLength(12).HasColumnType("VARCHAR(12)");
        builder.Property(c => c.EmploymentType).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(c => c.StartDate).IsRequired().HasColumnType("DATE");
        builder.Property(c => c.EndDate).HasColumnType("DATE");
        builder.Property(c => c.BasicSalary).IsRequired().HasColumnType("DECIMAL(14,2)");
        builder.Property(c => c.Status).IsRequired().HasMaxLength(20).HasColumnType("VARCHAR(20)");
        builder.Property(c => c.Notes).HasMaxLength(1000).HasColumnType("NVARCHAR(1000)");

        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc);
        builder.Property(c => c.IsDeleted).IsRequired().HasDefaultValue(false);

        builder.HasIndex(c => new { c.TenantId, c.ContractNumber }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.EmployeeId, c.StartDate });

        builder.HasOne(c => c.Employee).WithMany(e => e.Contracts).HasForeignKey(c => c.EmployeeId).OnDelete(DeleteBehavior.Cascade);
    }
}
