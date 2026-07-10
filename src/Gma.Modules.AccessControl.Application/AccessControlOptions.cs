namespace Gma.Modules.AccessControl.Application;

public sealed class AccessControlOptions
{
    public const string SectionName = "AccessControl";

    public BootstrapOptions Bootstrap { get; set; } = new();

    public sealed class BootstrapOptions
    {
        public bool AllowWhenAssignmentsExist { get; set; }
        public string OwnerRoleName { get; set; } = "owner";
    }
}
