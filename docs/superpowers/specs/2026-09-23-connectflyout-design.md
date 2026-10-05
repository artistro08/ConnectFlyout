# ConnectFlyout: Design Spec

A native Windows 11 tray app that controls Sony headphones through a WinUI 3 flyout and a WinUI 3 settings window, packaged as an installable MSIX.

## Goals

- Control the WF-1000XM6 and WH-1000XM4 from the Windows tray.
- Feel like a first-party Windows 11 flyout (Quick Settings / battery flyout style), not a port of the macOS menu bar app.
- Ship as an installable MSIX built on the Windows App SDK.
- First build must work end to end with the WF-1000XM6.
- Tests for all functionality, logging where it helps diagnose real problems.

## Non-Goals

- Microsoft Store listing (sideloaded MSIX with a self-signed cert).
- Linux or macOS support.
- Sony product photos (copyrighted; Fluent icons used instead).
- Experimental WH-1000XM4 features beyond what upstream supports.

## Standards

- C#: Microsoft .NET coding conventions, .NET analyzers at `latest-recommended`, nullable enabled, warnings as errors.
- C++: C++ Core Guidelines, C++20, MSVC `/W4 /WX /permissive- /analyze`, C++/WinRT conventions.
- `.editorconfig` at the repo root enforces formatting for both languages.
- US English everywhere.

## Upstream

