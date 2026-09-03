using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LibVLCSharp.Shared;

namespace VideoMonitor.Wpf.Playback;

public interface IPlaybackDiagnosticsWriter : IDisposable, IAsyncDisposable
{
    bool TryWrite(string line);
}

public readonly record struct PlaybackDiagnosticsCounters(
    float? Fps,
    float? InputBitrate,
    float? DemuxBitrate,
    int? Decoded,
    int? Displayed,
    int? Lost,
    int? Corrupted,
    int? Discontinuity);

public readonly record struct PlaybackDiagnosticsDelta(
    int? DecodedDelta,
    int? DisplayedDelta,
    int? LostDelta,
    int? CorruptedDelta,
    int? DiscontinuityDelta);

public static class PlaybackDiagnosticsDeltaCalculator
{
    public static PlaybackDiagnosticsDelta Calculate(
        PlaybackDiagnosticsCounters? previous,
        PlaybackDiagnosticsCounters current) =>
        new(
            Subtract(previous?.Decoded, current.Decoded),
            Subtract(previous?.Displayed, current.Displayed),
            Subtract(previous?.Lost, current.Lost),
            Subtract(previous?.Corrupted, current.Corrupted),
            Subtract(previous?.Discontinuity, current.Discontinuity));

    private static int? Subtract(int? previous, int? current) =>
        previous.HasValue && current.HasValue
            ? Math.Max(0, current.Value - previous.Value)
            : null;
}

public static class PlaybackDiagnosticsSanitizer
{
    private static readonly Regex UriPattern = new(
        @"(?i)\b(?:rtsp|https?)://[^\s""'<>]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Sanitize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return UriPattern.Replace(value, "[REDACTED_URL]");
    }
}

public static class PlaybackDiagnosticsNativeLogFilter
{
    private static readonly string[] PerformanceKeywords =
    [
        "avcodec",
        "decoder",
        "decode",
        "d3d11va",
        "dxva2",
        "hardware",
        "buffer",
        "buffering",
        "late",
        "drop",
        "dropped",
        "clock",
        "pts",
        "timestamp",
        "discontinuity"
    ];

    public static bool IsRelevant(string? message) =>
        message is not null
        && PerformanceKeywords.Any(keyword =>
            message.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}

public static class PlaybackDiagnosticsFormatter
{
    public static string FormatHeader(string libVlcVersion)
    {
        var timestampUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var timestampLocal = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture);
        var architecture = RuntimeInformation.ProcessArchitecture;
        var os = PlaybackDiagnosticsSanitizer.Sanitize(RuntimeInformation.OSDescription);
        var dotnet = Environment.Version;
        var safeVersion = PlaybackDiagnosticsSanitizer.Sanitize(libVlcVersion);

        return string.Join(
            Environment.NewLine,
            "PLAYBACK_DIAGNOSTICS",
            $"diagnosticsVersion=1 timestampUtc={timestampUtc} timestampLocal={timestampLocal}",
            $"processArchitecture={architecture} osVersion={os} dotnetVersion={dotnet}",
            $"libvlcVersion={safeVersion} samplingIntervalMs=1000",
            "options=--no-video-title-show,--stats");
    }

    public static string FormatEvent(
        Guid channelId,
        string streamId,
        string state,
        float? bufferingPercent = null)
    {
        var line =
            $"EVENT channel={channelId:N} stream={SafeToken(streamId)} state={SafeToken(state)}";
        return bufferingPercent is { } percent
            ? $"{line} percent={percent.ToString("0.0", CultureInfo.InvariantCulture)}"
            : line;
    }

    public static string FormatSample(
        Guid channelId,
        string streamId,
        PlaybackDiagnosticsCounters current,
        PlaybackDiagnosticsDelta delta) =>
        string.Join(
            " ",
            "SAMPLE",
            $"channel={channelId:N}",
            $"stream={SafeToken(streamId)}",
            $"fps={FormatNumber(current.Fps)}",
            $"inputBitrate={FormatNumber(current.InputBitrate)}",
            $"demuxBitrate={FormatNumber(current.DemuxBitrate)}",
            $"decoded={FormatNumber(current.Decoded)}",
            $"decodedDelta={FormatNumber(delta.DecodedDelta)}",
            $"displayed={FormatNumber(current.Displayed)}",
            $"displayedDelta={FormatNumber(delta.DisplayedDelta)}",
            $"lost={FormatNumber(current.Lost)}",
            $"lostDelta={FormatNumber(delta.LostDelta)}",
            $"demuxCorrupted={FormatNumber(current.Corrupted)}",
            $"corruptedDelta={FormatNumber(delta.CorruptedDelta)}",
            $"demuxDiscontinuity={FormatNumber(current.Discontinuity)}",
            $"discontinuityDelta={FormatNumber(delta.DiscontinuityDelta)}");

