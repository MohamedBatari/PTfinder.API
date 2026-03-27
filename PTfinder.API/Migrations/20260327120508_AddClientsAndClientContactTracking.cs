using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTfinder.API.Migrations
{
    /// <inheritdoc />
    public partial class AddClientsAndClientContactTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GoogleSub = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PictureUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TermsAccepted = table.Column<bool>(type: "bit", nullable: false),
                    TermsVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TermsAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PrivacyAccepted = table.Column<bool>(type: "bit", nullable: false),
                    PrivacyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PrivacyAcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastIpAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastUserAgent = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClientTimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClientContactViews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    CoachId = table.Column<int>(type: "int", nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Referrer = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClientTimeZone = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientContactViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientContactViews_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientContactViews_Coaches_CoachId",
                        column: x => x.CoachId,
                        principalTable: "Coaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientContactViews_ClientId_CoachId_CreatedAtUtc",
                table: "ClientContactViews",
                columns: new[] { "ClientId", "CoachId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientContactViews_CoachId",
                table: "ClientContactViews",
                column: "CoachId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Email",
                table: "Clients",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_GoogleSub",
                table: "Clients",
                column: "GoogleSub",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientContactViews");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
