using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.SqlServerMigrations.Migrations
{
    /// <inheritdoc />
    public partial class HardenAccessControlScopeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_access_scope_destroy_receipts_progress",
                schema: "access",
                table: "access_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_access_scope_destroy_operations_progress",
                schema: "access",
                table: "access_scope_destroy_operations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_scope_states_closure",
                schema: "access",
                table: "access_scope_states",
                sql: "(CAST(\"IsClosed\" AS integer) = 0 AND \"CloseRevision\" = 0 AND \"CloseOperationId\" IS NULL AND \"CloseRequestSha256\" IS NULL AND \"ClosedAtUtc\" IS NULL) OR (CAST(\"IsClosed\" AS integer) = 1 AND \"CloseRevision\" >= 1 AND \"CloseOperationId\" IS NOT NULL AND \"CloseRequestSha256\" IS NOT NULL AND \"ClosedAtUtc\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_scope_destroy_receipts_progress",
                schema: "access",
                table: "access_scope_destroy_receipts",
                sql: "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000 AND ((\"RemovedRecordCount\" = 0 AND \"CompletedBatchCount\" = 0) OR (\"RemovedRecordCount\" > 0 AND \"CompletedBatchCount\" > 0)) AND \"RemovalProofVersion\" = 1 AND \"CompletedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_scope_destroy_operations_progress",
                schema: "access",
                table: "access_scope_destroy_operations",
                sql: "\"BatchSize\" >= 1 AND \"BatchSize\" <= 1000 AND \"Stage\" >= 1 AND \"Stage\" <= 6 AND \"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0 AND \"ProofVersion\" = 1 AND \"UpdatedAtUtc\" >= \"StartedAtUtc\"");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [access].[access_scope_destroy_receipts_append_only]
                ON [access].[access_scope_destroy_receipts]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000,
                        'access-control scope destruction receipts are append-only',
                        1;
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER
                    [access].[access_scope_states_closed_immutable]
                ON [access].[access_scope_states]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    IF EXISTS (
                        SELECT 1
                        FROM deleted
                        WHERE [IsClosed] = 1)
                    BEGIN
                        THROW 51000,
                            'closed access-control scope state is immutable',
                            1;
                    END;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [access].[access_scope_destroy_receipts_append_only];
                """);

            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS
                    [access].[access_scope_states_closed_immutable];
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_access_scope_states_closure",
                schema: "access",
                table: "access_scope_states");

            migrationBuilder.DropCheckConstraint(
                name: "CK_access_scope_destroy_receipts_progress",
                schema: "access",
                table: "access_scope_destroy_receipts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_access_scope_destroy_operations_progress",
                schema: "access",
                table: "access_scope_destroy_operations");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_scope_destroy_receipts_progress",
                schema: "access",
                table: "access_scope_destroy_receipts",
                sql: "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_access_scope_destroy_operations_progress",
                schema: "access",
                table: "access_scope_destroy_operations",
                sql: "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");
        }
    }
}
