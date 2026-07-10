namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessBootstrapStateConfiguration : IEntityTypeConfiguration<AccessBootstrapState>
{
    public void Configure(EntityTypeBuilder<AccessBootstrapState> builder)
    {
        builder.ToTable("bootstrap_state");
        builder.HasKey(state => state.Id);
        builder.Property(state => state.ClaimedBy).HasMaxLength(AccessSubject.IdMaxLength);
        builder.HasData(new { Id = AccessBootstrapState.SingletonId });
    }
}
