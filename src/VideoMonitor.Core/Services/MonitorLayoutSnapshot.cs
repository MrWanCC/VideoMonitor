using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public sealed record MonitorLayoutSnapshot(
    IReadOnlyList<CameraInfo?> MainSlots,
    IReadOnlyList<CameraInfo?> SecondarySlots);
