using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentGate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SlackInteractionFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SlackFeedback",
                columns: table => new
                {
                    RequestKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SlackUserId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlackFeedback", x => x.RequestKey);
                    table.ForeignKey(
                        name: "FK_SlackFeedback_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlackFeedback_NextAttemptAt",
                table: "SlackFeedback",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_SlackFeedback_OrganizationId",
                table: "SlackFeedback",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SlackFeedback");
        }
    }
}
