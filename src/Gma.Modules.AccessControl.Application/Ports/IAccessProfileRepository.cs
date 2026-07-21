namespace Gma.Modules.AccessControl.Application.Ports;

using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;

internal interface IAccessProfileRepository
{
    Task<bool> KeyExistsAsync(AccessScope ownerScope, string key, CancellationToken cancellationToken);
    Task<AccessProfile?> GetAsync(Guid profileId, AccessScope ownerScope, bool tracking, CancellationToken cancellationToken);
    Task<AccessProfileDetails?> GetDetailsAsync(Guid profileId, AccessScope ownerScope, CancellationToken cancellationToken);
    Task<AccessProfileDetails?> GetDetailsByKeyAsync(AccessScope ownerScope, string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessProfileDetails>> ListDetailsForSubjectAsync(AccessSubject subject, AccessScope ownerScope, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScopedAccessProfileAssignmentDetails>> ListScopedDetailsForSubjectAsync(AccessSubject subject, AccessScope ownerScope, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessProfile>> ListTrackedAsync(IReadOnlyCollection<Guid> profileIds, AccessScope ownerScope, CancellationToken cancellationToken);
    Task<int> CountAssignmentsAsync(Guid profileId, CancellationToken cancellationToken);
    void Add(AccessProfile profile);
    Task<bool> AssignmentExistsAsync(Guid profileId, AccessSubject subject, AccessScope assignmentScope, CancellationToken cancellationToken);
    Task<AccessProfileAssignment?> GetAssignmentAsync(Guid profileId, AccessSubject subject, AccessScope assignmentScope, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccessProfileAssignment>> ListTrackedAssignmentsAsync(AccessSubject subject, AccessScope ownerScope, AccessScope? assignmentScope, CancellationToken cancellationToken);
    void AddAssignment(AccessProfileAssignment assignment);
    void RemoveAssignment(AccessProfileAssignment assignment);
    Task<AccessControlPage<AccessProfileDetails>> ListAsync(AccessScope ownerScope, bool includeArchived, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<AccessControlPage<AccessProfileAssignmentDetails>> ListAssignmentsAsync(Guid profileId, AccessScope ownerScope, PageRequest pageRequest, CancellationToken cancellationToken);
    Task<AccessControlPage<AccessProfileChangeDetails>> ListChangesAsync(Guid profileId, AccessScope ownerScope, PageRequest pageRequest, CancellationToken cancellationToken);
}
