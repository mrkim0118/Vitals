using System.Threading.Channels;
using Vitals.App.Models;
using Vitals.App.Native;

namespace Vitals.App.Services;

public sealed class MonitorRuntime : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly HardwareSensorService _hardwareSensorService = new();
    private readonly Channel<SystemSnapshot> _channel = Channel.CreateBounded<SystemSnapshot>(
        new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private Task? _samplingTask;
    private bool _disposed;

    public ChannelReader<SystemSnapshot> Snapshots => _channel.Reader;

    public bool Initialize()
    {
        var nativeInitialized = NativeMethods.Initialize();
        _hardwareSensorService.TryInitialize();

        if (!nativeInitialized)
        {
            return false;
        }

        _samplingTask = Task.Run(() => RunSamplingLoopAsync(_cts.Token));
        return true;
    }

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var topProcesses = Array.Empty<ProcessMetrics>();
            var lastProcessRefresh = DateTimeOffset.MinValue;

            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (DateTimeOffset.UtcNow - lastProcessRefresh >= TimeSpan.FromSeconds(3))
                {
                    topProcesses = NativeMethods.TrySampleTopProcesses().ToArray();
                    lastProcessRefresh = DateTimeOffset.UtcNow;
                }

                var snapshot = NativeMethods.TrySampleSystem(topProcesses);
                if (snapshot is not null)
                {
                    _channel.Writer.TryWrite(_hardwareSensorService.Enrich(snapshot));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _channel.Writer.TryComplete();

        if (_samplingTask is not null)
        {
            try
            {
                await _samplingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        NativeMethods.Shutdown();
        _hardwareSensorService.Dispose();
        _cts.Dispose();
    }
}
