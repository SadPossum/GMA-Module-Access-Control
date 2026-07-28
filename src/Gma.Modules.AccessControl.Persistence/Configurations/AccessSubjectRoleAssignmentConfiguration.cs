namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessSubjectRoleAssignmentConfiguration : IEntityTypeConfiguration<AccessSubjectRoleAssignment>
{
    public void Configure(EntityTypeBuilder<AccessSubjectRoleAssignment> builder)
    {
        builder.ToTable("subject_role_assignments");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.SubjectKind).IsRequired();
        builder.Property(assignment => assignment.SubjectId).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Ignore(assignment => assignment.Scope);
        builder.Property(assignment => assignment.ScopeValue)
            .HasColumnName("Scope")
            .HasMaxLength(AccessScope.MaxLength)
            .IsRequired();
        builder.Property(assignment => assignment.ScopeHash)
            .HasMaxLength(AccessScopeIndex.HashLength)
            .IsRequired();
        builder.Property(assignment => assignment.CreatedAtUtc).IsRequired();
        builder.Ignore(assignment => assignment.ExpiresAtUtc);
        builder.Ignore(assignment => assignment.RevokedAtUtc);
        builder.Property(assignment => assignment.ExpiresAtUnixMilliseconds);
        builder.Property(assignment => assignment.RevokedAtUnixMilliseconds);
        builder.HasIndex(assignment => new
        {
            assignment.SubjectKind,
            assignment.SubjectId,
            assignment.RoleId,
            assignment.ScopeHash
        }).HasDatabaseName("IX_role_assignments_subject_role_scope_hash");
        builder.HasIndex(assignment => new
        {
            assignment.SubjectKind,
            assignment.SubjectId,
            assignment.ScopeHash,
            assignment.RevokedAtUnixMilliseconds,
            assignment.ExpiresAtUnixMilliseconds
        }).HasDatabaseName("IX_role_assignments_subject_scope_hash_lifecycle");
        builder.HasIndex(assignment => new
        {
            assignment.ExpiresAtUnixMilliseconds,
            assignment.RevokedAtUnixMilliseconds
        }).HasDatabaseName("IX_role_assignments_expiry_revocation");
        builder.HasIndex(assignment => new
        {
            assignment.RoleId,
            assignment.RevokedAtUnixMilliseconds,
            assignment.ExpiresAtUnixMilliseconds
        }).HasDatabaseName("IX_role_assignments_role_lifecycle");
        builder.HasOne<AccessPrincipal>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.SubjectKind, assignment.SubjectId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
