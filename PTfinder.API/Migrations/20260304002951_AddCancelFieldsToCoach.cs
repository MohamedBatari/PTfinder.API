using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTfinder.API.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelFieldsToCoach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelAtPeriodEnd",
                table: "Coaches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAtUtc",
                table: "Coaches",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelAtPeriodEnd",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "CanceledAtUtc",
                table: "Coaches");
        }
    }
}
