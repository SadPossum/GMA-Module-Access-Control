namespace Gma.Modules.AccessControl.Application.Queries;

using Gma.Framework.Cqrs;

public sealed record ListAllowedAccessProfilePermissionsQuery : IQuery<IReadOnlyList<string>>;
