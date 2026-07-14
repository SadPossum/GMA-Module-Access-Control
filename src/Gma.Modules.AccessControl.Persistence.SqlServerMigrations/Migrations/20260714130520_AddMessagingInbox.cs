using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddMessagingInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Handler = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ScopeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessingStartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inbox_messages", x => new { x.Id, x.Handler });
                });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_Handler_Status",
                schema: "access",
                table: "inbox_messages",
                columns: new[] { "Handler", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_inbox_messages_Status_ProcessedAtUtc",
                schema: "access",
                table: "inbox_messages",
                columns: new[] { "Status", "ProcessedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "access");
        }
    }
}
