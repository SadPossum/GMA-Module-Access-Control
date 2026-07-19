namespace Gma.Modules.AccessControl.Domain.ValueObjects;

using Gma.Modules.AccessControl.Domain.Enums;

public sealed record AccessProfileSubject
{
    public const int IdMaxLength = 256;

    public AccessProfileSubject(AccessProfileSubjectKind kind, string id)
    {
        if (kind == AccessProfileSubjectKind.Unknown || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("An access-profile subject kind is required.", nameof(kind));
        }

        string candidate = id?.Trim() ?? string.Empty;
        if (candidate.Length is 0 or > IdMaxLength ||
            candidate.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException(
                $"An access-profile subject id must be {IdMaxLength} characters or fewer and contain no whitespace or control characters.",
                nameof(id));
        }

        this.Kind = kind;
        this.Id = candidate;
    }

    public AccessProfileSubjectKind Kind { get; }
    public string Id { get; }
}
