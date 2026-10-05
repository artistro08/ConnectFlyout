using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Tests.Fakes;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class HeadsetViewModelTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeHeadset _headset = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly AppSettings _settings = new(new InMemorySettingsStore());
    private ManagedHeadset _managed = null!;
    private HeadsetViewModel _viewModel = null!;

    [TestInitialize]
    public void CreateViewModel()
    {
        _managed = new ManagedHeadset("device-xm6", "AC:80:0A:00:00:06", "WF-1000XM6", _headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        _viewModel = new HeadsetViewModel(_managed, _settings, new LowBatteryMonitor(_settings, _notifications), _time, NullLogger.Instance);
    }

    [TestCleanup]
    public void DisposeViewModel() => _viewModel.Dispose();

    [TestMethod]
    public void ShowsHeadsetState()
    {
        Assert.IsTrue(_viewModel.IsAmbient);
        Assert.AreEqual(8, _viewModel.AmbientLevel);
        Assert.IsTrue(_viewModel.FocusOnVoice);
        Assert.IsTrue(_viewModel.ShowDualBattery);
        Assert.AreEqual("85%", _viewModel.LeftBatteryText);
        Assert.AreEqual("82%", _viewModel.RightBatteryText);
        Assert.AreEqual("95%", _viewModel.CaseBatteryText);
        Assert.AreEqual("Connected \u00b7 LDAC", _viewModel.StatusText);
    }

    [TestMethod]
    public void MissingEarbudHidesItsBatteryAndDoesNotAlert()
    {
        _settings.LowBatteryNotifications = true;

        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, null, 80, 90, false) });
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.IsTrue(_viewModel.ShowDualBattery);
        Assert.IsFalse(_viewModel.ShowLeftBattery);
        Assert.IsTrue(_viewModel.ShowRightBattery);
        Assert.AreEqual(0, _notifications.Shown.Count);
    }

    [TestMethod]
    public void MissingEarbudIsPolledUntilItComesBack()
    {
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, 85, null, 90, false) });
        var before = _headset.Commands.Count(command => Equals(command, "battery"));

        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.AreEqual(before + 1, _headset.Commands.Count(command => Equals(command, "battery")));

        // Right earbud back: polling stops
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, 85, 80, 90, false) });
        _time.Advance(TimeSpan.FromSeconds(15));
        Assert.AreEqual(before + 1, _headset.Commands.Count(command => Equals(command, "battery")));
    }

    [TestMethod]
    public void MomentaryNoEarbudsKeepsLastLevels()
    {
        // Taking an earbud out briefly reports neither side before the real levels follow
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(null, null, null, 90, false) });

        Assert.IsTrue(_viewModel.ShowLeftBattery);
        Assert.AreEqual("85%", _viewModel.LeftBatteryText);
        Assert.AreEqual("82%", _viewModel.RightBatteryText);
        Assert.AreEqual("90%", _viewModel.CaseBatteryText);
    }

    [TestMethod]
    public void CaseKeepsLastKnownLevelAcrossRestarts()
    {
        // The case only reports while an earbud sits in it; show what it last said, like Sony's app
        Assert.AreEqual("95%", _viewModel.CaseBatteryText);
        _viewModel.Dispose();

        var restarted = new FakeHeadset();
        restarted.RaiseStateChanged(restarted.State with { Battery = new BatteryLevels(null, 70, null, null, false) });
        var managed = new ManagedHeadset("device-xm6", "AC:80:0A:00:00:06", "WF-1000XM6", restarted) { ConnectionState = HeadsetConnectionState.Connected };
        _viewModel = new HeadsetViewModel(managed, _settings, new LowBatteryMonitor(_settings, _notifications), _time, NullLogger.Instance);

        Assert.IsTrue(_viewModel.ShowCaseBattery);
        Assert.AreEqual("95%", _viewModel.CaseBatteryText);
    }

    [TestMethod]
    public async Task TurnOffSendsPowerOff()
    {
        await _viewModel.PowerOffCommand.ExecuteAsync(null);

        CollectionAssert.Contains(_headset.Commands, "power off");
    }

    [TestMethod]
    public void AudioTextShowsCodecAndDseeWhenOn()
    {
        Assert.AreEqual("LDAC", _viewModel.AudioText);

        _viewModel.DseeIndex = 1;

        Assert.AreEqual("LDAC · DSEE Extreme", _viewModel.AudioText);
    }

    [TestMethod]
    public void WhXb900nUsesRegularDseeAndModelSpecificControls()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "SBC",
                ConnectionQuality = 1,
                VoiceGuidance = 1,
                Vpt = 0,
                SoundPosition = 0x01,
            },
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        Assert.AreEqual("DSEE", viewModel.DseeName);
        Assert.IsTrue(viewModel.HasConnectionQualityControl);
        Assert.IsTrue(viewModel.HasLegacySpatialControls);
        Assert.IsTrue(viewModel.HasVoiceGuidanceControl);
        Assert.IsFalse(viewModel.CanPowerOffFromPopup);
        Assert.AreEqual(5, viewModel.DisplayedAutoPowerOffOptions.Count);
        Assert.AreEqual(0, viewModel.ConnectionQualityIndex);
        Assert.AreEqual("SBC", HeadsetViewModel.ConnectionQualityOptions[0]);
        Assert.AreEqual("AAC", HeadsetViewModel.ConnectionQualityOptions[1]);
        Assert.AreEqual("High Quality", HeadsetViewModel.ConnectionQualityOptions[2]);
        Assert.IsTrue(viewModel.CanChangeConnectionMode);
        Assert.IsTrue(viewModel.CanEditHeadsetSound);
        Assert.IsTrue(viewModel.ShowPopupSoundSection);
        Assert.IsTrue(viewModel.ShowPopupConnectionSeparator);
        Assert.IsTrue(viewModel.VoiceGuidance);
        Assert.AreEqual(0, viewModel.VptPresetIndex);
        Assert.AreEqual(1, viewModel.SoundPositionIndex);
    }

    [TestMethod]
    public void LegacyControlsFollowFeatureFlagsInsteadOfModelName()
    {
        using var capableHeadset = new FakeHeadset("Feature Test", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "SBC",
                ConnectionQuality = 1,
                VoiceGuidance = 1,
                Vpt = 0,
                SoundPosition = 0,
            },
        };
        var capableManaged = new ManagedHeadset("feature-test", "AC:80:0A:00:09:01", "Feature Test", capableHeadset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var capableViewModel = new HeadsetViewModel(
            capableManaged,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        Assert.AreEqual("DSEE", capableViewModel.DseeName);
        Assert.IsTrue(capableViewModel.HasConnectionQualityControl);
        Assert.IsTrue(capableViewModel.HasLegacySpatialControls);
        Assert.IsTrue(capableViewModel.HasVoiceGuidanceControl);
        Assert.IsFalse(capableViewModel.CanPowerOffFromPopup);
        Assert.AreEqual(5, capableViewModel.DisplayedAutoPowerOffOptions.Count);

        using var namedHeadset = new FakeHeadset("WH-XB900N", FakeHeadset.Xm6Features);
        var namedManaged = new ManagedHeadset("name-only", "AC:80:0A:00:09:02", "WH-XB900N", namedHeadset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var namedViewModel = new HeadsetViewModel(
            namedManaged,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        Assert.AreEqual("DSEE Extreme", namedViewModel.DseeName);
        Assert.IsFalse(namedViewModel.HasConnectionQualityControl);
        Assert.IsFalse(namedViewModel.HasLegacySpatialControls);
        Assert.IsFalse(namedViewModel.HasVoiceGuidanceControl);
        Assert.IsTrue(namedViewModel.CanPowerOffFromPopup);
        Assert.AreEqual(6, namedViewModel.DisplayedAutoPowerOffOptions.Count);
    }

    [TestMethod]
    public void WhXb900nDisablesSoundControlsOutsideSbcAndAac()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "aptX",
                ConnectionQuality = 0,
                VoiceGuidance = 1,
            },
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        Assert.AreEqual(2, viewModel.ConnectionQualityIndex);
        Assert.IsFalse(viewModel.CanEditHeadsetSound);
        Assert.IsFalse(viewModel.ShowPopupSoundSection);
        Assert.IsFalse(viewModel.ShowPopupConnectionSeparator);
    }

    [TestMethod]
    public void WhXb900nKeepsPopupSoundVisibleButDisabledUntilCodecIsKnown()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with { ConnectionQuality = 0 },
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        Assert.IsTrue(viewModel.ShowPopupSoundSection);
        Assert.IsTrue(viewModel.ShowPopupConnectionSeparator);
        Assert.IsFalse(viewModel.CanEditHeadsetSound);
    }

    [TestMethod]
    public void SoundControlsStayDisabledWhenANonXbHeadsetIsDisconnected()
    {
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);

        Assert.IsFalse(_viewModel.CanEditHeadsetSound);
        Assert.IsFalse(_viewModel.ShowPopupConnectionSeparator);
    }

    [TestMethod]
    public async Task OtherHeadsetKeepsOriginalSoundCommandsWhenDisconnected()
    {
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);

        _viewModel.SelectedEqualizerIndex = 1;
        _viewModel.DseeIndex = 1;
        await _viewModel.ApplyCustomEqualizerCommand.ExecuteAsync(null);

        Assert.IsTrue(_viewModel.ShowPopupSoundSection);
        Assert.AreEqual("DSEE Extreme", _viewModel.DseeName);
        Assert.AreEqual(0x10, (int)_headset.Commands[0]);
        Assert.AreEqual(("dsee", true), ((string, bool))_headset.Commands[1]);
        Assert.IsInstanceOfType<EqualizerSetting>(_headset.Commands[2]);
    }

    [TestMethod]
    public async Task WhXb900nAacToHighQualityDisablesDseeAndWaitsForSbc()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "AAC",
                ConnectionQuality = 0,
                Dsee = true,
            },
            HoldCommands = true,
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        viewModel.ConnectionQualityIndex = 2;

        Assert.IsFalse(viewModel.CanChangeConnectionMode);
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.Commands.Count == 1));
        Assert.AreEqual(("dsee", false), ((string, bool))headset.Commands[0]);
        headset.CompleteHeldCommands();

        Assert.IsTrue(await TestWait.UntilAsync(() => headset.Commands.Count == 2));
        Assert.AreEqual(("connectionQuality", true), ((string, bool))headset.Commands[1]);

        headset.RaiseStateChanged(headset.State with { Codec = "SBC", ConnectionQuality = 1 });
        headset.CompleteHeldCommands();

        Assert.IsTrue(await TestWait.UntilAsync(() => headset.Commands.Count == 3));
        Assert.AreEqual(("connectionQuality", false), ((string, bool))headset.Commands[2]);
        headset.CompleteHeldCommands();

        Assert.IsTrue(await TestWait.UntilAsync(() => viewModel.DseeIndex == 0));
        Assert.IsTrue(await TestWait.UntilAsync(() => viewModel.CanChangeConnectionMode));
    }

    [TestMethod]
    public async Task ConnectionModeCodecWaitUsesFakeTime()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "AAC",
                ConnectionQuality = 0,
            },
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        viewModel.ConnectionQualityIndex = 2;
        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.IsTrue(await TestWait.UntilAsync(() => viewModel.ErrorMessage is not null));
        Assert.IsTrue(viewModel.CanChangeConnectionMode);
    }

    [TestMethod]
    public async Task SpatialSelectionWhileACommandIsInFlightSnapsBackToThePendingValue()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "SBC",
                ConnectionQuality = 1,
                Vpt = 0,
                SoundPosition = 0,
            },
            HoldCommands = true,
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);
        var resetNotifications = 0;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(HeadsetViewModel.VptPresetIndex))
            {
                resetNotifications++;
            }
        };

        viewModel.VptPresetIndex = 1;
        headset.RaiseEcho(headset.State with { Vpt = 0, SoundPosition = 0 });
        Assert.AreEqual(1, viewModel.VptPresetIndex);

        resetNotifications = 0;
        viewModel.VptPresetIndex = 2;

        Assert.AreEqual(1, viewModel.VptPresetIndex);
        Assert.AreEqual(1, resetNotifications);
        Assert.IsFalse(viewModel.CanChangeSpatialEffect);
        Assert.AreEqual(1, headset.Commands.Count);

        headset.CompleteHeldCommands();

        Assert.IsTrue(await TestWait.UntilAsync(() => viewModel.CanChangeSpatialEffect));
        Assert.AreEqual(1, viewModel.VptPresetIndex);
        Assert.AreEqual(0, viewModel.SoundPositionIndex);
    }

    [TestMethod]
    public async Task WhXb900nSpatialSelectionRevertsWhenTheCommandFails()
    {
        using var headset = new FakeHeadset("WH-XB900N", FakeHeadset.Xb900nFeatures)
        {
            State = HeadsetSnapshot.Empty with
            {
                Codec = "SBC",
                ConnectionQuality = 1,
                Vpt = 0,
                SoundPosition = 0,
            },
            FailNextCommandWith = new COMException("timeout", HeadsetErrorMessages.TimeoutHResult),
        };
        var managed = new ManagedHeadset("device-xb900n", "AC:80:0A:00:09:00", "WH-XB900N", headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
            IsWindowsConnected = true,
        };
        using var viewModel = new HeadsetViewModel(
            managed,
            _settings,
            new LowBatteryMonitor(_settings, _notifications),
            _time,
            NullLogger.Instance);

        viewModel.VptPresetIndex = 1;

        Assert.IsTrue(await TestWait.UntilAsync(() => viewModel.VptPresetIndex == 0));
        Assert.AreEqual("Your headphones didn't respond. Try again.", viewModel.ErrorMessage);
    }

    [TestMethod]
    public void StatusLineShowsOnlyWhenNotConnected()
    {
        Assert.IsFalse(_viewModel.ShowStatus);

        _viewModel.UpdateConnectionState(HeadsetConnectionState.Connecting);

        Assert.IsTrue(_viewModel.ShowStatus);
    }

    [TestMethod]
    public void NoiseModeTileSendsCommand()
    {
        _viewModel.SetNoiseModeCommand.Execute("NoiseCancelling");

        Assert.IsTrue(_viewModel.IsNoiseCancelling);
        Assert.AreEqual(new NoiseControlSetting(NoiseMode.NoiseCancelling, 8, true), _headset.Commands.Single());
    }

    [TestMethod]
    public void SameNoiseModeSendsNothing()
    {
        _viewModel.SetNoiseModeCommand.Execute("Ambient");

        Assert.AreEqual(0, _headset.Commands.Count);
    }

    [TestMethod]
    public void FailedCommandRevertsAndShowsErrorForFiveSeconds()
    {
        _headset.FailNextCommandWith = new COMException("timeout", HeadsetErrorMessages.TimeoutHResult);

        _viewModel.SetNoiseModeCommand.Execute("Off");

        Assert.IsTrue(_viewModel.IsAmbient);
        Assert.AreEqual("Your headphones didn't respond. Try again.", _viewModel.ErrorMessage);
        Assert.IsTrue(_viewModel.HasError);

        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.IsNull(_viewModel.ErrorMessage);
    }

    [TestMethod]
    public void AmbientSliderIsThrottledAndSendsFinalValue()
    {
        _viewModel.AmbientLevel = 9;
        _viewModel.AmbientLevel = 10;
        _viewModel.AmbientLevel = 11;
        _viewModel.AmbientLevel = 12;

        Assert.AreEqual(1, _headset.Commands.Count);
        _time.Advance(HeadsetViewModel.SliderInterval);

        CollectionAssert.AreEqual(
            new object[] { new NoiseControlSetting(NoiseMode.Ambient, 9, true), new NoiseControlSetting(NoiseMode.Ambient, 12, true) },
            _headset.Commands);
    }

    [TestMethod]
    public void EchoOfOlderLevelDoesNotMoveSliderWhileCommandsAreInFlight()
    {
        _headset.HoldCommands = true;
        _viewModel.AmbientLevel = 9;
        _viewModel.AmbientLevel = 12;
        _time.Advance(HeadsetViewModel.SliderInterval);

        _headset.RaiseEcho(_headset.State with { NoiseControl = new NoiseControlSetting(NoiseMode.Ambient, 9, true) });
        Assert.AreEqual(12, _viewModel.AmbientLevel);

        _headset.CompleteHeldCommands();
        Assert.AreEqual(12, _viewModel.AmbientLevel);
    }

    [TestMethod]
    public void PlaybackShowsWhenTwoDevicesAreConnected()
    {
        _headset.RaiseStateChanged(_headset.State with { PlaybackDevices = TwoDevices });

        Assert.IsTrue(_viewModel.ShowPlayback);
        Assert.AreEqual(2, _viewModel.PlaybackDevices.Count);
        Assert.IsTrue(_viewModel.PlaybackDevices[0].Playing);
    }

    [TestMethod]
    public void PlaybackHidesWithOneDeviceOrWithoutSupport()
    {
        Assert.IsFalse(_viewModel.ShowPlayback);

        _headset.RaiseStateChanged(_headset.State with { PlaybackDevices = [TwoDevices[0]] });

        Assert.IsFalse(_viewModel.ShowPlayback);
    }

    [TestMethod]
    public void PlaybackHidesWhileDisconnected()
    {
        _headset.RaiseStateChanged(_headset.State with { PlaybackDevices = TwoDevices });

        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);

        Assert.IsFalse(_viewModel.ShowPlayback);
    }

    [TestMethod]
    public void SwitchingPlaybackSendsTheDeviceAndMarksItPlaying()
    {
        _headset.RaiseStateChanged(_headset.State with { PlaybackDevices = TwoDevices });

        _viewModel.SwitchPlaybackCommand.Execute(_viewModel.PlaybackDevices[1]);

        CollectionAssert.Contains(_headset.Commands, ("switchPlayback", "AA:BB:CC:DD:EE:02"));
        Assert.IsTrue(_viewModel.PlaybackDevices[1].Playing);
        Assert.IsFalse(_viewModel.PlaybackDevices[0].Playing);
    }

    [TestMethod]
    public void RefusedSwitchPutsPlaybackBackAndSaysWhy()
    {
        _headset.RaiseStateChanged(_headset.State with { PlaybackDevices = TwoDevices });
        _headset.FailNextCommandWith = new COMException("refused", HeadsetErrorMessages.InvalidDataHResult);

        _viewModel.SwitchPlaybackCommand.Execute(_viewModel.PlaybackDevices[1]);

        Assert.IsTrue(_viewModel.PlaybackDevices[0].Playing);
        Assert.IsFalse(_viewModel.PlaybackDevices[1].Playing);
        Assert.AreEqual("Couldn't switch the audio. The other device may be on a call.", _viewModel.ErrorMessage);
    }

    [TestMethod]
    public void SwitchingToTheDeviceAlreadyPlayingSendsNothing()
    {
        _headset.RaiseStateChanged(_headset.State with { PlaybackDevices = TwoDevices });

        _viewModel.SwitchPlaybackCommand.Execute(_viewModel.PlaybackDevices[0]);

        Assert.AreEqual(0, _headset.Commands.Count);
    }

    private static PlaybackDevice[] TwoDevices { get; } =
    [
        new PlaybackDevice("AA:BB:CC:DD:EE:01", "DESKTOP", true),
        new PlaybackDevice("AA:BB:CC:DD:EE:02", "Pixel 9", false),
    ];

    [TestMethod]
    public void EqualizerKeepsItsPresetWhenTheListControlClearsIt()
    {
        // A ComboBox given a new preset list clears its selection and writes -1 back
        var before = _viewModel.SelectedEqualizerIndex;
        Assert.IsTrue(before >= 0);

        _viewModel.SelectedEqualizerIndex = -1;

        Assert.AreEqual(before, _viewModel.SelectedEqualizerIndex);
        Assert.AreEqual(0, _headset.Commands.Count);
    }

    [TestMethod]
    public void EqualizerRejectsOutOfRangePresetWithoutSavingIt()
    {
        var before = _viewModel.SelectedEqualizerIndex;

        _viewModel.SelectedEqualizerIndex = 999;

        Assert.AreEqual(before, _viewModel.SelectedEqualizerIndex);
        Assert.AreEqual(0, _headset.Commands.Count);
    }

    [TestMethod]
    public void AmbientSliderClampsAndRounds()
    {
        _viewModel.AmbientLevel = 25.4;
        Assert.AreEqual(20, _viewModel.AmbientLevel);

        _viewModel.AmbientLevel = 0.2;
        Assert.AreEqual(1, _viewModel.AmbientLevel);
    }

    [TestMethod]
    public void NotificationUpdatesControlsWithoutSendingCommands()
    {
        _headset.RaiseStateChanged(_headset.State with { NoiseControl = new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false) });

        Assert.IsTrue(_viewModel.IsNoiseCancelling);
        Assert.IsFalse(_viewModel.FocusOnVoice);
        Assert.AreEqual(8, _viewModel.AmbientLevel);
        Assert.AreEqual(0, _headset.Commands.Count);
    }

    [TestMethod]
    public void SceneSendsOneCommand()
    {
        _viewModel.ApplySceneCommand.Execute(new Scene("Aware", "\uE805", new NoiseControlSetting(NoiseMode.Ambient, 20, false)));

        Assert.AreEqual(new NoiseControlSetting(NoiseMode.Ambient, 20, false), _headset.Commands.Single());
        Assert.AreEqual(20, _viewModel.AmbientLevel);
        Assert.IsFalse(_viewModel.FocusOnVoice);
    }

    [TestMethod]
    public void NoSceneIsActiveWhenNoneMatches()
    {
        // Headset starts in ambient 8 with voice on; defaults are Focus (NC), Office (ambient 10, voice), Aware (ambient 20).
        Assert.IsFalse(_viewModel.SceneItems.Any(item => item.IsActive));
    }

    [TestMethod]
    public void AppliedSceneBecomesActive()
    {
        var aware = _viewModel.SceneItems.Single(item => item.Name == "Aware");

        _viewModel.ApplySceneCommand.Execute(aware.Scene);

        Assert.IsTrue(aware.IsActive);
        Assert.AreEqual(1, _viewModel.SceneItems.Count(item => item.IsActive));
    }

    [TestMethod]
    public void SceneFollowsManualChanges()
    {
        _viewModel.SetNoiseModeCommand.Execute("NoiseCancelling");
        Assert.IsTrue(_viewModel.SceneItems.Single(item => item.Name == "Focus").IsActive);

        _viewModel.SetNoiseModeCommand.Execute("Ambient");
        _time.Advance(HeadsetViewModel.SliderInterval);
        _viewModel.AmbientLevel = 10;
        Assert.IsTrue(_viewModel.SceneItems.Single(item => item.Name == "Office").IsActive);

        _viewModel.FocusOnVoice = false;
        Assert.IsFalse(_viewModel.SceneItems.Any(item => item.IsActive));
    }

    [TestMethod]
    public void HeadsetNotificationUpdatesActiveScene()
    {
        _headset.RaiseStateChanged(_headset.State with { NoiseControl = new NoiseControlSetting(NoiseMode.Ambient, 20, false) });

        Assert.IsTrue(_viewModel.SceneItems.Single(item => item.Name == "Aware").IsActive);
    }

    [TestMethod]
    public void EqualizerPickSendsPresetValue()
    {
        _viewModel.SelectedEqualizerIndex = 2;

        Assert.AreEqual(0x16, _headset.Commands.Single());
    }

    [TestMethod]
    public async Task CustomEqualizerSendsManualCurve()
    {
        _viewModel.ClearBass = 3;
        _viewModel.Band1 = -10;
        _viewModel.Band5 = 10;

        await _viewModel.ApplyCustomEqualizerCommand.ExecuteAsync(null);

        var sent = (EqualizerSetting)_headset.Commands.Single();
        Assert.AreEqual(EqualizerSetting.ManualPreset, sent.Preset);
        Assert.AreEqual(3, sent.ClearBass);
        CollectionAssert.AreEqual(new[] { -10, 0, 0, 0, 10 }, sent.Bands.ToArray());
    }

    [TestMethod]
    public void SystemSettingsSendCommands()
    {
        _viewModel.DseeIndex = 1;
        _viewModel.SpeakToChat = true;
        _viewModel.AdaptiveVolume = true;
        _viewModel.AutoPowerOffIndex = 5;

        CollectionAssert.AreEqual(
            new object[] { ("dsee", true), ("speakToChat", true), ("adaptiveVolume", true), ("autoPowerOff", 5) },
            _headset.Commands);
    }

    [TestMethod]
    public void ConnectionStateChangesStatusText()
    {
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Connecting);
        Assert.AreEqual("Connecting\u2026", _viewModel.StatusText);
        Assert.IsTrue(_viewModel.IsConnecting);
        Assert.IsFalse(_viewModel.IsConnected);

        // Windows still has the headphones; only the app let go
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);
        Assert.AreEqual("App Disconnected", _viewModel.StatusText);
        Assert.AreEqual("App Disconnected", _viewModel.DisconnectedMessage);

        _managed.IsWindowsConnected = false;
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);
        Assert.AreEqual("Disconnected", _viewModel.StatusText);
        Assert.AreEqual("Device is disconnected.", _viewModel.DisconnectedMessage);
    }

    [TestMethod]
    public void DisconnectedHeadsetShowsWarningAndReconnect()
    {
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);

        Assert.IsFalse(_viewModel.IsConnected);
        Assert.IsTrue(_viewModel.ShowDisconnectedWarning);
        Assert.IsTrue(_viewModel.ShowReconnect);
    }

    [TestMethod]
    public void HeadsetOffWindowsHidesReconnectAndBattery()
    {
        // Nothing to reconnect to until Windows has the headset again
        _managed.IsWindowsConnected = false;
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);

        Assert.IsTrue(_viewModel.ShowDisconnectedWarning);
        Assert.IsFalse(_viewModel.ShowReconnect);
        Assert.IsFalse(_viewModel.ShowBattery);
    }

    [TestMethod]
    public void HeadsetOnWindowsShowsBattery()
    {
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);

        Assert.IsTrue(_viewModel.ShowBattery);
    }

    [TestMethod]
    public void ConnectedHeadsetHidesWarningAndOffersDisconnect()
    {
        Assert.IsTrue(_viewModel.IsConnected);
        Assert.IsFalse(_viewModel.ShowDisconnectedWarning);
        Assert.IsFalse(_viewModel.ShowReconnect);
    }

    [TestMethod]
    public void ConnectingHidesWarningAndReconnect()
    {
        _viewModel.UpdateConnectionState(HeadsetConnectionState.Connecting);

        Assert.IsFalse(_viewModel.ShowDisconnectedWarning);
        Assert.IsFalse(_viewModel.ShowReconnect);
    }

    [TestMethod]
    public void DisconnectReleasesTheHeadset()
    {
        var raised = false;
        _viewModel.AutoConnectChanged += (_, _) => raised = true;

        _viewModel.DisconnectCommand.Execute(null);

        Assert.IsTrue(raised);
        Assert.IsTrue(_viewModel.IsReleased);
        Assert.IsFalse(_settings.IsAutoConnectEnabled("AC:80:0A:00:00:06"));
    }

    [TestMethod]
    public void ReconnectAsksForAReconnect()
    {
        var raised = false;
        _viewModel.ReconnectRequested += (_, _) => raised = true;

        _viewModel.ReconnectCommand.Execute(null);

        Assert.IsTrue(raised);
    }

    [TestMethod]
    public void TurningAutoConnectOffSavesAndRaisesEvent()
    {
        var raised = false;
        _viewModel.AutoConnectChanged += (_, _) => raised = true;

        _viewModel.AutoConnect = false;

        Assert.IsTrue(raised);
        Assert.IsFalse(_settings.IsAutoConnectEnabled("AC:80:0A:00:00:06"));
    }

    [TestMethod]
    public void LowBatteryNotifiesOnce()
    {
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(15, 15, 40, 90, false) });
        _headset.RaiseStateChanged(_headset.State with { Battery = new BatteryLevels(14, 14, 40, 90, false) });

        Assert.AreEqual(("WF-1000XM6", 15), _notifications.Shown.Single());
        Assert.AreEqual("AC:80:0A:00:00:06", _notifications.HeadsetIds.Single()); // a click opens this headset
    }
}

