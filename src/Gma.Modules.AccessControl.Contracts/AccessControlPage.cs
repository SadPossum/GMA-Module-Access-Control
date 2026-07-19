namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    bool HasMore);
