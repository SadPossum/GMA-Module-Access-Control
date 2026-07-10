namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
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
        builder.Property(assignment => assignment.CreatedAtUtc).IsRequired();
        builder.HasIndex(assignment => new
        {
            assignment.SubjectKind,
            assignment.SubjectId,
            assignment.RoleId,
            assignment.ScopeValue
        }).IsUnique();
        builder.HasIndex(assignment => new { assignment.SubjectKind, assignment.SubjectId, assignment.ScopeValue });
        builder.HasOne<AccessPrincipal>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.SubjectKind, assignment.SubjectId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
