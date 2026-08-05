namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Framework.Messaging;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessControlScopeStateConfiguration
    : IEntityTypeConfiguration<AccessControlScopeState>
{
    public void Configure(EntityTypeBuilder<AccessControlScopeState> builder)
    {
        builder.ToTable(
            "access_scope_states",
            table =>
            {
                table.HasTrigger(
                    "access_scope_states_closed_immutable");
                table.HasCheckConstraint(
                    "CK_access_scope_states_close_revision",
                    "\"CloseRevision\" >= 0");
                table.HasCheckConstraint(
                    "CK_access_scope_states_closure",
                    "(CAST(\"IsClosed\" AS integer) = 0 AND " +
                    "\"CloseRevision\" = 0 AND " +
                    "\"CloseOperationId\" IS NULL AND " +
                    "\"CloseRequestSha256\" IS NULL AND " +
                    "\"ClosedAtUtc\" IS NULL) OR " +
                    "(CAST(\"IsClosed\" AS integer) = 1 AND " +
                    "\"CloseRevision\" >= 1 AND " +
                    "\"CloseOperationId\" IS NOT NULL AND " +
                    "\"CloseRequestSha256\" IS NOT NULL AND " +
                    "\"ClosedAtUtc\" IS NOT NULL)");
            });
        builder.HasKey(state => state.ScopeHash);
        builder.Property(state => state.ScopeHash)
            .HasMaxLength(AccessScopeIndex.HashLength)
            .IsFixedLength()
            .ValueGeneratedNever();
        builder.Property(state => state.ScopeValue)
            .HasMaxLength(AccessScope.MaxLength)
            .IsRequired();
        builder.Property(state => state.TransportScopeId)
            .HasMaxLength(MessageScopeIds.MaxLength)
            .IsRequired();
        builder.Property(state => state.CloseRevision).IsRequired();
        builder.Property(state => state.CloseRequestSha256)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.HasIndex(state => state.TransportScopeId).IsUnique();
        builder.HasIndex(state => new
        {
            state.IsClosed,
            state.ClosedAtUtc,
            state.ScopeHash
        });
    }
}
