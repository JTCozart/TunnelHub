using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ztpr.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults below also seed any pre-existing settings row with safe values
            // (real domains can then be set from the admin UI; invites stay required).
            migrationBuilder.AddColumn<string>(
                name: "AppHost",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "localhost");

            migrationBuilder.AddColumn<string>(
                name: "BaseDomain",
                table: "AdminSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "lvh.me");

            migrationBuilder.AddColumn<bool>(
                name: "HttpsEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireInviteCode",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppHost",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "BaseDomain",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "HttpsEnabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "RequireInviteCode",
                table: "AdminSettings");
        }
    }
}
