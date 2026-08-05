using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gma.Modules.AccessControl.Persistence.PostgreSqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessControlScopeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_scope_states",
                schema: "access",
                columns: table => new
                {
                    ScopeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ScopeValue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TransportScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    CloseRevision = table.Column<long>(type: "bigint", nullable: false),
                    CloseOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseRequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_scope_states", x => x.ScopeHash);
                    table.CheckConstraint("CK_access_scope_states_close_revision", "\"CloseRevision\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "access_scope_destroy_operations",
                schema: "access",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ScopeValue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TransportScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "integer", nullable: false),
                    ProofVersion = table.Column<int>(type: "integer", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_scope_destroy_operations", x => x.OperationId);
                    table.CheckConstraint("CK_access_scope_destroy_operations_progress", "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");
                    table.CheckConstraint("CK_access_scope_destroy_operations_revisions", "\"ExpectedRevision\" >= 0 AND \"ResultingRevision\" > \"ExpectedRevision\"");
                    table.ForeignKey(
                        name: "FK_access_scope_destroy_operations_access_scope_states_ScopeHa~",
                        column: x => x.ScopeHash,
                        principalSchema: "access",
                        principalTable: "access_scope_states",
                        principalColumn: "ScopeHash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_scope_destroy_receipts",
                schema: "access",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ScopeValue = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    TransportScopeId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ExpectedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ResultingRevision = table.Column<long>(type: "bigint", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    RemovedRecordCount = table.Column<long>(type: "bigint", nullable: false),
                    CompletedBatchCount = table.Column<int>(type: "integer", nullable: false),
                    RemovalProofVersion = table.Column<int>(type: "integer", nullable: false),
                    RemovalProofSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_scope_destroy_receipts", x => x.OperationId);
                    table.CheckConstraint("CK_access_scope_destroy_receipts_progress", "\"RemovedRecordCount\" >= 0 AND \"CompletedBatchCount\" >= 0");
                    table.CheckConstraint("CK_access_scope_destroy_receipts_revisions", "\"ExpectedRevision\" >= 0 AND \"ResultingRevision\" > \"ExpectedRevision\"");
                    table.ForeignKey(
                        name: "FK_access_scope_destroy_receipts_access_scope_states_ScopeHash",
                        column: x => x.ScopeHash,
                        principalSchema: "access",
                        principalTable: "access_scope_states",
                        principalColumn: "ScopeHash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "access_scope_destroy_principal_candidates",
                schema: "access",
                columns: table => new
                {
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<int>(type: "integer", nullable: false),
                    SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_scope_destroy_principal_candidates", x => new { x.OperationId, x.SubjectKind, x.SubjectId });
                    table.ForeignKey(
                        name: "FK_access_scope_destroy_principal_candidates_access_scope_dest~",
                        column: x => x.OperationId,
                        principalSchema: "access",
                        principalTable: "access_scope_destroy_operations",
                        principalColumn: "OperationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_scope_destroy_operations_ScopeHash",
                schema: "access",
                table: "access_scope_destroy_operations",
                column: "ScopeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_scope_destroy_receipts_ScopeHash",
                schema: "access",
                table: "access_scope_destroy_receipts",
                column: "ScopeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_access_scope_states_IsClosed_ClosedAtUtc_ScopeHash",
                schema: "access",
                table: "access_scope_states",
                columns: new[] { "IsClosed", "ClosedAtUtc", "ScopeHash" });

            migrationBuilder.CreateIndex(
                name: "IX_access_scope_states_TransportScopeId",
                schema: "access",
                table: "access_scope_states",
                column: "TransportScopeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_scope_destroy_principal_candidates",
                schema: "access");

            migrationBuilder.DropTable(
                name: "access_scope_destroy_receipts",
                schema: "access");

            migrationBuilder.DropTable(
                name: "access_scope_destroy_operations",
                schema: "access");

            migrationBuilder.DropTable(
                name: "access_scope_states",
                schema: "access");
        }
    }
}
