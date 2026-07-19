namespace Gma.Modules.AccessControl.Domain.Enums;

public static class AccessProfileStatusNames
{
    public const string Active = "active";
    public const string Archived = "archived";

    public static string GetName(AccessProfileStatus status) => status switch
    {
        AccessProfileStatus.Active => Active,
        AccessProfileStatus.Archived => Archived,
        _ => throw new ArgumentException("Access-profile status must be defined and non-unknown.", nameof(status))
    };
}
