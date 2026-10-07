using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VhonaAI.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class WhoToCall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallCustomerActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SnoozeUntil = table.Column<DateOnly>(type: "date", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EvidenceKey = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallCustomerActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CallCustomerActions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CallCustomerActions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReminderDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    EditedByOwner = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReminderDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReminderDrafts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReminderDrafts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallCustomerActions_CustomerId",
                table: "CallCustomerActions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CallCustomerActions_OrganizationId_CustomerId_At",
                table: "CallCustomerActions",
                columns: new[] { "OrganizationId", "CustomerId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDrafts_CustomerId",
                table: "ReminderDrafts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReminderDrafts_OrganizationId_CustomerId",
                table: "ReminderDrafts",
                columns: new[] { "OrganizationId", "CustomerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallCustomerActions");

            migrationBuilder.DropTable(
                name: "ReminderDrafts");
        }
    }
}