Base: [marconvcm/sony-device-center](https://github.com/marconvcm/sony-device-center) (MIT).

`libs/sony-protocol` and `libs/sony-transport` are copied into `native/` and owned by this repo from then on. Linux and macOS code is removed. The upstream MIT license is kept in `LICENSE-THIRD-PARTY`. The Qt UI, CLI (`sonyctl`), and daemon (`sonyd`) are not used.

## Architecture

### Solution Layout

```
ConnectFlyout.sln
├─ native/
│  ├─ SonyProtocol/                    C++20 static lib (upstream copy)
│  ├─ SonyTransport/                   C++20 static lib (upstream copy, Windows only)
│  ├─ ConnectFlyout.Core/                C++/WinRT Windows Runtime Component
│  └─ ConnectFlyout.Native.Tests/        GoogleTest
├─ src/
│  ├─ ConnectFlyout.Core.Projection/     CsWinRT projection of ConnectFlyout.Core
│  ├─ ConnectFlyout.Presentation/        C# class library
│  ├─ ConnectFlyout.App/                 WinUI 3 app, single-project MSIX
│  └─ ConnectFlyout.Presentation.Tests/  MSTest
├─ scripts/
│  ├─ Build.ps1                        Builds the solution and runs every test suite
│  ├─ Build-Package.ps1                Builds the signed x64 and ARM64 MSIX packages
│  ├─ New-AppAssets.ps1                Draws the package logos and tray icons
│  └─ New-DevCertificate.ps1           Creates and trusts the self-signed signing cert
├─ .editorconfig
└─ LICENSE-THIRD-PARTY
```

### Stack

- Visual Studio 2026, .NET 10 (LTS), C++20, Windows App SDK 1.8 (self-contained), Windows SDK 10.0.26100.
- CommunityToolkit.Mvvm 8.4 for view models (`ObservableObject`, relay commands; no source-generated properties).
- Hand-written P/Invoke declarations in one `NativeMethods` class for Win32 (tray icon, monitor info, DPI, window corners).
- C++/WinRT 2.0.250303.1 for the component, CsWinRT 2.3.1 for its C# projection.
- GoogleTest 1.8.1 (Microsoft.googletest NuGet) for C++ tests, MSTest 3.11 for C# tests.
- Microsoft.Extensions.Logging with a small in-repo rolling file provider and source-generated `[LoggerMessage]` methods.

### Responsibilities

| Unit | Owns | Depends on |
|---|---|---|
| `SonyProtocol` | Frame codec, escaping, checksum, ack/sequence session, V1/V2 command tables, device profile registry | Nothing |
| `SonyTransport` | Bluetooth RFCOMM link via Winsock `AF_BTH`, `ITransport` interface so tests can supply a fake headset | Winsock |
| `ConnectFlyout.Core` | `HeadsetClient` runtime class: `ConnectAsync`, `Disconnect`, a `Set...Async` per feature, `RefreshBatteryAsync`, a `State` snapshot, events `StateChanged` and `Disconnected`, and static `SetLogHandler` / `SetDebugLogging` / `GetEqualizerPresets`. Converts every C++ exception into a failed async result; no exception crosses the ABI. Activated without registration (CsWinRT loads `ConnectFlyout.Core.dll` from the app folder). | `SonyProtocol`, `SonyTransport` |
| `ConnectFlyout.Presentation` | View models, device discovery (`DeviceWatcher` over paired, connected Bluetooth devices), picker/remembered-device rules, scenes, settings storage (`ApplicationData.Current.LocalSettings`), flyout placement math, logging | Projection, CommunityToolkit.Mvvm |
| `ConnectFlyout.App` | Tray icon (`Shell_NotifyIcon`), flyout window, settings window, MSIX manifest, startup task | Presentation |

A headset's identity everywhere in the app (remembered choice, auto-connect setting) is its upper-case Bluetooth address.

## Device Support

| Model | Protocol | Features |
|---|---|---|
| WF-1000XM6 | V2 | Battery (left, right, case), noise control (Off / ANC / Ambient), ambient level 1–20, Focus on Voice, EQ preset and custom 5-band + Clear Bass, DSEE, Speak-to-Chat, adaptive volume, auto power-off, firmware, codec |
| WH-1000XM4 | V1 | Battery (single), noise control, ambient level, Focus on Voice, EQ preset and custom 5-band + Clear Bass, firmware, codec |
| Unknown Sony model | V2, falls back to V1 if it doesn't answer the V2 noise control query | Battery and noise control only, labeled "Unverified model" |

Model selection is by Bluetooth device name through the profile registry. The RFCOMM link tries the V1 service UUID `96CC203E-5068-46ad-B32D-E316F5E069BA` first, then the V2 UUID `956C7B26-D49A-4BA8-B03F-B17D393CB6E2`, the same order upstream uses. Unknown models are never probed with the V2 battery opcode, because `0x22` powers a V1 headset off.

## Flyout

### Frame

- Fixed size on every page: 360 × 640 effective pixels (sized to the XM6 device page), capped to the monitor work area minus margins. Pages with less content leave empty space below, like the Windows battery flyout.
- 8px corner radius, 1px border, shadow.
- Body uses a `DesktopAcrylicBackdrop`. Footer is a darker layer strip, like the "Power report" row in the Windows battery flyout.
- System font, Segoe Fluent Icons, system accent color. Follows system light/dark theme.
- Slide-up + fade entrance (slide-down when the taskbar is on top).
- Closes on deactivation or Esc.
- Content between header and footer scrolls if it overflows. Footer stays pinned.
- Full keyboard navigation. Every control has an automation name.

### Placement

- The monitor comes from `Shell_NotifyIconGetRect` (cursor position as a fallback). The taskbar edge is the side where that monitor's work area is smaller than its bounds; an auto-hidden taskbar counts as bottom.
- Always right-aligned to that monitor's work area, 12px margin from the taskbar and the screen edge.
- Bottom taskbar: bottom-right. Top taskbar: top-right. Left or right taskbar: right side, vertically aligned to the bottom of the work area.
- Clamped inside the work area. DPI-aware per monitor.

### Pages

**Picker page:** shown when 2+ headsets are connected and none is remembered, or when the remembered headset is disconnected and 2+ others are connected. Title "Headphones". One row per headset: headphone/earbud icon, name, battery summary, battery glyph, chevron.

**Device page:**

1. Header: back arrow (only when 2+ headsets are connected), device name, status line ("Connected · LDAC").
2. Battery row: L / R / Case for the XM6, single value for the XM4.
3. Noise control: Off / ANC / Ambient as Quick Settings–style toggle tiles.
4. Ambient sound slider (1–20) with ticks and value label. Visible in Ambient mode.
5. Focus on Voice toggle switch.
6. Scene buttons (defaults: Focus, Office, Aware). A scene is a saved combination of noise mode, ambient level, and Focus on Voice.
7. Equalizer preset combo box.
8. DSEE combo box (XM6 only).
9. Playback: track title and artist, previous / play-pause / next via `GlobalSystemMediaTransportControlsSessionManager`, Windows output volume slider via Core Audio `IAudioEndpointVolume`.

**Footer (both pages):** "Headphone settings" text button on the left. Icon buttons on the right: Reconnect (device page only), Settings, More (menu with Quit).

### Page Rules

1. 0 headsets connected: empty state "No Sony headphones connected" with an "Open Bluetooth settings" link (`ms-settings:bluetooth`).
2. 1 headset connected: go straight to its device page, no back arrow.
3. 2+ connected, none remembered: picker page.
4. Picking a headset opens its device page and remembers it.
5. Remembered headset connected: flyout opens on its device page.
6. Remembered headset disconnected: fall back to the picker, or to the only connected headset if just one is left. The remembered choice is kept.
7. Remembered headset reconnects: flyout opens on its device page again.
8. Back arrow: clears the remembered choice and shows the picker.

### States

- Connecting: progress ring by the status line, controls disabled.
- Command failed: control reverts to the headset's real value, inline `InfoBar` error that dismisses after 5 seconds.

## Settings Window

WinUI 3 window, Mica backdrop, `NavigationView` with left menu:

1. **Devices:** connected Sony headsets, model, firmware, codec, forget (opens Bluetooth settings), auto-connect toggle. With auto-connect off, the headset stays listed but the app only opens its control link when Reconnect is pressed.
2. **Sound:** custom 5-band EQ + Clear Bass, DSEE (XM6).
3. **Noise & scenes:** edit scenes (name, icon, noise mode, ambient level, Focus on Voice), Speak-to-Chat (XM6).
4. **System:** auto power-off (XM6), adaptive volume (XM6).
5. **App:** launch at sign-in (MSIX startup task), low-battery notifications (threshold 20%, app notifications), theme (System / Light / Dark), Debug logging toggle, open log folder.

Controls a connected model doesn't support are hidden.

## Data Flow

1. `DeviceWatcher` reports paired Sony headsets as they connect or disconnect.
2. For each connected headset, Presentation creates one `HeadsetClient` with its Bluetooth address and protocol version.
3. `ConnectAsync` opens RFCOMM (V1 service UUID first, then V2). V2 sends the `00 00` → `01` handshake; like upstream, an unanswered handshake is logged and connecting continues. The client then reads the initial state (battery, noise control, EQ, DSEE, and the rest of the model's features). Battery and noise control are required; optional features that don't answer are logged and skipped.
4. The C++ read loop runs on a background thread. Headset notifications raise events. View models marshal events to the UI thread through the `SynchronizationContext` the Windows App SDK installs on it.
5. User changes send the command immediately. Slider drags are throttled to one command per 150ms, and the final value is always sent.

## Error Handling

| Failure | Behavior | Log level |
|---|---|---|
| No ack within 1s | Retry once, then fail. The control reverts and the error bar shows. | Warning |
| Link drops | Status Disconnected, page rules apply. Auto-reconnect at 1s, 2s, 5s, then every 30s while Windows reports the device as connected. | Information |
| Bad checksum or escaping | Frame dropped, not acked (headset resends). | Debug (raw bytes) |
| V2 handshake unanswered | Logged, connect continues (upstream behavior, known to work on the XM6). | Warning |
| Initial battery or noise control read fails after retry | Connect fails, link closed, reconnect schedule applies. | Warning |
| Unknown model | Try V2, fall back to V1. Reduced features, "Unverified model" label. | Information |
| Bluetooth off or access denied | Message with a link to Bluetooth settings. | Error |
| Unhandled exception | Logged with full details before exit. | Critical |

## Logging

- C#: `Microsoft.Extensions.Logging` with a rolling file provider writing to `ApplicationData.Current.LocalFolder\Logs`, 5 files × 1 MB.
- C++: upstream's `sony::Logger` sink forwards to the handler set with `HeadsetClient.SetLogHandler`, which writes through the C# logger, so there's one log file.
- Levels: Error for failures, Warning for retries, Information for connects and disconnects, Debug for raw frame bytes. Debug is off by default and switched in App settings.

## MSIX

- Single-project MSIX in `ConnectFlyout.App`.
- Platforms: x64, ARM64.
- Minimum OS: Windows 11 22H2 (10.0.22621.0).
- Capabilities: `runFullTrust`, `bluetooth`.
- `windows.startupTask` extension for launch at sign-in.
- Signed with a self-signed cert created and trusted by `scripts/New-DevCertificate.ps1`.
- Self-contained: .NET and the Windows App SDK runtime ship inside the package, and the C++ code links the static CRT, so installing needs no extra frameworks.
- Single instance: a second launch exits right away.

## Testing

### C++ (GoogleTest, `ConnectFlyout.Native.Tests`)

Always-on, no hardware:

1. **Frame codec:** round-trip, escaping of `3C` / `3D` / `3E`, checksum, 4-byte length, rejects bad checksum / bad escaping / truncated frames.
2. **V2 commands:** every Get/Set produces the exact expected bytes. Every reply is parsed into the right values (battery L/R/case, noise control + ambient + voice, EQ preset and custom bands + Clear Bass, DSEE, Speak-to-Chat, adaptive volume, auto power-off, firmware, codec).
3. **Connecting with a fake transport:**
   - V2 handshake is sent first; an unanswered handshake still connects.
   - A headset that never answers fails connect cleanly with a timeout; a refused link fails with a transport error.
   - Ack sequence flips correctly. Duplicate sequence numbers are dropped.
   - Replies match their requests even when a notification arrives first. Unsolicited notifications are delivered.
   - Timeout retries once, then errors and keeps the old state.
   - Link drop during a command returns an error in under 800 ms and reports the drop; a plain disconnect doesn't.
   - Reconnect after a drop succeeds.
   - Initial state read fills every field.
   - Corrupt frames are dropped without an ACK.
4. **Error mapping:** every error code maps to its HRESULT.

### WinRT Boundary and Hardware (MSTest)

The GoogleTest build available as a NuGet package (1.8.1) can't mark a test as skipped, so the tests that cross the WinRT boundary and the hardware tests run from C#, through the same projection the app uses:

- `HeadsetClient` activates without registration, resolves the XM6 profile, and fails with the right HRESULTs (invalid address, command while disconnected).
- Opt-in hardware test (WF-1000XM6), run only when `SONY_TEST_XM6_ADDRESS` is set, otherwise reported as skipped (Inconclusive): connect, read battery, set ANC → read back, set ambient level 8 → read back, restore, disconnect.

### C# (MSTest, `ConnectFlyout.Presentation.Tests`)

1. Page rules 1–8 above.
2. Flyout placement: taskbar bottom / top / left / right, single and multi-monitor, 100% / 150% / 200% scaling. Always right-aligned, always inside the work area.
3. View models: revert on command failure, slider throttle with final value sent, headset events update state.
4. Scenes and settings: save/load round-trip, applying a scene sends the right commands.
5. Logging: rolls at 1 MB, keeps 5 files, Debug toggle works.

### WH-1000XM4 (V1) Tests

Written in the same suites (V1 command bytes and reply parsing, fake-transport connection, and opt-in hardware tests gated by `SONY_TEST_XM4_ADDRESS`), tagged `XM4`:

- GoogleTest: test suite names prefixed `Xm4`, excluded with `--gtest_filter=-Xm4*` for the first build.
- MSTest: `[TestCategory("XM4")]`, excluded with `--filter TestCategory!=XM4` for the first build.

The first build's verification gate is XM6 only. XM4 tests are run once the XM6 build is confirmed working.

### First Build Verification

1. Build x64 and ARM64 with warnings as errors.
2. Run all non-XM4 C++ and C# tests. All pass.
3. Run XM6 hardware tests with `SONY_TEST_XM6_ADDRESS` set.
4. Produce and sign the MSIX, install it, and confirm on the XM6: tray icon appears, flyout opens bottom-right, battery shows, noise control, ambient slider, Focus on Voice, EQ, and DSEE change the headset.