[TestClass]
public sealed class LowBatteryMonitorTests
{
    private readonly FakeNotificationService _notifications = new();
    private readonly AppSettings _settings = new(new InMemorySettingsStore());

    [TestMethod]
    public void RearmsAfterRecovering()
    {
        var monitor = new LowBatteryMonitor(_settings, _notifications);

        monitor.Update("A", "XM4", new BatteryLevels(19, null, null, null, false));
        monitor.Update("A", "XM4", new BatteryLevels(25, null, null, null, false));
        monitor.Update("A", "XM4", new BatteryLevels(18, null, null, null, false));

        Assert.AreEqual(2, _notifications.Shown.Count);
    }

    [TestMethod]
    public void StaysQuietWhileCharging()
    {
        var monitor = new LowBatteryMonitor(_settings, _notifications);

        monitor.Update("A", "XM4", new BatteryLevels(10, null, null, null, true));

        Assert.AreEqual(0, _notifications.Shown.Count);
    }

    [TestMethod]
    public void StaysQuietWhenTurnedOff()
    {
        _settings.LowBatteryNotifications = false;
        var monitor = new LowBatteryMonitor(_settings, _notifications);

        monitor.Update("A", "XM4", new BatteryLevels(10, null, null, null, false));

        Assert.AreEqual(0, _notifications.Shown.Count);
    }
}

