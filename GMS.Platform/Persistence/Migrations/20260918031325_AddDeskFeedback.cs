using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeskFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "desk_feedback",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GymCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    GymName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SenderEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SenderDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    AppVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    PageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClientRequestId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    InternalNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ResponseToCustomer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReviewedByPlatformAdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_desk_feedback", x => x.Id);
                    table.CheckConstraint("CK_desk_feedback_category", "[Category] IN ('feature','problem','general','other')");
                    table.CheckConstraint("CK_desk_feedback_status", "[Status] IN ('new','under_review','planned','resolved','declined')");
                    table.ForeignKey(
                        name: "FK_desk_feedback_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "platform",
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_desk_feedback_Category",
                schema: "platform",
                table: "desk_feedback",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_desk_feedback_CreatedAtUtc",
                schema: "platform",
                table: "desk_feedback",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_desk_feedback_CustomerId",
                schema: "platform",
                table: "desk_feedback",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_desk_feedback_Status",
                schema: "platform",
                table: "desk_feedback",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_desk_feedback_TenantId",
                schema: "platform",
                table: "desk_feedback",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_desk_feedback_TenantId_ClientRequestId",
                schema: "platform",
                table: "desk_feedback",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true,
                filter: "[ClientRequestId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desk_feedback",
                schema: "platform");
        }
    }
}
