using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ztpr.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailEnabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailFromAddress",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailFromName",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailjetApiKey",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MailjetSecretKeyEnc",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireEmailConfirmation",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailEnabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "EmailFromAddress",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "EmailFromName",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "MailjetApiKey",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "MailjetSecretKeyEnc",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "RequireEmailConfirmation",
                table: "AdminSettings");
        }
    }
}