[TestClass]
public sealed class FlyoutViewModelTests
{
    private static readonly BluetoothDeviceInfo Xm6 = new("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true);
    private static readonly BluetoothDeviceInfo Xm4 = new("device-xm4", "WH-1000XM4", "ac:80:0a:00:00:04", true);
    private static readonly BluetoothDeviceInfo Xb900n = new("device-xb900n", "WH-XB900N", "ac:80:0a:00:09:00", true);

    private readonly FakeDeviceSource _source = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AppSettings _settings = new(new InMemorySettingsStore());
    private readonly FakeBluetoothAudio _audio = new();
    private readonly Dictionary<string, FakeHeadset> _fakes = [];
    private HeadsetManager _manager = null!;
    private FlyoutViewModel _flyout = null!;

    [TestInitialize]
    public void CreateFlyout()
    {
        _manager = new HeadsetManager(
            _source,
            name => _fakes[name] = name == "WH-XB900N"
                ? new FakeHeadset(name, FakeHeadset.Xb900nFeatures)
                : new FakeHeadset(name),
            _settings,
            _time,
            NullLogger<HeadsetManager>.Instance);
        var monitor = new LowBatteryMonitor(_settings, new FakeNotificationService());
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            managed => new HeadsetViewModel(managed, _settings, monitor, _time, NullLogger.Instance),
            _audio,
            _time);
        _manager.Start();
    }

