using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class AddCoachProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoachBio",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoachingLevel",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HourlyRateZAR",
                table: "Players",
                type: "numeric",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoachBio",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "CoachingLevel",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "HourlyRateZAR",
                table: "Players");
        }
    }
}
