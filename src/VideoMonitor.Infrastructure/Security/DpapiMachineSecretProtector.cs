using System.Security.Cryptography;

namespace VideoMonitor.Infrastructure.Security;

public sealed class DpapiMachineSecretProtector : IMachineSecretProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return ProtectedData.Protect(
            plaintext,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        ArgumentNullException.ThrowIfNull(protectedData);
        return ProtectedData.Unprotect(
            protectedData,
            optionalEntropy: null,
            DataProtectionScope.LocalMachine);
    }
}
