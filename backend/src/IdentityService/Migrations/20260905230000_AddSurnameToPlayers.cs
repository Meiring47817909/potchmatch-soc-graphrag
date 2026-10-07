using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Migrations
{
    /// <inheritdoc />
    public partial class AddSurnameToPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue backfills any players who registered before this
            // migration ran with an empty surname rather than leaving the column
            // null - new registrations always supply their own value (see
            // AuthController.Register), so the default only ever applies to that
            // one backfill.
            migrationBuilder.AddColumn<string>(
                name: "Surname",
                table: "Players",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Surname",
                table: "Players");
        }
    }
}