    [TestCleanup]
    public void DisposeFlyout()
    {
        _flyout.Dispose();
        _manager.Dispose();
    }

    // =========================================================================
    // CONNECT (BLUETOOTH AUDIO)
    // =========================================================================

    [TestMethod]
    public void ConnectOffersOnlyWhileWindowsIsDisconnected()
    {
        _source.Report(Xm6);
        _source.Report(Xm4 with { IsConnected = false });

        Assert.IsFalse(Headset("WF-1000XM6").ShowConnect);
        Assert.IsTrue(Headset("WH-1000XM4").ShowConnect);
    }

    [TestMethod]
    public void ConnectAsksWindowsAndShowsConnecting()
    {
        _source.Report(Xm4 with { IsConnected = false });
        var xm4 = Headset("WH-1000XM4");

        xm4.ConnectCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "AC:80:0A:00:00:04" }, _audio.Requests);
        Assert.IsTrue(xm4.IsBluetoothConnecting);
        Assert.AreEqual("Connecting…", xm4.StatusText);
        Assert.IsFalse(xm4.ShowConnect);
    }

    [TestMethod]
    public async Task ConnectFinishesOnceTheControlLinkIsOpen()
    {
        _source.Report(Xm4 with { IsConnected = false });
        var xm4 = Headset("WH-1000XM4");
        xm4.ConnectCommand.Execute(null);

        _source.Report(Xm4);

        Assert.IsTrue(await TestWait.UntilAsync(() => !xm4.IsBluetoothConnecting));
        Assert.IsTrue(xm4.IsConnected);
        Assert.IsNull(xm4.ErrorMessage);
    }

    [TestMethod]
    public void WindowsDroppingMidConnectRetriesOnce()
    {
        _source.Report(Xm4 with { IsConnected = false });
        var xm4 = Headset("WH-1000XM4");
        // The control link can't open while the headset keeps dropping off Windows
        _fakes["WH-1000XM4"].ConnectFailures.Enqueue(new COMException("gone", HeadsetErrorMessages.DisconnectedHResult));
        _fakes["WH-1000XM4"].ConnectFailures.Enqueue(new COMException("gone", HeadsetErrorMessages.DisconnectedHResult));
        xm4.ConnectCommand.Execute(null);

        _source.Report(Xm4);
        _source.Report(Xm4 with { IsConnected = false });
        _source.Report(Xm4);
        _source.Report(Xm4 with { IsConnected = false });

        Assert.AreEqual(2, _audio.Requests.Count);
        Assert.IsTrue(xm4.IsBluetoothConnecting); // still counting down to the give-up
    }

    [TestMethod]
    public void RefusedConnectSaysSo()
    {
        _source.Report(Xm4 with { IsConnected = false });
        var xm4 = Headset("WH-1000XM4");
        _audio.Accept = false;

        xm4.ConnectCommand.Execute(null);

        Assert.IsFalse(xm4.IsBluetoothConnecting);
        Assert.AreEqual("Couldn't connect. Make sure your headphones are on and nearby.", xm4.ErrorMessage);
        Assert.AreEqual(xm4.ErrorMessage, xm4.PickerStatusText); // the list row says so too
    }

    [TestMethod]
    public void PickerRowShowsStatusWithoutAnError()
    {
        _source.Report(Xm4 with { IsConnected = false });

        Assert.AreEqual("Disconnected", Headset("WH-1000XM4").PickerStatusText);
    }

    [TestMethod]
    public void ConnectThatNeverHappensGivesUp()
    {
        _source.Report(Xm4 with { IsConnected = false });
        var xm4 = Headset("WH-1000XM4");
        xm4.ConnectCommand.Execute(null);

        _time.Advance(HeadsetViewModel.BluetoothConnectTimeout);

        Assert.IsFalse(xm4.IsBluetoothConnecting);
        Assert.AreEqual("Couldn't connect. Make sure your headphones are on and nearby.", xm4.ErrorMessage);
    }

    [TestMethod]
    public void ConnectTakesBackAReleasedHeadset()
    {
        _source.Report(Xm4 with { IsConnected = false });
        var xm4 = Headset("WH-1000XM4");
        xm4.DisconnectCommand.Execute(null);

        xm4.ConnectCommand.Execute(null);

        Assert.IsFalse(xm4.IsReleased);
    }

    [TestMethod]
    public async Task WhXb900nAacModePerformsFullWindowsReconnect()
    {
        _source.Report(Xb900n);
        var xb900n = Headset("WH-XB900N");
        Assert.IsTrue(await TestWait.UntilAsync(() => xb900n.IsConnected));

        xb900n.ConnectionQualityIndex = 1;

        Assert.IsTrue(await TestWait.UntilAsync(() => _audio.DisconnectRequests.Count == 1));
        Assert.AreEqual("AC:80:0A:00:09:00", _audio.DisconnectRequests[0]);

        _source.Report(Xb900n with { IsConnected = false });

        Assert.IsTrue(await TestWait.UntilAsync(() =>
        {
            _time.Advance(TimeSpan.FromMilliseconds(100));
            return _audio.Requests.Count == 1;
        }));

        Assert.AreEqual(1, _audio.Requests.Count);
        Assert.AreEqual("AC:80:0A:00:09:00", _audio.Requests[0]);
    }

    [TestMethod]
    public void NotificationClickOpensThatHeadset()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);

        _flyout.ShowHeadset("AC:80:0A:00:00:04");

        Assert.IsTrue(_flyout.IsDeviceVisible);
        Assert.AreEqual("WH-1000XM4", _flyout.CurrentHeadset?.DeviceName);
    }

    [TestMethod]
    public void NotificationForAnUnknownHeadsetChangesNothing()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);

        _flyout.ShowHeadset("11:22:33:44:55:66");

        Assert.IsTrue(_flyout.IsPickerVisible);
    }

    private HeadsetViewModel Headset(string name) => _flyout.Headsets.Single(headset => headset.DeviceName == name);

    [TestMethod]
    public void StartsOnEmptyPage()
    {
        Assert.IsTrue(_flyout.IsEmptyVisible);
        Assert.IsNull(_flyout.CurrentHeadset);
    }

    [TestMethod]
    public void OneHeadsetOpensItsPageWithBackToThePicker()
    {
        _source.Report(Xm6);

        Assert.IsTrue(_flyout.IsDeviceVisible);
        Assert.IsTrue(_flyout.ShowBack);
        Assert.AreEqual("WF-1000XM6", _flyout.CurrentHeadset?.DeviceName);
    }

    [TestMethod]
    public void BackShowsThePickerEvenWithOneHeadset()
    {
        _source.Report(Xm6);

        _flyout.BackCommand.Execute(null);

        Assert.IsTrue(_flyout.IsPickerVisible);
        Assert.AreEqual(1, _flyout.Headsets.Count);
    }

    [TestMethod]
    public void HeadsetWindowsDisconnectedStaysListedAsDisconnected()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);

        _source.Report(Xm4 with { IsConnected = false });

        Assert.AreEqual(2, _flyout.Headsets.Count);
        var xm4 = _flyout.Headsets.Single(headset => headset.DeviceName == "WH-1000XM4");
        Assert.IsFalse(xm4.IsWindowsConnected);
        Assert.AreEqual("Disconnected", xm4.StatusText);
    }

    [TestMethod]
    public async Task TrayTooltipListsOnlyConnectedHeadsets()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);
        Assert.IsTrue(await TestWait.UntilAsync(() => _flyout.ConnectedHeadsets.Count == 2));

        _source.Report(Xm4 with { IsConnected = false });

        Assert.AreEqual("WF-1000XM6", _flyout.ConnectedHeadsets.Single().DeviceName);
    }

    [TestMethod]
    public void PickingADisconnectedHeadsetShowsItsPage()
    {
        _source.Report(Xm6);
        _source.Report(Xm4 with { IsConnected = false });
        _flyout.BackCommand.Execute(null);

        _flyout.PickCommand.Execute(_flyout.Headsets.Single(headset => headset.DeviceName == "WH-1000XM4"));

        Assert.IsTrue(_flyout.IsDeviceVisible);
        Assert.AreEqual("WH-1000XM4", _flyout.CurrentHeadset?.DeviceName);
        Assert.IsTrue(_flyout.CurrentHeadset?.ShowDisconnectedWarning);
    }

    [TestMethod]
    public async Task DisconnectThenReconnectFromTheFlyout()
    {
        _source.Report(Xm6);
        var headset = _flyout.CurrentHeadset!;
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        headset.DisconnectCommand.Execute(null);
        Assert.IsTrue(await TestWait.UntilAsync(() => !headset.IsConnected && headset.ShowReconnect));

        headset.ReconnectCommand.Execute(null);
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.IsConnected));
        Assert.IsFalse(headset.IsReleased);
    }

    [TestMethod]
    public void SecondHeadsetShowsPicker()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);

        Assert.IsTrue(_flyout.IsPickerVisible);
        Assert.AreEqual(2, _flyout.Headsets.Count);
    }

    [TestMethod]
    public void PickThenBack()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);

        _flyout.PickCommand.Execute(_flyout.Headsets[1]);
        Assert.IsTrue(_flyout.IsDeviceVisible);
        Assert.IsTrue(_flyout.ShowBack);
        Assert.AreEqual("WH-1000XM4", _flyout.CurrentHeadset?.DeviceName);

        _flyout.BackCommand.Execute(null);
        Assert.IsTrue(_flyout.IsPickerVisible);
    }

    [TestMethod]
    public void KnowsWhetherADefaultHeadsetIsSet()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);
        Assert.IsFalse(_flyout.HasDefaultHeadset);

        _flyout.PickCommand.Execute(_flyout.Headsets[0]);
        Assert.IsTrue(_flyout.HasDefaultHeadset);

        _flyout.BackCommand.Execute(null);
        Assert.IsFalse(_flyout.HasDefaultHeadset);
    }

    [TestMethod]
    public void RememberedHeadsetLeavingAndReturning()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);
        _flyout.PickCommand.Execute(_flyout.Headsets[0]);

        _source.Report(Xm6 with { IsConnected = false });
        Assert.AreEqual("WH-1000XM4", _flyout.CurrentHeadset?.DeviceName);
        Assert.IsTrue(_flyout.ShowBack);

        _source.Report(Xm6);
        Assert.AreEqual("WF-1000XM6", _flyout.CurrentHeadset?.DeviceName);
        Assert.IsTrue(_flyout.ShowBack);
    }

    [TestMethod]
    public void SettingsAndQuitRaiseEvents()
    {
        var settings = 0;
        var quit = 0;
        _flyout.SettingsRequested += (_, _) => settings++;
        _flyout.QuitRequested += (_, _) => quit++;

        _flyout.OpenSettingsCommand.Execute(null);
        _flyout.QuitCommand.Execute(null);

        Assert.AreEqual(1, settings);
        Assert.AreEqual(1, quit);
    }
}

