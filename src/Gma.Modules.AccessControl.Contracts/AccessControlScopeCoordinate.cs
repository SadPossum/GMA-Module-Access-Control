namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlScopeCoordinate(
    AccessScope RootScope,
    string TransportScopeId);
