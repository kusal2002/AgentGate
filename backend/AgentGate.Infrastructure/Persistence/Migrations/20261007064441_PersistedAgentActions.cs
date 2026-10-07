using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistedAgentActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Agents_Id_OrganizationId",
                table: "Agents",
                columns: new[] { "Id", "OrganizationId" });

            migrationBuilder.CreateTable(
                name: "AgentActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    RiskLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MatchedPolicyId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TestEvaluation = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentActions_Agents_AgentId_OrganizationId",
                        columns: x => new { x.AgentId, x.OrganizationId },
                        principalTable: "Agents",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActions_AgentId_OrganizationId",
                table: "AgentActions",
                columns: new[] { "AgentId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActions_OrganizationId_AgentId_IdempotencyKey",
                table: "AgentActions",
                columns: new[] { "OrganizationId", "AgentId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentActions_OrganizationId_CreatedAt_Id",
                table: "AgentActions",
                columns: new[] { "OrganizationId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentActions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Agents_Id_OrganizationId",
                table: "Agents");
        }
    }
}
