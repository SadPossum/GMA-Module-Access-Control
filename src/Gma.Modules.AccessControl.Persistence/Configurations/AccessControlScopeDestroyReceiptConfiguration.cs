namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Framework.Messaging;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LifecycleLimits =
    Gma.Modules.AccessControl.Contracts.AccessControlScopeLifecycleLimits;

internal sealed class AccessControlScopeDestroyReceiptConfiguration
    : IEntityTypeConfiguration<AccessControlScopeDestroyReceipt>
{
    public void Configure(
        EntityTypeBuilder<AccessControlScopeDestroyReceipt> builder)
    {
        builder.ToTable(
            "access_scope_destroy_receipts",
            table =>
            {
                table.HasTrigger(
                    "access_scope_destroy_receipts_append_only");
                table.HasCheckConstraint(
                    "CK_access_scope_destroy_receipts_revisions",
                    "\"ExpectedRevision\" >= 0 AND " +
                    "\"ResultingRevision\" > \"ExpectedRevision\"");
                table.HasCheckConstraint(
                    "CK_access_scope_destroy_receipts_progress",
                    "\"BatchSize\" >= 1 AND \"BatchSize\" <= " +
                    LifecycleLimits.MaximumDestroyBatchSize +
                    " AND ((\"RemovedRecordCount\" = 0 AND " +
                    "\"CompletedBatchCount\" = 0) OR " +
                    "(\"RemovedRecordCount\" > 0 AND " +
                    "\"CompletedBatchCount\" > 0)) AND " +
                    "\"RemovalProofVersion\" = 1 AND " +
                    "\"CompletedAtUtc\" >= \"StartedAtUtc\"");
            });
        builder.HasKey(receipt => receipt.OperationId);
        builder.Property(receipt => receipt.OperationId)
            .ValueGeneratedNever();
        builder.Property(receipt => receipt.ScopeHash)
            .HasMaxLength(AccessScopeIndex.HashLength)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.ScopeValue)
            .HasMaxLength(AccessScope.MaxLength)
            .IsRequired();
        builder.Property(receipt => receipt.TransportScopeId)
            .HasMaxLength(MessageScopeIds.MaxLength)
            .IsRequired();
        builder.Property(receipt => receipt.RequestSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(receipt => receipt.RemovalProofSha256)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(receipt => receipt.ScopeHash).IsUnique();
        builder.HasOne<AccessControlScopeState>()
            .WithOne()
            .HasForeignKey<AccessControlScopeDestroyReceipt>(
                receipt => receipt.ScopeHash)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
