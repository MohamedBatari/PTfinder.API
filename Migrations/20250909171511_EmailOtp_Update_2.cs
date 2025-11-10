using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTfinder.API.Migrations
{
    /// <inheritdoc />
    public partial class EmailOtp_Update_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Availabilities_CoachId",
                table: "Availabilities");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "EmailOtps",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "CodeHash",
                table: "EmailOtps",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "EmailOtps",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "EmailOtps",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<string>(
                name: "TimeSlot",
                table: "Availabilities",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtps_Email",
                table: "EmailOtps",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtps_Email_CodeHash",
                table: "EmailOtps",
                columns: new[] { "Email", "CodeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailOtps_ExpiresUtc",
                table: "EmailOtps",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_CoachId_AvailableDate_TimeSlot",
                table: "Availabilities",
                columns: new[] { "CoachId", "AvailableDate", "TimeSlot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_EmailOtps_Email",
                table: "EmailOtps");

            migrationBuilder.DropIndex(
                name: "IX_EmailOtps_Email_CodeHash",
                table: "EmailOtps");

            migrationBuilder.DropIndex(
                name: "IX_EmailOtps_ExpiresUtc",
                table: "EmailOtps");

            migrationBuilder.DropIndex(
                name: "IX_Availabilities_CoachId_AvailableDate_TimeSlot",
                table: "Availabilities");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "EmailOtps");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "EmailOtps",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(320)",
                oldMaxLength: 320);

            migrationBuilder.AlterColumn<string>(
                name: "CodeHash",
                table: "EmailOtps",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<int>(
                name: "Attempts",
                table: "EmailOtps",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "TimeSlot",
                table: "Availabilities",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateIndex(
                name: "IX_Availabilities_CoachId",
                table: "Availabilities",
                column: "CoachId");
        }
    }
}
