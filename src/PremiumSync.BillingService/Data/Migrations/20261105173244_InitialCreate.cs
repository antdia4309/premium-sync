using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PremiumSync.BillingService.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceTransactionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PolicyNumber = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Premium = table.Column<decimal>(type: "TEXT", nullable: false),
                    FeeAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CommissionRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_PolicyNumber",
                table: "Transactions",
                column: "PolicyNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceTransactionId",
                table: "Transactions",
                column: "SourceTransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
