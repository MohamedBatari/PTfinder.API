using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTfinder.API.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationToCoach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationExpiresUtc",
                table: "Coaches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationToken",
                table: "Coaches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Coaches",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerificationExpiresUtc",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "EmailVerificationToken",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Coaches");
        }
    }
}
