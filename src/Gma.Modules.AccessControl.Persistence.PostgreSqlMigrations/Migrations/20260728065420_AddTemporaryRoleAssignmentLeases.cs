using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryRoleAssignmentLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_subject_role_assignments_RoleId",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropIndex(
                name: "IX_subject_role_assignments_SubjectKind_SubjectId_RoleId_Scope",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropIndex(
                name: "IX_subject_role_assignments_SubjectKind_SubjectId_Scope",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.AddColumn<long>(
                name: "ExpiresAtUnixMilliseconds",
                schema: "access",
                table: "subject_role_assignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevokedAtUnixMilliseconds",
                schema: "access",
                table: "subject_role_assignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeHash",
                schema: "access",
                table: "subject_role_assignments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE access.subject_role_assignments
                SET "ScopeHash" = upper(encode(sha256(convert_to("Scope", 'UTF8')), 'hex'));
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeHash",
                schema: "access",
                table: "subject_role_assignments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_expiry_revocation",
                schema: "access",
                table: "subject_role_assignments",
                columns: new[] { "ExpiresAtUnixMilliseconds", "RevokedAtUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_role_lifecycle",
                schema: "access",
                table: "subject_role_assignments",
                columns: new[] { "RoleId", "RevokedAtUnixMilliseconds", "ExpiresAtUnixMilliseconds" });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_subject_role_scope_hash",
                schema: "access",
                table: "subject_role_assignments",
                columns: new[] { "SubjectKind", "SubjectId", "RoleId", "ScopeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_role_assignments_subject_scope_hash_lifecycle",
                schema: "access",
                table: "subject_role_assignments",
                columns: new[] { "SubjectKind", "SubjectId", "ScopeHash", "RevokedAtUnixMilliseconds", "ExpiresAtUnixMilliseconds" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_role_assignments_expiry_revocation",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_role_lifecycle",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_subject_role_scope_hash",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropIndex(
                name: "IX_role_assignments_subject_scope_hash_lifecycle",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.Sql(
                """
                WITH ranked_assignments AS (
                    SELECT
                        "Id",
                        ROW_NUMBER() OVER (
                            PARTITION BY "SubjectKind", "SubjectId", "RoleId", "Scope"
                            ORDER BY
                                CASE
                                    WHEN "RevokedAtUnixMilliseconds" IS NULL
                                         AND (
                                             "ExpiresAtUnixMilliseconds" IS NULL
                                             OR "ExpiresAtUnixMilliseconds" >
                                                (EXTRACT(EPOCH FROM NOW()) * 1000)::bigint
                                         )
                                    THEN 0
                                    ELSE 1
                                END,
                                "CreatedAtUtc" DESC,
                                "Id" DESC
                        ) AS row_number
                    FROM access.subject_role_assignments
                )
                DELETE FROM access.subject_role_assignments AS assignment
                USING ranked_assignments
                WHERE assignment."Id" = ranked_assignments."Id"
                  AND ranked_assignments.row_number > 1;
                """);

            migrationBuilder.DropColumn(
                name: "ExpiresAtUnixMilliseconds",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropColumn(
                name: "RevokedAtUnixMilliseconds",
                schema: "access",
                table: "subject_role_assignments");

            migrationBuilder.DropColumn(
                name: "ScopeHash",
                schema: "access",
                table: "subject_role_assignments");

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
    }
}
