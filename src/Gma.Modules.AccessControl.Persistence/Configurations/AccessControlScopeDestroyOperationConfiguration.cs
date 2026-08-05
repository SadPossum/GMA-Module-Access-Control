namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Framework.Messaging;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LifecycleLimits =
    Gma.Modules.AccessControl.Contracts.AccessControlScopeLifecycleLimits;

internal sealed class AccessControlScopeDestroyOperationConfiguration
    : IEntityTypeConfiguration<AccessControlScopeDestroyOperation>
{
    public void Configure(
        EntityTypeBuilder<AccessControlScopeDestroyOperation> builder)
    {
        builder.ToTable(
            "access_scope_destroy_operations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_access_scope_destroy_operations_revisions",
                    "\"ExpectedRevision\" >= 0 AND " +
                    "\"ResultingRevision\" > \"ExpectedRevision\"");
                table.HasCheckConstraint(
                    "CK_access_scope_destroy_operations_progress",
                    "\"BatchSize\" >= 1 AND \"BatchSize\" <= " +
                    LifecycleLimits.MaximumDestroyBatchSize +
                    " AND \"Stage\" >= 1 AND \"Stage\" <= 6 AND " +
                    "\"RemovedRecordCount\" >= 0 AND " +
                    "\"CompletedBatchCount\" >= 0 AND " +
                    "\"ProofVersion\" = 1 AND " +
                    "\"UpdatedAtUtc\" >= \"StartedAtUtc\"");
            });
        builder.HasKey(operation => operation.OperationId);
        builder.Property(operation => operation.OperationId)
            .ValueGeneratedNever();
        builder.Property(operation => operation.ScopeHash)
            .HasMaxLength(AccessScopeIndex.HashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.ScopeValue)
            .HasMaxLength(AccessScope.MaxLength)
            .IsRequired();
        builder.Property(operation => operation.TransportScopeId)
            .HasMaxLength(MessageScopeIds.MaxLength)
            .IsRequired();
        builder.Property(operation => operation.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(operation => operation.Stage)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(operation => operation.RemovalProofSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(operation => operation.ScopeHash).IsUnique();
        builder.HasOne<AccessControlScopeState>()
            .WithOne()
            .HasForeignKey<AccessControlScopeDestroyOperation>(
                operation => operation.ScopeHash)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
