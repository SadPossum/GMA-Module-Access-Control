using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialAccessControlSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "principals",
                schema: "access",
                columns: table => new
                {
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_principals", x => new { x.Kind, x.SubjectId });
                });

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_permissions_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subject_role_assignments",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subject_role_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subject_role_assignments_principals_SubjectKind_SubjectId",
                        columns: x => new { x.SubjectKind, x.SubjectId },
                        principalSchema: "access",
                        principalTable: "principals",
                        principalColumns: new[] { "Kind", "SubjectId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subject_role_assignments_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "access",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_RoleId_PermissionCode",
                schema: "access",
                table: "role_permissions",
                columns: new[] { "RoleId", "PermissionCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_roles_Name",
                schema: "access",
                table: "roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subject_role_assignments_RoleId",
                schema: "access",
                table: "subject_role_assignments",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_subject_role_assignments_SubjectKind_SubjectId_RoleId_Scope",
                schema: "access",
                table: "subject_role_assignments",
                columns: new[] { "SubjectKind", "SubjectId", "RoleId", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subject_role_assignments_SubjectKind_SubjectId_Scope",
                schema: "access",
                table: "subject_role_assignments",
                columns: new[] { "SubjectKind", "SubjectId", "Scope" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "subject_role_assignments",
                schema: "access");

            migrationBuilder.DropTable(
                name: "principals",
                schema: "access");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "access");
        }
    }
}
