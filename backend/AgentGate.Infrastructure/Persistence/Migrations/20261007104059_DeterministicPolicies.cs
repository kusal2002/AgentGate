using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeterministicPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchedPolicyName",
                table: "AgentActions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PolicyUpdatedAt",
                table: "AgentActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerRole",
                table: "AgentActions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ConditionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewerRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RiskLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SeedKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.Id);
                    table.UniqueConstraint("AK_Policies_Id_OrganizationId", x => new { x.Id, x.OrganizationId });
                    table.ForeignKey(
                        name: "FK_Policies_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentActions_MatchedPolicyId_OrganizationId",
                table: "AgentActions",
                columns: new[] { "MatchedPolicyId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_OrganizationId_ActionType_Enabled",
                table: "Policies",
                columns: new[] { "OrganizationId", "ActionType", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_Policies_OrganizationId_SeedKey",
                table: "Policies",
                columns: new[] { "OrganizationId", "SeedKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AgentActions_Policies_MatchedPolicyId_OrganizationId",
                table: "AgentActions",
                columns: new[] { "MatchedPolicyId", "OrganizationId" },
                principalTable: "Policies",
                principalColumns: new[] { "Id", "OrganizationId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentActions_Policies_MatchedPolicyId_OrganizationId",
                table: "AgentActions");

            migrationBuilder.DropTable(
                name: "Policies");

            migrationBuilder.DropIndex(
                name: "IX_AgentActions_MatchedPolicyId_OrganizationId",
                table: "AgentActions");

            migrationBuilder.DropColumn(
                name: "MatchedPolicyName",
                table: "AgentActions");

            migrationBuilder.DropColumn(
                name: "PolicyUpdatedAt",
                table: "AgentActions");

            migrationBuilder.DropColumn(
                name: "ReviewerRole",
                table: "AgentActions");
        }
    }
}
