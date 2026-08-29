namespace VideoMonitor.Infrastructure.Security;

public interface ISecretProtector
{
    Task<string> ProtectAsync(
        string plaintext,
        string purpose,
        CancellationToken cancellationToken = default);

    Task<string> UnprotectAsync(
        string protectedValue,
        string purpose,
        CancellationToken cancellationToken = default);
}
