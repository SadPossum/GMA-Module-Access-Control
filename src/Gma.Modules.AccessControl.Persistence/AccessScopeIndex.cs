namespace Gma.Modules.AccessControl.Persistence;

using System.Security.Cryptography;
using System.Text;

internal static class AccessScopeIndex
{
    public const int HashLength = 64;

    public static string Create(string canonicalScope) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalScope)));
}
