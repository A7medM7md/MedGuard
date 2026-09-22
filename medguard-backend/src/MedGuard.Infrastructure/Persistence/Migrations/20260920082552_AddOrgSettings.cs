using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrgSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    WarningMarginPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    AutoQuarantineOnBreach = table.Column<bool>(type: "bit", nullable: false),
                    CriticalAlertChannels = table.Column<int>(type: "int", nullable: false),
                    WarningAlertChannels = table.Column<int>(type: "int", nullable: false),
                    DeviceSilentAfterMinutes = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgSettings");
        }
    }
}
