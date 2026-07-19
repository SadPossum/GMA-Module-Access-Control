namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record GetAccessProfileQuery(Guid ProfileId, AccessScope OwnerScope)
    : IQuery<AccessProfileDetails>;
