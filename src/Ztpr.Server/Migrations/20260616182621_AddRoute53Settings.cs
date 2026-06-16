using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ztpr.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddRoute53Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Route53AccessKeyId",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Route53Enabled",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Route53HostedZoneId",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Route53SecretAccessKeyEnc",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Route53AccessKeyId",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "Route53Enabled",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "Route53HostedZoneId",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "Route53SecretAccessKeyEnc",
                table: "AdminSettings");
        }
    }
}
