using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PremiumSync.PolicyService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnualPremiumChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualPremiumChange",
                table: "Transactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualPremiumChange",
                table: "Transactions");
        }
    }
}
