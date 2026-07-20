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

    public async Task<IReadOnlyList<AccessDecision>> DecideManyAsync(
        IReadOnlyList<AccessRequirement> requirements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        IReadOnlyList<bool> matches = await repository
            .HasPermissionsAsync(requirements, cancellationToken)
            .ConfigureAwait(false);
        if (matches.Count != requirements.Count)
        {
            throw new InvalidOperationException("The persisted access-control decision result count is invalid.");
        }

        return matches
            .Select(allowed => allowed
                ? AccessDecision.Allowed()
                : AccessDecision.Abstain(
                    AccessDecisionReasonCodes.ProviderAbstained,
                    "No persisted access-control grant matched the requirement."))
            .ToArray();
    }
}
