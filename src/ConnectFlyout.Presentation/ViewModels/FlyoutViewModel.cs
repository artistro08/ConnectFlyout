using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Navigation;

namespace ConnectFlyout.Presentation.ViewModels;

/// <summary>
/// The tray flyout: which page shows, the headsets, and the footer actions.
/// </summary>
public sealed class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly HeadsetManager _manager;
    private readonly FlyoutNavigator _navigator;
    private readonly Func<ManagedHeadset, HeadsetViewModel> _createHeadsetViewModel;
    private readonly IBluetoothAudio _bluetoothAudio;
    private readonly TimeProvider _timeProvider;
    private readonly UiContext _ui = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();

    private static readonly TimeSpan AudioDisconnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AudioDisconnectPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan AudioReconnectSettleDelay = TimeSpan.FromMilliseconds(500);

    private FlyoutRoute _route = new(FlyoutPageKind.Empty, null, false);
    private HeadsetViewModel? _currentHeadset;

    public FlyoutViewModel(
        HeadsetManager manager,
        FlyoutNavigator navigator,
        Func<ManagedHeadset, HeadsetViewModel> createHeadsetViewModel,
        IBluetoothAudio bluetoothAudio,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _manager = manager;
        _navigator = navigator;
        _createHeadsetViewModel = createHeadsetViewModel;
        _bluetoothAudio = bluetoothAudio;
        _timeProvider = timeProvider;

        PickCommand = new RelayCommand<HeadsetViewModel>(Pick);
        BackCommand = new RelayCommand(Back);
        ReconnectCommand = new RelayCommand(Reconnect);
        OpenSettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));
        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke(this, EventArgs.Empty));

        _manager.HeadsetAdded += OnHeadsetAdded;
        _manager.HeadsetRemoved += OnHeadsetRemoved;
        _manager.ConnectionStateChanged += OnConnectionStateChanged;
        foreach (var headset in _manager.Headsets)
        {
            Add(headset);
        }
        Refresh();
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    public ObservableCollection<HeadsetViewModel> Headsets { get; } = [];

    /// <summary>
    /// Headsets with an open control link, for the tray icon's hover tooltip. A snapshot:
    /// read it each time the tooltip opens.
    /// </summary>
    public IReadOnlyList<HeadsetViewModel> ConnectedHeadsets => [.. Headsets.Where(headset => headset.IsConnected)];

    public HeadsetViewModel? CurrentHeadset
    {
        get => _currentHeadset;
        private set => SetProperty(ref _currentHeadset, value);
    }

    public FlyoutPageKind Page => _route.Kind;

    public bool IsEmptyVisible => _route.Kind == FlyoutPageKind.Empty;

    public bool IsPickerVisible => _route.Kind == FlyoutPageKind.Picker;

    public bool IsDeviceVisible => _route.Kind == FlyoutPageKind.Device;

    public bool ShowBack => _route.ShowBack;

    /// <summary>
    /// A headset was picked as the default. The picker then only shows briefly while that
    /// headset connects, so it skips its entrance animation.
    /// </summary>
    public bool HasDefaultHeadset => _navigator.HasRemembered;

    public IRelayCommand<HeadsetViewModel> PickCommand { get; }

    public IRelayCommand BackCommand { get; }

    public IRelayCommand ReconnectCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand QuitCommand { get; }

    /// <summary>
    /// Opens the page of the headset with this ID, as picking it would (a notification about
    /// it was clicked). Unknown IDs are ignored.
    /// </summary>
    public void ShowHeadset(string headsetId) => Pick(Headsets.FirstOrDefault(headset => headset.Id == headsetId));

    /// <summary>
    /// Refreshes battery. Called each time the flyout opens.
    /// </summary>
    public Task OnOpenedAsync()
    {
        Refresh();
        return CurrentHeadset?.RefreshBatteryAsync() ?? Task.CompletedTask;
    }

    public void Dispose()
    {
        _lifetimeCancellation.Cancel();
        _manager.HeadsetAdded -= OnHeadsetAdded;
        _manager.HeadsetRemoved -= OnHeadsetRemoved;
        _manager.ConnectionStateChanged -= OnConnectionStateChanged;
        foreach (var headset in Headsets)
        {
            headset.AutoConnectChanged -= OnAutoConnectChanged;
            headset.ReconnectRequested -= OnReconnectRequested;
            headset.AudioReconnectRequested -= OnAudioReconnectRequested;
            headset.ConnectRequested -= OnConnectRequested;
            headset.Dispose();
        }
        Headsets.Clear();
        _lifetimeCancellation.Dispose();
    }

    private void Pick(HeadsetViewModel? headset)
    {
        if (headset is null)
        {
            return;
        }
        _navigator.Pick(headset.Id, headset.IsAvailable);
        Refresh();
    }

    private void Back()
    {
        _navigator.Back();
        Refresh();
    }

    private void Reconnect()
    {
        if (CurrentHeadset is not null)
        {
            _manager.Reconnect(CurrentHeadset.Id);
        }
    }

    private void OnHeadsetAdded(object? sender, ManagedHeadset headset) => _ui.Post(() =>
    {
        Add(headset);
        Refresh();
    });

    private void OnHeadsetRemoved(object? sender, ManagedHeadset headset) => _ui.Post(() =>
    {
        var viewModel = Headsets.FirstOrDefault(item => item.Id == headset.Id);
        if (viewModel is not null)
        {
            Headsets.Remove(viewModel);
            viewModel.AutoConnectChanged -= OnAutoConnectChanged;
            viewModel.ReconnectRequested -= OnReconnectRequested;
            viewModel.AudioReconnectRequested -= OnAudioReconnectRequested;
            viewModel.ConnectRequested -= OnConnectRequested;
            viewModel.Dispose();
        }
        Refresh();
    });

    // Also covers Windows connecting or disconnecting the headset, which can change the page
    private void OnConnectionStateChanged(object? sender, ManagedHeadset headset) => _ui.Post(() =>
    {
        var viewModel = Headsets.FirstOrDefault(item => item.Id == headset.Id);
        viewModel?.UpdateConnectionState(headset.ConnectionState);
        if (viewModel?.TakeConnectRetry() == true)
        {
            _ = RequestAudioConnectAsync(viewModel);
        }
        Refresh();
    });

    private void OnAutoConnectChanged(object? sender, EventArgs e)
    {
        if (sender is HeadsetViewModel headset)
        {
            _manager.ApplyAutoConnect(headset.Id);
            Refresh();
        }
    }

    private void OnReconnectRequested(object? sender, EventArgs e)
    {
        if (sender is HeadsetViewModel headset)
        {
            _manager.Reconnect(headset.Id);
            headset.RaiseAutoConnectChanged();
            Refresh();
        }
    }

    private async void OnAudioReconnectRequested(object? sender, AudioReconnectRequestedEventArgs e)
    {
        if (sender is not HeadsetViewModel headset || !headset.Features.ConnectionQuality)
        {
            e.Complete(false);
            return;
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            e.CancellationToken,
            _lifetimeCancellation.Token);
        var cancellationToken = cancellation.Token;
        var succeeded = false;
        try
        {
            if (!await _bluetoothAudio.DisconnectAsync(headset.Id).ConfigureAwait(true))
            {
                return;
            }

            // Wait For HeadsetManager To Observe The Full Windows Bluetooth Disconnect
            var deadline = _timeProvider.GetUtcNow() + AudioDisconnectTimeout;
            while (headset.IsWindowsConnected)
            {
                var remaining = deadline - _timeProvider.GetUtcNow();
                if (remaining <= TimeSpan.Zero)
                {
                    return;
                }

                await Task.Delay(
                    remaining < AudioDisconnectPollInterval ? remaining : AudioDisconnectPollInterval,
                    _timeProvider,
                    cancellationToken).ConfigureAwait(true);
            }

            await Task.Delay(AudioReconnectSettleDelay, _timeProvider, cancellationToken).ConfigureAwait(true);
            succeeded = await _bluetoothAudio.ConnectAsync(headset.Id).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // The Headset View Model Reports The Failed Reconnect
        }
        finally
        {
            e.Complete(succeeded);
        }
    }

    // Connect asks Windows for the headset's audio; the connect then arrives like any other
    // Windows connect, and the headset view model gives up on its own if it never does
    private async void OnConnectRequested(object? sender, EventArgs e)
    {
        if (sender is not HeadsetViewModel headset || !headset.ShowConnect)
        {
            return;
        }
        headset.StartBluetoothConnect();

        // Connecting on purpose takes back headphones let go of with Disconnect
        if (headset.IsReleased)
        {
            headset.AutoConnect = true;
        }

        await RequestAudioConnectAsync(headset).ConfigureAwait(true);
    }

    private async Task RequestAudioConnectAsync(HeadsetViewModel headset)
    {
        bool accepted;
        try
        {
            accepted = await _bluetoothAudio.ConnectAsync(headset.Id).ConfigureAwait(true);
        }
        catch (Exception)
        {
            accepted = false;
        }
        if (!accepted)
        {
            _ui.Post(headset.FailBluetoothConnect);
        }
    }

    private void Add(ManagedHeadset headset)
    {
        if (Headsets.Any(item => item.Id == headset.Id))
        {
            return;
        }
        var viewModel = _createHeadsetViewModel(headset);
        viewModel.AutoConnectChanged += OnAutoConnectChanged;
        viewModel.ReconnectRequested += OnReconnectRequested;
        viewModel.AudioReconnectRequested += OnAudioReconnectRequested;
        viewModel.ConnectRequested += OnConnectRequested;
        Headsets.Add(viewModel);
    }

    private void Refresh()
    {
        _route = _navigator.Resolve([.. Headsets.Select(headset => new HeadsetAvailability(headset.Id, headset.IsAvailable))]);
        CurrentHeadset = _route.HeadsetId is null ? null : Headsets.FirstOrDefault(headset => headset.Id == _route.HeadsetId);
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsPickerVisible));
        OnPropertyChanged(nameof(IsDeviceVisible));
        OnPropertyChanged(nameof(ShowBack));
        OnPropertyChanged(nameof(HasDefaultHeadset));
    }
}
