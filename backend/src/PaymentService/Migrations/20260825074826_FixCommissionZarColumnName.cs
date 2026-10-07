using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentService.Migrations
{
    /// <inheritdoc />
    public partial class FixCommissionZarColumnName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PlayerId",
                table: "EscrowTransactions",
                newName: "PayerId");

            migrationBuilder.RenameColumn(
                name: "CommisionZAR",
                table: "EscrowTransactions",
                newName: "CommissionZAR");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PayerId",
                table: "EscrowTransactions",
                newName: "PlayerId");

            migrationBuilder.RenameColumn(
                name: "CommissionZAR",
                table: "EscrowTransactions",
                newName: "CommisionZAR");
        }
    }
}
