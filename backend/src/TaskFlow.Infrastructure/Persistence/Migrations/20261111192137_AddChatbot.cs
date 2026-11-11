using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChatbot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Locale = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    CurrentIntent = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CurrentStep = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Slots = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConsecutiveFallbacks = table.Column<int>(type: "int", nullable: false),
                    LastSequence = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Sender = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    QuickReplyId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntentLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Source = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    RedactedText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    NormalizedText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PredictedIntent = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    SecondIntent = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    SecondConfidence = table.Column<double>(type: "float", nullable: true),
                    ReceivedLocale = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    DetectedLanguage = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    ModelVersion = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    Outcome = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    Accepted = table.Column<bool>(type: "bit", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntentLogs_ChatSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SessionId_Sequence",
                table: "ChatMessages",
                columns: new[] { "SessionId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_ExpiresAt",
                table: "ChatSessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_Status",
                table: "ChatSessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_IntentLogs_CreatedAt",
                table: "IntentLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_IntentLogs_PredictedIntent",
                table: "IntentLogs",
                column: "PredictedIntent");

            migrationBuilder.CreateIndex(
                name: "IX_IntentLogs_SessionId",
                table: "IntentLogs",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "IntentLogs");

            migrationBuilder.DropTable(
                name: "ChatSessions");
        }
    }
}
