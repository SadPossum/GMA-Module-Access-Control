using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedProfileAssignmentGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_access_profile_assignments_ProfileId_SubjectKind_SubjectId",
                schema: "access",
                table: "access_profile_assignments");

            migrationBuilder.DropIndex(
                name: "IX_access_profile_assignments_SubjectKind_SubjectId_ProfileId",
                schema: "access",
                table: "access_profile_assignments");

            migrationBuilder.AddColumn<string>(
                name: "AssignmentScope",
                schema: "access",
                table: "access_profile_changes",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignmentScope",
                schema: "access",
                table: "access_profile_assignments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE access.access_profile_assignments AS assignment
                SET "AssignmentScope" = profile."OwnerScope"
                FROM access.access_profiles AS profile
                WHERE assignment."ProfileId" = profile."Id";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "AssignmentScope",
                schema: "access",
                table: "access_profile_assignments",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(1024)",
                oldMaxLength: 1024,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_profile_assignments_ProfileId_SubjectKind_SubjectId_~",
                schema: "access",
                table: "access_profile_assignments",
                columns: new[] { "ProfileId", "SubjectKind", "SubjectId", "AssignmentScope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_profile_assignments_SubjectKind_SubjectId_Assignment~",
                schema: "access",
                table: "access_profile_assignments",
                columns: new[] { "SubjectKind", "SubjectId", "AssignmentScope", "ProfileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_access_profile_assignments_ProfileId_SubjectKind_SubjectId_~",
                schema: "access",
                table: "access_profile_assignments");

            migrationBuilder.DropIndex(
                name: "IX_access_profile_assignments_SubjectKind_SubjectId_Assignment~",
                schema: "access",
                table: "access_profile_assignments");

            migrationBuilder.Sql(
                """
                DELETE FROM access.access_profile_assignments AS assignment
                USING (
                    SELECT ranked."Id"
                    FROM (
                        SELECT
                            candidate."Id",
                            ROW_NUMBER() OVER (
                                PARTITION BY candidate."ProfileId", candidate."SubjectKind", candidate."SubjectId"
                                ORDER BY
                                    CASE WHEN candidate."AssignmentScope" = profile."OwnerScope" THEN 0 ELSE 1 END,
                                    candidate."CreatedAtUtc",
                                    candidate."Id") AS row_number
                        FROM access.access_profile_assignments AS candidate
                        INNER JOIN access.access_profiles AS profile ON profile."Id" = candidate."ProfileId"
                    ) AS ranked
                    WHERE ranked.row_number > 1
                ) AS stale
                WHERE assignment."Id" = stale."Id";
                """);

            migrationBuilder.DropColumn(
                name: "AssignmentScope",
                schema: "access",
                table: "access_profile_changes");

            migrationBuilder.DropColumn(
                name: "AssignmentScope",
                schema: "access",
                table: "access_profile_assignments");

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
        }
    }
}
