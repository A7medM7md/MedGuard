using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedGuard.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorReadingMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MessageId",
                table: "SensorReadings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SensorReadings_MessageId",
                table: "SensorReadings",
                column: "MessageId",
                unique: true,
                filter: "[MessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SensorReadings_MessageId",
                table: "SensorReadings");

            migrationBuilder.DropColumn(
                name: "MessageId",
                table: "SensorReadings");
        }
    }
}
