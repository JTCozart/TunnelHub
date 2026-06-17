using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ztpr.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMailjetUsageLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MailjetContactLimit",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Default to Mailjet's free-tier monthly allowance so existing deployments get a
            // meaningful usage bar without having to touch settings first.
            migrationBuilder.AddColumn<int>(
                name: "MailjetMonthlyEmailLimit",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 6000);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MailjetContactLimit",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "MailjetMonthlyEmailLimit",
                table: "AdminSettings");
        }
    }
}
