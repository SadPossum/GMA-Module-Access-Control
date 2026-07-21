namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessProfileChangeConfiguration : IEntityTypeConfiguration<AccessProfileChange>
{
    public void Configure(EntityTypeBuilder<AccessProfileChange> builder)
    {
        builder.ToTable("access_profile_changes");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).ValueGeneratedNever();
        builder.Property(change => change.Kind).HasConversion<int>().IsRequired();
        builder.Property(change => change.ActorKind).IsRequired();
        builder.Property(change => change.ActorId).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Property(change => change.SubjectId).HasMaxLength(AccessSubject.IdMaxLength);
        builder.Property(change => change.AssignmentScopeValue)
            .HasColumnName("AssignmentScope")
            .HasMaxLength(AccessScope.MaxLength);
        builder.Property(change => change.ProfileVersion).IsRequired();
        builder.Property(change => change.OccurredAtUtc).IsRequired();
        builder.HasIndex(change => new { change.ProfileId, change.OccurredAtUtc, change.Id });
    }
}
