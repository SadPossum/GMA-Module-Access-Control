namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeDestroyResult(
    AccessControlScopeDestroyStatus Status,
    AccessControlScopeDestroyProgress? Progress,
    AccessControlScopeDestroyReceipt? Receipt);
