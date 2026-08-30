using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GMS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityBookingSaleIdempotencyIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_activity_bookings_SaleId",
                table: "activity_bookings");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_SaleId",
                table: "activity_bookings",
                column: "SaleId",
                unique: true,
                filter: "[IsDeleted] = 0 AND [SaleId] IS NOT NULL AND [Status] IN ('booked', 'checked_in', 'cancelled_late', 'no_show')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_activity_bookings_SaleId",
                table: "activity_bookings");

            migrationBuilder.CreateIndex(
                name: "IX_activity_bookings_SaleId",
                table: "activity_bookings",
                column: "SaleId");
        }
    }
}
