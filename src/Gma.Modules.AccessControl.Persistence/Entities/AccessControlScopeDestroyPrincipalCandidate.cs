namespace Gma.Modules.AccessControl.Persistence.Entities;

using Gma.Framework.AccessControl;

internal sealed class AccessControlScopeDestroyPrincipalCandidate
{
    private AccessControlScopeDestroyPrincipalCandidate() { }

    public AccessControlScopeDestroyPrincipalCandidate(
        Guid operationId,
        AccessSubjectKind subjectKind,
        string subjectId)
    {
        if (operationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A destruction operation id is required.",
                nameof(operationId));
        }

        AccessSubject subject = new(subjectKind, subjectId);
        this.OperationId = operationId;
        this.SubjectKind = (int)subject.Kind;
        this.SubjectId = subject.Id;
    }

    public Guid OperationId { get; private set; }
    public int SubjectKind { get; private set; }
    public string SubjectId { get; private set; } = string.Empty;
}
