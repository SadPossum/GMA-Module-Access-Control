namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application.Ports;

internal sealed class PersistedAccessControlDecisionProvider(IAccessControlRbacRepository repository)
    : IAccessDecisionProvider
{
    public async Task<AccessDecision> DecideAsync(
        AccessRequirement requirement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        bool allowed = await repository
            .HasPermissionAsync(requirement.Subject, requirement.Permission, requirement.Scope, cancellationToken)
            .ConfigureAwait(false);

        return allowed
            ? AccessDecision.Allowed()
            : AccessDecision.Abstain(
                AccessDecisionReasonCodes.ProviderAbstained,
                "No persisted access-control grant matched the requirement.");
    }
}
