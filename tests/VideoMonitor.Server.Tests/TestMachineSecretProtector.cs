using System.Security.Cryptography;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Server.Tests;

internal sealed class TestMachineSecretProtector : IMachineSecretProtector
{
    public byte[] Protect(byte[] plaintext) =>
        plaintext.Select(value => (byte)(value ^ 0x5A)).ToArray();

    public byte[] Unprotect(byte[] protectedData) =>
        protectedData.Select(value => (byte)(value ^ 0x5A)).ToArray();
}

internal sealed class FailingMachineSecretProtector : IMachineSecretProtector
{
    public byte[] Protect(byte[] plaintext) =>
        throw new CryptographicException("machine-protection-test-failure");

    public byte[] Unprotect(byte[] protectedData) =>
        throw new CryptographicException("machine-protection-test-failure");
}
