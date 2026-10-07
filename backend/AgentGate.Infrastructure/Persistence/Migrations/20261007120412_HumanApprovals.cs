using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HumanApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_AgentActions_Id_OrganizationId",
                table: "AgentActions",
                columns: new[] { "Id", "OrganizationId" });

            migrationBuilder.CreateTable(
                name: "ApprovalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReviewerRole = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewerComment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRequests", x => x.Id);
                    table.UniqueConstraint("AK_ApprovalRequests_Id_OrganizationId", x => new { x.Id, x.OrganizationId });
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_AgentActions_ActionId_OrganizationId",
                        columns: x => new { x.ActionId, x.OrganizationId },
                        principalTable: "AgentActions",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalRequests_Users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_ApprovalRequests_ApprovalRequestId_Organi~",
                        columns: x => new { x.ApprovalRequestId, x.OrganizationId },
                        principalTable: "ApprovalRequests",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApprovalDecisions_Users_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_ApprovalRequestId_OrganizationId",
                table: "ApprovalDecisions",
                columns: new[] { "ApprovalRequestId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_OrganizationId_ApprovalRequestId",
                table: "ApprovalDecisions",
                columns: new[] { "OrganizationId", "ApprovalRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDecisions_ReviewerUserId",
                table: "ApprovalDecisions",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ActionId_OrganizationId",
                table: "ApprovalRequests",
                columns: new[] { "ActionId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_OrganizationId_Status_RequestedAt_Id",
                table: "ApprovalRequests",
                columns: new[] { "OrganizationId", "Status", "RequestedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_ResolvedByUserId",
                table: "ApprovalRequests",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRequests_Status_ExpiresAt",
                table: "ApprovalRequests",
                columns: new[] { "Status", "ExpiresAt" });
            migrationBuilder.Sql("""
                INSERT INTO "ApprovalRequests" ("Id", "OrganizationId", "ActionId", "Status", "ReviewerRole", "RequestedAt", "ExpiresAt", "ReviewerComment", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), "OrganizationId", "Id", 'Pending', COALESCE("ReviewerRole", 'Reviewer'),
                       now(), now() + interval '24 hours', '', now(), now()
                FROM "AgentActions" WHERE "Decision" = 'Review' AND "Status" = 'AwaitingApproval' AND NOT "TestEvaluation";

                CREATE FUNCTION agentgate_approval_decisions_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Approval decisions are append-only' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER approval_decisions_immutable BEFORE UPDATE OR DELETE ON "ApprovalDecisions"
                FOR EACH ROW EXECUTE FUNCTION agentgate_approval_decisions_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDecisions");
            migrationBuilder.Sql("DROP FUNCTION agentgate_approval_decisions_immutable();");

            migrationBuilder.DropTable(
                name: "ApprovalRequests");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AgentActions_Id_OrganizationId",
                table: "AgentActions");
        }
    }
}
