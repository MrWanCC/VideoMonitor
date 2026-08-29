namespace VideoMonitor.Infrastructure.Security;

public sealed class UnsupportedMachineSecretProtector : IMachineSecretProtector
{
    private static PlatformNotSupportedException CreateException() =>
        new("当前平台尚未配置 VideoMonitor Server 的机器级密钥保护实现。");

    public byte[] Protect(byte[] plaintext) => throw CreateException();

    public byte[] Unprotect(byte[] protectedData) => throw CreateException();
}