[TestClass]
public sealed class SettingsViewModelTests
{
    private readonly FakeDeviceSource _source = new();
    private readonly AppSettings _settings = new(new InMemorySettingsStore());
    private readonly FakeStartupTaskService _startup = new();
    private readonly LogLevelSwitch _logLevel = new();
    private readonly List<AppTheme> _themes = [];
    private readonly List<bool> _nativeDebug = [];
    private HeadsetManager _manager = null!;
    private FlyoutViewModel _flyout = null!;
    private SettingsViewModel _viewModel = null!;

    [TestInitialize]
    public void CreateViewModel()
    {
        var time = new FakeTimeProvider();
        _manager = new HeadsetManager(
            _source,
            name => new FakeHeadset(name, name == "WH-1000XM4" ? FakeHeadset.Xm4Features : null),
            _settings,
            time,
            NullLogger<HeadsetManager>.Instance);
        var monitor = new LowBatteryMonitor(_settings, new FakeNotificationService());
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            managed => new HeadsetViewModel(managed, _settings, monitor, time, NullLogger.Instance),
            new FakeBluetoothAudio(),
            time);
        _viewModel = new SettingsViewModel(_flyout, _settings, _logLevel, _startup, _themes.Add, _nativeDebug.Add, _ => { }, "C:\\Logs", NullLogger.Instance);
        _manager.Start();
    }

    [TestCleanup]
    public void Dispose()
    {
        _flyout.Dispose();
        _manager.Dispose();
    }

    [TestMethod]
    public void SelectsFirstHeadsetWhenOneConnects()
    {
        Assert.IsTrue(_viewModel.NoHeadset);

        _source.Report(new BluetoothDeviceInfo("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true));

        Assert.AreEqual(0, _viewModel.SelectedHeadsetIndex);
        Assert.AreEqual("WF-1000XM6", _viewModel.SelectedHeadset?.DeviceName);
    }

    [TestMethod]
    public void OpeningSettingsSelectsTheHeadsetShownInTheFlyout()
    {
        _source.Report(new BluetoothDeviceInfo("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true));
        _source.Report(new BluetoothDeviceInfo("device-xm4", "WH-1000XM4", "ac:80:0a:00:00:04", true));
        _flyout.PickCommand.Execute(_flyout.Headsets.Single(headset => headset.DeviceName == "WH-1000XM4"));

        _viewModel.SelectCurrentHeadset();

        Assert.AreEqual("WH-1000XM4", _viewModel.SelectedHeadset?.DeviceName);
    }

    [TestMethod]
    public void OpenSettingsKeepTheirHeadsetWhenTheFlyoutSwitches()
    {
        _source.Report(new BluetoothDeviceInfo("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true));
        _source.Report(new BluetoothDeviceInfo("device-xm4", "WH-1000XM4", "ac:80:0a:00:00:04", true));
        _viewModel.SelectedHeadsetIndex = _flyout.Headsets.IndexOf(_flyout.Headsets.Single(headset => headset.DeviceName == "WF-1000XM6"));

        _flyout.PickCommand.Execute(_flyout.Headsets.Single(headset => headset.DeviceName == "WH-1000XM4"));

        Assert.AreEqual("WF-1000XM6", _viewModel.SelectedHeadset?.DeviceName);
    }

    [TestMethod]
    public void SystemPageSaysSoWhenAHeadsetHasNoSystemOptions()
    {
        _source.Report(new BluetoothDeviceInfo("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true));
        _source.Report(new BluetoothDeviceInfo("device-xm4", "WH-1000XM4", "ac:80:0a:00:00:04", true));
        Assert.IsFalse(_viewModel.NoSystemOptions);

        _viewModel.SelectedHeadsetIndex = _flyout.Headsets.IndexOf(_flyout.Headsets.Single(headset => headset.DeviceName == "WH-1000XM4"));

        Assert.IsTrue(_viewModel.NoSystemOptions);
    }

    [TestMethod]
    public void SelectionChangesOnlyAfterEveryListenerSawTheNewHeadset()
    {
        // A ComboBox bound to Headsets subscribes after the view model. Setting
        // SelectedIndex before it has the new item throws ArgumentException in WinUI.
        var previous = SynchronizationContext.Current;
        var context = new QueueingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var time = new FakeTimeProvider();
            var settings = new AppSettings(new InMemorySettingsStore());
            var source = new FakeDeviceSource();
            using var manager = new HeadsetManager(source, name => new FakeHeadset(name), settings, time, NullLogger<HeadsetManager>.Instance);
            var monitor = new LowBatteryMonitor(settings, new FakeNotificationService());
            using var flyout = new FlyoutViewModel(
                manager,
                new FlyoutNavigator(settings),
                managed => new HeadsetViewModel(managed, settings, monitor, time, NullLogger.Instance),
                new FakeBluetoothAudio(),
                time);
            var viewModel = new SettingsViewModel(flyout, settings, new LogLevelSwitch(), new FakeStartupTaskService(), _ => { }, _ => { }, _ => { }, "C:\\Logs", NullLogger.Instance);
            manager.Start();

            var controlItems = 0;
            flyout.Headsets.CollectionChanged += (_, _) => controlItems = flyout.Headsets.Count;
            var itemsWhenSelected = -1;
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.SelectedHeadsetIndex))
                {
                    itemsWhenSelected = controlItems;
                }
            };

            source.Report(new BluetoothDeviceInfo("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true));
            context.RunPending();

            Assert.AreEqual(0, viewModel.SelectedHeadsetIndex);
            Assert.AreEqual(1, itemsWhenSelected, "SelectedHeadsetIndex changed before the list control had the headset");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [TestMethod]
    public void ThemeChoiceIsSavedAndApplied()
    {
        _viewModel.ThemeIndex = (int)AppTheme.Dark;

        Assert.AreEqual(AppTheme.Dark, _settings.Theme);
        CollectionAssert.AreEqual(new[] { AppTheme.Dark }, _themes);
    }

    [TestMethod]
    public void DebugLoggingSwitchesBothLoggers()
    {
        _viewModel.DebugLogging = true;

        Assert.AreEqual(Microsoft.Extensions.Logging.LogLevel.Debug, _logLevel.MinimumLevel);
        CollectionAssert.AreEqual(new[] { true }, _nativeDebug);
    }

    [TestMethod]
    public async Task LaunchAtSignInReflectsWhatWindowsAllowed()
    {
        _startup.RefuseEnable = true;
        await _viewModel.LoadAsync();

        _viewModel.LaunchAtSignIn = true;

        Assert.IsTrue(await TestWait.UntilAsync(() => !_viewModel.LaunchAtSignIn));
    }

    [TestMethod]
    public void SavingScenesStoresEdits()
    {
        _viewModel.Scenes[0].Name = "Deep work";
        _viewModel.Scenes[0].ModeIndex = (int)NoiseMode.Ambient;
        _viewModel.Scenes[0].AmbientLevel = 5;

        _viewModel.SaveScenesCommand.Execute(null);

        Assert.AreEqual(new Scene("Deep work", "\uE708", new NoiseControlSetting(NoiseMode.Ambient, 5, false)), _settings.Scenes[0]);
    }

    [TestMethod]
    public void SceneEditorShowsItsAmbientLevel()
    {
        var changed = new List<string?>();
        _viewModel.Scenes[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _viewModel.Scenes[0].AmbientLevel = 7.4;

        Assert.AreEqual("7", _viewModel.Scenes[0].AmbientLevelText);
        CollectionAssert.Contains(changed, nameof(SceneEditorViewModel.AmbientLevelText));
    }

    [TestMethod]
    public void ResettingScenesRestoresDefaults()
    {
        _viewModel.Scenes[0].Name = "Changed";
        _viewModel.SaveScenesCommand.Execute(null);

        _viewModel.ResetScenesCommand.Execute(null);

        CollectionAssert.AreEqual(Scene.Defaults.ToList(), _settings.Scenes.ToList());
    }
}
