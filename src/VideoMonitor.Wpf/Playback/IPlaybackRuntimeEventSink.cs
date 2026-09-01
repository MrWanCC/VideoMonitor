namespace VideoMonitor.Wpf.Playback;

public interface IPlaybackRuntimeEventSink
{
    void Publish(PlaybackRuntimeEvent runtimeEvent);
}
