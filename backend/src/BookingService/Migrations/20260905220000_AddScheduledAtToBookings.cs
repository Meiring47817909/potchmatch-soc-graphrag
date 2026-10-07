using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingService.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledAtToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValueSql backfills any bookings that already exist (e.g. ones
            // created before this migration ran) with "now" rather than leaving
            // them with an invalid all-zero DateTime - new rows always supply their
            // own value explicitly (see BookingController.Create), so the default
            // only ever actually applies to this one backfill.
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                table: "Bookings");
        }
    }
}
