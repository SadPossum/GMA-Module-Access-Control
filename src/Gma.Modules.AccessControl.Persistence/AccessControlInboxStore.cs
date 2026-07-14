namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.Messaging.Infrastructure;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;

internal sealed class AccessControlInboxStore(
    AccessControlDbContext dbContext,
    ISystemClock clock,
    IIdGenerator idGenerator)
    : EfInboxStore<AccessControlDbContext>(
        dbContext,
        clock,
        idGenerator,
        AccessControlMigrations.Schema);
