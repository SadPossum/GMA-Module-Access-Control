namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessProfileAssignmentConfiguration : IEntityTypeConfiguration<AccessProfileAssignment>
{
    public void Configure(EntityTypeBuilder<AccessProfileAssignment> builder)
    {
        builder.ToTable("access_profile_assignments");
        builder.HasKey(assignment => assignment.Id);
        builder.Property(assignment => assignment.Id).ValueGeneratedNever();
        builder.Property(assignment => assignment.SubjectKind).IsRequired();
        builder.Property(assignment => assignment.SubjectId).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Property(assignment => assignment.CreatedByKind).IsRequired();
        builder.Property(assignment => assignment.CreatedById).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Property(assignment => assignment.CreatedAtUtc).IsRequired();
        builder.HasIndex(assignment => new { assignment.ProfileId, assignment.SubjectKind, assignment.SubjectId }).IsUnique();
        builder.HasIndex(assignment => new { assignment.SubjectKind, assignment.SubjectId, assignment.ProfileId });
        builder.HasOne(assignment => assignment.Profile)
            .WithMany()
            .HasForeignKey(assignment => assignment.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AccessPrincipal>()
            .WithMany()
            .HasForeignKey(assignment => new { assignment.SubjectKind, assignment.SubjectId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
