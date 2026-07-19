using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedAccessProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_profiles",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerScope = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByKind = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastChangedByKind = table.Column<int>(type: "integer", nullable: false),
                    LastChangedById = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "access_profile_assignments",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedByKind = table.Column<int>(type: "integer", nullable: false),
                    CreatedById = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_profile_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_profile_assignments_access_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "access",
                        principalTable: "access_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_access_profile_assignments_principals_SubjectKind_SubjectId",
                        columns: x => new { x.SubjectKind, x.SubjectId },
                        principalSchema: "access",
                        principalTable: "principals",
                        principalColumns: new[] { "Kind", "SubjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_profile_changes",
                schema: "access",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ActorKind = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SubjectKind = table.Column<int>(type: "integer", nullable: true),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ProfileVersion = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_profile_changes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_access_profile_changes_access_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "access",
                        principalTable: "access_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_profile_permissions",
                schema: "access",
                columns: table => new
                {
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionCode = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_profile_permissions", x => new { x.ProfileId, x.PermissionCode });
                    table.ForeignKey(
                        name: "FK_access_profile_permissions_access_profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalSchema: "access",
                        principalTable: "access_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_profile_assignments_ProfileId_SubjectKind_SubjectId",
                schema: "access",
                table: "access_profile_assignments",
                columns: new[] { "ProfileId", "SubjectKind", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_profile_assignments_SubjectKind_SubjectId_ProfileId",
                schema: "access",
                table: "access_profile_assignments",
                columns: new[] { "SubjectKind", "SubjectId", "ProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_access_profile_changes_ProfileId_OccurredAtUtc_Id",
                schema: "access",
                table: "access_profile_changes",
                columns: new[] { "ProfileId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_access_profile_permissions_PermissionCode",
                schema: "access",
                table: "access_profile_permissions",
                column: "PermissionCode");

            migrationBuilder.CreateIndex(
                name: "IX_access_profiles_OwnerScope_Key",
                schema: "access",
                table: "access_profiles",
                columns: new[] { "OwnerScope", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_profiles_OwnerScope_Status_Key",
                schema: "access",
                table: "access_profiles",
                columns: new[] { "OwnerScope", "Status", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_profile_assignments",
                schema: "access");

            migrationBuilder.DropTable(
                name: "access_profile_changes",
                schema: "access");

            migrationBuilder.DropTable(
                name: "access_profile_permissions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "access_profiles",
                schema: "access");
        }
    }
}