    public static string FormatNativeLog(LogLevel level, string? module, string message) =>
        $"NATIVE_LOG level={level} module={SafeToken(PlaybackDiagnosticsSanitizer.Sanitize(module ?? "unknown"))} message={PlaybackDiagnosticsSanitizer.Sanitize(message)}";

    private static string FormatNumber(float? value) =>
        value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "unavailable";

    private static string FormatNumber(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unavailable";

    private static string SafeToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var safe = new string(value
            .Take(128)
            .Select(character =>
                char.IsLetterOrDigit(character)
                    || character is '-' or '_' or '.'
                    ? character
                    : '_')
            .ToArray());
        return string.IsNullOrEmpty(safe) ? "unknown" : safe;
    }
}

public sealed class PlaybackDiagnosticsWriter : IPlaybackDiagnosticsWriter
{
    private const int QueueCapacity = 4096;

    private readonly Channel<string> lines = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    private readonly StreamWriter writer;
    private readonly Task writeTask;
    private readonly object disposeGate = new();
    private int disposed;
    private int writerFailed;
    private Task? disposeTask;

    public PlaybackDiagnosticsWriter(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        writer = new StreamWriter(
            new FileStream(
                fullPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan));
        FilePath = fullPath;
        writeTask = WriteLoopAsync();
    }

    internal PlaybackDiagnosticsWriter(Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(outputStream);
        writer = new StreamWriter(outputStream)
        {
            AutoFlush = true
        };
        FilePath = string.Empty;
        writeTask = WriteLoopAsync();
    }

    public string FilePath { get; }

    public static PlaybackDiagnosticsWriter? TryCreateDefault(string libVlcVersion)
    {
        try
        {
            var writer = new PlaybackDiagnosticsWriter(GetDefaultLogPath());
            foreach (var line in PlaybackDiagnosticsFormatter.FormatHeader(libVlcVersion)
                .Split(Environment.NewLine, StringSplitOptions.None))
            {
                writer.TryWrite(line);
            }

            return writer;
        }
        catch
        {
            Debug.WriteLine("Playback diagnostics unavailable.");
            return null;
        }
    }

    public static string GetDefaultLogPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Path.Combine(
            root,
            "VideoMonitor",
            "Logs",
            $"playback-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public bool TryWrite(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Volatile.Read(ref disposed) != 0
            || Volatile.Read(ref writerFailed) != 0)
        {
            return false;
        }

        return lines.Writer.TryWrite(PlaybackDiagnosticsSanitizer.Sanitize(line));
    }

    public ValueTask DisposeAsync()
    {
        lock (disposeGate)
        {
            if (disposeTask is null)
            {
                Interlocked.Exchange(ref disposed, 1);
                lines.Writer.TryComplete();
                disposeTask = CompleteAsync();
            }

            return new ValueTask(disposeTask);
        }
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task CompleteAsync()
    {
        await writeTask.ConfigureAwait(false);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var line in lines.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            MarkWriterFailed();
        }
        finally
        {
            try
            {
                writer.Dispose();
            }
            catch
            {
                MarkWriterFailed();
            }
        }
    }

    private void MarkWriterFailed()
    {
        if (Interlocked.Exchange(ref writerFailed, 1) == 0)
        {
            Debug.WriteLine("Playback diagnostics unavailable.");
        }

        lines.Writer.TryComplete();
    }
}

public sealed class PlaybackDiagnosticsSampler : IDisposable, IAsyncDisposable
{
    private readonly Guid channelId;
    private readonly string streamId;
    private readonly Func<PlaybackDiagnosticsCounters> readCounters;
    private readonly IPlaybackDiagnosticsWriter writer;
    private readonly TimeSpan interval;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task samplingTask;
    private PlaybackDiagnosticsCounters? previous;
    private int disposed;

