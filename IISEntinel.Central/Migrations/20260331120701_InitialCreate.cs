using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IISEntinel.Central.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentIdentifier = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: false),
                    Fqdn = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AgentVersion = table.Column<string>(type: "TEXT", nullable: false),
                    SecretHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ApprovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevokedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaxUses = table.Column<int>(type: "INTEGER", nullable: false),
                    UsedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDisabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_AgentIdentifier",
                table: "Agents",
                column: "AgentIdentifier",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentTokens_TokenHash",
                table: "EnrollmentTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Agents");

            migrationBuilder.DropTable(
                name: "EnrollmentTokens");
        }
    }
}
