using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public interface IMediaRuntimeStore
{
    MediaRuntimeSnapshot GetSnapshot();
}
