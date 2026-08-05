namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application.Ports;
using Microsoft.Extensions.Logging;

internal sealed partial class AccessControlScopeWriteAdmission(
    IEnumerable<IAccessControlScopeWriteAdmissionReader> readers,
    ILogger<AccessControlScopeWriteAdmission> logger)
{
    public async Task<bool> AreOpenAsync(
        IReadOnlyCollection<AccessScope> accessScopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessScopes);
        if (accessScopes.Any(scope => scope is null))
        {
            throw new ArgumentException(
                "Access scopes cannot contain null values.",
                nameof(accessScopes));
        }

        AccessScope[] normalized = accessScopes
            .Where(scope => !scope.IsGlobal)
            .Distinct()
            .OrderBy(scope => scope.Value, StringComparer.Ordinal)
            .ToArray();
        if (normalized.Length == 0)
        {
            return true;
        }

        foreach (IAccessControlScopeWriteAdmissionReader reader in readers)
        {
            try
            {
                if (!await reader.AreOpenAsync(normalized, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogReaderFailure(
                    logger,
                    reader.GetType().FullName,
                    exception.GetType().Name);
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(
        EventId = 5103,
        Level = LogLevel.Warning,
        Message = "Access-control scope write admission reader {ReaderType} failed with {ExceptionType}.")]
    private static partial void LogReaderFailure(
        ILogger logger,
        string? readerType,
        string exceptionType);
}
