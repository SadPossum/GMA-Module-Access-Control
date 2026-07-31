namespace Gma.Modules.AccessControl.Contracts;

public interface IAccessProfileMutationAdmissionPolicy
{
    ValueTask<AccessProfileMutationAdmissionDecision> EvaluateAsync(
        AccessProfileMutationAdmissionContext context,
        CancellationToken cancellationToken = default);
}
