using System.Threading;
using System.Threading.Tasks;

namespace VideoMonitor.Wpf.Configuration;

public interface IClientSettingsStore
{
    ClientSettings Load();

    Task SaveAsync(
        ClientSettings settings,
        CancellationToken cancellationToken = default);
}
