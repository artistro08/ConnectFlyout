using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// Tracks the paired Sony headsets and keeps a control link open to each one Windows has
/// connected.
/// </summary>
/// <remarks>
/// A headset is listed from the moment it's seen paired until it's unpaired, whether or not
/// Windows has it connected right now. While Windows has it connected, the control link is
/// (re)opened on a schedule of 0 s, 1 s, 2 s, 5 s, then every 30 s until it succeeds; a link that
/// drops restarts that schedule at 1 s, and one that drops mid-connect counts as a failed attempt.
/// <see cref="Release"/> lets go of a headset and stays off
/// it (remembered across restarts) until <see cref="Reconnect"/>.
/// </remarks>
public sealed class HeadsetManager : IDisposable
{
    private static readonly TimeSpan[] ConnectDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
    ];

    private static readonly TimeSpan SteadyConnectDelay = TimeSpan.FromSeconds(30);

    private readonly IBluetoothDeviceSource _source;
    private readonly Func<string, IHeadset> _createHeadset;
    private readonly AppSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HeadsetManager> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ManagedHeadset> _headsets = [];
    private bool _disposed;

    /// <param name="source">Paired Bluetooth devices from Windows.</param>
    /// <param name="createHeadset">Creates the control link for a device name.</param>
    /// <param name="settings">Auto-connect choices and the headsets already known to be Sony.</param>
    /// <param name="timeProvider">Clock for the reconnect schedule.</param>
    /// <param name="logger">Logger.</param>
    public HeadsetManager(
        IBluetoothDeviceSource source,
        Func<string, IHeadset> createHeadset,
        AppSettings settings,
        TimeProvider timeProvider,
        ILogger<HeadsetManager> logger)
    {
        _source = source;
        _createHeadset = createHeadset;
        _settings = settings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Wait after Windows connects a headset before opening the control link. A headset still
    /// setting up its audio drops a link opened straight away, and can drop off Windows with it.
    /// </summary>
    public TimeSpan WindowsConnectSettle { get; init; } = TimeSpan.Zero;

    public event EventHandler<ManagedHeadset>? HeadsetAdded;

    public event EventHandler<ManagedHeadset>? HeadsetRemoved;

    public event EventHandler<ManagedHeadset>? ConnectionStateChanged;

    public IReadOnlyList<ManagedHeadset> Headsets
    {
        get
        {
            lock (_gate)
            {
                return [.. _headsets.Values];
            }
        }
    }

    public void Start()
    {
        _source.DeviceChanged += OnDeviceChanged;
        _source.DeviceRemoved += OnDeviceRemoved;
        _source.StartWatching();
    }

    /// <summary>
    /// Drops the current link and connects again right away. Also undoes <see cref="Release"/>.
    /// </summary>
    /// <remarks>
    /// Works even when Windows doesn't have the headset connected: opening the link can bring it
    /// back. That tries once; the retry schedule only runs while Windows has it connected.
    /// </remarks>
    public void Reconnect(string id)
    {
        var headset = Find(id);
        if (headset is null)
        {
            return;
        }

        LogMessages.Reconnecting(_logger, headset.Name);
        _settings.SetAutoConnectEnabled(id, true);
        headset.Headset.Disconnect();
        StartConnectLoop(headset, 0);
    }

    /// <summary>
    /// Lets go of a headset so another device (like the Sony phone app) can control it, and
    /// stays off it, across restarts too, until <see cref="Reconnect"/>. Windows audio keeps
    /// playing.
    /// </summary>
    public void Release(string id)
    {
        _settings.SetAutoConnectEnabled(id, false);
        ApplyAutoConnect(id);
    }

    /// <summary>
    /// Applies a changed auto-connect setting to a listed headset.
    /// </summary>
    public void ApplyAutoConnect(string id)
    {
        var headset = Find(id);
        if (headset is null)
        {
            return;
        }

        if (_settings.IsAutoConnectEnabled(id))
        {
            if (headset.IsWindowsConnected && headset.ConnectionState == HeadsetConnectionState.Disconnected)
            {
                StartConnectLoop(headset, 0);
            }
            return;
        }

        CancelConnectLoop(headset);
        headset.Headset.Disconnect();
        SetState(headset, HeadsetConnectionState.Disconnected);
    }

    public void Dispose()
    {
        List<ManagedHeadset> headsets;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            headsets = [.. _headsets.Values];
            _headsets.Clear();
        }

        _source.DeviceChanged -= OnDeviceChanged;
        _source.DeviceRemoved -= OnDeviceRemoved;
        _source.StopWatching();

        foreach (var headset in headsets)
        {
            CancelConnectLoop(headset);
            headset.Headset.Dispose();
        }
    }

    private static string NormalizeAddress(string address) => address.Trim().ToUpperInvariant();

    private ManagedHeadset? Find(string id)
    {
        lock (_gate)
        {
            return _headsets.Values.FirstOrDefault(headset => headset.Id == id);
        }
    }

    private void OnDeviceChanged(object? sender, BluetoothDeviceInfo device)
    {
        if (string.IsNullOrWhiteSpace(device.Address))
        {
            return;
        }

        // A headset first seen under its Sony name is remembered by address, so it keeps
        // working (and keeps its model) after it's renamed in Windows.
        var id = NormalizeAddress(device.Address);
        var model = _settings.GetKnownHeadsetName(id);
        if (model is null)
        {
            if (!SonyDeviceNameFilter.IsSony(device.Name))
            {
                return;
            }
            model = device.Name;
            _settings.SetKnownHeadsetName(id, model);
        }

        var headset = Track(device, id, model);
        if (headset is null)
        {
            return;
        }

        // Only act on changes: Windows repeats updates that don't change the connection
        bool changed;
        lock (_gate)
        {
            changed = headset.IsWindowsConnected != device.IsConnected || headset.Name != device.Name;
            headset.IsWindowsConnected = device.IsConnected;
            headset.Name = device.Name;
        }
        if (!changed)
        {
            return;
        }

        if (device.IsConnected)
        {
            LogMessages.WindowsConnected(_logger, headset.Name);
            ConnectionStateChanged?.Invoke(this, headset);
            if (_settings.IsAutoConnectEnabled(headset.Id) && headset.ConnectionState == HeadsetConnectionState.Disconnected)
            {
                StartConnectLoop(headset, 0, WindowsConnectSettle);
            }
            return;
        }

        LogMessages.WindowsDisconnected(_logger, headset.Name);
        CancelConnectLoop(headset);
        headset.Headset.Disconnect();
        SetState(headset, HeadsetConnectionState.Disconnected);
        ConnectionStateChanged?.Invoke(this, headset);
    }

    private void OnDeviceRemoved(object? sender, string deviceId) => Untrack(deviceId);

    // The listed headset for a device, added (not yet Windows-connected) the first time it's seen
    private ManagedHeadset? Track(BluetoothDeviceInfo device, string id, string model)
    {
        ManagedHeadset headset;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }
            if (_headsets.TryGetValue(device.Id, out var existing))
            {
                return existing;
            }
            headset = new ManagedHeadset(device.Id, id, device.Name, _createHeadset(model));
            _headsets[device.Id] = headset;
        }

        headset.Headset.Disconnected += (_, _) => OnLinkDropped(headset);
        HeadsetAdded?.Invoke(this, headset);
        return headset;
    }

    private void Untrack(string deviceId)
    {
        ManagedHeadset? headset;
        lock (_gate)
        {
            if (!_headsets.Remove(deviceId, out headset))
            {
                return;
            }
        }

        LogMessages.Unpaired(_logger, headset.Name);
        CancelConnectLoop(headset);
        headset.Headset.Dispose();
        HeadsetRemoved?.Invoke(this, headset);
    }

    private void OnLinkDropped(ManagedHeadset headset)
    {
        lock (_gate)
        {
            if (!_headsets.ContainsKey(headset.DeviceId))
            {
                return;
            }

            // A drop mid-connect fails that attempt and the running loop backs off; restarting
            // the schedule here would retry every second for as long as the headset keeps
            // refusing the link
            if (headset.ConnectionState == HeadsetConnectionState.Connecting && headset.ConnectLoop is not null)
            {
                headset.DroppedWhileConnecting = true;
                return;
            }
        }

        LogMessages.LinkDropped(_logger, headset.Name);
        SetState(headset, HeadsetConnectionState.Disconnected);
        if (headset.IsWindowsConnected && _settings.IsAutoConnectEnabled(headset.Id))
        {
            StartConnectLoop(headset, 1);
        }
    }

    private void StartConnectLoop(ManagedHeadset headset, int firstDelayIndex, TimeSpan settle = default)
    {
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        Task? previousTask;
        lock (_gate)
        {
            previous = headset.ConnectLoop;
            previousTask = headset.ConnectTask;
            headset.ConnectLoop = cancellation;
        }
        previous?.Cancel();
        previous?.Dispose();

        var task = ConnectLoopAsync(headset, firstDelayIndex, settle, previousTask, cancellation.Token);
        lock (_gate)
        {
            headset.ConnectTask = task;
        }
    }

    private void CancelConnectLoop(ManagedHeadset headset)
    {
        CancellationTokenSource? loop;
        lock (_gate)
        {
            loop = headset.ConnectLoop;
            headset.ConnectLoop = null;
        }
        loop?.Cancel();
        loop?.Dispose();
    }

    private async Task ConnectLoopAsync(ManagedHeadset headset, int delayIndex, TimeSpan settle, Task? previousLoop, CancellationToken cancellationToken)
    {
        // Never run two connects on one headset: let a cancelled loop's in-flight connect finish first.
        if (previousLoop is not null)
        {
            await previousLoop.ConfigureAwait(false);
        }

        try
        {
            if (settle > TimeSpan.Zero)
            {
                await Task.Delay(settle, _timeProvider, cancellationToken).ConfigureAwait(false);
            }

            while (true)
            {
                var delay = delayIndex < ConnectDelays.Length ? ConnectDelays[delayIndex] : SteadyConnectDelay;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();

                lock (_gate)
                {
                    headset.DroppedWhileConnecting = false;
                }
                SetState(headset, HeadsetConnectionState.Connecting);
                try
                {
                    await headset.Headset.ConnectAsync(headset.Id).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    // Check for a drop and mark it connected in one step, so a drop can't land between them
                    lock (_gate)
                    {
                        if (headset.DroppedWhileConnecting)
                        {
                            throw new IOException("The headset dropped the link while connecting.");
                        }
                        headset.ConnectionState = HeadsetConnectionState.Connected;
                    }
                    ConnectionStateChanged?.Invoke(this, headset);
                    LogMessages.LinkOpen(_logger, headset.Name);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMessages.ConnectFailed(_logger, ex, headset.Name, delayIndex + 1);
                    SetState(headset, HeadsetConnectionState.Disconnected);

                    // Out of range or off: one try is enough; Windows connecting it restarts the schedule
                    if (!headset.IsWindowsConnected)
                    {
                        return;
                    }
                }

                delayIndex++;
            }
        }
        catch (OperationCanceledException)
        {
            // The headset was disconnected, unpaired or reconnected manually.
        }
    }

    private void SetState(ManagedHeadset headset, HeadsetConnectionState state)
    {
        lock (_gate)
        {
            if (headset.ConnectionState == state)
            {
                return;
            }
            headset.ConnectionState = state;
        }
        ConnectionStateChanged?.Invoke(this, headset);
    }
}
