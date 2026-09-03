using System.Reflection;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class PlaybackDiagnosticsTests
{
    [Fact]
    public void Sanitizer_RemovesCredentialsAndQueryTokensFromUrls()
    {
        var sanitizerType = GetRequiredType("PlaybackDiagnosticsSanitizer");
        var method = GetRequiredMethod(sanitizerType, "Sanitize");
        const string input =
            "rtsp://admin:123456@192.168.0.2:554/Streaming/Channels/101?ticket=abcdef";

        var sanitized = (string)method.Invoke(null, [input])!;

        Assert.DoesNotContain("admin", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("123456", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("abcdef", sanitized, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_URL]", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SampleDeltaCalculator_ComputesPerSecondCounterDeltas()
    {
        var countersType = GetRequiredType("PlaybackDiagnosticsCounters");
        var constructor = countersType.GetConstructors().Single();
        var previous = CreateCounters(constructor, 100, 95, 5, 2, 3);
        var current = CreateCounters(constructor, 125, 116, 9, 5, 4);
        var calculatorType = GetRequiredType("PlaybackDiagnosticsDeltaCalculator");
        var calculate = GetRequiredMethod(calculatorType, "Calculate");

        var delta = calculate.Invoke(null, [previous, current])!;
        var deltaType = delta.GetType();

        Assert.Equal(25, ReadProperty(deltaType, delta, "DecodedDelta"));
        Assert.Equal(21, ReadProperty(deltaType, delta, "DisplayedDelta"));
        Assert.Equal(4, ReadProperty(deltaType, delta, "LostDelta"));
        Assert.Equal(3, ReadProperty(deltaType, delta, "CorruptedDelta"));
        Assert.Equal(1, ReadProperty(deltaType, delta, "DiscontinuityDelta"));
    }

    [Fact]
    public async Task Writer_WritesQueuedLinesInOrder()
    {
        var writerType = GetRequiredType("PlaybackDiagnosticsWriter");
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoMonitor.PlaybackDiagnosticsTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(testRoot, "diagnostics.log");

        try
        {
            var writer = Activator.CreateInstance(writerType, path)!;
            var tryWrite = GetRequiredMethod(writerType, "TryWrite");

            Assert.True((bool)tryWrite.Invoke(writer, ["first"])!);
            Assert.True((bool)tryWrite.Invoke(writer, ["second"])!);
            Assert.True((bool)tryWrite.Invoke(writer, ["third"])!);

            await ((IAsyncDisposable)writer).DisposeAsync();

            Assert.Equal(["first", "second", "third"], File.ReadAllLines(path));
        }
        finally
        {
            DeleteTestDirectory(testRoot);
        }
    }

    [Fact]
    public async Task Writer_DisposeFlushesQueuedLines()
    {
        var writerType = GetRequiredType("PlaybackDiagnosticsWriter");
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoMonitor.PlaybackDiagnosticsTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(testRoot, "diagnostics.log");

        try
        {
            var writer = Activator.CreateInstance(writerType, path)!;
            var tryWrite = GetRequiredMethod(writerType, "TryWrite");

            Assert.True((bool)tryWrite.Invoke(writer, ["flushed-before-dispose"])!);
            await ((IAsyncDisposable)writer).DisposeAsync();

            Assert.Equal(
                ["flushed-before-dispose"],
                File.ReadAllLines(path));
        }
        finally
        {
            DeleteTestDirectory(testRoot);
        }
    }

    [Fact]
    public async Task Writer_SanitizesQueuedUrlsBeforeWriting()
    {
        var writerType = GetRequiredType("PlaybackDiagnosticsWriter");
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoMonitor.PlaybackDiagnosticsTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(testRoot, "diagnostics.log");

        try
        {
            var writer = Activator.CreateInstance(writerType, path)!;
            var tryWrite = GetRequiredMethod(writerType, "TryWrite");

            Assert.True((bool)tryWrite.Invoke(
                writer,
                ["source=rtsp://admin:123456@192.168.0.2/live?ticket=abcdef"])!);
            await ((IAsyncDisposable)writer).DisposeAsync();

            var contents = File.ReadAllText(path);
            Assert.DoesNotContain("admin", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("123456", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdef", contents, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestDirectory(testRoot);
        }
    }

    [Fact]
    public void NativeLogFilter_OnlyKeepsPerformanceKeywords()
    {
        var filterType = GetRequiredType("PlaybackDiagnosticsNativeLogFilter");
        var method = GetRequiredMethod(filterType, "IsRelevant");

        Assert.True((bool)method.Invoke(null, ["decoder dropped late frame"])!);
        Assert.False((bool)method.Invoke(null, ["catalog request completed"])!);
    }

    [Fact]
    public void NativeLogFormatter_SanitizesUrlsInModuleAndMessage()
    {
        var formatterType = GetRequiredType("PlaybackDiagnosticsFormatter");
        var method = GetRequiredMethod(formatterType, "FormatNativeLog");
        var logLevelType = method.GetParameters()[0].ParameterType;
        var level = Enum.GetValues(logLevelType).GetValue(0)
            ?? throw new InvalidOperationException("Missing LibVLC log level value.");

        var formatted = (string)method.Invoke(
            null,
            [
                level,
                "rtsp://module-user:module-secret@192.168.0.2/module",
                "decoder opened http://message-user:message-secret@192.168.0.3/live?ticket=token"
            ])!;

        Assert.DoesNotContain("module-user", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("module-secret", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("message-user", formatted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message-secret", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("token", formatted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED_URL]", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Sampler_FirstSampleUsesUnavailableDeltasAndDisposeStopsIt()
    {
        var countersType = GetRequiredType("PlaybackDiagnosticsCounters");
        var constructor = countersType.GetConstructors().Single();
        var counters = CreateCounters(constructor, 100, 95, 5, 2, 3);
        var samplerType = GetRequiredType("PlaybackDiagnosticsSampler");
        var writer = new RecordingWriter();
        var readCounters = new Func<PlaybackDiagnosticsCounters>(() =>
            new PlaybackDiagnosticsCounters(25f, 1.5f, 1.25f, 100, 95, 5, 2, 3));
        var samplerConstructor = samplerType.GetConstructors().Single();
        var sampler = samplerConstructor.Invoke(
        [
            Guid.NewGuid(),
            "camera001",
            readCounters,
            writer,
            TimeSpan.FromHours(1)
        ]);
        var sampleOnce = GetRequiredMethod(samplerType, "SampleOnce");

        Assert.True((bool)sampleOnce.Invoke(sampler, null)!);
        Assert.Contains("decodedDelta=unavailable", writer.Lines.Single());

        ((IDisposable)sampler).Dispose();

        Assert.False((bool)sampleOnce.Invoke(sampler, null)!);
        Assert.Single(writer.Lines);
    }

    [Fact]
    public void VlcPlaybackService_UsesStatsAndOneGlobalDiagnosticsSubscription()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var sourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "Playback",
            "VlcPlaybackService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("\"--stats\"", source);
        Assert.Contains("libVlc.Log +=", source);
        Assert.Contains("libVlc.Log -=", source);
        Assert.Contains("PlaybackDiagnosticsWriter", source);
    }

    private static Type GetRequiredType(string name) =>
        typeof(VlcPlaybackService).Assembly.GetType(
            $"VideoMonitor.Wpf.Playback.{name}")
        ?? throw new InvalidOperationException($"Missing diagnostics type: {name}");

    private static MethodInfo GetRequiredMethod(Type type, string name) =>
        type.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Missing diagnostics method: {type.Name}.{name}");

    private static object CreateCounters(
        ConstructorInfo constructor,
        int decoded,
        int displayed,
        int lost,
        int corrupted,
        int discontinuity) =>
        constructor.Invoke(
        [
            25f,
            1.5f,
            1.25f,
            decoded,
            displayed,
            lost,
            corrupted,
            discontinuity
        ]);

    private static object? ReadProperty(Type type, object value, string name) =>
        type.GetProperty(name)?.GetValue(value);

    private static void DeleteTestDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class RecordingWriter : IPlaybackDiagnosticsWriter
    {
        public List<string> Lines { get; } = [];

        public bool TryWrite(string line)
        {
            Lines.Add(line);
            return true;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }
}
