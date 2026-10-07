using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlackIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ApprovalDecisions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Dashboard");

            migrationBuilder.CreateTable(
                name: "SlackDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MessageTs = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SentStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlackDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SlackDeliveries_ApprovalRequests_ApprovalId_OrganizationId",
                        columns: x => new { x.ApprovalId, x.OrganizationId },
                        principalTable: "ApprovalRequests",
                        principalColumns: new[] { "Id", "OrganizationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlackIntegrations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlackIntegrations", x => x.OrganizationId);
                    table.ForeignKey(
                        name: "FK_SlackIntegrations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SlackReviewers",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlackUserId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlackReviewers", x => new { x.OrganizationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_SlackReviewers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SlackReviewers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlackDeliveries_ApprovalId_OrganizationId",
                table: "SlackDeliveries",
                columns: new[] { "ApprovalId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SlackDeliveries_NextAttemptAt",
                table: "SlackDeliveries",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_SlackDeliveries_OrganizationId_ApprovalId",
                table: "SlackDeliveries",
                columns: new[] { "OrganizationId", "ApprovalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlackReviewers_OrganizationId_SlackUserId",
                table: "SlackReviewers",
                columns: new[] { "OrganizationId", "SlackUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SlackReviewers_UserId",
                table: "SlackReviewers",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlackDeliveries");

            migrationBuilder.DropTable(
                name: "SlackIntegrations");

            migrationBuilder.DropTable(
                name: "SlackReviewers");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ApprovalDecisions");
        }
    }
}
