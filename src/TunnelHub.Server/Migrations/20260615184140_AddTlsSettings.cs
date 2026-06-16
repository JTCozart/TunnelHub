using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunnelHub.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTlsSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AcmeEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcmeEmail = table.Column<string>(type: "TEXT", nullable: true),
                    AcmeAgreeTos = table.Column<bool>(type: "INTEGER", nullable: false),
                    UseStaging = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcmeAccountKeyPem = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssuedCertificates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    PfxData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    NotAfter = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedCertificates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCertificates_Host",
                table: "IssuedCertificates",
                column: "Host",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminSettings");

            migrationBuilder.DropTable(
                name: "IssuedCertificates");
        }
    }
}
