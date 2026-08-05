using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.Migrations
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
                CREATE FUNCTION
                    access.reject_access_scope_destroy_receipt_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'access-control scope destruction receipts are append-only';
                END;
                $$;

                CREATE TRIGGER access_scope_destroy_receipts_append_only
                BEFORE UPDATE OR DELETE
                ON access.access_scope_destroy_receipts
                FOR EACH ROW
                EXECUTE FUNCTION
                    access.reject_access_scope_destroy_receipt_mutation();

                CREATE FUNCTION access.reject_closed_access_scope_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF OLD."IsClosed" THEN
                        RAISE EXCEPTION
                            'closed access-control scope state is immutable';
                    END IF;

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER access_scope_states_closed_immutable
                BEFORE UPDATE OR DELETE
                ON access.access_scope_states
                FOR EACH ROW
                EXECUTE FUNCTION
                    access.reject_closed_access_scope_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS access_scope_destroy_receipts_append_only
                ON access.access_scope_destroy_receipts;

                DROP FUNCTION IF EXISTS
                    access.reject_access_scope_destroy_receipt_mutation();

                DROP TRIGGER IF EXISTS access_scope_states_closed_immutable
                ON access.access_scope_states;

                DROP FUNCTION IF EXISTS
                    access.reject_closed_access_scope_mutation();
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
