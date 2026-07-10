using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAtomicBootstrapGate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bootstrap_state",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bootstrap_state", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "access",
                table: "bootstrap_state",
                columns: new[] { "Id", "ClaimedAtUtc", "ClaimedBy" },
                values: new object[] { 1, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bootstrap_state",
                schema: "access");
        }
    }
}
