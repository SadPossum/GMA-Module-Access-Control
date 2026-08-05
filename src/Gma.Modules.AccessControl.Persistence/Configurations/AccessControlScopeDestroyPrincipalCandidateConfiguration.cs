namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessControlScopeDestroyPrincipalCandidateConfiguration
    : IEntityTypeConfiguration<AccessControlScopeDestroyPrincipalCandidate>
{
    public void Configure(
        EntityTypeBuilder<AccessControlScopeDestroyPrincipalCandidate> builder)
    {
        builder.ToTable("access_scope_destroy_principal_candidates");
        builder.HasKey(candidate => new
        {
            candidate.OperationId,
            candidate.SubjectKind,
            candidate.SubjectId
        });
        builder.Property(candidate => candidate.SubjectId)
            .HasMaxLength(AccessSubject.IdMaxLength)
            .IsRequired();
        builder.HasOne<AccessControlScopeDestroyOperation>()
            .WithMany()
            .HasForeignKey(candidate => candidate.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