    public PlaybackDiagnosticsSampler(
        Guid channelId,
        string streamId,
        Func<PlaybackDiagnosticsCounters> readCounters,
        IPlaybackDiagnosticsWriter writer,
        TimeSpan? interval = null)
    {
        this.channelId = channelId;
        this.streamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        this.readCounters = readCounters ?? throw new ArgumentNullException(nameof(readCounters));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        this.interval = interval ?? TimeSpan.FromSeconds(1);
        if (this.interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        samplingTask = RunAsync(cancellation.Token);
    }

    public bool SampleOnce()
    {
        if (Volatile.Read(ref disposed) != 0)
        {
            return false;
        }

        try
        {
            var current = readCounters();
            var delta = PlaybackDiagnosticsDeltaCalculator.Calculate(previous, current);
            previous = current;
            writer.TryWrite(PlaybackDiagnosticsFormatter.FormatSample(
                channelId,
                streamId,
                current,
                delta));
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch
        {
            Debug.WriteLine("Playback diagnostics sampler stopped.");
            cancellation.Cancel();
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            cancellation.Cancel();
        }

        return new ValueTask(CompleteAsync());
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private async Task CompleteAsync()
    {
        try
        {
            await samplingTask.ConfigureAwait(false);
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!SampleOnce())
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            Debug.WriteLine("Playback diagnostics sampler stopped.");
        }
    }
}

public sealed class PlaybackDiagnosticsSession : IDisposable
{
    private readonly Guid channelId;
    private readonly string streamId;
    private readonly MediaPlayer mediaPlayer;
    private readonly IPlaybackDiagnosticsWriter writer;
    private readonly PlaybackDiagnosticsSampler sampler;
    private int disposed;

    public PlaybackDiagnosticsSession(
        Guid channelId,
        string streamId,
        Media media,
        MediaPlayer mediaPlayer,
        IPlaybackDiagnosticsWriter writer)
    {
        this.channelId = channelId;
        this.streamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        ArgumentNullException.ThrowIfNull(media);
        this.mediaPlayer = mediaPlayer ?? throw new ArgumentNullException(nameof(mediaPlayer));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));

        RecordEvent("SESSION_CREATE");
        this.mediaPlayer.Opening += OnOpening;
        this.mediaPlayer.Buffering += OnBuffering;
        this.mediaPlayer.Playing += OnPlaying;
        this.mediaPlayer.Stopped += OnStopped;
        this.mediaPlayer.EncounteredError += OnError;
        sampler = new PlaybackDiagnosticsSampler(
            channelId,
            streamId,
            ReadCounters,
            writer);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        RecordEvent("STOPPED");
        mediaPlayer.Opening -= OnOpening;
        mediaPlayer.Buffering -= OnBuffering;
        mediaPlayer.Playing -= OnPlaying;
        mediaPlayer.Stopped -= OnStopped;
        mediaPlayer.EncounteredError -= OnError;
        sampler.Dispose();
        RecordEvent("SESSION_DISPOSE");
    }

    private PlaybackDiagnosticsCounters ReadCounters()
    {
        var stats = mediaPlayer.Media?.Statistics ?? default;
        return new(
            mediaPlayer.Fps,
            stats.InputBitrate,
            stats.DemuxBitrate,
            stats.DecodedVideo,
            stats.DisplayedPictures,
            stats.LostPictures,
            stats.DemuxCorrupted,
            stats.DemuxDiscontinuity);
    }

    private void OnOpening(object? sender, EventArgs args) => RecordEvent("OPENING");

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs args) =>
        RecordEvent("BUFFERING", args.Cache);

    private void OnPlaying(object? sender, EventArgs args) => RecordEvent("PLAYING");

    private void OnStopped(object? sender, EventArgs args) => RecordEvent("STOPPED");

    private void OnError(object? sender, EventArgs args) => RecordEvent("ERROR");

    private void RecordEvent(string state, float? bufferingPercent = null)
    {
        try
        {
            writer.TryWrite(PlaybackDiagnosticsFormatter.FormatEvent(
                channelId,
                streamId,
                state,
                bufferingPercent));
        }
        catch
        {
            Debug.WriteLine("Playback diagnostics unavailable.");
        }
    }
}
