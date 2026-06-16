using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunnelHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedHostnames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagedHostnames",
                table: "AdminSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RenewWithinDays",
                table: "AdminSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ManagedHostnames",
                table: "AdminSettings");

            migrationBuilder.DropColumn(
                name: "RenewWithinDays",
                table: "AdminSettings");
        }
    }
}
