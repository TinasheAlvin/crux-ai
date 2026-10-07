using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VhonaAI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminConsole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisabledAt",
                table: "Organizations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Target = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlatformCallDefaults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoppedMissedCycles = table.Column<int>(type: "int", nullable: false),
                    DroppedPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DroppedMonths = table.Column<int>(type: "int", nullable: false),
                    MinimumInvoiceHistory = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformCallDefaults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditEntries_At",
                table: "AdminAuditEntries",
                column: "At");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditEntries");

            migrationBuilder.DropTable(
                name: "PlatformCallDefaults");

            migrationBuilder.DropColumn(
                name: "DisabledAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DisabledAt",
                table: "Organizations");
        }
    }
}
