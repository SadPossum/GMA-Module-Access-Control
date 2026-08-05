namespace Gma.Modules.AccessControl.Contracts;

public static class AccessControlScopeLifecycleLimits
{
    public const int MaximumPageSize = 200;
    public const int MaximumCursorLength = 80;
    public const int MaximumDestroyBatchSize = 1_000;
}
