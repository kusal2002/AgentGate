using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgentIdentityAndApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                    table.UniqueConstraint("AK_Agents_Id_OrganizationId_Environment", x => new { x.Id, x.OrganizationId, x.Environment });
                    table.ForeignKey(
                        name: "FK_Agents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AgentApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentApiKeys_Agents_AgentId_OrganizationId_Environment",
                        columns: x => new { x.AgentId, x.OrganizationId, x.Environment },
                        principalTable: "Agents",
                        principalColumns: new[] { "Id", "OrganizationId", "Environment" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentApiKeys_AgentId_OrganizationId_Environment",
                table: "AgentApiKeys",
                columns: new[] { "AgentId", "OrganizationId", "Environment" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentApiKeys_KeyPrefix",
                table: "AgentApiKeys",
                column: "KeyPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentApiKeys_OrganizationId_AgentId",
                table: "AgentApiKeys",
                columns: new[] { "OrganizationId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_OrganizationId_Slug",
                table: "Agents",
                columns: new[] { "OrganizationId", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentApiKeys");

            migrationBuilder.DropTable(
                name: "Agents");
        }
    }
}
