namespace Gma.Modules.AccessControl.Persistence;

internal sealed class AccessControlScopeClosedException()
    : InvalidOperationException(
        "The access-control scope is closed and cannot accept new access state.");
