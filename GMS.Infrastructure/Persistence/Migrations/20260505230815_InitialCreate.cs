using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: false),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Africa/Cairo"),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    MaxMembers = table.Column<int>(type: "int", nullable: false, defaultValue: 1000),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SubscriptionStartDate = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    SubscriptionEndDate = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProfilePhotoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "staff"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    LastLoginAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_app_users_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "membership_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    NameAr = table.Column<string>(type: "NVARCHAR(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DescriptionAr = table.Column<string>(type: "NVARCHAR(500)", maxLength: 500, nullable: false),
                    PlanType = table.Column<string>(type: "VARCHAR(30)", maxLength: 30, nullable: false, defaultValue: "monthly_unlimited"),
                    DurationDays = table.Column<int>(type: "int", nullable: false),
                    SessionCount = table.Column<int>(type: "INT", nullable: true),
                    Price = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "EGP"),
                    TimeRestrictionStart = table.Column<TimeOnly>(type: "TIME", nullable: true),
                    TimeRestrictionEnd = table.Column<TimeOnly>(type: "TIME", nullable: true),
                    InvitationQuota = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_membership_plans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_membership_plans_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gym_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FullNameAr = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    NationalIdEncrypted = table.Column<string>(type: "NVARCHAR(MAX)", maxLength: 4000, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "DATE", nullable: false),
                    ProfilePhotoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AppUserId = table.Column<Guid>(type: "UNIQUEIDENTIFIER", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    InvitationQuotaRemaining = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    LastInvitationResetDate = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gym_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gym_members_app_users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gym_members_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "member_invitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitingMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConvertedMemberId = table.Column<Guid>(type: "UNIQUEIDENTIFIER", nullable: true),
                    GuestName = table.Column<string>(type: "NVARCHAR(200)", maxLength: 200, nullable: false),
                    GuestPhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    VisitDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false, defaultValue: "pending"),
                    QuotaPeriod = table.Column<string>(type: "NVARCHAR(7)", maxLength: 7, nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    VisitedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    ConvertedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_member_invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_member_invitations_gym_members_ConvertedMemberId",
                        column: x => x.ConvertedMemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_member_invitations_gym_members_InvitingMemberId",
                        column: x => x.InvitingMemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_member_invitations_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "DATE", nullable: false),
                    Status = table.Column<string>(type: "VARCHAR(15)", maxLength: 15, nullable: false, defaultValue: "active"),
                    SessionsRemaining = table.Column<int>(type: "INT", nullable: true),
                    FrozenFromDate = table.Column<DateOnly>(type: "DATE", nullable: true),
                    FrozenUntilDate = table.Column<DateOnly>(type: "DATE", nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "cash"),
                    AmountPaid = table.Column<decimal>(type: "DECIMAL(12,2)", nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    LastRenewalDate = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_memberships_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_memberships_membership_plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "membership_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_memberships_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "gym_attendance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "UNIQUEIDENTIFIER", nullable: true),
                    CheckInAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false),
                    CheckOutAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    EntryMethod = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false, defaultValue: "qr"),
                    ManualReason = table.Column<string>(type: "NVARCHAR(100)", maxLength: 100, nullable: true),
                    Duration = table.Column<TimeSpan>(type: "TIME", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "DATETIME2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gym_attendance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gym_attendance_app_users_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "app_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_gym_attendance_gym_members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "gym_members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_gym_attendance_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gym_attendance_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_Email",
                table: "app_users",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_app_users_TenantId",
                table: "app_users",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_app_users_TenantId_IsActive",
                table: "app_users",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_app_users_TenantId_UserId",
                table: "app_users",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_MemberId",
                table: "gym_attendance",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_MembershipId",
                table: "gym_attendance",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_StaffUserId",
                table: "gym_attendance",
                column: "StaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_TenantId",
                table: "gym_attendance",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_TenantId_CheckInAtUtc",
                table: "gym_attendance",
                columns: new[] { "TenantId", "CheckInAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_gym_attendance_TenantId_MemberId",
                table: "gym_attendance",
                columns: new[] { "TenantId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_AppUserId",
                table: "gym_members",
                column: "AppUserId",
                unique: true,
                filter: "[AppUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId",
                table: "gym_members",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_IsActive",
                table: "gym_members",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_IsDeleted",
                table: "gym_members",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_MemberNumber",
                table: "gym_members",
                columns: new[] { "TenantId", "MemberNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gym_members_TenantId_PhoneNumber",
                table: "gym_members",
                columns: new[] { "TenantId", "PhoneNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_ConvertedMemberId",
                table: "member_invitations",
                column: "ConvertedMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_InvitingMemberId",
                table: "member_invitations",
                column: "InvitingMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId",
                table: "member_invitations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_InvitingMemberId",
                table: "member_invitations",
                columns: new[] { "TenantId", "InvitingMemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_IsDeleted",
                table: "member_invitations",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_QuotaPeriod",
                table: "member_invitations",
                columns: new[] { "TenantId", "QuotaPeriod" });

            migrationBuilder.CreateIndex(
                name: "IX_member_invitations_TenantId_Status",
                table: "member_invitations",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_plans_TenantId",
                table: "membership_plans",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_membership_plans_TenantId_IsActive",
                table: "membership_plans",
                columns: new[] { "TenantId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_membership_plans_TenantId_PlanType",
                table: "membership_plans",
                columns: new[] { "TenantId", "PlanType" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_MemberId",
                table: "memberships",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_PlanId",
                table: "memberships",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_TenantId",
                table: "memberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_memberships_TenantId_IsDeleted",
                table: "memberships",
                columns: new[] { "TenantId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_TenantId_MemberId",
                table: "memberships",
                columns: new[] { "TenantId", "MemberId" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_TenantId_Status",
                table: "memberships",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_memberships_TenantId_Status_EndDate",
                table: "memberships",
                columns: new[] { "TenantId", "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Email",
                table: "tenants",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tenants_IsActive",
                table: "tenants",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gym_attendance");

            migrationBuilder.DropTable(
                name: "member_invitations");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "gym_members");

            migrationBuilder.DropTable(
                name: "membership_plans");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}
