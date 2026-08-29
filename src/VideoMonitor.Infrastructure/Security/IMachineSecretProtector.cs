namespace VideoMonitor.Infrastructure.Security;

public interface IMachineSecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] protectedData);
}
