using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ztpr.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTunnelLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults match the previous appsettings values so existing deployments
            // keep their limits (0 would mean "disabled" for these).
            migrationBuilder.AddColumn<int>(
                name: "IdleTimeoutMinutes",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "MaxTunnelHours",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);

            migrationBuilder.AddColumn<int>(
                name: "MaxTunnelsPerKey",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "ReaperIntervalSeconds",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IdleTimeoutMinutes",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "MaxTunnelHours",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "MaxTunnelsPerKey",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "ReaperIntervalSeconds",
                table: "AdminSettings");
        }
    }
}
