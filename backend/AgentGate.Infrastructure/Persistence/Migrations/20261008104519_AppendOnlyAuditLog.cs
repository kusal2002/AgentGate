using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AppendOnlyAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovalRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ActorType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    IPAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_AgentActions_ActionId_OrganizationId",
                        columns: x => new { x.ActionId, x.OrganizationId },
                        principalTable: "AgentActions",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Agents_AgentId_OrganizationId",
                        columns: x => new { x.AgentId, x.OrganizationId },
                        principalTable: "Agents",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_ApprovalRequests_ApprovalRequestId_Organization~",
                        columns: x => new { x.ApprovalRequestId, x.OrganizationId },
                        principalTable: "ApprovalRequests",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActionId_OrganizationId",
                table: "AuditEvents",
                columns: new[] { "ActionId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AgentId_OrganizationId",
                table: "AuditEvents",
                columns: new[] { "AgentId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ApprovalRequestId_OrganizationId",
                table: "AuditEvents",
                columns: new[] { "ApprovalRequestId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_ActionId_CreatedAt_EventOrder_Id",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "ActionId", "CreatedAt", "EventOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_AgentId",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "AgentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_CreatedAt_EventOrder_Id",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "CreatedAt", "EventOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_OrganizationId_EventType",
                table: "AuditEvents",
                columns: new[] { "OrganizationId", "EventType" });
            migrationBuilder.Sql("""
                INSERT INTO "AuditEvents" ("Id", "OrganizationId", "AgentId", "ActionId", "ApprovalRequestId", "EventType", "ActorType", "MetadataJson", "CreatedAt", "EventOrder")
                SELECT gen_random_uuid(), a."OrganizationId", a."AgentId", a."Id", r."Id", 'action.imported', 'System',
                    jsonb_build_object('historicalSnapshot', true, 'originalRequestedAt', a."CreatedAt", 'observedStatus', a."Status", 'decision', a."Decision", 'approvalStatus', r."Status"),
                    now(), 0
                FROM "AgentActions" a LEFT JOIN "ApprovalRequests" r ON r."ActionId" = a."Id" AND r."OrganizationId" = a."OrganizationId";

                CREATE FUNCTION agentgate_audit_events_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit events are append-only' USING ERRCODE = '23514';
                END;
                $$;
                CREATE TRIGGER audit_events_immutable BEFORE UPDATE OR DELETE ON "AuditEvents"
                FOR EACH ROW EXECUTE FUNCTION agentgate_audit_events_immutable();
                CREATE TRIGGER audit_events_no_truncate BEFORE TRUNCATE ON "AuditEvents"
                FOR EACH STATEMENT EXECUTE FUNCTION agentgate_audit_events_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");
            migrationBuilder.Sql("DROP FUNCTION agentgate_audit_events_immutable();");
        }
    }
}
