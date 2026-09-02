using System.Net;
using System.Net.Http.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Composition;

public sealed class FormalPlaybackCompositionTests
{
    [Fact]
    public async Task EngineFactoryFailureIsHandledByCoordinator()
    {
        var factoryCalls = 0;
        await using var composition = await CreateCompositionAsync(() =>
        {
            factoryCalls++;
            throw new InvalidOperationException("engine init failed");
        });
        var tile = new VideoTileViewModel();
        var coordinator = composition.CreateFormalPlaybackCoordinator(tile);

        await coordinator.StartAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            StreamType.Main);

        Assert.Equal(1, factoryCalls);
        Assert.Equal(PlaybackState.Error, tile.PlaybackState);
        Assert.Equal("播放失败", tile.PlaybackErrorTitle);
        Assert.Equal("PLAYBACK_FAILED", tile.PlaybackErrorDetail);
    }

    [Fact]
    public async Task EngineFactoryFailureCanRetryOnLaterExplicitStart()
    {
        var factoryCalls = 0;
        var engine = new CountingFormalPlaybackEngine();
        await using var composition = await CreateCompositionAsync(() =>
        {
            factoryCalls++;
            return factoryCalls == 1
                ? throw new InvalidOperationException("engine init failed")
                : engine;
        });
        var tile = new VideoTileViewModel();
        var coordinator = composition.CreateFormalPlaybackCoordinator(tile);
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();

        await coordinator.StartAsync(deviceId, channelId, StreamType.Main);
        Assert.Equal(PlaybackState.Error, tile.PlaybackState);

        await coordinator.StartAsync(deviceId, channelId, StreamType.Main);

        Assert.Equal(2, factoryCalls);
        Assert.Equal(PlaybackState.Playing, tile.PlaybackState);
        Assert.NotNull(coordinator.CurrentSession);
        Assert.Single(engine.StartedSessions);
    }

    [Fact]
    public async Task SevenCoordinatorsShareOneSuccessfulFormalEngine()
    {
        var factoryCalls = 0;
        var engine = new CountingFormalPlaybackEngine();
        await using var composition = await CreateCompositionAsync(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return engine;
        });
        var coordinators = Enumerable.Range(0, 7)
            .Select(_ =>
            {
                var tile = new VideoTileViewModel();
                return composition.CreateFormalPlaybackCoordinator(tile);
            })
            .ToArray();

        await Task.WhenAll(coordinators.Select((coordinator, index) =>
            coordinator.StartAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                index % 2 == 0 ? StreamType.Main : StreamType.Sub)));

        Assert.Equal(1, factoryCalls);
        Assert.Equal(7, engine.StartedSessions.Count);
        Assert.Equal(
            7,
            coordinators
                .Select(coordinator => coordinator.CurrentSession)
                .OfType<PlaybackSession>()
                .Distinct()
                .Count());
    }

    private static async Task<ApplicationCatalogComposition> CreateCompositionAsync(
        Func<IFormalPlaybackEngine> formalEngineFactory)
    {
        var dependencies = new ApplicationCatalogComposition.Dependencies
        {
            ClientSettingsStoreFactory = () => new TestClientSettingsStore(),
            CentralHttpClientFactory = () => new HttpClient(new FormalPlaybackHandler()),
            UiDispatcherFactory = () => new InlineDispatcher(),
            CentralPlaybackEngineFactory = () => new CountingPlaybackEngine(),
            CentralFormalPlaybackEngineFactory = formalEngineFactory
        };
        var composition = await ApplicationCatalogComposition.CreateAsync(
                new LocalPlaybackConfiguration(
                    new SingleCameraTestOptions { Enabled = false },
                    new ZlmOptions()),
                dependencies)
            .ConfigureAwait(false);
        composition.StartCentralCoordinator();
        await EventuallyAsync(() =>
            composition.Coordinator!.Status.State == ServerConnectionState.Connected);
        return composition;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException("Condition was not reached.");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class TestClientSettingsStore : IClientSettingsStore
    {
        public ClientSettings Load() =>
            new(new ClientServerSettings("https://server-b"));

        public Task SaveAsync(
            ClientSettings settings,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FormalPlaybackHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = request.RequestUri?.AbsolutePath switch
            {
                "/health/ready" => new HttpResponseMessage(HttpStatusCode.OK),
                "/api/v1/catalog" => JsonResponse(new CatalogSnapshotDto([], [])),
                "/api/v1/playback/streams/ensure" => JsonResponse(
                    new EnsurePlaybackStreamResponse(
                        "formal-stream",
                        new Uri("https://server-b/live/formal-stream"),
                        DateTimeOffset.UtcNow.AddMinutes(1),
                        StreamRuntimeState.Ready)),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
            return Task.FromResult(response);
        }

        private static HttpResponseMessage JsonResponse<T>(T value) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(value)
            };
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class CountingFormalPlaybackEngine : IFormalPlaybackEngine, IDisposable
    {
        private readonly Dictionary<PlaybackSession, IPlaybackRuntimeEventSink> eventSinks = [];

        public List<PlaybackSession> StartedSessions { get; } = [];

        public PlaybackSession Prepare(
            FormalPlaybackSource source,
            IPlaybackRuntimeEventSink eventSink)
        {
            var session = new PlaybackSession(
                source.ChannelId,
                source.StreamId,
                null,
                null);
            lock (StartedSessions)
            {
                StartedSessions.Add(session);
                eventSinks.Add(session, eventSink);
            }

            return session;
        }

        public void Play(PlaybackSession session)
        {
            lock (StartedSessions)
            {
                eventSinks[session].Publish(
                    PlaybackRuntimeEvent.ForPlaying(session.CameraChannelId));
            }
        }

        public void Stop(PlaybackSession session) => session.Dispose();

        public void Dispose()
        {
        }
    }

    private sealed class CountingPlaybackEngine : IPlaybackEngine
    {
        public PlaybackSession Start(PlaybackSource source) =>
            new(source, null, null);

        public void Stop(PlaybackSession session) => session.Dispose();
    }
}
