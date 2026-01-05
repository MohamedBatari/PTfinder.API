using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTfinder.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTermsAcceptanceToCoach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientTimeZone",
                table: "Coaches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TermsAcceptedAtUtc",
                table: "Coaches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsAcceptedIp",
                table: "Coaches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsVersionAccepted",
                table: "Coaches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "Coaches",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientTimeZone",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedAtUtc",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "TermsAcceptedIp",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "TermsVersionAccepted",
                table: "Coaches");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "Coaches");
        }
    }
}
