using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Services;

public sealed record MonitorLayoutSnapshot(
    IReadOnlyList<CameraInfo> MainSlots,
    IReadOnlyList<CameraInfo> SecondarySlots);
