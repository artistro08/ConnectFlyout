# ConnectFlyout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build ConnectFlyout, a native Windows 11 WinUI 3 tray app (flyout plus settings window, packaged as MSIX) that controls the WF-1000XM6 and WH-1000XM4 over Bluetooth.

**Architecture:** Upstream sony-device-center's protocol and transport code is copied into two C++20 static libraries, wrapped by a new `HeadsetController` and exposed to C# through one C++/WinRT runtime class (`HeadsetClient`). A C# Presentation library holds every view model and rule (device discovery, reconnect schedule, page rules, flyout placement, logging) so it can be unit tested without UI. The WinUI 3 app is a thin shell: tray icon, acrylic flyout, Mica settings window, single-project MSIX.

**Tech Stack:** Visual Studio 2026, C++20 + C++/WinRT 2.0.250303.1, .NET 10, Windows App SDK 1.8.260804001 (self-contained), CsWinRT 2.3.1, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Logging 10.0.12, GoogleTest 1.8.1.8 (NuGet), MSTest 3.11.1.

**Spec:** [docs/superpowers/specs/2026-09-23-ConnectFlyout-design.md](../specs/2026-09-23-ConnectFlyout-design.md)

## Global Constraints

- Repo root: `D:\ConnectFlyout`. Every command runs from the repo root in PowerShell 7.
- Windows 11 22H2+ (`TargetPlatformMinVersion` 10.0.22621.0), Windows SDK 10.0.26100.0, platforms x64 and ARM64. Tests run on x64 only.
- C#: `net10.0-windows10.0.26100.0`, nullable on, `TreatWarningsAsErrors`, `AnalysisLevel` latest-recommended, `EnforceCodeStyleInBuild`. Fix analyzer errors in code; suppress a rule only in a folder `.editorconfig` with a comment saying why (only the test project does this).
- C++: C++20, `/W4 /WX /permissive- /sdl`, `/analyze` on product code, static CRT (`/MT`, `/MTd`). All set once in `native/Directory.Build.targets`.
- The dotnet CLI can't build `.vcxproj` files, so every build goes through Visual Studio's MSBuild. Each build step finds it with this line:
  `$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1`
  `dotnet test --no-build` is used to run the C# tests after that build.
- Package versions are pinned exactly as written. Don't upgrade them while executing the plan.
- Upstream is pinned to commit `dea38969b501a4a167f330dff104414531e80eae` of https://github.com/marconvcm/sony-device-center (MIT).
- US English in code, comments, UI text and docs.
- Don't commit or push. Devin commits when they ask for it. (This replaces the plan template's per-task commit steps.)
- WH-1000XM4 tests are written in every task but excluded from the first-build gate: GoogleTest `--gtest_filter=-Xm4*`, MSTest `--filter "TestCategory!=XM4"`.

## Review Focus

Five situations that bite real use but that plain feature tests miss, and the test that pins each one:

1. **Headphones switched off while Windows still lists them connected.** The app must keep retrying on the 1 s / 2 s / 5 s / 30 s schedule without hanging the UI, and stop once Windows disconnects them. Pinned by `HeadsetManagerTests.FailedConnectRetriesAtOneTwoFiveThenThirtySeconds` and `WindowsDisconnectStopsRetrying` (Task 10).
2. **Dragging the ambient slider fast.** At most one command per 150 ms, the final value always sent, and headset echoes don't make the slider jump back. Pinned by `ThrottlerTests.*` (Task 9) and `HeadsetViewModelTests.AmbientSliderIsThrottledAndSendsFinalValue` (Task 11).
3. **Taskbar on top, on the left, on a second monitor, or at 150 %/200 % scaling.** The flyout stays right-aligned and inside the work area. Pinned by `FlyoutPlacementTests.*` (Task 9).
4. **Pressing a button on the earbuds while the app is sending a command.** The notification must not be taken as the reply, and the reply must still be matched. Pinned by `Xm6Connection.ReplyIsMatchedWhenNotificationArrivesFirst` (Task 4).
5. **Remembered headset gets unpaired or renamed.** The flyout must fall back to the picker, not a blank page. Pinned by `FlyoutNavigatorTests.UnknownRememberedHeadsetShowsPicker` (Task 8).

## Pre-Flight Verification

Every file in this plan was compiled and tested before the plan was written, on the machine this plan targets:

| Checked | Result |
|---|---|
| Upstream protocol + transport sources under `/W4 /WX /permissive- /sdl /analyze` | Clean |
| `SonyTransport`, `SonyProtocol`, `ConnectFlyout.Core` built by MSBuild from these exact `.vcxproj` files (x64, VS 2022 v143 toolset) | Clean, including code analysis |
| `ConnectFlyout.Native.Tests` (compiled with cl.exe, and built by MSBuild from this `.vcxproj` with only the GoogleTest import path shortened for the scratch folder) | 50 tests pass; the 41 non-XM4 tests passed 3 repeated runs |
| `ConnectFlyout.Presentation.Tests` (includes real WinRT activation of `ConnectFlyout.Core.dll`) | 95 pass, 1 skipped (hardware), stable over 5 runs |
| `ConnectFlyout.App` (WinUI 3, all XAML) | 0 warnings, 0 errors |
| MSIX packaging (unsigned) | 85 MB package containing the app, `ConnectFlyout.Core.dll`, assets and the self-contained runtime |

Not verifiable on that machine, so the tasks call these out where they come up:

- **.NET 10 and Visual Studio 2026.** The checks ran on .NET 9 with VS 2022 (the only toolchain installed). The only differences are the `TargetFramework` line and the toolset `$(DefaultPlatformToolset)` resolves to.
- **The C# → C++ `ProjectReference` in `ConnectFlyout.Core.Projection`.** It's Microsoft's documented CsWinRT pattern, but the old VS install had no .NET workload to build it through MSBuild. The checks fed the same `.winmd` in with `<CsWinRTInputs>` instead. The fallback is in Task 7's troubleshooting note.
- **ARM64**, **signing**, **installing**, and the running app with real headphones: Task 13.

---

### Task 0: Toolchain

**Files:** none.

**Interfaces:**
- Produces: Visual Studio 2026 with the C++ desktop, .NET desktop and Windows App SDK workloads, the ARM64 C++ tools, Windows SDK 10.0.26100, and the .NET 10 SDK (installed with the .NET workload).

- [ ] **Step 1: Devin installs Visual Studio 2026 Community with the workloads**

This needs an elevated prompt and Devin's approval, so Devin runs it, not the agent.

````powershell
winget install --id Microsoft.VisualStudio.Community --exact --override "--wait --passive --add Microsoft.VisualStudio.Workload.NativeDesktop --add Microsoft.VisualStudio.Workload.ManagedDesktop --add Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs --add Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cpp --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --includeRecommended"
````

- [ ] **Step 2: Verify the toolchain**

Expected: an 18.x version line, `True` three times, and a `10.0.` SDK line.

````powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
& $vswhere -latest -property catalog_productDisplayVersion
[bool](& $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
[bool](& $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath)
[bool](& $vswhere -latest -requires Microsoft.VisualStudio.Workload.ManagedDesktop -property installationPath)
dotnet --list-sdks | Select-String '^10\.'
````

---

### Task 1: Repository Scaffolding

**Files:**
- Create: `.gitignore`, `.editorconfig`, `nuget.config`, `LICENSE-THIRD-PARTY`, `native/Directory.Build.props`, `native/Directory.Build.targets`, `src/Directory.Build.props`

**Interfaces:**
- Produces: shared build settings every later project inherits (output to `bin/native/<Platform>/<Configuration>/`, compiler flags, C# analyzer settings), and `packages/` as the restore folder for C++ `packages.config` projects.

- [ ] **Step 1: Create `.gitignore`**

````text
.vs/
bin/
obj/
packages/
AppPackages/
BundleArtifacts/
Generated Files/
TestResults/
*.user
*.cer
*.pfx
````

- [ ] **Step 2: Create `.editorconfig`**

````ini
root = true

[*]
charset = utf-8
end_of_line = crlf
indent_style = space
indent_size = 4
insert_final_newline = true
trim_trailing_whitespace = true

[*.{csproj,vcxproj,props,targets,config,appxmanifest,manifest,json,yml,yaml}]
indent_size = 2

[*.{xaml,xml}]
indent_size = 4

# =========================================================================
# C# (Microsoft .NET coding conventions)
# =========================================================================

[*.cs]
csharp_style_namespace_declarations = file_scoped:warning
csharp_prefer_braces = true:warning
csharp_new_line_before_open_brace = all
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
dotnet_sort_system_directives_first = true
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
dotnet_style_qualification_for_method = false:suggestion

# Private fields are _camelCase
dotnet_naming_rule.private_fields_are_underscore_camel.symbols = private_fields
dotnet_naming_rule.private_fields_are_underscore_camel.style = underscore_camel
dotnet_naming_rule.private_fields_are_underscore_camel.severity = suggestion
dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private
dotnet_naming_style.underscore_camel.required_prefix = _
dotnet_naming_style.underscore_camel.capitalization = camel_case

# NativeMethods mirrors Win32 names (WM_APP, NOTIFYICONDATAW) on purpose
[**/NativeMethods.cs]
dotnet_diagnostic.CA1707.severity = none

# =========================================================================
# C++ (C++ Core Guidelines; formatting only, analysis runs in the build)
# =========================================================================

[*.{cpp,h,idl}]
cpp_indent_braces = false
cpp_new_line_before_open_brace_namespace = same_line
cpp_new_line_before_open_brace_function = same_line
cpp_new_line_before_open_brace_block = same_line
````

- [ ] **Step 3: Create `nuget.config`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <!-- packages.config restores for the C++ projects land in the repo's packages folder -->
    <add key="repositoryPath" value="packages" />
  </config>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
````

- [ ] **Step 4: Copy the upstream license verbatim**

Clones the pinned upstream commit once; later tasks copy source files from the same clone.

````powershell
git clone https://github.com/marconvcm/sony-device-center "$env:TEMP\sony-device-center"
git -C "$env:TEMP\sony-device-center" checkout dea38969b501a4a167f330dff104414531e80eae
Copy-Item "$env:TEMP\sony-device-center\LICENSE" .\LICENSE-THIRD-PARTY
````

- [ ] **Step 5: Create `native/Directory.Build.props`**

````xml
<Project>
  <!-- Shared output folders for every native project -->
  <PropertyGroup>
    <OutDir>$(MSBuildThisFileDirectory)..\bin\native\$(Platform)\$(Configuration)\</OutDir>
    <IntDir>$(MSBuildThisFileDirectory)..\obj\native\$(MSBuildProjectName)\$(Platform)\$(Configuration)\</IntDir>
  </PropertyGroup>
</Project>
````

- [ ] **Step 6: Create `native/Directory.Build.targets`**

````xml
<Project>
  <!--
    Compiler settings for every native project, applied after the Visual C++ defaults so they win.
    C++ Core Guidelines-friendly: C++20, /W4 /WX, /permissive-, /sdl and code analysis on our code
    (third-party headers included with angle brackets are treated as external and skipped).
  -->
  <ItemDefinitionGroup>
    <ClCompile>
      <LanguageStandard>stdcpp20</LanguageStandard>
      <WarningLevel>Level4</WarningLevel>
      <TreatWarningAsError>true</TreatWarningAsError>
      <ConformanceMode>true</ConformanceMode>
      <SDLCheck>true</SDLCheck>
      <EnablePREfast Condition="'$(SonyDisableCodeAnalysis)' != 'true'">true</EnablePREfast>
      <TreatAngleIncludeAsExternal>true</TreatAngleIncludeAsExternal>
      <ExternalWarningLevel>TurnOffAllWarnings</ExternalWarningLevel>
      <DisableAnalyzeExternal>true</DisableAnalyzeExternal>
      <MultiProcessorCompilation>true</MultiProcessorCompilation>
      <PrecompiledHeader>NotUsing</PrecompiledHeader>
      <PreprocessorDefinitions>WIN32_LEAN_AND_MEAN;NOMINMAX;%(PreprocessorDefinitions)</PreprocessorDefinitions>
    </ClCompile>
  </ItemDefinitionGroup>

  <!-- Static CRT, so the package needs no VC++ runtime framework -->
  <ItemDefinitionGroup Condition="'$(Configuration)' == 'Debug'">
    <ClCompile>
      <RuntimeLibrary>MultiThreadedDebug</RuntimeLibrary>
    </ClCompile>
  </ItemDefinitionGroup>
  <ItemDefinitionGroup Condition="'$(Configuration)' == 'Release'">
    <ClCompile>
      <RuntimeLibrary>MultiThreaded</RuntimeLibrary>
    </ClCompile>
  </ItemDefinitionGroup>
</Project>
````

- [ ] **Step 7: Create `src/Directory.Build.props`**

````xml
<Project>
  <!-- Shared settings for every C# project -->
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22621.0</TargetPlatformMinVersion>
    <SupportedOSPlatformVersion>10.0.22621.0</SupportedOSPlatformVersion>
    <Platforms>x64;ARM64</Platforms>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <Authors>Devin Green</Authors>
  </PropertyGroup>
</Project>
````

- [ ] **Step 8: Verify**

Expected: seven files listed, and the license starts with `MIT License`.

````powershell
Get-ChildItem .gitignore, .editorconfig, nuget.config, LICENSE-THIRD-PARTY, native\Directory.Build.props, native\Directory.Build.targets, src\Directory.Build.props -Force | Select-Object -ExpandProperty Name
Get-Content .\LICENSE-THIRD-PARTY -TotalCount 1
````

---

### Task 2: Transport Library (Bluetooth Link)

**Files:**
- Copy from upstream: `native/SonyTransport/include/sony/transport/{ITransport,DeviceAddress,SonyError,FakeTransport,IDeviceDiscovery,DiscoveredDevice,Logger}.h`, `native/SonyTransport/src/{FakeTransport,Logger}.cpp`
- Create: `native/SonyTransport/include/sony/transport/BluetoothAddress.h`, `native/SonyTransport/src/BluetoothAddress.cpp`, `native/SonyTransport/include/sony/transport/WindowsRfcommTransport.h`, `native/SonyTransport/src/WindowsRfcommTransport.cpp`, `native/SonyTransport/SonyTransport.vcxproj`
- Test: `native/ConnectFlyout.Native.Tests/BluetoothAddressTests.cpp`, `native/ConnectFlyout.Native.Tests/ConnectFlyout.Native.Tests.vcxproj`, `native/ConnectFlyout.Native.Tests/packages.config`

**Interfaces:**
- Consumes: upstream `sony::transport::ITransport` (`connect(const DeviceAddress&)`, `disconnect() noexcept`, `isConnected() const noexcept`, `send(std::span<const std::byte>)`, `receive(std::span<std::byte>)`), `sony::SonyException(SonyErrorCode, std::string)`, `sony::Logger`.
- Produces: `std::optional<uint64_t> sony::transport::parseBluetoothAddress(std::string_view) noexcept`; `sony::transport::WindowsRfcommTransport final : ITransport` (receive throws `SonyErrorCode::Timeout` on the 2.5 s receive timeout and `Disconnected` when the link closes; connect throws `TransportFailure`).

- [ ] **Step 1: Copy the upstream transport files**

````powershell
$upstream = "$env:TEMP\sony-device-center\libs\sony-transport"
New-Item -ItemType Directory -Force native\SonyTransport\include\sony\transport, native\SonyTransport\src | Out-Null
foreach ($name in 'ITransport', 'DeviceAddress', 'SonyError', 'FakeTransport', 'IDeviceDiscovery', 'DiscoveredDevice', 'Logger') {
    Copy-Item "$upstream\include\sony\transport\$name.h" native\SonyTransport\include\sony\transport\
}
foreach ($name in 'FakeTransport', 'Logger') {
    Copy-Item "$upstream\src\$name.cpp" native\SonyTransport\src\
}
````

- [ ] **Step 2: Write the failing test** (tests project first, with only this test file and the transport reference)

- [ ] **Step 2a: Create `native/ConnectFlyout.Native.Tests/BluetoothAddressTests.cpp`**

````cpp
#include "sony/transport/BluetoothAddress.h"

#include <gtest/gtest.h>

using sony::transport::parseBluetoothAddress;

TEST(BluetoothAddress, ParsesColonSeparated) {
    EXPECT_EQ(parseBluetoothAddress("AA:BB:CC:DD:EE:FF").value_or(0), 0xAABBCCDDEEFFULL);
}

TEST(BluetoothAddress, ParsesLowercaseWithDashes) {
    EXPECT_EQ(parseBluetoothAddress("ac-80-0a-12-34-56").value_or(0), 0xAC800A123456ULL);
}

TEST(BluetoothAddress, ParsesBareHex) {
    EXPECT_EQ(parseBluetoothAddress("AC800A123456").value_or(0), 0xAC800A123456ULL);
}

TEST(BluetoothAddress, RejectsTooFewDigits) {
    EXPECT_FALSE(parseBluetoothAddress("AA:BB:CC:DD:EE").has_value());
}

TEST(BluetoothAddress, RejectsTooManyDigits) {
    EXPECT_FALSE(parseBluetoothAddress("AA:BB:CC:DD:EE:FF:00").has_value());
}

TEST(BluetoothAddress, RejectsNonHexDigits) {
    EXPECT_FALSE(parseBluetoothAddress("ZZ:BB:CC:DD:EE:FF").has_value());
}

TEST(BluetoothAddress, RejectsEmptyText) {
    EXPECT_FALSE(parseBluetoothAddress("").has_value());
}
````

- [ ] **Step 2b: Create `native/ConnectFlyout.Native.Tests/packages.config`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static" version="1.8.1.8" targetFramework="native" />
</packages>
````

- [ ] **Step 2c: Create `native/ConnectFlyout.Native.Tests/ConnectFlyout.Native.Tests.vcxproj`**

Use the final file from Task 5, Step 1, but for now keep only `BluetoothAddressTests.cpp` in the `ClCompile` item group, remove the `ClInclude` for `FakeHeadset.h`, and keep only the `SonyTransport` project reference. Tasks 3–5 add the rest back one step at a time.

- [ ] **Step 3: Create the library project (sources come next, so the build fails) `native/SonyTransport/SonyTransport.vcxproj`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup Label="ProjectConfigurations">
    <ProjectConfiguration Include="Debug|x64">
      <Configuration>Debug</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|x64">
      <Configuration>Release</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Debug|ARM64">
      <Configuration>Debug</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|ARM64">
      <Configuration>Release</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
  </ItemGroup>

  <PropertyGroup Label="Globals">
    <VCProjectVersion>17.0</VCProjectVersion>
    <ProjectGuid>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}</ProjectGuid>
    <RootNamespace>SonyTransport</RootNamespace>
    <WindowsTargetPlatformVersion>10.0.26100.0</WindowsTargetPlatformVersion>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props" />

  <PropertyGroup Label="Configuration">
    <ConfigurationType>StaticLibrary</ConfigurationType>
    <PlatformToolset>$(DefaultPlatformToolset)</PlatformToolset>
    <CharacterSet>Unicode</CharacterSet>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Debug'">true</UseDebugLibraries>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Release'">false</UseDebugLibraries>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props" />

  <ItemDefinitionGroup>
    <ClCompile>
      <AdditionalIncludeDirectories>$(MSBuildThisFileDirectory)include;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
    </ClCompile>
  </ItemDefinitionGroup>

  <ItemGroup>
    <ClInclude Include="include\sony\transport\BluetoothAddress.h" />
    <ClInclude Include="include\sony\transport\DeviceAddress.h" />
    <ClInclude Include="include\sony\transport\DiscoveredDevice.h" />
    <ClInclude Include="include\sony\transport\FakeTransport.h" />
    <ClInclude Include="include\sony\transport\IDeviceDiscovery.h" />
    <ClInclude Include="include\sony\transport\ITransport.h" />
    <ClInclude Include="include\sony\transport\Logger.h" />
    <ClInclude Include="include\sony\transport\SonyError.h" />
    <ClInclude Include="include\sony\transport\WindowsRfcommTransport.h" />
  </ItemGroup>

  <ItemGroup>
    <ClCompile Include="src\BluetoothAddress.cpp" />
    <ClCompile Include="src\FakeTransport.cpp" />
    <ClCompile Include="src\Logger.cpp" />
    <ClCompile Include="src\WindowsRfcommTransport.cpp" />
  </ItemGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />
</Project>
````

- [ ] **Step 4: Run the build to see it fail**

Expected: FAIL with `C1083: Cannot open include file: 'sony/transport/BluetoothAddress.h'`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 5: Create `native/SonyTransport/include/sony/transport/BluetoothAddress.h`**

````cpp
#pragma once

#include <cstdint>
#include <optional>
#include <string_view>

namespace sony::transport {

// Parses "AA:BB:CC:DD:EE:FF", "AA-BB-CC-DD-EE-FF" or "AABBCCDDEEFF" (any case)
// into the 48-bit value Winsock expects in SOCKADDR_BTH::btAddr.
[[nodiscard]] std::optional<uint64_t> parseBluetoothAddress(std::string_view text) noexcept;

} // namespace sony::transport
````

- [ ] **Step 6: Create `native/SonyTransport/src/BluetoothAddress.cpp`**

````cpp
#include "sony/transport/BluetoothAddress.h"

namespace sony::transport {

namespace {

[[nodiscard]] constexpr int hexValue(char c) noexcept {
    if (c >= '0' && c <= '9') {
        return c - '0';
    }
    if (c >= 'a' && c <= 'f') {
        return c - 'a' + 10;
    }
    if (c >= 'A' && c <= 'F') {
        return c - 'A' + 10;
    }
    return -1;
}

} // namespace

std::optional<uint64_t> parseBluetoothAddress(std::string_view text) noexcept {
    constexpr int kDigits = 12;

    uint64_t value = 0;
    int digits = 0;
    for (const char c : text) {
        if (c == ':' || c == '-') {
            continue;
        }
        const int nibble = hexValue(c);
        if (nibble < 0 || digits == kDigits) {
            return std::nullopt;
        }
        value = (value << 4) | static_cast<uint64_t>(nibble);
        ++digits;
    }

    if (digits != kDigits) {
        return std::nullopt;
    }
    return value;
}

} // namespace sony::transport
````

- [ ] **Step 7: Create `native/SonyTransport/include/sony/transport/WindowsRfcommTransport.h`**

````cpp
#pragma once

#include "ITransport.h"

#include <atomic>
#include <cstdint>

namespace sony::transport {

// RFCOMM link to a Sony headset over Winsock (AF_BTH).
//
// Ported from upstream's WindowsBluetoothConnector: tries the V1 service UUID
// first, then the V2 UUID, requires authentication and encryption, and sets a
// 2.5 s receive timeout so the session reader can check its stop flag.
class WindowsRfcommTransport final : public ITransport {
public:
    WindowsRfcommTransport() noexcept;
    ~WindowsRfcommTransport() override;

    WindowsRfcommTransport(const WindowsRfcommTransport&) = delete;
    WindowsRfcommTransport& operator=(const WindowsRfcommTransport&) = delete;

    void connect(const DeviceAddress& address) override;
    void disconnect() noexcept override;
    [[nodiscard]] bool isConnected() const noexcept override;
    size_t send(std::span<const std::byte> data) override;
    size_t receive(std::span<std::byte> buffer) override;

private:
    // Returns 0 on success, otherwise the Winsock error code.
    [[nodiscard]] int tryConnect(uint64_t address, const char* serviceUuid);

    std::atomic<uintptr_t> _socket;
    std::atomic<bool> _connected{false};
};

} // namespace sony::transport
````

- [ ] **Step 8: Create `native/SonyTransport/src/WindowsRfcommTransport.cpp`**

````cpp
#include "sony/transport/WindowsRfcommTransport.h"

#include "sony/transport/BluetoothAddress.h"
#include "sony/transport/Logger.h"
#include "sony/transport/SonyError.h"

#include <winsock2.h>
#include <ws2bth.h>
#include <rpc.h>

#include <mutex>
#include <string>

#pragma comment(lib, "ws2_32.lib")
#pragma comment(lib, "rpcrt4.lib")

namespace sony::transport {

namespace {

constexpr auto kServiceUuidV1 = "96CC203E-5068-46ad-B32D-E316F5E069BA";
constexpr auto kServiceUuidV2 = "956C7B26-D49A-4BA8-B03F-B17D393CB6E2";
constexpr DWORD kReceiveTimeoutMs = 2500;
constexpr uintptr_t kInvalidSocket = static_cast<uintptr_t>(INVALID_SOCKET);

void ensureWinsock() {
    static std::once_flag once;
    std::call_once(once, [] {
        WSADATA data{};
        const int result = ::WSAStartup(MAKEWORD(2, 2), &data);
        if (result != 0) {
            throw SonyException(SonyErrorCode::TransportFailure, "WSAStartup failed: " + std::to_string(result));
        }
    });
}

void setSocketOption(SOCKET socket, int level, int name, DWORD value) {
    if (::setsockopt(socket, level, name, reinterpret_cast<const char*>(&value), sizeof(value)) == SOCKET_ERROR) {
        const int error = ::WSAGetLastError();
        ::closesocket(socket);
        throw SonyException(SonyErrorCode::TransportFailure, "setsockopt failed: " + std::to_string(error));
    }
}

SOCKET createSocket() {
    const SOCKET socket = ::socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
    if (socket == INVALID_SOCKET) {
        throw SonyException(SonyErrorCode::TransportFailure, "Couldn't create Bluetooth socket: " + std::to_string(::WSAGetLastError()));
    }
    setSocketOption(socket, SOL_RFCOMM, SO_BTH_AUTHENTICATE, TRUE);
    setSocketOption(socket, SOL_RFCOMM, SO_BTH_ENCRYPT, TRUE);
    setSocketOption(socket, SOL_SOCKET, SO_RCVTIMEO, kReceiveTimeoutMs);
    return socket;
}

} // namespace

WindowsRfcommTransport::WindowsRfcommTransport() noexcept
    : _socket(kInvalidSocket) {}

WindowsRfcommTransport::~WindowsRfcommTransport() {
    disconnect();
}

void WindowsRfcommTransport::connect(const DeviceAddress& address) {
    disconnect();

    const auto parsed = parseBluetoothAddress(address.str());
    if (!parsed) {
        throw SonyException(SonyErrorCode::TransportFailure, "Invalid Bluetooth address: " + address.str());
    }
    ensureWinsock();

    // Same order as upstream: V1 service first, then the V2 service.
    if (tryConnect(*parsed, kServiceUuidV1) == 0) {
        Logger::info(LogCategory::Transport, "RFCOMM connected on the V1 service");
        return;
    }
    const int error = tryConnect(*parsed, kServiceUuidV2);
    if (error == 0) {
        Logger::info(LogCategory::Transport, "RFCOMM connected on the V2 service");
        return;
    }
    throw SonyException(SonyErrorCode::TransportFailure, "Couldn't connect to " + address.str() + " (Winsock error " + std::to_string(error) + ")");
}

int WindowsRfcommTransport::tryConnect(uint64_t address, const char* serviceUuid) {
    const SOCKET socket = createSocket();

    // SOCKADDR_BTH is packed, so parse the GUID into an aligned local first.
    GUID serviceClassId{};
    if (::UuidFromStringA(reinterpret_cast<RPC_CSTR>(const_cast<char*>(serviceUuid)), &serviceClassId) != RPC_S_OK) {
        ::closesocket(socket);
        throw SonyException(SonyErrorCode::TransportFailure, std::string("Invalid service UUID ") + serviceUuid);
    }

    SOCKADDR_BTH target{};
    target.addressFamily = AF_BTH;
    target.btAddr = address;
    target.serviceClassId = serviceClassId;

    if (::connect(socket, reinterpret_cast<const sockaddr*>(&target), sizeof(target)) == SOCKET_ERROR) {
        const int error = ::WSAGetLastError();
        ::closesocket(socket);
        Logger::debug(LogCategory::Transport, std::string("RFCOMM connect to ") + serviceUuid + " failed: " + std::to_string(error));
        return error;
    }

    _socket.store(static_cast<uintptr_t>(socket));
    _connected.store(true);
    return 0;
}

void WindowsRfcommTransport::disconnect() noexcept {
    _connected.store(false);
    const uintptr_t socket = _socket.exchange(kInvalidSocket);
    if (socket != kInvalidSocket) {
        ::shutdown(static_cast<SOCKET>(socket), SD_BOTH);
        ::closesocket(static_cast<SOCKET>(socket));
    }
}

bool WindowsRfcommTransport::isConnected() const noexcept {
    return _connected.load();
}

size_t WindowsRfcommTransport::send(std::span<const std::byte> data) {
    const uintptr_t socket = _socket.load();
    if (!_connected.load() || socket == kInvalidSocket) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport not connected");
    }
    const int sent = ::send(static_cast<SOCKET>(socket), reinterpret_cast<const char*>(data.data()), static_cast<int>(data.size()), 0);
    if (sent == SOCKET_ERROR) {
        const int error = ::WSAGetLastError();
        _connected.store(false);
        throw SonyException(SonyErrorCode::Disconnected, "Bluetooth send failed: " + std::to_string(error));
    }
    return static_cast<size_t>(sent);
}

size_t WindowsRfcommTransport::receive(std::span<std::byte> buffer) {
    const uintptr_t socket = _socket.load();
    if (!_connected.load() || socket == kInvalidSocket) {
        throw SonyException(SonyErrorCode::Disconnected, "Transport not connected");
    }
    const int received = ::recv(static_cast<SOCKET>(socket), reinterpret_cast<char*>(buffer.data()), static_cast<int>(buffer.size()), 0);
    if (received == SOCKET_ERROR) {
        const int error = ::WSAGetLastError();
        if (error == WSAETIMEDOUT) {
            throw SonyException(SonyErrorCode::Timeout, "Receive timed out");
        }
        _connected.store(false);
        throw SonyException(SonyErrorCode::Disconnected, "Bluetooth receive failed: " + std::to_string(error));
    }
    if (received == 0) {
        _connected.store(false);
        throw SonyException(SonyErrorCode::Disconnected, "Headset closed the connection");
    }
    return static_cast<size_t>(received);
}

} // namespace sony::transport
````

- [ ] **Step 9: Build and run the tests**

Expected: `[  PASSED  ] 7 tests.`

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
.\bin\native\x64\Debug\ConnectFlyout.Native.Tests.exe
````

---

### Task 3: Protocol Library (Upstream Copy Plus Fixes)

**Files:**
- Copy from upstream: 15 headers into `native/SonyProtocol/include/sony/protocol/`, 8 sources into `native/SonyProtocol/src/` (list in Step 1)
- Modify: `native/SonyProtocol/include/sony/protocol/DeviceProfile.h`, `native/SonyProtocol/include/sony/protocol/SonyProtocolSession.h`, `native/SonyProtocol/src/SonyProtocolSession.cpp`
- Create: `native/SonyProtocol/include/sony/protocol/ErrorMapping.h`, `native/SonyProtocol/SonyProtocol.vcxproj`
- Test: `native/ConnectFlyout.Native.Tests/FrameCodecTests.cpp`, `native/ConnectFlyout.Native.Tests/DeviceProfileTests.cpp`

**Interfaces:**
- Consumes: Task 2's `SonyTransport` library.
- Produces (upstream, unchanged unless listed): `FrameCodec`, `SonyProtocolSession`, `ProtocolV1`, `ProtocolV2`, `DeviceProfileRegistry`, `DeviceEventDispatcher`, `equalizerPresets()`, `DeviceState`. New: `SonyProtocolSession::onDisconnected(std::function<void()>)` (fires on the reader thread only when the link drops without `disconnect()`); send/request timeouts default to 1000 ms; `constexpr int32_t sony::protocol::toHresult(SonyErrorCode) noexcept`.

- [ ] **Step 1: Copy the upstream protocol files**

````powershell
$upstream = "$env:TEMP\sony-device-center\libs\sony-protocol"
New-Item -ItemType Directory -Force native\SonyProtocol\include\sony\protocol, native\SonyProtocol\src | Out-Null
foreach ($name in 'DataType', 'SonyFrame', 'FrameCodec', 'SonyError', 'SemanticTypes', 'DeviceState', 'IProtocol', 'ProtocolV1', 'ProtocolV2', 'SonyProtocolSession', 'DeviceProfile', 'DeviceProfileRegistry', 'EqualizerPresets', 'DeviceEvents', 'DeviceEventDispatcher') {
    Copy-Item "$upstream\include\sony\protocol\$name.h" native\SonyProtocol\include\sony\protocol\
}
foreach ($name in 'FrameCodec.cpp', 'ProtocolHelpers.h', 'ProtocolV1.cpp', 'ProtocolV2.cpp', 'SonyProtocolSession.cpp', 'DeviceProfileRegistry.cpp', 'EqualizerPresets.cpp', 'DeviceEventDispatcher.cpp') {
    Copy-Item "$upstream\src\$name" native\SonyProtocol\src\
}
````

- [ ] **Step 2: Write the failing test `native/ConnectFlyout.Native.Tests/FrameCodecTests.cpp`**

````cpp
#include "sony/protocol/FrameCodec.h"

#include <gtest/gtest.h>

#include <algorithm>
#include <functional>

using sony::SonyErrorCode;
using sony::SonyException;
using sony::protocol::DataType;
using sony::protocol::FrameCodec;
using sony::protocol::SonyFrame;

namespace {

using Bytes = std::vector<uint8_t>;

SonyErrorCode errorCodeOf(const std::function<void()>& action) {
    try {
        action();
    } catch (const SonyException& ex) {
        return ex.code();
    }
    ADD_FAILURE() << "Expected a SonyException";
    return SonyErrorCode::ProtocolViolation;
}

} // namespace

TEST(FrameCodec, EncodesHandshakeFrame) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x00, 0x00}};

    // 3e | type 0c | seq 00 | length 00 00 00 02 | payload 00 00 | checksum 0e | 3c
    const Bytes expected{0x3e, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x0e, 0x3c};
    EXPECT_EQ(FrameCodec::encode(frame), expected);
}

TEST(FrameCodec, RoundTripsAFrame) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 1, .payload = {0x68, 0x17, 0x01, 0x01, 0x01, 0x01, 0x08}};

    EXPECT_EQ(FrameCodec::decode(FrameCodec::encode(frame)), frame);
}

TEST(FrameCodec, EscapesAllThreeMarkerBytes) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x3c, 0x3d, 0x3e}};
    const Bytes encoded = FrameCodec::encode(frame);

    const Bytes escapedPayload{0x3d, 0x2c, 0x3d, 0x2d, 0x3d, 0x2e};
    EXPECT_NE(std::search(encoded.begin(), encoded.end(), escapedPayload.begin(), escapedPayload.end()), encoded.end());
    EXPECT_EQ(FrameCodec::decode(encoded), frame);
}

TEST(FrameCodec, ChecksumIsByteSumModulo256) {
    const Bytes data{0xff, 0x02};
    EXPECT_EQ(FrameCodec::calculateChecksum(data), 0x01);
}

TEST(FrameCodec, WritesLengthBigEndian) {
    const SonyFrame frame{.type = DataType::DataMdr, .sequence = 0, .payload = Bytes(300, 0x01)};
    const Bytes encoded = FrameCodec::encode(frame);

    EXPECT_EQ(encoded[3], 0x00);
    EXPECT_EQ(encoded[4], 0x00);
    EXPECT_EQ(encoded[5], 0x01);
    EXPECT_EQ(encoded[6], 0x2c);
}

TEST(FrameCodec, RejectsBadChecksum) {
    Bytes encoded = FrameCodec::encode(SonyFrame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x00, 0x00}});
    encoded[encoded.size() - 2] = 0x0f;

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidChecksum);
}

TEST(FrameCodec, RejectsUnknownEscapeSequence) {
    const Bytes encoded{0x3e, 0x0c, 0x3d, 0x99, 0x00, 0x00, 0x00, 0x00, 0x0c, 0x3c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}

TEST(FrameCodec, RejectsTruncatedFrame) {
    const Bytes encoded{0x3e, 0x0c, 0x00, 0x3c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}

TEST(FrameCodec, RejectsLengthLongerThanData) {
    const Bytes encoded{0x3e, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x09, 0x00, 0x15, 0x3c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}

TEST(FrameCodec, RejectsMissingDelimiters) {
    const Bytes encoded{0x0c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0c};

    EXPECT_EQ(errorCodeOf([&] { (void)FrameCodec::decode(encoded); }), SonyErrorCode::InvalidFrame);
}
````

- [ ] **Step 3: Write the failing test `native/ConnectFlyout.Native.Tests/DeviceProfileTests.cpp`**

````cpp
#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/ErrorMapping.h"

#include <gtest/gtest.h>

using sony::SonyErrorCode;
using sony::protocol::DeviceProfileRegistry;
using sony::protocol::SonyModel;
using sony::protocol::SonyProtocolVersion;
using sony::protocol::toHresult;

TEST(DeviceProfile, IdentifiesWf1000Xm6) {
    EXPECT_TRUE(DeviceProfileRegistry::identifyModel("WF-1000XM6") == SonyModel::WF1000XM6);
    EXPECT_TRUE(DeviceProfileRegistry::identifyModel("LE_WF-1000XM6") == SonyModel::WF1000XM6);
}

TEST(DeviceProfile, Wf1000Xm6UsesV2WithEarbudBatteries) {
    const auto profile = DeviceProfileRegistry::getProfileForDevice("WF-1000XM6");

    EXPECT_TRUE(profile.protocol == SonyProtocolVersion::V2);
    EXPECT_TRUE(profile.capabilities.dualBattery);
    EXPECT_TRUE(profile.capabilities.dsee);
    EXPECT_TRUE(profile.capabilities.speakToChat);
}

TEST(DeviceProfile, NamesWf1000Xm6) {
    // Upstream fell through to "Unknown" for this model.
    EXPECT_EQ(to_string(SonyModel::WF1000XM6), "WF-1000XM6");
}

TEST(DeviceProfile, UnknownNamesFallBackToUnknownModel) {
    EXPECT_TRUE(DeviceProfileRegistry::identifyModel("Galaxy Buds") == SonyModel::Unknown);
}

TEST(ErrorMapping, MapsEveryCodeToItsHresult) {
    EXPECT_EQ(toHresult(SonyErrorCode::Timeout), static_cast<int32_t>(0x800705B4));
    EXPECT_EQ(toHresult(SonyErrorCode::Disconnected), static_cast<int32_t>(0x8007048F));
    EXPECT_EQ(toHresult(SonyErrorCode::Unsupported), static_cast<int32_t>(0x80004001));
    EXPECT_EQ(toHresult(SonyErrorCode::InvalidFrame), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::InvalidChecksum), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::InvalidResponse), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::ProtocolViolation), static_cast<int32_t>(0x8007000D));
    EXPECT_EQ(toHresult(SonyErrorCode::TransportFailure), static_cast<int32_t>(0x800704C9));
}
````

- [ ] **Step 4: Register the tests and the protocol library in the test project**

In `native/ConnectFlyout.Native.Tests/ConnectFlyout.Native.Tests.vcxproj`, add to the `ClCompile` item group:

````xml
    <ClCompile Include="DeviceProfileTests.cpp" />
    <ClCompile Include="FrameCodecTests.cpp" />
````

and add this project reference next to the `SonyTransport` one:

````xml
    <ProjectReference Include="..\SonyProtocol\SonyProtocol.vcxproj">
      <Project>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}</Project>
    </ProjectReference>
````

- [ ] **Step 5: Create `native/SonyProtocol/SonyProtocol.vcxproj`**

Use the final file below, but leave out `HeadsetController.h`, `V1Notifications.h`, `HeadsetController.cpp` and `V1Notifications.cpp` for now (Task 4 adds them).

- [ ] **Step 5a: Reference: final `native/SonyProtocol/SonyProtocol.vcxproj`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup Label="ProjectConfigurations">
    <ProjectConfiguration Include="Debug|x64">
      <Configuration>Debug</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|x64">
      <Configuration>Release</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Debug|ARM64">
      <Configuration>Debug</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|ARM64">
      <Configuration>Release</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
  </ItemGroup>

  <PropertyGroup Label="Globals">
    <VCProjectVersion>17.0</VCProjectVersion>
    <ProjectGuid>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}</ProjectGuid>
    <RootNamespace>SonyProtocol</RootNamespace>
    <WindowsTargetPlatformVersion>10.0.26100.0</WindowsTargetPlatformVersion>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props" />

  <PropertyGroup Label="Configuration">
    <ConfigurationType>StaticLibrary</ConfigurationType>
    <PlatformToolset>$(DefaultPlatformToolset)</PlatformToolset>
    <CharacterSet>Unicode</CharacterSet>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Debug'">true</UseDebugLibraries>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Release'">false</UseDebugLibraries>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props" />

  <ItemDefinitionGroup>
    <ClCompile>
      <AdditionalIncludeDirectories>$(MSBuildThisFileDirectory)include;$(MSBuildThisFileDirectory)..\SonyTransport\include;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
    </ClCompile>
  </ItemDefinitionGroup>

  <ItemGroup>
    <ClInclude Include="include\sony\protocol\DataType.h" />
    <ClInclude Include="include\sony\protocol\DeviceEventDispatcher.h" />
    <ClInclude Include="include\sony\protocol\DeviceEvents.h" />
    <ClInclude Include="include\sony\protocol\DeviceProfile.h" />
    <ClInclude Include="include\sony\protocol\DeviceProfileRegistry.h" />
    <ClInclude Include="include\sony\protocol\DeviceState.h" />
    <ClInclude Include="include\sony\protocol\EqualizerPresets.h" />
    <ClInclude Include="include\sony\protocol\ErrorMapping.h" />
    <ClInclude Include="include\sony\protocol\FrameCodec.h" />
    <ClInclude Include="include\sony\protocol\HeadsetController.h" />
    <ClInclude Include="include\sony\protocol\IProtocol.h" />
    <ClInclude Include="include\sony\protocol\ProtocolV1.h" />
    <ClInclude Include="include\sony\protocol\ProtocolV2.h" />
    <ClInclude Include="include\sony\protocol\SemanticTypes.h" />
    <ClInclude Include="include\sony\protocol\SonyError.h" />
    <ClInclude Include="include\sony\protocol\SonyFrame.h" />
    <ClInclude Include="include\sony\protocol\SonyProtocolSession.h" />
    <ClInclude Include="include\sony\protocol\V1Notifications.h" />
    <ClInclude Include="src\ProtocolHelpers.h" />
  </ItemGroup>

  <ItemGroup>
    <ClCompile Include="src\DeviceEventDispatcher.cpp" />
    <ClCompile Include="src\DeviceProfileRegistry.cpp" />
    <ClCompile Include="src\EqualizerPresets.cpp" />
    <ClCompile Include="src\FrameCodec.cpp" />
    <ClCompile Include="src\HeadsetController.cpp" />
    <ClCompile Include="src\ProtocolV1.cpp" />
    <ClCompile Include="src\ProtocolV2.cpp" />
    <ClCompile Include="src\SonyProtocolSession.cpp" />
    <ClCompile Include="src\V1Notifications.cpp" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SonyTransport\SonyTransport.vcxproj">
      <Project>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}</Project>
    </ProjectReference>
  </ItemGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />
</Project>
````

- [ ] **Step 6: Run the build to see it fail**

Expected: FAIL with `C1083: Cannot open include file: 'sony/protocol/ErrorMapping.h'`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 7: Create `native/SonyProtocol/include/sony/protocol/ErrorMapping.h`**

````cpp
#pragma once

#include "SonyError.h"

#include <cstdint>

namespace sony::protocol {

// HRESULT reported across the WinRT boundary for each error code. The C# side
// turns these back into user-facing messages (HeadsetErrorMessages).
[[nodiscard]] constexpr int32_t toHresult(SonyErrorCode code) noexcept {
    switch (code) {
        case SonyErrorCode::Timeout:
            return static_cast<int32_t>(0x800705B4); // HRESULT_FROM_WIN32(ERROR_TIMEOUT)
        case SonyErrorCode::Disconnected:
            return static_cast<int32_t>(0x8007048F); // HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED)
        case SonyErrorCode::Unsupported:
            return static_cast<int32_t>(0x80004001); // E_NOTIMPL
        case SonyErrorCode::InvalidFrame:
        case SonyErrorCode::InvalidChecksum:
        case SonyErrorCode::InvalidResponse:
        case SonyErrorCode::ProtocolViolation:
            return static_cast<int32_t>(0x8007000D); // HRESULT_FROM_WIN32(ERROR_INVALID_DATA)
        case SonyErrorCode::TransportFailure:
            return static_cast<int32_t>(0x800704C9); // HRESULT_FROM_WIN32(ERROR_CONNECTION_REFUSED)
    }
    return static_cast<int32_t>(0x80004005); // E_FAIL
}

} // namespace sony::protocol
````

- [ ] **Step 8: Fix upstream's missing XM6 name in `DeviceProfile.h`**

Upstream's `to_string(SonyModel)` has no WF-1000XM6 case, so the app would show "Unknown". In `native/SonyProtocol/include/sony/protocol/DeviceProfile.h` replace:

````cpp
        case SonyModel::WF1000XM5: return "WF-1000XM5";
````

with:

````cpp
        case SonyModel::WF1000XM5: return "WF-1000XM5";
        case SonyModel::WF1000XM6: return "WF-1000XM6";
````

- [ ] **Step 9: Session fixes in `SonyProtocolSession.h`**

Why: the app needs to hear about a dropped link (reconnect schedule), the spec's command timeout is 1 s (upstream used 2 s), and a callback that releases the session on the reader thread must not `std::terminate` on a self-join. Make each replacement below in `native/SonyProtocol/include/sony/protocol/SonyProtocolSession.h`; each "replace" text appears exactly once.

1. Replace:

````cpp
    using NotificationCallback = std::function<void(const SonyFrame&)>;
````

   with:

````cpp
    using NotificationCallback = std::function<void(const SonyFrame&)>;
    using DisconnectedCallback = std::function<void()>;
````

2. Replace:

````cpp
    void send(const SonyFrame& frame, std::chrono::milliseconds timeout = std::chrono::milliseconds(2000));
````

   with:

````cpp
    void send(const SonyFrame& frame, std::chrono::milliseconds timeout = std::chrono::milliseconds(1000));
````

3. Replace:

````cpp
        std::chrono::milliseconds timeout = std::chrono::milliseconds(2000));
````

   with:

````cpp
        std::chrono::milliseconds timeout = std::chrono::milliseconds(1000));
````

4. Replace:

````cpp
    void onNotification(NotificationCallback callback);
````

   with:

````cpp
    void onNotification(NotificationCallback callback);

    // Register a callback for when the link drops without disconnect() being called.
    // Runs on the reader thread.
    void onDisconnected(DisconnectedCallback callback);
````

5. Replace:

````cpp
    std::atomic<bool> _running{false};
````

   with:

````cpp
    std::atomic<bool> _running{false};
    std::atomic<bool> _stopRequested{false};
````

6. Replace:

````cpp
    std::vector<NotificationCallback> _notificationCallbacks;
````

   with:

````cpp
    std::vector<NotificationCallback> _notificationCallbacks;
    DisconnectedCallback _disconnectedCallback;
````

- [ ] **Step 10: Session fixes in `SonyProtocolSession.cpp`**

Same reasons, plus corrupt frames now get a Debug log with their bytes. Make each replacement in `native/SonyProtocol/src/SonyProtocolSession.cpp`:

1. Replace:

````cpp
void SonyProtocolSession::_startReader() {
    _stopReader();
    _running.store(true);
````

   with:

````cpp
void SonyProtocolSession::_startReader() {
    _stopReader();
    _stopRequested.store(false);
    _running.store(true);
````

2. Replace:

````cpp
void SonyProtocolSession::_stopReader() noexcept {
    _running.store(false);
````

   with:

````cpp
void SonyProtocolSession::_stopReader() noexcept {
    _stopRequested.store(true);
    _running.store(false);
````

3. Replace:

````cpp
        if (std::this_thread::get_id() != _readerThread.get_id()) {
            _readerThread.join();
        }
    }
}
````

   with:

````cpp
        if (std::this_thread::get_id() != _readerThread.get_id()) {
            _readerThread.join();
        } else {
            // Called from a callback on the reader thread: it can't join itself.
            _readerThread.detach();
        }
    }
}
````

4. Replace:

````cpp
    _running.store(false);
    {
        std::lock_guard lock(_sessionMtx);
        _ackCv.notify_all();
        _responseCv.notify_all();
    }
}

void SonyProtocolSession::_handleIncomingBytes
````

   with:

````cpp
    _running.store(false);
    DisconnectedCallback disconnectedCallback;
    {
        std::lock_guard lock(_sessionMtx);
        _ackCv.notify_all();
        _responseCv.notify_all();
        if (!_stopRequested.load()) {
            disconnectedCallback = _disconnectedCallback;
        }
    }

    if (!_stopRequested.load()) {
        Logger::info(LogCategory::Session, "Link dropped");
    }
    if (disconnectedCallback) {
        try {
            disconnectedCallback();
        } catch (...) {
            // Suppress exceptions from client callbacks
        }
    }
}

void SonyProtocolSession::_handleIncomingBytes
````

5. Replace:

````cpp
        } catch (const SonyException&) {
            // Corrupt frame: dropped without crash, continue loop
        } catch (const std::exception&) {
````

   with:

````cpp
        } catch (const SonyException& ex) {
            // Corrupt frame: dropped without an ACK so the headset resends it
            Logger::debug(LogCategory::Session, std::string("Dropped corrupt frame (") + ex.what() + "): " + Logger::formatHex(frameSlice));
        } catch (const std::exception&) {
````

6. Replace:

````cpp
void SonyProtocolSession::onNotification(NotificationCallback callback) {
    std::lock_guard lock(_sessionMtx);
    _notificationCallbacks.push_back(std::move(callback));
}
````

   with:

````cpp
void SonyProtocolSession::onNotification(NotificationCallback callback) {
    std::lock_guard lock(_sessionMtx);
    _notificationCallbacks.push_back(std::move(callback));
}

void SonyProtocolSession::onDisconnected(DisconnectedCallback callback) {
    std::lock_guard lock(_sessionMtx);
    _disconnectedCallback = std::move(callback);
}
````

- [ ] **Step 11: Build and run the tests**

Expected: `[  PASSED  ] 22 tests.`

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
.\bin\native\x64\Debug\ConnectFlyout.Native.Tests.exe
````

---

### Task 4: Headset Controller and XM6 Connection Tests

**Files:**
- Create: `native/SonyProtocol/include/sony/protocol/HeadsetController.h`, `native/SonyProtocol/src/HeadsetController.cpp`, `native/SonyProtocol/include/sony/protocol/V1Notifications.h`, `native/SonyProtocol/src/V1Notifications.cpp`
- Test: `native/ConnectFlyout.Native.Tests/FakeHeadset.h`, `native/ConnectFlyout.Native.Tests/Xm6ConnectionTests.cpp`

**Interfaces:**
- Consumes: `SonyProtocolSession` (Task 3, including `onDisconnected`), `ProtocolV1/V2`, `DeviceProfileRegistry::getProfileForDevice`, `DeviceEventDispatcher::parseNotificationPayload(payload, state, false)`.
- Produces: `class sony::protocol::HeadsetController` with `HeadsetController(std::unique_ptr<transport::ITransport>, std::string_view deviceName)`, `connect(const DeviceAddress&)` (throws `SonyException`, closes the link on failure), `disconnect() noexcept`, `isConnected()`, `profile()`, `generation()`, `state()`, `refreshBattery()`, `setNoiseControl(const NoiseControlState&)`, `setEqualizerPreset(int)`, `setEqualizerCustom(int, const std::array<int,5>&)`, `setDsee(bool)`, `setSpeakToChat(bool)`, `setAdaptiveVolume(bool)`, `setAutoPowerOff(int)`, `onStateChanged(std::function<void(const DeviceState&)>)`, `onDisconnected(std::function<void()>)`. `bool sony::protocol::applyV1Notification(std::span<const uint8_t>, DeviceState&)`. Test helpers `sony::test::FakeHeadset` (`reply`, `ignore`, `notify`, `notifyTwice`, `requests`, `requestSequences`), `waitUntil`, `kTestAddress`, `scriptXm6Connect`, `scriptXm4Connect`.

- [ ] **Step 1: Write the scripted fake headset `native/ConnectFlyout.Native.Tests/FakeHeadset.h`**

````cpp
#pragma once

#include "sony/protocol/FrameCodec.h"
#include "sony/protocol/SonyFrame.h"
#include "sony/transport/FakeTransport.h"

#include <chrono>
#include <cstdint>
#include <deque>
#include <initializer_list>
#include <mutex>
#include <span>
#include <thread>
#include <vector>

namespace sony::test {

using Payload = std::vector<uint8_t>;

// Scripted headset on top of upstream's FakeTransport.
//
// Each queued reply answers one host request, in order, at the moment the host
// writes it: an ACK carrying the next expected sequence number, then any data
// frames. Device data frames alternate sequence 0/1 like real hardware so the
// session doesn't drop them as duplicates. ACKs the host sends back never
// consume a reply.
class FakeHeadset : public transport::FakeTransport {
public:
    // Answer the next request with an ACK followed by these data payloads.
    void reply(std::initializer_list<Payload> payloads = {}) {
        std::lock_guard lock(_scriptMutex);
        _replies.push_back(Reply{true, std::vector<Payload>(payloads)});
    }

    // Leave the next request unanswered: no ACK, no data.
    void ignore() {
        std::lock_guard lock(_scriptMutex);
        _replies.push_back(Reply{false, {}});
    }

    // Send an unsolicited data frame right now.
    void notify(const Payload& payload) {
        queueIncoming(encodeData(payload));
    }

    // Send the same data frame twice with the same sequence number.
    void notifyTwice(const Payload& payload) {
        const auto frame = encodeData(payload);
        queueIncoming(frame);
        queueIncoming(frame);
    }

    // Payloads of every DataMdr request the host sent, in order.
    [[nodiscard]] std::vector<Payload> requests() const {
        std::lock_guard lock(_scriptMutex);
        return _requests;
    }

    // Sequence numbers of every DataMdr request the host sent, in order.
    [[nodiscard]] std::vector<uint8_t> requestSequences() const {
        std::lock_guard lock(_scriptMutex);
        return _requestSequences;
    }

    size_t send(std::span<const std::byte> data) override {
        const size_t written = transport::FakeTransport::send(data);

        protocol::SonyFrame request;
        try {
            std::vector<uint8_t> bytes;
            bytes.reserve(data.size());
            for (const auto b : data) {
                bytes.push_back(static_cast<uint8_t>(b));
            }
            request = protocol::FrameCodec::decode(bytes);
        } catch (...) {
            return written;
        }
        if (request.type != protocol::DataType::DataMdr) {
            return written;
        }

        Reply reply;
        bool hasReply = false;
        {
            std::lock_guard lock(_scriptMutex);
            _requests.push_back(request.payload);
            _requestSequences.push_back(request.sequence);
            if (!_replies.empty()) {
                reply = std::move(_replies.front());
                _replies.pop_front();
                hasReply = true;
            }
        }
        if (!hasReply || !reply.ack) {
            return written;
        }

        std::vector<uint8_t> incoming = protocol::FrameCodec::encode(protocol::SonyFrame{
            .type = protocol::DataType::Ack,
            .sequence = static_cast<uint8_t>(1 - (request.sequence & 1)),
            .payload = {},
        });
        for (const auto& payload : reply.payloads) {
            const auto frame = encodeData(payload);
            incoming.insert(incoming.end(), frame.begin(), frame.end());
        }
        queueIncoming(incoming);
        return written;
    }

private:
    struct Reply {
        bool ack{true};
        std::vector<Payload> payloads;
    };

    std::vector<uint8_t> encodeData(const Payload& payload) {
        uint8_t sequence = 0;
        {
            std::lock_guard lock(_scriptMutex);
            sequence = _deviceSequence;
            _deviceSequence ^= 1;
        }
        return protocol::FrameCodec::encode(protocol::SonyFrame{
            .type = protocol::DataType::DataMdr,
            .sequence = sequence,
            .payload = payload,
        });
    }

    mutable std::mutex _scriptMutex;
    std::deque<Reply> _replies;
    std::vector<Payload> _requests;
    std::vector<uint8_t> _requestSequences;
    uint8_t _deviceSequence{0};
};

// Polls until the predicate holds or the timeout passes.
template <typename Predicate>
bool waitUntil(Predicate predicate, std::chrono::milliseconds timeout = std::chrono::milliseconds(3000)) {
    const auto deadline = std::chrono::steady_clock::now() + timeout;
    while (std::chrono::steady_clock::now() < deadline) {
        if (predicate()) {
            return true;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }
    return predicate();
}

inline constexpr const char* kTestAddress = "AC:80:0A:12:34:56";

// Replies for everything HeadsetController::connect asks a WF-1000XM6.
inline void scriptXm6Connect(FakeHeadset& headset, bool answerHandshake = true) {
    if (answerHandshake) {
        headset.reply({{0x01, 0x00}});                                              // 00 00 handshake
    } else {
        headset.reply();                                                            // 00 00 handshake, ACK only
    }
    headset.reply();                                                                // 22 00 single battery: earbuds don't answer
    headset.reply({{0x23, 0x09, 85, 0x00, 82, 0x00}});                              // 22 09 left 85, right 82
    headset.reply({{0x23, 0x0a, 95, 0x00}});                                        // 22 0a case 95
    headset.reply({{0x67, 0x17, 0x01, 0x01, 0x01, 0x01, 0x08}});                    // 66 17 ambient 8, voice on
    headset.reply({{0x57, 0x00, 0x10, 0x06, 0x0a, 0x0a, 0x0b, 0x0c, 0x0a, 0x09}});  // 56 00 Bright, bands 0 1 2 0 -1
    headset.reply({{0xe7, 0x01, 0x01}});                                            // e6 01 DSEE on
    headset.reply({{0xf7, 0x0c, 0x00}});                                            // f6 0c Speak-to-Chat on (inverted)
    headset.reply({{0xf7, 0x0a, 0x01}});                                            // f6 0a adaptive volume off (inverted)
    headset.reply({{0x27, 0x05, 0x10, 0x00}});                                      // 26 05 when taken off (index 5)
    headset.reply({{0x05, 0x02, 0x05, '1', '.', '2', '.', '0'}});                   // 04 02 firmware 1.2.0
    headset.reply({{0x13, 0x02, 0x10}});                                            // 12 02 LDAC
}

// Replies for everything HeadsetController::connect asks a WH-1000XM4.
inline void scriptXm4Connect(FakeHeadset& headset) {
    headset.reply({{0x67, 0x02, 0x11, 0x01, 0x02, 0x01, 0x00, 0x00}});             // 66 02 init poll: noise cancelling
    headset.reply({{0x11, 0x00, 60, 0x00}});                                        // 10 00 battery 60
    headset.reply({{0x67, 0x02, 0x11, 0x01, 0x00, 0x01, 0x01, 0x0f}});             // 66 02 ambient 15, voice on
    headset.reply({{0x57, 0x01, 0x16, 0x06, 0x0c, 0x0a, 0x0a, 0x0a, 0x0a, 0x0a}}); // 56 01 Bass Boost, Clear Bass +2
    headset.reply({{0x05, 0x02, 0x05, '3', '.', '0', '.', '1'}});                   // 04 02 firmware 3.0.1
    headset.reply({{0x19, 0x00, 0x02}});                                            // 18 00 AAC
}

} // namespace sony::test
````

- [ ] **Step 2: Write the failing tests `native/ConnectFlyout.Native.Tests/Xm6ConnectionTests.cpp`**

````cpp
#include "FakeHeadset.h"

#include "sony/protocol/HeadsetController.h"

#include <gtest/gtest.h>

#include <atomic>
#include <memory>
#include <mutex>
#include <thread>

using namespace std::chrono_literals;

using sony::SonyErrorCode;
using sony::SonyException;
using sony::protocol::DataType;
using sony::protocol::DeviceState;
using sony::protocol::FrameCodec;
using sony::protocol::HeadsetController;
using sony::protocol::NoiseControlMode;
using sony::protocol::NoiseControlState;
using sony::protocol::SonyFrame;
using sony::test::FakeHeadset;
using sony::test::kTestAddress;
using sony::test::Payload;
using sony::test::scriptXm6Connect;
using sony::test::waitUntil;

namespace {

class Xm6Connection : public ::testing::Test {
protected:
    void SetUp() override {
        auto transport = std::make_unique<FakeHeadset>();
        headset = transport.get();
        controller = std::make_unique<HeadsetController>(std::move(transport), "WF-1000XM6");
    }

    void connect() {
        scriptXm6Connect(*headset);
        controller->connect(kTestAddress);
    }

    SonyErrorCode errorCodeOf(const std::function<void()>& action) {
        try {
            action();
        } catch (const SonyException& ex) {
            return ex.code();
        }
        ADD_FAILURE() << "Expected a SonyException";
        return SonyErrorCode::ProtocolViolation;
    }

    FakeHeadset* headset{};
    std::unique_ptr<HeadsetController> controller;
};

} // namespace

TEST_F(Xm6Connection, ConnectReadsFullInitialState) {
    connect();

    ASSERT_TRUE(controller->isConnected());
    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.left.value_or(-1), 85);
    EXPECT_EQ(state.battery.right.value_or(-1), 82);
    EXPECT_EQ(state.battery.caseBattery.value_or(-1), 95);
    EXPECT_TRUE(state.noiseControl.mode == NoiseControlMode::Ambient);
    EXPECT_EQ(state.noiseControl.ambientLevel, 8);
    EXPECT_TRUE(state.noiseControl.focusOnVoice);
    EXPECT_EQ(state.equalizer.preset, 0x10);
    EXPECT_EQ(state.equalizer.clearBass, 0);
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{0, 1, 2, 0, -1}));
    EXPECT_TRUE(state.dsee);
    EXPECT_TRUE(state.speakToChat);
    EXPECT_FALSE(state.adaptiveVolume);
    EXPECT_EQ(state.autoPowerOff, 5);
    EXPECT_EQ(state.firmware, "1.2.0");
    EXPECT_EQ(state.codec, "LDAC");
}

TEST_F(Xm6Connection, ConnectStartsWithV2Handshake) {
    connect();

    const auto requests = headset->requests();
    ASSERT_FALSE(requests.empty());
    EXPECT_EQ(requests.front(), (Payload{0x00, 0x00}));
}

TEST_F(Xm6Connection, ConnectSucceedsWhenHandshakeIsUnanswered) {
    scriptXm6Connect(*headset, false);
    controller->connect(kTestAddress);

    EXPECT_TRUE(controller->isConnected());
    EXPECT_EQ(controller->state().codec, "LDAC");
}

TEST_F(Xm6Connection, ConnectFailsCleanlyWhenHeadsetNeverAnswers) {
    EXPECT_EQ(errorCodeOf([&] { controller->connect(kTestAddress); }), SonyErrorCode::Timeout);
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, ConnectFailsWhenLinkIsRefused) {
    headset->setFailConnect(true);

    EXPECT_EQ(errorCodeOf([&] { controller->connect(kTestAddress); }), SonyErrorCode::TransportFailure);
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, RequestSequenceNumbersAlternate) {
    connect();

    const auto sequences = headset->requestSequences();
    ASSERT_GT(sequences.size(), 2u);
    for (size_t i = 1; i < sequences.size(); ++i) {
        EXPECT_NE(sequences[i], sequences[i - 1]) << "request " << i;
    }
}

TEST_F(Xm6Connection, SetNoiseControlSendsV2BytesAndPublishesState) {
    connect();
    std::vector<DeviceState> published;
    controller->onStateChanged([&](const DeviceState& state) { published.push_back(state); });

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::NoiseCancelling, .ambientLevel = 0, .focusOnVoice = false});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x17, 0x01, 0x01, 0x00, 0x00, 0x01}));
    EXPECT_TRUE(controller->state().noiseControl.mode == NoiseControlMode::NoiseCancelling);
    ASSERT_EQ(published.size(), 1u);
    EXPECT_TRUE(published.front().noiseControl.mode == NoiseControlMode::NoiseCancelling);
}

TEST_F(Xm6Connection, SetAmbientSendsLevelAndVoice) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::Ambient, .ambientLevel = 14, .focusOnVoice = true});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x17, 0x01, 0x01, 0x01, 0x01, 0x0e}));
    EXPECT_EQ(controller->state().noiseControl.ambientLevel, 14);
}

TEST_F(Xm6Connection, SettersSendV2Bytes) {
    connect();

    headset->reply();
    controller->setEqualizerPreset(0x16);
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x00, 0x16, 0x00}));

    headset->reply();
    controller->setEqualizerCustom(3, {-10, -5, 0, 5, 10});
    EXPECT_EQ(headset->requests().back(), (Payload{0x58, 0x00, 0xa0, 0x06, 0x0d, 0x00, 0x05, 0x0a, 0x0f, 0x14}));

    headset->reply();
    controller->setDsee(false);
    EXPECT_EQ(headset->requests().back(), (Payload{0xe8, 0x01, 0x00}));

    headset->reply();
    controller->setSpeakToChat(true);
    EXPECT_EQ(headset->requests().back(), (Payload{0xf8, 0x0c, 0x00, 0x01}));

    headset->reply();
    controller->setAdaptiveVolume(false);
    EXPECT_EQ(headset->requests().back(), (Payload{0xf8, 0x0a, 0x01}));

    headset->reply();
    controller->setAutoPowerOff(2);
    EXPECT_EQ(headset->requests().back(), (Payload{0x28, 0x05, 0x01, 0x01}));

    const DeviceState state = controller->state();
    EXPECT_EQ(state.equalizer.preset, 0xa0);
    EXPECT_EQ(state.equalizer.clearBass, 3);
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{-10, -5, 0, 5, 10}));
    EXPECT_FALSE(state.dsee);
    EXPECT_TRUE(state.speakToChat);
    EXPECT_FALSE(state.adaptiveVolume);
    EXPECT_EQ(state.autoPowerOff, 2);
}

TEST_F(Xm6Connection, CommandRetriesOnceAfterTimeout) {
    connect();

    headset->ignore();
    headset->reply();
    controller->setDsee(false);

    const auto requests = headset->requests();
    EXPECT_EQ(requests[requests.size() - 1], (Payload{0xe8, 0x01, 0x00}));
    EXPECT_EQ(requests[requests.size() - 2], (Payload{0xe8, 0x01, 0x00}));
    EXPECT_FALSE(controller->state().dsee);
}

TEST_F(Xm6Connection, CommandFailsAfterSecondTimeoutAndKeepsState) {
    connect();

    headset->ignore();
    headset->ignore();

    EXPECT_EQ(errorCodeOf([&] { controller->setDsee(false); }), SonyErrorCode::Timeout);
    EXPECT_TRUE(controller->state().dsee);
}

TEST_F(Xm6Connection, NotificationUpdatesStateAndPublishes) {
    connect();
    std::mutex mutex;
    std::vector<DeviceState> published;
    controller->onStateChanged([&](const DeviceState& state) {
        std::lock_guard lock(mutex);
        published.push_back(state);
    });

    headset->notify({0x69, 0x17, 0x01, 0x01, 0x00, 0x00, 0x00});

    EXPECT_TRUE(waitUntil([&] { return controller->state().noiseControl.mode == NoiseControlMode::NoiseCancelling; }));
    std::lock_guard lock(mutex);
    EXPECT_EQ(published.size(), 1u);
}

TEST_F(Xm6Connection, BatteryNotificationUpdatesEarbuds) {
    connect();

    headset->notify({0x25, 0x09, 40, 0x00, 41, 0x01});

    EXPECT_TRUE(waitUntil([&] { return controller->state().battery.left.value_or(-1) == 40; }));
    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.right.value_or(-1), 41);
    EXPECT_TRUE(state.battery.charging);
}

TEST_F(Xm6Connection, DuplicateNotificationIsHandledOnce) {
    connect();
    std::atomic<int> count{0};
    controller->onStateChanged([&](const DeviceState&) { ++count; });

    headset->notifyTwice({0x25, 0x09, 50, 0x00, 49, 0x00});

    EXPECT_TRUE(waitUntil([&] { return count.load() >= 1; }));
    std::this_thread::sleep_for(200ms);
    EXPECT_EQ(count.load(), 1);
}

TEST_F(Xm6Connection, ReplyIsMatchedWhenNotificationArrivesFirst) {
    connect();

    headset->reply();                                                                       // 22 00 unanswered
    headset->reply({{0x69, 0x17, 0x01, 0x00, 0x00, 0x00, 0x00}, {0x23, 0x09, 70, 0x00, 71, 0x00}}); // noise control off, then 22 09 reply
    headset->reply({{0x23, 0x0a, 90, 0x00}});                                               // 22 0a
    controller->refreshBattery();

    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.left.value_or(-1), 70);
    EXPECT_EQ(state.battery.right.value_or(-1), 71);
    EXPECT_EQ(state.battery.caseBattery.value_or(-1), 90);
    EXPECT_TRUE(waitUntil([&] { return controller->state().noiseControl.mode == NoiseControlMode::Off; }));
}

TEST_F(Xm6Connection, CorruptFrameIsDroppedWithoutAck) {
    connect();
    const size_t sentBefore = headset->sentCount();

    std::vector<uint8_t> corrupt = FrameCodec::encode(SonyFrame{.type = DataType::DataMdr, .sequence = 0, .payload = {0x25, 0x09, 10, 0x00, 10, 0x00}});
    ASSERT_EQ(corrupt[corrupt.size() - 2], 0x54);
    corrupt[corrupt.size() - 2] = 0x55;
    headset->queueIncoming(corrupt);
    headset->notify({0x25, 0x09, 33, 0x00, 34, 0x00});

    EXPECT_TRUE(waitUntil([&] { return controller->state().battery.left.value_or(-1) == 33; }));
    EXPECT_EQ(headset->sentCount(), sentBefore + 1);
}

TEST_F(Xm6Connection, LinkDropFailsPendingCommandQuicklyAndReportsDisconnect) {
    connect();
    std::atomic<bool> dropped{false};
    controller->onDisconnected([&] { dropped = true; });

    headset->ignore();
    const auto started = std::chrono::steady_clock::now();
    std::thread dropper([&] {
        std::this_thread::sleep_for(100ms);
        headset->simulateDisconnect();
    });

    EXPECT_EQ(errorCodeOf([&] { controller->setDsee(false); }), SonyErrorCode::Disconnected);
    dropper.join();

    EXPECT_LT(std::chrono::steady_clock::now() - started, 800ms);
    EXPECT_TRUE(waitUntil([&] { return dropped.load(); }));
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, DisconnectDoesNotReportALinkDrop) {
    connect();
    std::atomic<bool> dropped{false};
    controller->onDisconnected([&] { dropped = true; });

    controller->disconnect();
    std::this_thread::sleep_for(100ms);

    EXPECT_FALSE(dropped.load());
    EXPECT_FALSE(controller->isConnected());
}

TEST_F(Xm6Connection, ReconnectsAfterLinkDrop) {
    connect();
    headset->simulateDisconnect();
    ASSERT_TRUE(waitUntil([&] { return !controller->isConnected(); }));

    scriptXm6Connect(*headset);
    controller->connect(kTestAddress);

    EXPECT_TRUE(controller->isConnected());
    EXPECT_EQ(controller->state().battery.left.value_or(-1), 85);
}
````

- [ ] **Step 3: Register the new files**

In `ConnectFlyout.Native.Tests.vcxproj` add `<ClCompile Include="Xm6ConnectionTests.cpp" />` and an item group with `<ClInclude Include="FakeHeadset.h" />`. In `SonyProtocol.vcxproj` add `HeadsetController.h`, `V1Notifications.h` (`ClInclude`) and `HeadsetController.cpp`, `V1Notifications.cpp` (`ClCompile`) exactly as in the final file shown in Task 3, Step 5a.

- [ ] **Step 4: Run the build to see it fail**

Expected: FAIL with `C1083: Cannot open include file: 'sony/protocol/HeadsetController.h'`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 5: Create `native/SonyProtocol/include/sony/protocol/V1Notifications.h`**

````cpp
#pragma once

#include "DeviceState.h"

#include <cstdint>
#include <span>

namespace sony::protocol {

// Applies a protocol V1 (WH-1000XM4 era) notification or reply to the state.
//
// Upstream's DeviceEventDispatcher only understands V2 layouts, so V1 devices
// route their notifications here instead. Recognized payloads:
//   11/13 00 <level> <charging>                         battery
//   67/69 02 <effect> <nc> <dualSingle> <asm> <voice> <level>  noise control
//   57/59 01 <preset> 06 <bass+10> <b1..b5 +10>          equalizer
// Returns true when the payload was recognized and the state changed.
bool applyV1Notification(std::span<const uint8_t> payload, DeviceState& state);

} // namespace sony::protocol
````

- [ ] **Step 6: Create `native/SonyProtocol/src/V1Notifications.cpp`**

````cpp
#include "sony/protocol/V1Notifications.h"

namespace sony::protocol {

namespace {

constexpr uint8_t kBatterySingle = 0x00;
constexpr uint8_t kNcAsmInquired = 0x02;
constexpr uint8_t kEqInquired = 0x01;
constexpr uint8_t kDualSingleOff = 0x00;

bool applyBattery(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.size() < 4 || payload[1] != kBatterySingle) {
        return false;
    }
    state.battery.main = static_cast<int>(payload[2]);
    state.battery.charging = payload[3] == 1;
    return true;
}

bool applyNoiseControl(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.size() < 8 || payload[1] != kNcAsmInquired) {
        return false;
    }
    const bool on = payload[2] != 0;
    const bool ambient = payload[4] == kDualSingleOff;

    if (!on) {
        state.noiseControl.mode = NoiseControlMode::Off;
    } else if (ambient) {
        state.noiseControl.mode = NoiseControlMode::Ambient;
    } else {
        state.noiseControl.mode = NoiseControlMode::NoiseCancelling;
    }
    state.noiseControl.ambientLevel = state.noiseControl.mode == NoiseControlMode::Ambient ? static_cast<int>(payload[7]) : 0;
    state.noiseControl.focusOnVoice = payload[6] == 1;
    return true;
}

bool applyEqualizer(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.size() < 10 || payload[1] != kEqInquired) {
        return false;
    }
    state.equalizer.preset = static_cast<int>(payload[2]);
    state.equalizer.clearBass = static_cast<int>(payload[4]) - 10;
    for (size_t i = 0; i < state.equalizer.bands.size(); ++i) {
        state.equalizer.bands[i] = static_cast<int>(payload[5 + i]) - 10;
    }
    return true;
}

} // namespace

bool applyV1Notification(std::span<const uint8_t> payload, DeviceState& state) {
    if (payload.empty()) {
        return false;
    }

    switch (payload[0]) {
        case 0x11:
        case 0x13:
            return applyBattery(payload, state);
        case 0x67:
        case 0x69:
            return applyNoiseControl(payload, state);
        case 0x57:
        case 0x59:
            return applyEqualizer(payload, state);
        default:
            return false;
    }
}

} // namespace sony::protocol
````

- [ ] **Step 7: Create `native/SonyProtocol/include/sony/protocol/HeadsetController.h`**

````cpp
#pragma once

#include "DeviceEventDispatcher.h"
#include "DeviceProfile.h"
#include "DeviceState.h"
#include "IProtocol.h"
#include "SonyProtocolSession.h"
#include "sony/transport/ITransport.h"

#include <array>
#include <atomic>
#include <functional>
#include <memory>
#include <mutex>
#include <string_view>

namespace sony::protocol {

// Owns one headset connection: the session, the command set for its protocol
// generation, and the last state the headset confirmed.
//
// Every call blocks the calling thread; the WinRT wrapper runs them on the
// thread pool. A command that times out is retried once before the timeout is
// reported. State only changes after the headset acknowledges a command or
// sends a notification, so callers can revert their UI to state() on failure.
class HeadsetController {
public:
    using StateCallback = std::function<void(const DeviceState&)>;
    using DisconnectedCallback = std::function<void()>;

    HeadsetController(std::unique_ptr<transport::ITransport> transport, std::string_view deviceName);
    ~HeadsetController();

    HeadsetController(const HeadsetController&) = delete;
    HeadsetController& operator=(const HeadsetController&) = delete;

    // Opens the link, runs the protocol handshake and reads the initial state.
    // Throws SonyException; the link is closed again on failure.
    void connect(const transport::DeviceAddress& address);
    void disconnect() noexcept;
    [[nodiscard]] bool isConnected() const noexcept;

    [[nodiscard]] const DeviceProfile& profile() const noexcept;
    [[nodiscard]] ProtocolGeneration generation() const noexcept;
    [[nodiscard]] DeviceState state() const;

    void refreshBattery();
    void setNoiseControl(const NoiseControlState& value);
    void setEqualizerPreset(int preset);
    void setEqualizerCustom(int clearBass, const std::array<int, 5>& bands);
    void setDsee(bool enabled);
    void setSpeakToChat(bool enabled);
    void setAdaptiveVolume(bool enabled);
    void setAutoPowerOff(int index);

    // Called after every confirmed state change. Runs on the calling thread for
    // commands and on the session reader thread for notifications.
    void onStateChanged(StateCallback callback);

    // Called on the session reader thread when the link drops unexpectedly.
    void onDisconnected(DisconnectedCallback callback);

private:
    template <typename Operation>
    auto withRetry(Operation&& operation) -> decltype(operation());

    template <typename Read>
    void readOptional(std::string_view feature, bool supported, Read&& read);

    void createProtocol(ProtocolGeneration generation);
    void detectGeneration();
    void readInitialState();
    void handleNotification(const SonyFrame& frame);
    void updateState(const std::function<void(DeviceState&)>& mutation);
    void publish(const DeviceState& snapshot);

    DeviceProfile _profile;
    std::unique_ptr<SonyProtocolSession> _session;
    std::unique_ptr<IProtocol> _protocol;
    std::atomic<ProtocolGeneration> _generation{ProtocolGeneration::V1};
    DeviceEventDispatcher _dispatcher;

    mutable std::mutex _stateMutex;
    DeviceState _state;

    std::mutex _callbackMutex;
    StateCallback _stateCallback;
    DisconnectedCallback _disconnectedCallback;
};

} // namespace sony::protocol
````

- [ ] **Step 8: Create `native/SonyProtocol/src/HeadsetController.cpp`**

````cpp
#include "sony/protocol/HeadsetController.h"

#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/EqualizerPresets.h"
#include "sony/protocol/ProtocolV1.h"
#include "sony/protocol/ProtocolV2.h"
#include "sony/protocol/V1Notifications.h"
#include "sony/transport/Logger.h"

#include <string>

namespace sony::protocol {

namespace {

constexpr std::string_view kCategory = LogCategory::Device;

// Unknown Sony models only get the features every generation shares.
DeviceCapabilities unknownModelCapabilities() noexcept {
    DeviceCapabilities capabilities;
    capabilities.battery = true;
    capabilities.noiseCancelling = true;
    capabilities.ambientSound = true;
    capabilities.focusOnVoice = true;
    return capabilities;
}

} // namespace

HeadsetController::HeadsetController(std::unique_ptr<transport::ITransport> transport, std::string_view deviceName)
    : _profile(DeviceProfileRegistry::getProfileForDevice(deviceName)),
      _session(std::make_unique<SonyProtocolSession>(std::move(transport))) {
    // Unknown models start on V2 and fall back to V1 during connect.
    if (_profile.model == SonyModel::Unknown) {
        _profile.capabilities = unknownModelCapabilities();
        createProtocol(ProtocolGeneration::V2);
    } else {
        createProtocol(_profile.protocol == SonyProtocolVersion::V2 ? ProtocolGeneration::V2 : ProtocolGeneration::V1);
    }

    _session->onNotification([this](const SonyFrame& frame) { handleNotification(frame); });
    _session->onDisconnected([this] {
        DisconnectedCallback callback;
        {
            std::lock_guard lock(_callbackMutex);
            callback = _disconnectedCallback;
        }
        if (callback) {
            callback();
        }
    });
}

HeadsetController::~HeadsetController() {
    disconnect();
}

void HeadsetController::connect(const transport::DeviceAddress& address) {
    Logger::info(kCategory, "Connecting to " + std::string(to_string(_profile.model)) + " at " + address.str());
    _session->connect(address);

    try {
        _protocol->initDevice();
        if (_profile.model == SonyModel::Unknown) {
            detectGeneration();
        }
        readInitialState();
    } catch (...) {
        _session->disconnect();
        throw;
    }

    Logger::info(kCategory, "Connected to " + std::string(to_string(_profile.model)));
}

void HeadsetController::disconnect() noexcept {
    _session->disconnect();
}

bool HeadsetController::isConnected() const noexcept {
    return _session->isConnected();
}

const DeviceProfile& HeadsetController::profile() const noexcept {
    return _profile;
}

ProtocolGeneration HeadsetController::generation() const noexcept {
    return _generation.load();
}

DeviceState HeadsetController::state() const {
    std::lock_guard lock(_stateMutex);
    return _state;
}

void HeadsetController::refreshBattery() {
    const BatteryState battery = withRetry([&] { return _protocol->getBattery(); });
    updateState([&](DeviceState& state) { state.battery = battery; });
}

void HeadsetController::setNoiseControl(const NoiseControlState& value) {
    withRetry([&] { _protocol->setNoiseControl(value); });
    updateState([&](DeviceState& state) {
        state.noiseControl = value;
        if (value.mode != NoiseControlMode::Ambient) {
            state.noiseControl.ambientLevel = 0;
        }
    });
}

void HeadsetController::setEqualizerPreset(int preset) {
    withRetry([&] { _protocol->setEqualizerPreset(preset); });
    updateState([&](DeviceState& state) { state.equalizer.preset = preset; });
}

void HeadsetController::setEqualizerCustom(int clearBass, const std::array<int, 5>& bands) {
    withRetry([&] { _protocol->setEqualizerCustom(clearBass, bands); });
    updateState([&](DeviceState& state) {
        state.equalizer.preset = static_cast<int>(EqualizerPreset::Manual);
        state.equalizer.clearBass = clearBass;
        state.equalizer.bands = bands;
    });
}

void HeadsetController::setDsee(bool enabled) {
    withRetry([&] { _protocol->setDsee(enabled); });
    updateState([&](DeviceState& state) { state.dsee = enabled; });
}

void HeadsetController::setSpeakToChat(bool enabled) {
    withRetry([&] { _protocol->setSpeakToChat(enabled); });
    updateState([&](DeviceState& state) { state.speakToChat = enabled; });
}

void HeadsetController::setAdaptiveVolume(bool enabled) {
    withRetry([&] { _protocol->setAdaptiveVolume(enabled); });
    updateState([&](DeviceState& state) { state.adaptiveVolume = enabled; });
}

void HeadsetController::setAutoPowerOff(int index) {
    withRetry([&] { _protocol->setAutoPowerOff(index); });
    updateState([&](DeviceState& state) { state.autoPowerOff = index; });
}

void HeadsetController::onStateChanged(StateCallback callback) {
    std::lock_guard lock(_callbackMutex);
    _stateCallback = std::move(callback);
}

void HeadsetController::onDisconnected(DisconnectedCallback callback) {
    std::lock_guard lock(_callbackMutex);
    _disconnectedCallback = std::move(callback);
}

template <typename Operation>
auto HeadsetController::withRetry(Operation&& operation) -> decltype(operation()) {
    try {
        return operation();
    } catch (const SonyException& ex) {
        if (ex.code() != SonyErrorCode::Timeout) {
            throw;
        }
        Logger::warn(kCategory, std::string("Command timed out, retrying once: ") + ex.what());
    }
    return operation();
}

template <typename Read>
void HeadsetController::readOptional(std::string_view feature, bool supported, Read&& read) {
    if (!supported) {
        return;
    }
    try {
        withRetry(read);
    } catch (const SonyException& ex) {
        Logger::warn(kCategory, "Couldn't read " + std::string(feature) + ": " + ex.what());
    }
}

void HeadsetController::createProtocol(ProtocolGeneration generation) {
    if (generation == ProtocolGeneration::V2) {
        _protocol = std::make_unique<ProtocolV2>(*_session);
    } else {
        _protocol = std::make_unique<ProtocolV1>(*_session);
    }
    _generation.store(generation);
}

void HeadsetController::detectGeneration() {
    // Noise control GET is harmless on both generations. Never probe with the
    // V2 battery opcode: 0x22 powers a V1 headset off.
    try {
        (void)withRetry([&] { return _protocol->getNoiseControl(); });
    } catch (const SonyException&) {
        Logger::info(kCategory, "Unknown model didn't answer V2 commands, trying V1");
        createProtocol(ProtocolGeneration::V1);
        _protocol->initDevice();
    }
}

void HeadsetController::readInitialState() {
    const DeviceCapabilities& capabilities = _profile.capabilities;
    DeviceState initial;

    initial.battery = withRetry([&] { return _protocol->getBattery(); });
    initial.noiseControl = withRetry([&] { return _protocol->getNoiseControl(); });

    readOptional("equalizer", capabilities.equalizer, [&] { initial.equalizer = _protocol->getEqualizer(); });
    readOptional("DSEE", capabilities.dsee, [&] { initial.dsee = _protocol->getDsee(); });
    readOptional("Speak-to-Chat", capabilities.speakToChat, [&] { initial.speakToChat = _protocol->getSpeakToChat(); });
    readOptional("adaptive volume", capabilities.adaptiveVolume, [&] { initial.adaptiveVolume = _protocol->getAdaptiveVolume(); });
    readOptional("auto power-off", capabilities.autoPowerOff, [&] { initial.autoPowerOff = _protocol->getAutoPowerOff(); });
    readOptional("firmware", capabilities.firmwareInfo, [&] { initial.firmware = _protocol->getFirmwareVersion(); });
    readOptional("codec", capabilities.codecInfo, [&] { initial.codec = _protocol->getCodec(); });

    {
        std::lock_guard lock(_stateMutex);
        _state = initial;
    }
    publish(initial);
}

void HeadsetController::handleNotification(const SonyFrame& frame) {
    bool handled = false;
    DeviceState snapshot;
    {
        std::lock_guard lock(_stateMutex);
        handled = _generation.load() == ProtocolGeneration::V2
            ? _dispatcher.parseNotificationPayload(frame.payload, _state, false)
            : applyV1Notification(frame.payload, _state);
        if (handled) {
            snapshot = _state;
        }
    }

    if (!handled) {
        Logger::debug(kCategory, "Ignored notification: " + Logger::formatHex(frame.payload));
        return;
    }
    publish(snapshot);
}

void HeadsetController::updateState(const std::function<void(DeviceState&)>& mutation) {
    DeviceState snapshot;
    {
        std::lock_guard lock(_stateMutex);
        mutation(_state);
        snapshot = _state;
    }
    publish(snapshot);
}

void HeadsetController::publish(const DeviceState& snapshot) {
    StateCallback callback;
    {
        std::lock_guard lock(_callbackMutex);
        callback = _stateCallback;
    }
    if (!callback) {
        return;
    }
    try {
        callback(snapshot);
    } catch (...) {
        Logger::error(kCategory, "State callback threw");
    }
}

} // namespace sony::protocol
````

- [ ] **Step 9: Build and run the tests**

Expected: `[  PASSED  ] 41 tests.` The run takes about 35 s because several tests wait out real 1 s timeouts.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
.\bin\native\x64\Debug\ConnectFlyout.Native.Tests.exe
````

---

### Task 5: WH-1000XM4 (V1) Tests

**Files:**
- Test: `native/ConnectFlyout.Native.Tests/Xm4Tests.cpp`
- Modify: `native/ConnectFlyout.Native.Tests/ConnectFlyout.Native.Tests.vcxproj` (now final)

**Interfaces:**
- Consumes: `HeadsetController`, `applyV1Notification`, `scriptXm4Connect` (Task 4).
- Produces: `Xm4*` test suites, excluded from the first-build gate.

- [ ] **Step 1: Replace the test project with its final version `native/ConnectFlyout.Native.Tests/ConnectFlyout.Native.Tests.vcxproj`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup Label="ProjectConfigurations">
    <ProjectConfiguration Include="Debug|x64">
      <Configuration>Debug</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|x64">
      <Configuration>Release</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
  </ItemGroup>

  <PropertyGroup Label="Globals">
    <VCProjectVersion>17.0</VCProjectVersion>
    <ProjectGuid>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}</ProjectGuid>
    <RootNamespace>ConnectFlyoutNativeTests</RootNamespace>
    <WindowsTargetPlatformVersion>10.0.26100.0</WindowsTargetPlatformVersion>
    <!-- GoogleTest's assertion macros trip code analysis (C6326), so tests skip it -->
    <SonyDisableCodeAnalysis>true</SonyDisableCodeAnalysis>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props" />

  <PropertyGroup Label="Configuration">
    <ConfigurationType>Application</ConfigurationType>
    <PlatformToolset>$(DefaultPlatformToolset)</PlatformToolset>
    <CharacterSet>Unicode</CharacterSet>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Debug'">true</UseDebugLibraries>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Release'">false</UseDebugLibraries>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props" />

  <ItemDefinitionGroup>
    <ClCompile>
      <AdditionalIncludeDirectories>$(MSBuildThisFileDirectory)..\SonyProtocol\include;$(MSBuildThisFileDirectory)..\SonyTransport\include;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
    </ClCompile>
    <Link>
      <SubSystem>Console</SubSystem>
    </Link>
  </ItemDefinitionGroup>

  <ItemGroup>
    <ClInclude Include="FakeHeadset.h" />
  </ItemGroup>

  <ItemGroup>
    <ClCompile Include="BluetoothAddressTests.cpp" />
    <ClCompile Include="DeviceProfileTests.cpp" />
    <ClCompile Include="FrameCodecTests.cpp" />
    <ClCompile Include="Xm4Tests.cpp" />
    <ClCompile Include="Xm6ConnectionTests.cpp" />
  </ItemGroup>

  <ItemGroup>
    <None Include="packages.config" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SonyProtocol\SonyProtocol.vcxproj">
      <Project>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}</Project>
    </ProjectReference>
    <ProjectReference Include="..\SonyTransport\SonyTransport.vcxproj">
      <Project>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}</Project>
    </ProjectReference>
  </ItemGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />

  <ImportGroup Label="ExtensionTargets">
    <Import Project="..\..\packages\Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static.1.8.1.8\build\native\Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static.targets" Condition="Exists('..\..\packages\Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static.1.8.1.8\build\native\Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static.targets')" />
  </ImportGroup>

  <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
    <Error Condition="!Exists('..\..\packages\Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static.1.8.1.8\build\native\Microsoft.googletest.v140.windesktop.msvcstl.static.rt-static.targets')" Text="GoogleTest isn't restored. Build with: msbuild -restore -p:RestorePackagesConfig=true" />
  </Target>
</Project>
````

- [ ] **Step 2: Create `native/ConnectFlyout.Native.Tests/Xm4Tests.cpp`**

````cpp
// WH-1000XM4 (protocol V1) tests. Every suite name starts with "Xm4" so the
// first build can leave them out with --gtest_filter=-Xm4*.

#include "FakeHeadset.h"

#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/HeadsetController.h"
#include "sony/protocol/V1Notifications.h"

#include <gtest/gtest.h>

#include <memory>

using sony::protocol::DeviceProfileRegistry;
using sony::protocol::DeviceState;
using sony::protocol::HeadsetController;
using sony::protocol::NoiseControlMode;
using sony::protocol::NoiseControlState;
using sony::protocol::SonyModel;
using sony::protocol::SonyProtocolVersion;
using sony::protocol::applyV1Notification;
using sony::test::FakeHeadset;
using sony::test::kTestAddress;
using sony::test::Payload;
using sony::test::scriptXm4Connect;
using sony::test::waitUntil;

namespace {

class Xm4Connection : public ::testing::Test {
protected:
    void SetUp() override {
        auto transport = std::make_unique<FakeHeadset>();
        headset = transport.get();
        controller = std::make_unique<HeadsetController>(std::move(transport), "WH-1000XM4");
    }

    void connect() {
        scriptXm4Connect(*headset);
        controller->connect(kTestAddress);
    }

    FakeHeadset* headset{};
    std::unique_ptr<HeadsetController> controller;
};

} // namespace

TEST(Xm4Profile, UsesV1WithSingleBattery) {
    const auto profile = DeviceProfileRegistry::getProfileForDevice("WH-1000XM4");

    EXPECT_TRUE(profile.model == SonyModel::WH1000XM4);
    EXPECT_TRUE(profile.protocol == SonyProtocolVersion::V1);
    EXPECT_FALSE(profile.capabilities.dualBattery);
    EXPECT_FALSE(profile.capabilities.dsee);
}

TEST_F(Xm4Connection, ConnectReadsV1State) {
    connect();

    ASSERT_TRUE(controller->isConnected());
    const DeviceState state = controller->state();
    EXPECT_EQ(state.battery.main.value_or(-1), 60);
    EXPECT_FALSE(state.battery.left.has_value());
    EXPECT_TRUE(state.noiseControl.mode == NoiseControlMode::Ambient);
    EXPECT_EQ(state.noiseControl.ambientLevel, 15);
    EXPECT_TRUE(state.noiseControl.focusOnVoice);
    EXPECT_EQ(state.equalizer.preset, 0x16);
    EXPECT_EQ(state.equalizer.clearBass, 2);
    EXPECT_EQ(state.firmware, "3.0.1");
    EXPECT_EQ(state.codec, "AAC");
}

TEST_F(Xm4Connection, NeverSendsThePowerOffOpcode) {
    connect();
    headset->reply({{0x11, 0x00, 58, 0x00}});
    controller->refreshBattery();

    for (const auto& request : headset->requests()) {
        ASSERT_FALSE(request.empty());
        EXPECT_NE(request.front(), 0x22);
    }
}

TEST_F(Xm4Connection, SetAmbientSendsV1Bytes) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::Ambient, .ambientLevel = 15, .focusOnVoice = true});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x11, 0x01, 0x00, 0x01, 0x01, 0x0f}));
}

TEST_F(Xm4Connection, SetNoiseCancellingSendsV1Bytes) {
    connect();

    headset->reply();
    controller->setNoiseControl(NoiseControlState{.mode = NoiseControlMode::NoiseCancelling, .ambientLevel = 0, .focusOnVoice = false});

    EXPECT_EQ(headset->requests().back(), (Payload{0x68, 0x02, 0x11, 0x01, 0x02, 0x01, 0x00, 0x00}));
}

TEST_F(Xm4Connection, V1NotificationUpdatesState) {
    connect();

    headset->notify({0x69, 0x02, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00});

    EXPECT_TRUE(waitUntil([&] { return controller->state().noiseControl.mode == NoiseControlMode::Off; }));
}

TEST(Xm4Notifications, ParsesBattery) {
    DeviceState state;
    const Payload payload{0x13, 0x00, 40, 0x01};

    EXPECT_TRUE(applyV1Notification(payload, state));
    EXPECT_EQ(state.battery.main.value_or(-1), 40);
    EXPECT_TRUE(state.battery.charging);
}

TEST(Xm4Notifications, ParsesEqualizer) {
    DeviceState state;
    const Payload payload{0x59, 0x01, 0xa0, 0x06, 0x0a, 0x00, 0x05, 0x0a, 0x0f, 0x14};

    EXPECT_TRUE(applyV1Notification(payload, state));
    EXPECT_EQ(state.equalizer.preset, 0xa0);
    EXPECT_EQ(state.equalizer.bands, (std::array<int, 5>{-10, -5, 0, 5, 10}));
}

TEST(Xm4Notifications, IgnoresV2Layouts) {
    DeviceState state;
    const Payload payload{0x69, 0x17, 0x01, 0x01, 0x00, 0x00, 0x00};

    EXPECT_FALSE(applyV1Notification(payload, state));
}
````

- [ ] **Step 3: Build and run the XM6 gate and the XM4 suites separately**

Expected: first run `[  PASSED  ] 41 tests.`, second run `[  PASSED  ] 9 tests.`

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
.\bin\native\x64\Debug\ConnectFlyout.Native.Tests.exe --gtest_filter=-Xm4*
.\bin\native\x64\Debug\ConnectFlyout.Native.Tests.exe --gtest_filter=Xm4*
````

---

### Task 6: WinRT Component (`ConnectFlyout.Core`)

**Files:**
- Create: `native/ConnectFlyout.Core/HeadsetClient.idl`, `native/ConnectFlyout.Core/pch.h`, `native/ConnectFlyout.Core/ConnectFlyout.Core.def`, `native/ConnectFlyout.Core/HeadsetClient.h`, `native/ConnectFlyout.Core/HeadsetClient.cpp`, `native/ConnectFlyout.Core/packages.config`, `native/ConnectFlyout.Core/ConnectFlyout.Core.vcxproj`

**Interfaces:**
- Consumes: `HeadsetController`, `toHresult`, `WindowsRfcommTransport`, `equalizerPresets()`, `sony::Logger::setLogSink/setDeveloperMode/setLogLevel`.
- Produces: runtime class `ConnectFlyout.Core.HeadsetClient` exactly as declared in the IDL below (C# sees it through Task 7's projection). Output: `bin/native/<Platform>/<Configuration>/ConnectFlyout.Core.dll` and `.winmd`.
- Tests for this boundary run from C# in Task 7 (`HeadsetClientActivationTests`), because they need the projection.

- [ ] **Step 1: Create `native/ConnectFlyout.Core/HeadsetClient.idl`**

````idl
namespace ConnectFlyout.Core
{
    enum NoiseMode
    {
        Off = 0,
        NoiseCancelling = 1,
        Ambient = 2
    };

    enum ProtocolGeneration
    {
        V1 = 0,
        V2 = 1
    };

    enum NativeLogLevel
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    };

    // Battery levels in percent. -1 means the headset didn't report that cell.
    struct BatteryInfo
    {
        Int32 Main;
        Int32 Left;
        Int32 Right;
        Int32 CaseBattery;
        Boolean Charging;
    };

    struct NoiseControlInfo
    {
        NoiseMode Mode;
        Int32 AmbientLevel;
        Boolean FocusOnVoice;
    };

    // Band and Clear Bass values run from -10 to 10.
    struct EqualizerInfo
    {
        Int32 Preset;
        Int32 ClearBass;
        Int32 Band1;
        Int32 Band2;
        Int32 Band3;
        Int32 Band4;
        Int32 Band5;
    };

    struct HeadsetCapabilities
    {
        Boolean DualBattery;
        Boolean NoiseCancelling;
        Boolean AmbientSound;
        Boolean FocusOnVoice;
        Boolean Equalizer;
        Boolean ClearBass;
        Boolean Dsee;
        Boolean SpeakToChat;
        Boolean AdaptiveVolume;
        Boolean AutoPowerOff;
        Boolean FirmwareInfo;
        Boolean CodecInfo;
    };

    struct HeadsetState
    {
        BatteryInfo Battery;
        NoiseControlInfo NoiseControl;
        EqualizerInfo Equalizer;
        Boolean Dsee;
        Boolean SpeakToChat;
        Boolean AdaptiveVolume;
        Int32 AutoPowerOff;
        String Firmware;
        String Codec;
    };

    struct EqualizerPresetInfo
    {
        Int32 Value;
        String Name;
    };

    delegate void NativeLogHandler(NativeLogLevel level, String message);

    // One Sony headset over Bluetooth RFCOMM. Every async call runs on the
    // thread pool and fails with an HRESULT from sony::protocol::toHresult.
    runtimeclass HeadsetClient : Windows.Foundation.IClosable
    {
        HeadsetClient(String deviceName);

        static void SetLogHandler(NativeLogHandler handler);
        static void SetDebugLogging(Boolean enabled);
        static EqualizerPresetInfo[] GetEqualizerPresets();

        String DeviceName { get; };
        String ModelName { get; };
        Boolean IsKnownModel { get; };
        ProtocolGeneration Protocol { get; };
        HeadsetCapabilities Capabilities { get; };
        Boolean IsConnected { get; };
        HeadsetState State { get; };

        Windows.Foundation.IAsyncAction ConnectAsync(String bluetoothAddress);
        void Disconnect();
        Windows.Foundation.IAsyncAction RefreshBatteryAsync();
        Windows.Foundation.IAsyncAction SetNoiseControlAsync(NoiseControlInfo value);
        Windows.Foundation.IAsyncAction SetEqualizerPresetAsync(Int32 preset);
        Windows.Foundation.IAsyncAction SetEqualizerCustomAsync(EqualizerInfo value);
        Windows.Foundation.IAsyncAction SetDseeAsync(Boolean enabled);
        Windows.Foundation.IAsyncAction SetSpeakToChatAsync(Boolean enabled);
        Windows.Foundation.IAsyncAction SetAdaptiveVolumeAsync(Boolean enabled);
        Windows.Foundation.IAsyncAction SetAutoPowerOffAsync(Int32 index);

        // Raised on a background thread after every confirmed change.
        event Windows.Foundation.TypedEventHandler<HeadsetClient, HeadsetState> StateChanged;

        // Raised on a background thread when the link drops unexpectedly.
        event Windows.Foundation.TypedEventHandler<HeadsetClient, Object> Disconnected;
    }
}
````

- [ ] **Step 2: Create `native/ConnectFlyout.Core/pch.h`**

````cpp
#pragma once

#include <unknwn.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
````

- [ ] **Step 3: Create `native/ConnectFlyout.Core/ConnectFlyout.Core.def`**

````text
EXPORTS
DllCanUnloadNow = WINRT_CanUnloadNow PRIVATE
DllGetActivationFactory = WINRT_GetActivationFactory PRIVATE
````

- [ ] **Step 4: Create `native/ConnectFlyout.Core/HeadsetClient.h`**

````cpp
#pragma once

#include "HeadsetClient.g.h"

#include "sony/protocol/HeadsetController.h"

#include <functional>
#include <memory>

namespace winrt::ConnectFlyout::Core::implementation {

struct HeadsetClient : HeadsetClientT<HeadsetClient> {
    explicit HeadsetClient(hstring const& deviceName);

    // Registers the controller callbacks once the object can hand out weak references.
    void final_construct();

    static void SetLogHandler(Core::NativeLogHandler const& handler);
    static void SetDebugLogging(bool enabled);
    static com_array<Core::EqualizerPresetInfo> GetEqualizerPresets();

    hstring DeviceName() const;
    hstring ModelName() const;
    bool IsKnownModel() const;
    Core::ProtocolGeneration Protocol() const;
    Core::HeadsetCapabilities Capabilities() const;
    bool IsConnected() const;
    Core::HeadsetState State() const;

    Windows::Foundation::IAsyncAction ConnectAsync(hstring bluetoothAddress);
    void Disconnect();
    Windows::Foundation::IAsyncAction RefreshBatteryAsync();
    Windows::Foundation::IAsyncAction SetNoiseControlAsync(Core::NoiseControlInfo value);
    Windows::Foundation::IAsyncAction SetEqualizerPresetAsync(int32_t preset);
    Windows::Foundation::IAsyncAction SetEqualizerCustomAsync(Core::EqualizerInfo value);
    Windows::Foundation::IAsyncAction SetDseeAsync(bool enabled);
    Windows::Foundation::IAsyncAction SetSpeakToChatAsync(bool enabled);
    Windows::Foundation::IAsyncAction SetAdaptiveVolumeAsync(bool enabled);
    Windows::Foundation::IAsyncAction SetAutoPowerOffAsync(int32_t index);

    event_token StateChanged(Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Core::HeadsetState> const& handler);
    void StateChanged(event_token const& token) noexcept;
    event_token Disconnected(Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Windows::Foundation::IInspectable> const& handler);
    void Disconnected(event_token const& token) noexcept;

    void Close();

private:
    using Command = std::function<void(sony::protocol::HeadsetController&)>;

    Windows::Foundation::IAsyncAction RunAsync(Command command);

    hstring m_deviceName;
    // Shared so a command still running on the thread pool keeps it alive.
    std::shared_ptr<sony::protocol::HeadsetController> m_controller;

    event<Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Core::HeadsetState>> m_stateChanged;
    event<Windows::Foundation::TypedEventHandler<Core::HeadsetClient, Windows::Foundation::IInspectable>> m_disconnected;
};

} // namespace winrt::ConnectFlyout::Core::implementation

namespace winrt::ConnectFlyout::Core::factory_implementation {

struct HeadsetClient : HeadsetClientT<HeadsetClient, implementation::HeadsetClient> {};

} // namespace winrt::ConnectFlyout::Core::factory_implementation
````

- [ ] **Step 5: Create `native/ConnectFlyout.Core/HeadsetClient.cpp`**

````cpp
#include "pch.h"

#include "HeadsetClient.h"
#if __has_include("HeadsetClient.g.cpp")
#include "HeadsetClient.g.cpp"
#endif

#include "sony/protocol/DeviceProfileRegistry.h"
#include "sony/protocol/EqualizerPresets.h"
#include "sony/protocol/ErrorMapping.h"
#include "sony/transport/Logger.h"
#include "sony/transport/WindowsRfcommTransport.h"

#include <mutex>
#include <string>

using namespace winrt::Windows::Foundation;

namespace winrt::ConnectFlyout::Core::implementation {

namespace {

// =========================================================================
// LOGGING
// =========================================================================

std::mutex& logHandlerMutex() {
    static std::mutex mutex;
    return mutex;
}

struct LogHandlerHolder {
    Core::NativeLogHandler handler{nullptr};
};

// Never destroyed: releasing a managed delegate while the process shuts down
// can crash, so the last handler is deliberately leaked.
Core::NativeLogHandler& logHandler() {
    static auto* holder = new LogHandlerHolder{};
    return holder->handler;
}

Core::NativeLogLevel toNativeLevel(sony::LogLevel level) noexcept {
    switch (level) {
        case sony::LogLevel::Trace:
        case sony::LogLevel::Debug:
            return Core::NativeLogLevel::Debug;
        case sony::LogLevel::Info:
            return Core::NativeLogLevel::Information;
        case sony::LogLevel::Warn:
            return Core::NativeLogLevel::Warning;
        case sony::LogLevel::Error:
        case sony::LogLevel::Off:
            return Core::NativeLogLevel::Error;
    }
    return Core::NativeLogLevel::Information;
}

// =========================================================================
// CONVERSIONS
// =========================================================================

int32_t levelOrUnknown(const std::optional<int>& value) noexcept {
    return value.has_value() ? static_cast<int32_t>(*value) : -1;
}

Core::NoiseMode toNoiseMode(sony::protocol::NoiseControlMode mode) noexcept {
    switch (mode) {
        case sony::protocol::NoiseControlMode::NoiseCancelling:
            return Core::NoiseMode::NoiseCancelling;
        case sony::protocol::NoiseControlMode::Ambient:
            return Core::NoiseMode::Ambient;
        case sony::protocol::NoiseControlMode::Off:
            return Core::NoiseMode::Off;
    }
    return Core::NoiseMode::Off;
}

sony::protocol::NoiseControlMode fromNoiseMode(Core::NoiseMode mode) noexcept {
    switch (mode) {
        case Core::NoiseMode::NoiseCancelling:
            return sony::protocol::NoiseControlMode::NoiseCancelling;
        case Core::NoiseMode::Ambient:
            return sony::protocol::NoiseControlMode::Ambient;
        case Core::NoiseMode::Off:
            return sony::protocol::NoiseControlMode::Off;
    }
    return sony::protocol::NoiseControlMode::Off;
}

Core::HeadsetState toHeadsetState(const sony::protocol::DeviceState& state) {
    Core::HeadsetState result{};
    result.Battery.Main = levelOrUnknown(state.battery.main);
    result.Battery.Left = levelOrUnknown(state.battery.left);
    result.Battery.Right = levelOrUnknown(state.battery.right);
    result.Battery.CaseBattery = levelOrUnknown(state.battery.caseBattery);
    result.Battery.Charging = state.battery.charging;

    result.NoiseControl.Mode = toNoiseMode(state.noiseControl.mode);
    result.NoiseControl.AmbientLevel = state.noiseControl.ambientLevel;
    result.NoiseControl.FocusOnVoice = state.noiseControl.focusOnVoice;

    result.Equalizer.Preset = state.equalizer.preset;
    result.Equalizer.ClearBass = state.equalizer.clearBass;
    result.Equalizer.Band1 = state.equalizer.bands[0];
    result.Equalizer.Band2 = state.equalizer.bands[1];
    result.Equalizer.Band3 = state.equalizer.bands[2];
    result.Equalizer.Band4 = state.equalizer.bands[3];
    result.Equalizer.Band5 = state.equalizer.bands[4];

    result.Dsee = state.dsee;
    result.SpeakToChat = state.speakToChat;
    result.AdaptiveVolume = state.adaptiveVolume;
    result.AutoPowerOff = state.autoPowerOff;
    result.Firmware = to_hstring(state.firmware);
    result.Codec = to_hstring(state.codec);
    return result;
}

} // namespace

// =========================================================================
// CONSTRUCTION
// =========================================================================

HeadsetClient::HeadsetClient(hstring const& deviceName)
    : m_deviceName(deviceName),
      m_controller(std::make_shared<sony::protocol::HeadsetController>(
          std::make_unique<sony::transport::WindowsRfcommTransport>(),
          to_string(deviceName))) {}

void HeadsetClient::final_construct() {
    weak_ref<HeadsetClient> weak = get_weak();

    m_controller->onStateChanged([weak](const sony::protocol::DeviceState& state) {
        if (auto self = weak.get()) {
            self->m_stateChanged(*self, toHeadsetState(state));
        }
    });
    m_controller->onDisconnected([weak] {
        if (auto self = weak.get()) {
            self->m_disconnected(*self, nullptr);
        }
    });
}

// =========================================================================
// STATIC METHODS
// =========================================================================

void HeadsetClient::SetLogHandler(Core::NativeLogHandler const& handler) {
    {
        std::lock_guard lock(logHandlerMutex());
        logHandler() = handler;
    }

    sony::Logger::setLogSink([](sony::LogLevel level, std::string_view category, std::string_view message) {
        Core::NativeLogHandler current{nullptr};
        {
            std::lock_guard lock(logHandlerMutex());
            current = logHandler();
        }
        if (!current) {
            return;
        }
        try {
            current(toNativeLevel(level), to_hstring(std::string(category) + ": " + std::string(message)));
        } catch (...) {
            // Never let a logging failure take down the headset link.
        }
    });
}

void HeadsetClient::SetDebugLogging(bool enabled) {
    sony::Logger::setDeveloperMode(enabled);
    sony::Logger::setLogLevel(enabled ? sony::LogLevel::Debug : sony::LogLevel::Info);
}

com_array<Core::EqualizerPresetInfo> HeadsetClient::GetEqualizerPresets() {
    const auto& presets = sony::protocol::equalizerPresets();

    com_array<Core::EqualizerPresetInfo> result(static_cast<uint32_t>(presets.size()));
    for (size_t i = 0; i < presets.size(); ++i) {
        result[static_cast<uint32_t>(i)] = Core::EqualizerPresetInfo{
            static_cast<int32_t>(presets[i].preset),
            to_hstring(presets[i].displayName),
        };
    }
    return result;
}

// =========================================================================
// PROPERTIES
// =========================================================================

hstring HeadsetClient::DeviceName() const {
    return m_deviceName;
}

hstring HeadsetClient::ModelName() const {
    const auto model = m_controller->profile().model;
    if (model == sony::protocol::SonyModel::Unknown) {
        return m_deviceName;
    }
    return to_hstring(sony::protocol::to_string(model));
}

bool HeadsetClient::IsKnownModel() const {
    return m_controller->profile().model != sony::protocol::SonyModel::Unknown;
}

Core::ProtocolGeneration HeadsetClient::Protocol() const {
    return m_controller->generation() == sony::protocol::ProtocolGeneration::V2
        ? Core::ProtocolGeneration::V2
        : Core::ProtocolGeneration::V1;
}

Core::HeadsetCapabilities HeadsetClient::Capabilities() const {
    const auto& capabilities = m_controller->profile().capabilities;

    Core::HeadsetCapabilities result{};
    result.DualBattery = capabilities.dualBattery;
    result.NoiseCancelling = capabilities.noiseCancelling;
    result.AmbientSound = capabilities.ambientSound;
    result.FocusOnVoice = capabilities.focusOnVoice;
    result.Equalizer = capabilities.equalizer;
    result.ClearBass = capabilities.clearBass;
    result.Dsee = capabilities.dsee;
    result.SpeakToChat = capabilities.speakToChat;
    result.AdaptiveVolume = capabilities.adaptiveVolume;
    result.AutoPowerOff = capabilities.autoPowerOff;
    result.FirmwareInfo = capabilities.firmwareInfo;
    result.CodecInfo = capabilities.codecInfo;
    return result;
}

bool HeadsetClient::IsConnected() const {
    return m_controller->isConnected();
}

Core::HeadsetState HeadsetClient::State() const {
    return toHeadsetState(m_controller->state());
}

// =========================================================================
// COMMANDS
// =========================================================================

IAsyncAction HeadsetClient::ConnectAsync(hstring bluetoothAddress) {
    std::string address = to_string(bluetoothAddress);
    return RunAsync([address](sony::protocol::HeadsetController& controller) { controller.connect(address); });
}

void HeadsetClient::Disconnect() {
    m_controller->disconnect();
}

IAsyncAction HeadsetClient::RefreshBatteryAsync() {
    return RunAsync([](sony::protocol::HeadsetController& controller) { controller.refreshBattery(); });
}

IAsyncAction HeadsetClient::SetNoiseControlAsync(Core::NoiseControlInfo value) {
    const sony::protocol::NoiseControlState state{
        .mode = fromNoiseMode(value.Mode),
        .ambientLevel = value.AmbientLevel,
        .focusOnVoice = value.FocusOnVoice,
    };
    return RunAsync([state](sony::protocol::HeadsetController& controller) { controller.setNoiseControl(state); });
}

IAsyncAction HeadsetClient::SetEqualizerPresetAsync(int32_t preset) {
    return RunAsync([preset](sony::protocol::HeadsetController& controller) { controller.setEqualizerPreset(preset); });
}

IAsyncAction HeadsetClient::SetEqualizerCustomAsync(Core::EqualizerInfo value) {
    const std::array<int, 5> bands{value.Band1, value.Band2, value.Band3, value.Band4, value.Band5};
    const int clearBass = value.ClearBass;
    return RunAsync([clearBass, bands](sony::protocol::HeadsetController& controller) { controller.setEqualizerCustom(clearBass, bands); });
}

IAsyncAction HeadsetClient::SetDseeAsync(bool enabled) {
    return RunAsync([enabled](sony::protocol::HeadsetController& controller) { controller.setDsee(enabled); });
}

IAsyncAction HeadsetClient::SetSpeakToChatAsync(bool enabled) {
    return RunAsync([enabled](sony::protocol::HeadsetController& controller) { controller.setSpeakToChat(enabled); });
}

IAsyncAction HeadsetClient::SetAdaptiveVolumeAsync(bool enabled) {
    return RunAsync([enabled](sony::protocol::HeadsetController& controller) { controller.setAdaptiveVolume(enabled); });
}

IAsyncAction HeadsetClient::SetAutoPowerOffAsync(int32_t index) {
    return RunAsync([index](sony::protocol::HeadsetController& controller) { controller.setAutoPowerOff(index); });
}

IAsyncAction HeadsetClient::RunAsync(Command command) {
    auto strong = get_strong();
    auto controller = m_controller;

    co_await resume_background();

    // No C++ exception may cross the ABI: each one becomes an HRESULT.
    hresult failure{S_OK};
    hstring message;
    try {
        command(*controller);
    } catch (const sony::SonyException& ex) {
        failure = hresult{sony::protocol::toHresult(ex.code())};
        message = to_hstring(std::string_view{ex.what()});
    } catch (const std::exception& ex) {
        failure = hresult{E_FAIL};
        message = to_hstring(std::string_view{ex.what()});
    }

    if (failure != S_OK) {
        throw hresult_error(failure, message);
    }
}

// =========================================================================
// EVENTS
// =========================================================================

event_token HeadsetClient::StateChanged(TypedEventHandler<Core::HeadsetClient, Core::HeadsetState> const& handler) {
    return m_stateChanged.add(handler);
}

void HeadsetClient::StateChanged(event_token const& token) noexcept {
    m_stateChanged.remove(token);
}

event_token HeadsetClient::Disconnected(TypedEventHandler<Core::HeadsetClient, IInspectable> const& handler) {
    return m_disconnected.add(handler);
}

void HeadsetClient::Disconnected(event_token const& token) noexcept {
    m_disconnected.remove(token);
}

void HeadsetClient::Close() {
    m_controller->disconnect();
}

} // namespace winrt::ConnectFlyout::Core::implementation
````

- [ ] **Step 6: Create `native/ConnectFlyout.Core/packages.config`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<packages>
  <package id="Microsoft.Windows.CppWinRT" version="2.0.250303.1" targetFramework="native" />
</packages>
````

- [ ] **Step 7: Create `native/ConnectFlyout.Core/ConnectFlyout.Core.vcxproj`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<Project DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="..\..\packages\Microsoft.Windows.CppWinRT.2.0.250303.1\build\native\Microsoft.Windows.CppWinRT.props" Condition="Exists('..\..\packages\Microsoft.Windows.CppWinRT.2.0.250303.1\build\native\Microsoft.Windows.CppWinRT.props')" />

  <ItemGroup Label="ProjectConfigurations">
    <ProjectConfiguration Include="Debug|x64">
      <Configuration>Debug</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|x64">
      <Configuration>Release</Configuration>
      <Platform>x64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Debug|ARM64">
      <Configuration>Debug</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
    <ProjectConfiguration Include="Release|ARM64">
      <Configuration>Release</Configuration>
      <Platform>ARM64</Platform>
    </ProjectConfiguration>
  </ItemGroup>

  <PropertyGroup Label="Globals">
    <VCProjectVersion>17.0</VCProjectVersion>
    <ProjectGuid>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}</ProjectGuid>
    <ProjectName>ConnectFlyout.Core</ProjectName>
    <RootNamespace>ConnectFlyout.Core</RootNamespace>
    <DefaultLanguage>en-US</DefaultLanguage>
    <WindowsTargetPlatformVersion>10.0.26100.0</WindowsTargetPlatformVersion>
    <WindowsTargetPlatformMinVersion>10.0.22621.0</WindowsTargetPlatformMinVersion>
    <CppWinRTOptimized>true</CppWinRTOptimized>
    <CppWinRTRootNamespaceAutoMerge>true</CppWinRTRootNamespaceAutoMerge>
    <MinimalCoreWin>true</MinimalCoreWin>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.Default.props" />

  <PropertyGroup Label="Configuration">
    <ConfigurationType>DynamicLibrary</ConfigurationType>
    <PlatformToolset>$(DefaultPlatformToolset)</PlatformToolset>
    <CharacterSet>Unicode</CharacterSet>
    <GenerateManifest>false</GenerateManifest>
    <DesktopCompatible>true</DesktopCompatible>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Debug'">true</UseDebugLibraries>
    <UseDebugLibraries Condition="'$(Configuration)' == 'Release'">false</UseDebugLibraries>
  </PropertyGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.props" />

  <ItemDefinitionGroup>
    <ClCompile>
      <AdditionalIncludeDirectories>$(MSBuildThisFileDirectory);$(MSBuildThisFileDirectory)..\SonyProtocol\include;$(MSBuildThisFileDirectory)..\SonyTransport\include;%(AdditionalIncludeDirectories)</AdditionalIncludeDirectories>
      <AdditionalOptions>/bigobj %(AdditionalOptions)</AdditionalOptions>
    </ClCompile>
    <Link>
      <SubSystem>Windows</SubSystem>
      <ModuleDefinitionFile>ConnectFlyout.Core.def</ModuleDefinitionFile>
    </Link>
  </ItemDefinitionGroup>

  <ItemGroup>
    <ClInclude Include="pch.h" />
    <ClInclude Include="HeadsetClient.h">
      <DependentUpon>HeadsetClient.idl</DependentUpon>
    </ClInclude>
  </ItemGroup>

  <ItemGroup>
    <ClCompile Include="HeadsetClient.cpp">
      <DependentUpon>HeadsetClient.idl</DependentUpon>
    </ClCompile>
    <ClCompile Include="$(GeneratedFilesDir)module.g.cpp" />
  </ItemGroup>

  <ItemGroup>
    <Midl Include="HeadsetClient.idl" />
  </ItemGroup>

  <ItemGroup>
    <None Include="packages.config" />
    <None Include="ConnectFlyout.Core.def" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\SonyProtocol\SonyProtocol.vcxproj">
      <Project>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}</Project>
    </ProjectReference>
    <ProjectReference Include="..\SonyTransport\SonyTransport.vcxproj">
      <Project>{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}</Project>
    </ProjectReference>
  </ItemGroup>

  <Import Project="$(VCTargetsPath)\Microsoft.Cpp.targets" />

  <ImportGroup Label="ExtensionTargets">
    <Import Project="..\..\packages\Microsoft.Windows.CppWinRT.2.0.250303.1\build\native\Microsoft.Windows.CppWinRT.targets" Condition="Exists('..\..\packages\Microsoft.Windows.CppWinRT.2.0.250303.1\build\native\Microsoft.Windows.CppWinRT.targets')" />
  </ImportGroup>

  <Target Name="EnsureNuGetPackageBuildImports" BeforeTargets="PrepareForBuild">
    <Error Condition="!Exists('..\..\packages\Microsoft.Windows.CppWinRT.2.0.250303.1\build\native\Microsoft.Windows.CppWinRT.props')" Text="C++/WinRT isn't restored. Build with: msbuild -restore -p:RestorePackagesConfig=true" />
  </Target>
</Project>
````

- [ ] **Step 8: Build**

Expected: `ConnectFlyout.Core.vcxproj -> ...\bin\native\x64\Debug\ConnectFlyout.Core.dll` and a `ConnectFlyout.Core.winmd` beside it, no warnings.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild native\ConnectFlyout.Core\ConnectFlyout.Core.vcxproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
Get-ChildItem bin\native\x64\Debug\ConnectFlyout.Core.*
````

---

### Task 7: Projection, Headset Models and the WinRT Boundary Tests

**Files:**
- Create: `src/ConnectFlyout.Core.Projection/ConnectFlyout.Core.Projection.csproj`, `src/ConnectFlyout.Presentation/ConnectFlyout.Presentation.csproj`, `src/ConnectFlyout.Presentation/Headsets/{HeadsetModels,IHeadset,WinRtHeadset,HeadsetErrorMessages}.cs`, `src/ConnectFlyout.Presentation/Common/Glyphs.cs`, `src/ConnectFlyout.Presentation/Devices/SonyDeviceNameFilter.cs`
- Test: `src/ConnectFlyout.Presentation.Tests/ConnectFlyout.Presentation.Tests.csproj`, `src/ConnectFlyout.Presentation.Tests/.editorconfig`, `src/ConnectFlyout.Presentation.Tests/TestSupport.cs`, `src/ConnectFlyout.Presentation.Tests/BasicsTests.cs`, `src/ConnectFlyout.Presentation.Tests/NativeHeadsetTests.cs`

**Interfaces:**
- Consumes: `ConnectFlyout.Core.HeadsetClient` (Task 6).
- Produces: `IHeadset` (members listed in the file), records `BatteryLevels(int? Main, int? Left, int? Right, int? Case, bool Charging)` with `Lowest`, `NoiseControlSetting(NoiseMode, int AmbientLevel, bool FocusOnVoice)`, `EqualizerSetting(int Preset, int ClearBass, ImmutableArray<int> Bands)` with `ManualPreset = 0xa0`, `HeadsetFeatures(...)`, `HeadsetSnapshot(...)` with `Empty`, `EqualizerPresetOption(int Value, string Name)`; enums `NoiseMode { Off, NoiseCancelling, Ambient }`, `HeadsetConnectionState { Disconnected, Connecting, Connected }`; `WinRtHeadset(string deviceName) : IHeadset`; `HeadsetErrorMessages.Describe(Exception)` plus the five HRESULT constants; `Glyphs.Battery(int?)`, `Glyphs.Play/Pause/BatteryUnknown`; `SonyDeviceNameFilter.IsSony(string?)`.

- [ ] **Step 1: Create `src/ConnectFlyout.Core.Projection/ConnectFlyout.Core.Projection.csproj`**

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <!--
    C# projection of the native ConnectFlyout.Core Windows Runtime component (CsWinRT).
    The native DLL rides along as content, so every project that references this one
    (the app, its MSIX package and the tests) gets ConnectFlyout.Core.dll next to it.
  -->
  <PropertyGroup>
    <CsWinRTIncludes>ConnectFlyout.Core</CsWinRTIncludes>
    <CsWinRTGeneratedFilesDir>$(OutDir)</CsWinRTGeneratedFilesDir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.CsWinRT" Version="2.3.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\native\ConnectFlyout.Core\ConnectFlyout.Core.vcxproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="..\..\bin\native\$(Platform)\$(Configuration)\ConnectFlyout.Core.dll" Link="ConnectFlyout.Core.dll" CopyToOutputDirectory="PreserveNewest" Visible="false" />
  </ItemGroup>

</Project>
````

- [ ] **Step 2: Create `src/ConnectFlyout.Presentation/ConnectFlyout.Presentation.csproj`**

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ConnectFlyout.Core.Projection\ConnectFlyout.Core.Projection.csproj" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="ConnectFlyout.Presentation.Tests" />
  </ItemGroup>

</Project>
````

- [ ] **Step 3: Create `src/ConnectFlyout.Presentation.Tests/ConnectFlyout.Presentation.Tests.csproj`**

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="MSTest" Version="3.11.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ConnectFlyout.Presentation\ConnectFlyout.Presentation.csproj" />
  </ItemGroup>

</Project>
````

- [ ] **Step 4: Create `src/ConnectFlyout.Presentation.Tests/.editorconfig`**

````ini
[*.cs]
# Test fixtures dispose in [TestCleanup], which CA1001 can't see
dotnet_diagnostic.CA1001.severity = none

# Inline expected arrays read better in assertions
dotnet_diagnostic.CA1861.severity = none

# Tests call ILogger directly to exercise the file logger
dotnet_diagnostic.CA1848.severity = none

# Tests throw COMException to stand in for native HRESULT failures
dotnet_diagnostic.CA2201.severity = none
````

- [ ] **Step 5: Create `src/ConnectFlyout.Presentation.Tests/TestSupport.cs`**

````csharp
[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]

namespace ConnectFlyout.Presentation.Tests;

/// <summary>
/// Polling helper for work that finishes on another thread.
/// </summary>
internal static class TestWait
{
    public static async Task<bool> UntilAsync(Func<bool> condition, int timeoutMilliseconds = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        return condition();
    }
}
````

- [ ] **Step 6: Write the failing tests `src/ConnectFlyout.Presentation.Tests/BasicsTests.cs`**

````csharp
using System.Runtime.InteropServices;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class SonyDeviceNameFilterTests
{
    [TestMethod]
    [DataRow("WF-1000XM6")]
    [DataRow("WH-1000XM4")]
    [DataRow("LE_WF-1000XM6")]
    [DataRow("LinkBuds S")]
    [DataRow("Sony ULT WEAR")]
    public void AcceptsSonyNames(string name) => Assert.IsTrue(SonyDeviceNameFilter.IsSony(name));

    [TestMethod]
    [DataRow("Galaxy Buds2 Pro")]
    [DataRow("AirPods Pro")]
    [DataRow("")]
    [DataRow(null)]
    public void RejectsOtherNames(string? name) => Assert.IsFalse(SonyDeviceNameFilter.IsSony(name));
}

[TestClass]
public sealed class HeadsetErrorMessagesTests
{
    [TestMethod]
    public void DescribesTimeout() =>
        Assert.AreEqual("Your headphones didn't respond. Try again.", HeadsetErrorMessages.Describe(new COMException("", HeadsetErrorMessages.TimeoutHResult)));

    [TestMethod]
    public void DescribesDisconnect() =>
        Assert.AreEqual("Your headphones disconnected.", HeadsetErrorMessages.Describe(new COMException("", HeadsetErrorMessages.DisconnectedHResult)));

    [TestMethod]
    public void DescribesTransportFailure() =>
        Assert.AreEqual(
            "Couldn't connect. Make sure Bluetooth is on and your headphones are nearby.",
            HeadsetErrorMessages.Describe(new COMException("", HeadsetErrorMessages.TransportFailureHResult)));

    [TestMethod]
    public void FallsBackForUnknownErrors() =>
        Assert.AreEqual("Something went wrong talking to your headphones.", HeadsetErrorMessages.Describe(new InvalidOperationException()));
}

[TestClass]
public sealed class GlyphsTests
{
    [TestMethod]
    public void PicksBatteryStepByTens()
    {
        Assert.AreEqual("", Glyphs.Battery(5));
        Assert.AreEqual("", Glyphs.Battery(85));
        Assert.AreEqual("", Glyphs.Battery(100));
        Assert.AreEqual(Glyphs.BatteryUnknown, Glyphs.Battery(null));
    }
}

[TestClass]
public sealed class BatteryLevelsTests
{
    [TestMethod]
    public void LowestIgnoresCaseAndUnknownCells()
    {
        Assert.AreEqual(12, new BatteryLevels(null, 12, 40, 5, false).Lowest);
        Assert.AreEqual(60, new BatteryLevels(60, null, null, null, false).Lowest);
        Assert.IsNull(BatteryLevels.Unknown.Lowest);
    }
}
````

- [ ] **Step 7: Write the failing tests `src/ConnectFlyout.Presentation.Tests/NativeHeadsetTests.cs`**

````csharp
using ConnectFlyout.Presentation.Headsets;
using Core = ConnectFlyout.Core;

namespace ConnectFlyout.Presentation.Tests;

/// <summary>
/// Crosses the real WinRT boundary into ConnectFlyout.Core.dll, the same way the app does.
/// </summary>
[TestClass]
public sealed class HeadsetClientActivationTests
{
    [TestMethod]
    public void ResolvesWf1000Xm6Profile()
    {
        using var client = new Core.HeadsetClient("WF-1000XM6");

        Assert.AreEqual("WF-1000XM6", client.ModelName);
        Assert.IsTrue(client.IsKnownModel);
        Assert.AreEqual(Core.ProtocolGeneration.V2, client.Protocol);
        Assert.IsTrue(client.Capabilities.DualBattery);
        Assert.IsTrue(client.Capabilities.Dsee);
        Assert.IsFalse(client.IsConnected);
    }

    [TestMethod]
    public void UnknownNameKeepsDeviceNameAsModel()
    {
        using var client = new Core.HeadsetClient("Some Headphones");

        Assert.AreEqual("Some Headphones", client.ModelName);
        Assert.IsFalse(client.IsKnownModel);
        Assert.IsFalse(client.Capabilities.Equalizer);
    }

    [TestMethod]
    public async Task InvalidAddressFailsWithTransportFailureHResult()
    {
        using var client = new Core.HeadsetClient("WF-1000XM6");

        var exception = await Assert.ThrowsAsync<Exception>(async () => await client.ConnectAsync("not-an-address"));

        Assert.AreEqual(HeadsetErrorMessages.TransportFailureHResult, exception.HResult);
    }

    [TestMethod]
    public async Task CommandWhileDisconnectedFailsWithDisconnectedHResult()
    {
        using var client = new Core.HeadsetClient("WF-1000XM6");

        var exception = await Assert.ThrowsAsync<Exception>(async () => await client.SetDseeAsync(true));

        Assert.AreEqual(HeadsetErrorMessages.DisconnectedHResult, exception.HResult);
    }

    [TestMethod]
    public void ReturnsEqualizerPresets()
    {
        var presets = Core.HeadsetClient.GetEqualizerPresets();

        Assert.IsTrue(presets.Any(preset => preset.Value == 0x16 && preset.Name == "Bass Boost"));
        Assert.IsTrue(presets.Any(preset => preset.Value == 0xa0 && preset.Name == "Manual"));
    }

    [TestMethod]
    public void WrapperStartsWithUnknownBattery()
    {
        using var headset = new WinRtHeadset("WF-1000XM6");

        Assert.IsNull(headset.State.Battery.Left);
        Assert.AreEqual(5, headset.State.Equalizer.Bands.Length);
    }
}

/// <summary>
/// Talks to real headphones. Each test is skipped (Inconclusive) unless its address variable
/// is set, e.g. <c>$env:SONY_TEST_XM6_ADDRESS = "AC:80:0A:12:34:56"</c>.
/// </summary>
[TestClass]
public sealed class HardwareTests
{
    [TestMethod]
    [TestCategory("Hardware")]
    public async Task Wf1000Xm6RoundTrip() => await RoundTripAsync("WF-1000XM6", "SONY_TEST_XM6_ADDRESS");

    [TestMethod]
    [TestCategory("Hardware")]
    [TestCategory("XM4")]
    public async Task Wh1000Xm4RoundTrip() => await RoundTripAsync("WH-1000XM4", "SONY_TEST_XM4_ADDRESS");

    private static async Task RoundTripAsync(string model, string variable)
    {
        var address = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(address))
        {
            Assert.Inconclusive($"Set {variable} to the {model}'s Bluetooth address to run this test.");
        }

        using var headset = new WinRtHeadset(model);
        await headset.ConnectAsync(address);
        var original = headset.State.NoiseControl;

        Assert.IsNotNull(headset.State.Battery.Lowest ?? headset.State.Battery.Main, "battery was not reported");

        try
        {
            await headset.SetNoiseControlAsync(new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false));
            Assert.AreEqual(NoiseMode.NoiseCancelling, headset.State.NoiseControl.Mode);

            await headset.SetNoiseControlAsync(new NoiseControlSetting(NoiseMode.Ambient, 8, original.FocusOnVoice));
            Assert.AreEqual(NoiseMode.Ambient, headset.State.NoiseControl.Mode);
            Assert.AreEqual(8, headset.State.NoiseControl.AmbientLevel);
        }
        finally
        {
            await headset.SetNoiseControlAsync(original);
            headset.Disconnect();
        }
    }
}
````

- [ ] **Step 8: Run the build to see it fail**

Expected: FAIL with `CS0234`/`CS0246` for `ConnectFlyout.Presentation.Headsets`, `Devices` and `Common`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 9: Create `src/ConnectFlyout.Presentation/Headsets/HeadsetModels.cs`**

````csharp
using System.Collections.Immutable;

namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// Noise control mode on the headset.
/// </summary>
public enum NoiseMode
{
    Off,
    NoiseCancelling,
    Ambient,
}

/// <summary>
/// State of the app's Bluetooth control link, separate from the Windows audio connection.
/// </summary>
public enum HeadsetConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Battery levels in percent. Null means the headset didn't report that cell.
/// </summary>
public sealed record BatteryLevels(int? Main, int? Left, int? Right, int? Case, bool Charging)
{
    public static BatteryLevels Unknown { get; } = new(null, null, null, null, false);

    /// <summary>
    /// Lowest level across the headset's own cells (not the case). Null when none are known.
    /// </summary>
    public int? Lowest => new[] { Main, Left, Right }.Min();
}

/// <summary>
/// Noise control settings as the headset reports or receives them.
/// </summary>
public sealed record NoiseControlSetting(NoiseMode Mode, int AmbientLevel, bool FocusOnVoice);

/// <summary>
/// Equalizer preset plus the custom curve. Clear Bass and band values run from -10 to 10.
/// </summary>
public sealed record EqualizerSetting(int Preset, int ClearBass, ImmutableArray<int> Bands)
{
    /// <summary>
    /// Preset value the headset uses for a custom curve.
    /// </summary>
    public const int ManualPreset = 0xa0;
}

/// <summary>
/// Features the connected model supports.
/// </summary>
public sealed record HeadsetFeatures(
    bool DualBattery,
    bool NoiseCancelling,
    bool AmbientSound,
    bool FocusOnVoice,
    bool Equalizer,
    bool ClearBass,
    bool Dsee,
    bool SpeakToChat,
    bool AdaptiveVolume,
    bool AutoPowerOff,
    bool FirmwareInfo,
    bool CodecInfo);

/// <summary>
/// Everything the headset last confirmed.
/// </summary>
public sealed record HeadsetSnapshot(
    BatteryLevels Battery,
    NoiseControlSetting NoiseControl,
    EqualizerSetting Equalizer,
    bool Dsee,
    bool SpeakToChat,
    bool AdaptiveVolume,
    int AutoPowerOff,
    string Firmware,
    string Codec)
{
    public static HeadsetSnapshot Empty { get; } = new(
        BatteryLevels.Unknown,
        new NoiseControlSetting(NoiseMode.Off, 0, false),
        new EqualizerSetting(0, 0, [0, 0, 0, 0, 0]),
        false,
        false,
        false,
        0,
        "",
        "");
}

/// <summary>
/// One entry in the equalizer preset picker.
/// </summary>
public sealed record EqualizerPresetOption(int Value, string Name);
````

- [ ] **Step 10: Create `src/ConnectFlyout.Presentation/Headsets/IHeadset.cs`**

````csharp
namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// One Sony headset's control link.
/// </summary>
/// <remarks>
/// Every command completes once the headset acknowledges it, and <see cref="State"/> only
/// changes after that, so callers can revert their UI to <see cref="State"/> when a command fails.
/// Events are raised on a background thread.
/// </remarks>
public interface IHeadset : IDisposable
{
    string DeviceName { get; }

    string ModelName { get; }

    bool IsKnownModel { get; }

    HeadsetFeatures Features { get; }

    HeadsetSnapshot State { get; }

    IReadOnlyList<EqualizerPresetOption> EqualizerPresets { get; }

    event EventHandler<HeadsetSnapshot>? StateChanged;

    event EventHandler? Disconnected;

    Task ConnectAsync(string bluetoothAddress);

    void Disconnect();

    Task RefreshBatteryAsync();

    Task SetNoiseControlAsync(NoiseControlSetting value);

    Task SetEqualizerPresetAsync(int preset);

    Task SetEqualizerCustomAsync(EqualizerSetting value);

    Task SetDseeAsync(bool enabled);

    Task SetSpeakToChatAsync(bool enabled);

    Task SetAdaptiveVolumeAsync(bool enabled);

    Task SetAutoPowerOffAsync(int index);
}
````

- [ ] **Step 11: Create `src/ConnectFlyout.Presentation/Headsets/WinRtHeadset.cs`**

````csharp
using Core = ConnectFlyout.Core;

namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// <see cref="IHeadset"/> backed by the native <see cref="Core.HeadsetClient"/>.
/// </summary>
public sealed class WinRtHeadset : IHeadset
{
    private readonly Core.HeadsetClient _client;

    public WinRtHeadset(string deviceName)
    {
        _client = new Core.HeadsetClient(deviceName);
        Features = ToFeatures(_client.Capabilities);
        EqualizerPresets = [.. Core.HeadsetClient.GetEqualizerPresets().Select(preset => new EqualizerPresetOption(preset.Value, preset.Name))];

        _client.StateChanged += OnStateChanged;
        _client.Disconnected += OnDisconnected;
    }

    public event EventHandler<HeadsetSnapshot>? StateChanged;

    public event EventHandler? Disconnected;

    public string DeviceName => _client.DeviceName;

    public string ModelName => _client.ModelName;

    public bool IsKnownModel => _client.IsKnownModel;

    public HeadsetFeatures Features { get; }

    public HeadsetSnapshot State => ToSnapshot(_client.State);

    public IReadOnlyList<EqualizerPresetOption> EqualizerPresets { get; }

    public Task ConnectAsync(string bluetoothAddress) => _client.ConnectAsync(bluetoothAddress).AsTask();

    public void Disconnect() => _client.Disconnect();

    public Task RefreshBatteryAsync() => _client.RefreshBatteryAsync().AsTask();

    public Task SetNoiseControlAsync(NoiseControlSetting value) =>
        _client.SetNoiseControlAsync(new Core.NoiseControlInfo((Core.NoiseMode)value.Mode, value.AmbientLevel, value.FocusOnVoice)).AsTask();

    public Task SetEqualizerPresetAsync(int preset) => _client.SetEqualizerPresetAsync(preset).AsTask();

    public Task SetEqualizerCustomAsync(EqualizerSetting value) =>
        _client.SetEqualizerCustomAsync(new Core.EqualizerInfo(
            value.Preset,
            value.ClearBass,
            value.Bands[0],
            value.Bands[1],
            value.Bands[2],
            value.Bands[3],
            value.Bands[4])).AsTask();

    public Task SetDseeAsync(bool enabled) => _client.SetDseeAsync(enabled).AsTask();

    public Task SetSpeakToChatAsync(bool enabled) => _client.SetSpeakToChatAsync(enabled).AsTask();

    public Task SetAdaptiveVolumeAsync(bool enabled) => _client.SetAdaptiveVolumeAsync(enabled).AsTask();

    public Task SetAutoPowerOffAsync(int index) => _client.SetAutoPowerOffAsync(index).AsTask();

    public void Dispose()
    {
        _client.StateChanged -= OnStateChanged;
        _client.Disconnected -= OnDisconnected;
        _client.Dispose();
    }

    internal static HeadsetSnapshot ToSnapshot(Core.HeadsetState state) => new(
        new BatteryLevels(
            Known(state.Battery.Main),
            Known(state.Battery.Left),
            Known(state.Battery.Right),
            Known(state.Battery.CaseBattery),
            state.Battery.Charging),
        new NoiseControlSetting((NoiseMode)state.NoiseControl.Mode, state.NoiseControl.AmbientLevel, state.NoiseControl.FocusOnVoice),
        new EqualizerSetting(
            state.Equalizer.Preset,
            state.Equalizer.ClearBass,
            [state.Equalizer.Band1, state.Equalizer.Band2, state.Equalizer.Band3, state.Equalizer.Band4, state.Equalizer.Band5]),
        state.Dsee,
        state.SpeakToChat,
        state.AdaptiveVolume,
        state.AutoPowerOff,
        state.Firmware ?? "",
        state.Codec ?? "");

    private static int? Known(int level) => level < 0 ? null : level;

    private static HeadsetFeatures ToFeatures(Core.HeadsetCapabilities capabilities) => new(
        capabilities.DualBattery,
        capabilities.NoiseCancelling,
        capabilities.AmbientSound,
        capabilities.FocusOnVoice,
        capabilities.Equalizer,
        capabilities.ClearBass,
        capabilities.Dsee,
        capabilities.SpeakToChat,
        capabilities.AdaptiveVolume,
        capabilities.AutoPowerOff,
        capabilities.FirmwareInfo,
        capabilities.CodecInfo);

    private void OnStateChanged(Core.HeadsetClient sender, Core.HeadsetState args) => StateChanged?.Invoke(this, ToSnapshot(args));

    private void OnDisconnected(Core.HeadsetClient sender, object args) => Disconnected?.Invoke(this, EventArgs.Empty);
}
````

- [ ] **Step 12: Create `src/ConnectFlyout.Presentation/Headsets/HeadsetErrorMessages.cs`**

````csharp
namespace ConnectFlyout.Presentation.Headsets;

/// <summary>
/// Turns the HRESULTs the native layer reports (sony::protocol::toHresult) into messages for people.
/// </summary>
public static class HeadsetErrorMessages
{
    public const int TimeoutHResult = unchecked((int)0x800705B4);
    public const int DisconnectedHResult = unchecked((int)0x8007048F);
    public const int UnsupportedHResult = unchecked((int)0x80004001);
    public const int InvalidDataHResult = unchecked((int)0x8007000D);
    public const int TransportFailureHResult = unchecked((int)0x800704C9);

    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.HResult switch
        {
            TimeoutHResult => "Your headphones didn't respond. Try again.",
            DisconnectedHResult => "Your headphones disconnected.",
            UnsupportedHResult => "These headphones don't support that setting.",
            InvalidDataHResult => "Your headphones sent a reply the app didn't understand.",
            TransportFailureHResult => "Couldn't connect. Make sure Bluetooth is on and your headphones are nearby.",
            _ => "Something went wrong talking to your headphones.",
        };
    }
}
````

- [ ] **Step 13: Create `src/ConnectFlyout.Presentation/Common/Glyphs.cs`**

````csharp
namespace ConnectFlyout.Presentation.Common;

/// <summary>
/// Segoe Fluent Icons glyphs the view models pick at runtime.
/// </summary>
public static class Glyphs
{
    public const string Play = "";
    public const string Pause = "";
    public const string BatteryUnknown = "";

    /// <summary>
    /// Battery0 (E850) to Battery9 (E859) in 10% steps, Battery10 (E83F) at 100%.
    /// </summary>
    public static string Battery(int? level)
    {
        if (level is null)
        {
            return BatteryUnknown;
        }
        var step = Math.Clamp(level.Value / 10, 0, 10);
        return step == 10 ? "" : ((char)(0xE850 + step)).ToString();
    }
}
````

- [ ] **Step 14: Create `src/ConnectFlyout.Presentation/Devices/SonyDeviceNameFilter.cs`**

````csharp
namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// Decides whether a Bluetooth device name belongs to a Sony headset.
/// </summary>
/// <remarks>
/// Same name markers upstream's platform discovery uses.
/// </remarks>
public static class SonyDeviceNameFilter
{
    private static readonly string[] Markers = ["WH-", "WF-", "WI-", "MDR-", "LINKBUDS", "ULT WEAR", "SONY"];

    public static bool IsSony(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
````

- [ ] **Step 15: Build and run the tests**

Expected: `Passed!  - Failed: 0, Passed: 21, Skipped: 1`. The skip is the XM6 hardware test (no address set).

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
dotnet test src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj --no-build -p:Platform=x64 --filter "TestCategory!=XM4"
````

> **Troubleshooting (not expected):** if the build reports that the projection can't consume the `.vcxproj` reference (for example `NETSDK1130` or "no winmd found"), change the `ProjectReference` in `ConnectFlyout.Core.Projection.csproj` to
> `<ProjectReference Include="..\..\native\ConnectFlyout.Core\ConnectFlyout.Core.vcxproj" ReferenceOutputAssembly="false" />`
> and add `<CsWinRTInputs Include="..\..\bin\native\$(Platform)\$(Configuration)\ConnectFlyout.Core.winmd" />`. That's the exact form the pre-flight checks built and tested.
>
> If restore says the Windows SDK projection needs a newer `Microsoft.Windows.CsWinRT`, set the version it names in the projection project and tell Devin.

---

### Task 8: Settings, Scenes and Page Rules

**Files:**
- Create: `src/ConnectFlyout.Presentation/Settings/ISettingsStore.cs`, `src/ConnectFlyout.Presentation/Settings/AppSettings.cs`, `src/ConnectFlyout.Presentation/Settings/StartupTaskService.cs`, `src/ConnectFlyout.Presentation/Scenes/Scene.cs`, `src/ConnectFlyout.Presentation/Navigation/FlyoutNavigator.cs`
- Test: `src/ConnectFlyout.Presentation.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: `NoiseControlSetting`, `NoiseMode` (Task 7).
- Produces: `ISettingsStore` (`GetString`, `SetString(key, null)` removes), `InMemorySettingsStore`, `LocalSettingsStore`; `AppSettings(ISettingsStore)` with `RememberedHeadsetId`, `LowBatteryNotifications` (default true), `Theme` (`AppTheme { System, Light, Dark }`), `DebugLogging`, `Scenes` (falls back to `Scene.Defaults`), `IsAutoConnectEnabled(id)` (default true), `SetAutoConnectEnabled(id, bool)`; `Scene(string Name, string Glyph, NoiseControlSetting Setting)` with `Defaults` (Focus, Office, Aware); `FlyoutNavigator(AppSettings)` with `Resolve(IReadOnlyList<string>) -> FlyoutRoute(FlyoutPageKind Kind, string? HeadsetId, bool ShowBack)`, `Pick(id)`, `Back()`; `IStartupTaskService` + `StartupTaskService` (task ID `ConnectFlyoutStartup`).

- [ ] **Step 1: Write the failing tests `src/ConnectFlyout.Presentation.Tests/SettingsTests.cs`**

````csharp
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void UsesDefaultsWhenNothingIsSaved()
    {
        var settings = new AppSettings(new InMemorySettingsStore());

        Assert.IsNull(settings.RememberedHeadsetId);
        Assert.IsTrue(settings.LowBatteryNotifications);
        Assert.AreEqual(AppTheme.System, settings.Theme);
        Assert.IsFalse(settings.DebugLogging);
        Assert.IsTrue(settings.IsAutoConnectEnabled("AC:80:0A:12:34:56"));
        CollectionAssert.AreEqual(Scene.Defaults.ToList(), settings.Scenes.ToList());
    }

    [TestMethod]
    public void RoundTripsEveryValue()
    {
        var store = new InMemorySettingsStore();
        var settings = new AppSettings(store)
        {
            RememberedHeadsetId = "AC:80:0A:12:34:56",
            LowBatteryNotifications = false,
            Theme = AppTheme.Dark,
            DebugLogging = true,
        };
        settings.SetAutoConnectEnabled("AC:80:0A:12:34:56", false);

        var reloaded = new AppSettings(store);
        Assert.AreEqual("AC:80:0A:12:34:56", reloaded.RememberedHeadsetId);
        Assert.IsFalse(reloaded.LowBatteryNotifications);
        Assert.AreEqual(AppTheme.Dark, reloaded.Theme);
        Assert.IsTrue(reloaded.DebugLogging);
        Assert.IsFalse(reloaded.IsAutoConnectEnabled("AC:80:0A:12:34:56"));
    }

    [TestMethod]
    public void RoundTripsScenes()
    {
        var store = new InMemorySettingsStore();
        Scene[] scenes = [new Scene("Gym", "", new NoiseControlSetting(NoiseMode.Ambient, 14, true))];
        new AppSettings(store).Scenes = scenes;

        CollectionAssert.AreEqual(scenes, new AppSettings(store).Scenes.ToList());
    }

    [TestMethod]
    public void FallsBackToDefaultScenesWhenSavedJsonIsBroken()
    {
        var store = new InMemorySettingsStore();
        store.SetString("Scenes", "{not json");

        CollectionAssert.AreEqual(Scene.Defaults.ToList(), new AppSettings(store).Scenes.ToList());
    }

    [TestMethod]
    public void ClearingRememberedHeadsetRemovesIt()
    {
        var settings = new AppSettings(new InMemorySettingsStore()) { RememberedHeadsetId = "A" };
        settings.RememberedHeadsetId = null;

        Assert.IsNull(settings.RememberedHeadsetId);
    }
}

[TestClass]
public sealed class FlyoutNavigatorTests
{
    private const string Xm6 = "AC:80:0A:00:00:06";
    private const string Xm4 = "AC:80:0A:00:00:04";

    private readonly AppSettings _settings = new(new InMemorySettingsStore());

    [TestMethod]
    public void NoHeadsetsShowsEmptyPage() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Empty, null, false), Navigator().Resolve([]));

    [TestMethod]
    public void OneHeadsetGoesStraightToItsPage() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, false), Navigator().Resolve([Xm6]));

    [TestMethod]
    public void SeveralHeadsetsWithNothingRememberedShowsPicker() =>
        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Picker, null, false), Navigator().Resolve([Xm6, Xm4]));

    [TestMethod]
    public void PickingRemembersTheHeadset()
    {
        Navigator().Pick(Xm4);

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm4, true), Navigator().Resolve([Xm6, Xm4]));
    }

    [TestMethod]
    public void RememberedHeadsetDisconnectedFallsBackToPicker()
    {
        _settings.RememberedHeadsetId = Xm6;

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Picker, null, false), Navigator().Resolve([Xm4, "AC:80:0A:00:00:05"]));
    }

    [TestMethod]
    public void RememberedHeadsetDisconnectedFallsBackToOnlyRemainingHeadset()
    {
        _settings.RememberedHeadsetId = Xm6;

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm4, false), Navigator().Resolve([Xm4]));
        Assert.AreEqual(Xm6, _settings.RememberedHeadsetId);
    }

    [TestMethod]
    public void RememberedHeadsetReconnectingReturnsToItsPage()
    {
        _settings.RememberedHeadsetId = Xm6;
        _ = Navigator().Resolve([Xm4]);

        Assert.AreEqual(new FlyoutRoute(FlyoutPageKind.Device, Xm6, true), Navigator().Resolve([Xm4, Xm6]));
    }

    [TestMethod]
    public void BackClearsTheRememberedHeadset()
    {
        _settings.RememberedHeadsetId = Xm6;
        Navigator().Back();

        Assert.IsNull(_settings.RememberedHeadsetId);
        Assert.AreEqual(FlyoutPageKind.Picker, Navigator().Resolve([Xm6, Xm4]).Kind);
    }

    [TestMethod]
    public void UnknownRememberedHeadsetShowsPicker()
    {
        _settings.RememberedHeadsetId = "UNPAIRED";

        Assert.AreEqual(FlyoutPageKind.Picker, Navigator().Resolve([Xm6, Xm4]).Kind);
    }

    private FlyoutNavigator Navigator() => new(_settings);
}
````

- [ ] **Step 2: Run the build to see it fail**

Expected: FAIL with `CS0234` for `ConnectFlyout.Presentation.Settings`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 3: Create `src/ConnectFlyout.Presentation/Settings/ISettingsStore.cs`**

````csharp
using Windows.Foundation.Collections;
using Windows.Storage;

namespace ConnectFlyout.Presentation.Settings;

/// <summary>
/// String key/value storage for app settings.
/// </summary>
public interface ISettingsStore
{
    string? GetString(string key);

    /// <summary>
    /// Stores the value, or removes the key when <paramref name="value"/> is null.
    /// </summary>
    void SetString(string key, string? value);
}

/// <summary>
/// Settings kept in memory. Used by tests.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public string? GetString(string key) => _values.GetValueOrDefault(key);

    public void SetString(string key, string? value)
    {
        if (value is null)
        {
            _values.Remove(key);
            return;
        }
        _values[key] = value;
    }
}

/// <summary>
/// Settings in the package's local app data. Needs package identity (the installed MSIX).
/// </summary>
public sealed class LocalSettingsStore : ISettingsStore
{
    private readonly IPropertySet _values = ApplicationData.Current.LocalSettings.Values;

    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value as string : null;

    public void SetString(string key, string? value)
    {
        if (value is null)
        {
            _values.Remove(key);
            return;
        }
        _values[key] = value;
    }
}
````

- [ ] **Step 4: Create `src/ConnectFlyout.Presentation/Scenes/Scene.cs`**

````csharp
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Scenes;

/// <summary>
/// A saved noise control combination shown as a button in the flyout.
/// </summary>
/// <param name="Name">Button label.</param>
/// <param name="Glyph">Segoe Fluent Icons glyph.</param>
/// <param name="Setting">What the button applies.</param>
public sealed record Scene(string Name, string Glyph, NoiseControlSetting Setting)
{
    /// <summary>
    /// Scenes a fresh install starts with.
    /// </summary>
    public static IReadOnlyList<Scene> Defaults { get; } =
    [
        new Scene("Focus", "", new NoiseControlSetting(NoiseMode.NoiseCancelling, 0, false)),
        new Scene("Office", "", new NoiseControlSetting(NoiseMode.Ambient, 10, true)),
        new Scene("Aware", "", new NoiseControlSetting(NoiseMode.Ambient, 20, false)),
    ];
}
````

- [ ] **Step 5: Create `src/ConnectFlyout.Presentation/Settings/AppSettings.cs`**

````csharp
using System.Globalization;
using System.Text.Json;
using ConnectFlyout.Presentation.Scenes;

namespace ConnectFlyout.Presentation.Settings;

/// <summary>
/// App theme choice.
/// </summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Typed access to every app setting.
/// </summary>
public sealed class AppSettings
{
    private const string RememberedHeadsetKey = "RememberedHeadset";
    private const string LowBatteryNotificationsKey = "LowBatteryNotifications";
    private const string ThemeKey = "Theme";
    private const string DebugLoggingKey = "DebugLogging";
    private const string ScenesKey = "Scenes";
    private const string AutoConnectKeyPrefix = "AutoConnect.";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ISettingsStore _store;

    public AppSettings(ISettingsStore store)
    {
        _store = store;
    }

    /// <summary>
    /// Headset the flyout reopens on, by <see cref="Devices.ManagedHeadset.Id"/>. Null shows the picker.
    /// </summary>
    public string? RememberedHeadsetId
    {
        get => _store.GetString(RememberedHeadsetKey);
        set => _store.SetString(RememberedHeadsetKey, value);
    }

    public bool LowBatteryNotifications
    {
        get => GetBool(LowBatteryNotificationsKey, true);
        set => SetBool(LowBatteryNotificationsKey, value);
    }

    public AppTheme Theme
    {
        get => Enum.TryParse<AppTheme>(_store.GetString(ThemeKey), out var theme) ? theme : AppTheme.System;
        set => _store.SetString(ThemeKey, value.ToString());
    }

    public bool DebugLogging
    {
        get => GetBool(DebugLoggingKey, false);
        set => SetBool(DebugLoggingKey, value);
    }

    /// <summary>
    /// Saved scenes, or <see cref="Scene.Defaults"/> when none are saved or the saved JSON is unreadable.
    /// </summary>
    public IReadOnlyList<Scene> Scenes
    {
        get
        {
            var json = _store.GetString(ScenesKey);
            if (string.IsNullOrEmpty(json))
            {
                return Scene.Defaults;
            }
            try
            {
                return JsonSerializer.Deserialize<List<Scene>>(json, JsonOptions) ?? [.. Scene.Defaults];
            }
            catch (JsonException)
            {
                return Scene.Defaults;
            }
        }
        set => _store.SetString(ScenesKey, JsonSerializer.Serialize(value, JsonOptions));
    }

    public bool IsAutoConnectEnabled(string headsetId) => GetBool(AutoConnectKeyPrefix + headsetId, true);

    public void SetAutoConnectEnabled(string headsetId, bool enabled) => SetBool(AutoConnectKeyPrefix + headsetId, enabled);

    private bool GetBool(string key, bool fallback) =>
        bool.TryParse(_store.GetString(key), out var value) ? value : fallback;

    private void SetBool(string key, bool value) => _store.SetString(key, value.ToString(CultureInfo.InvariantCulture));
}
````

- [ ] **Step 6: Create `src/ConnectFlyout.Presentation/Settings/StartupTaskService.cs`**

````csharp
using Windows.ApplicationModel;

namespace ConnectFlyout.Presentation.Settings;

/// <summary>
/// Launch-at-sign-in switch.
/// </summary>
public interface IStartupTaskService
{
    Task<bool> IsEnabledAsync();

    /// <summary>
    /// Returns whether the task ended up enabled. Windows can refuse when the person or a policy
    /// turned it off in Settings.
    /// </summary>
    Task<bool> SetEnabledAsync(bool enabled);
}

/// <summary>
/// <see cref="IStartupTaskService"/> over the MSIX startup task declared in Package.appxmanifest.
/// </summary>
public sealed class StartupTaskService : IStartupTaskService
{
    public const string TaskId = "ConnectFlyoutStartup";

    public async Task<bool> IsEnabledAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (!enabled)
        {
            task.Disable();
            return false;
        }

        var state = await task.RequestEnableAsync();
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }
}
````

- [ ] **Step 7: Create `src/ConnectFlyout.Presentation/Navigation/FlyoutNavigator.cs`**

````csharp
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Navigation;

/// <summary>
/// Which page the flyout shows.
/// </summary>
public enum FlyoutPageKind
{
    Empty,
    Picker,
    Device,
}

/// <summary>
/// The page to show, the headset it's for, and whether the back arrow is visible.
/// </summary>
public sealed record FlyoutRoute(FlyoutPageKind Kind, string? HeadsetId, bool ShowBack);

/// <summary>
/// Picks the flyout page from the connected headsets and the remembered choice.
/// </summary>
/// <remarks>
/// Rules:
/// 1. No headsets: empty page.
/// 2. One headset: its device page, no back arrow.
/// 3. Several headsets and the remembered one is among them: its device page with a back arrow.
/// 4. Several headsets otherwise: the picker.
/// The remembered choice survives disconnects, so the device page comes back when that headset
/// reconnects. Only <see cref="Back"/> clears it.
/// </remarks>
public sealed class FlyoutNavigator
{
    private readonly AppSettings _settings;

    public FlyoutNavigator(AppSettings settings)
    {
        _settings = settings;
    }

    public FlyoutRoute Resolve(IReadOnlyList<string> connectedHeadsetIds)
    {
        ArgumentNullException.ThrowIfNull(connectedHeadsetIds);

        if (connectedHeadsetIds.Count == 0)
        {
            return new FlyoutRoute(FlyoutPageKind.Empty, null, false);
        }
        if (connectedHeadsetIds.Count == 1)
        {
            return new FlyoutRoute(FlyoutPageKind.Device, connectedHeadsetIds[0], false);
        }

        var remembered = _settings.RememberedHeadsetId;
        if (remembered is not null && connectedHeadsetIds.Contains(remembered))
        {
            return new FlyoutRoute(FlyoutPageKind.Device, remembered, true);
        }
        return new FlyoutRoute(FlyoutPageKind.Picker, null, false);
    }

    public void Pick(string headsetId) => _settings.RememberedHeadsetId = headsetId;

    public void Back() => _settings.RememberedHeadsetId = null;
}
````

- [ ] **Step 8: Build and run the tests**

Expected: `Passed: 35, Skipped: 1`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
dotnet test src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj --no-build -p:Platform=x64 --filter "TestCategory!=XM4"
````

---

### Task 9: Flyout Placement, Logging and Slider Throttling

**Files:**
- Create: `src/ConnectFlyout.Presentation/Placement/FlyoutPlacement.cs`, `src/ConnectFlyout.Presentation/Logging/RollingFileLoggerProvider.cs`, `src/ConnectFlyout.Presentation/Logging/LogMessages.cs`, `src/ConnectFlyout.Presentation/Common/Throttler.cs`, `src/ConnectFlyout.Presentation/Common/UiContext.cs`
- Test: `src/ConnectFlyout.Presentation.Tests/PlacementAndLoggingTests.cs`

**Interfaces:**
- Produces: `PixelRect(int Left, int Top, int Right, int Bottom)` with `Width`/`Height`; `TaskbarEdge`; `FlyoutPlacement.DetectEdge(monitor, workArea)`, `FlyoutPlacement.Calculate(monitor, workArea, scale)` (360 × 640 DIP, 12 DIP margin); `LogLevelSwitch.MinimumLevel`; `RollingFileLoggerProvider(directory, levelSwitch, timeProvider, maxFileBytes = 1_048_576, maxFiles = 5)` with `CurrentFilePath`; internal `LogMessages` (source-generated, used from Task 10 on); `Throttler(TimeSpan, TimeProvider)` with `Run(Func<Task>)`, `HasPending`; `UiContext.Post(Action)`.

- [ ] **Step 1: Write the failing tests `src/ConnectFlyout.Presentation.Tests/PlacementAndLoggingTests.cs`**

````csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Placement;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class FlyoutPlacementTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);

    [TestMethod]
    public void BottomTaskbarPutsFlyoutBottomRight()
    {
        var workArea = new PixelRect(0, 0, 1920, 1032);

        Assert.AreEqual(TaskbarEdge.Bottom, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1548, 380, 1908, 1020), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void TopTaskbarPutsFlyoutTopRight()
    {
        var workArea = new PixelRect(0, 48, 1920, 1080);

        Assert.AreEqual(TaskbarEdge.Top, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1548, 60, 1908, 700), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void LeftTaskbarStillPutsFlyoutOnTheRight()
    {
        var workArea = new PixelRect(48, 0, 1920, 1080);

        Assert.AreEqual(TaskbarEdge.Left, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1548, 428, 1908, 1068), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void RightTaskbarKeepsFlyoutLeftOfIt()
    {
        var workArea = new PixelRect(0, 0, 1872, 1080);

        Assert.AreEqual(TaskbarEdge.Right, FlyoutPlacement.DetectEdge(Monitor, workArea));
        Assert.AreEqual(new PixelRect(1500, 428, 1860, 1068), FlyoutPlacement.Calculate(Monitor, workArea, 1.0));
    }

    [TestMethod]
    public void AutoHiddenTaskbarCountsAsBottom() =>
        Assert.AreEqual(TaskbarEdge.Bottom, FlyoutPlacement.DetectEdge(Monitor, Monitor));

    [TestMethod]
    public void ScalesWithDpi()
    {
        var monitor = new PixelRect(0, 0, 2880, 1620);
        var workArea = new PixelRect(0, 0, 2880, 1548);

        Assert.AreEqual(new PixelRect(2322, 570, 2862, 1530), FlyoutPlacement.Calculate(monitor, workArea, 1.5));
    }

    [TestMethod]
    public void SecondaryMonitorUsesItsOwnCoordinates()
    {
        var monitor = new PixelRect(-1920, 0, 0, 1080);
        var workArea = new PixelRect(-1920, 0, 0, 1032);

        Assert.AreEqual(new PixelRect(-372, 380, -12, 1020), FlyoutPlacement.Calculate(monitor, workArea, 1.0));
    }

    [TestMethod]
    public void ShrinksToFitShortWorkArea()
    {
        var monitor = new PixelRect(0, 0, 1280, 600);
        var workArea = new PixelRect(0, 0, 1280, 552);

        var rect = FlyoutPlacement.Calculate(monitor, workArea, 2.0);

        Assert.IsTrue(rect.Top >= workArea.Top && rect.Bottom <= workArea.Bottom, $"{rect} leaves {workArea}");
        Assert.AreEqual(1280 - 24, rect.Right);
    }
}

[TestClass]
public sealed class RollingFileLoggerTests
{
    private string _directory = "";

    [TestInitialize]
    public void CreateDirectory() => _directory = Path.Combine(Path.GetTempPath(), "ConnectFlyout-tests", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void DeleteDirectory()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [TestMethod]
    public void WritesFormattedLine()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 23, 15, 4, 5, 123, TimeSpan.Zero));
        using var provider = new RollingFileLoggerProvider(_directory, new LogLevelSwitch(), time);

        provider.CreateLogger("Tests").Log(LogLevel.Information, "hello {Name}", "world");

        Assert.AreEqual("2026-09-23T15:04:05.123Z [Information] Tests: hello world" + Environment.NewLine, File.ReadAllText(provider.CurrentFilePath));
    }

    [TestMethod]
    public void SkipsDebugUntilSwitchAllowsIt()
    {
        var levelSwitch = new LogLevelSwitch();
        using var provider = new RollingFileLoggerProvider(_directory, levelSwitch, TimeProvider.System);
        var logger = provider.CreateLogger("Tests");

        logger.Log(LogLevel.Debug, "hidden");
        Assert.IsFalse(File.Exists(provider.CurrentFilePath));

        levelSwitch.MinimumLevel = LogLevel.Debug;
        logger.Log(LogLevel.Debug, "shown");
        StringAssert.Contains(File.ReadAllText(provider.CurrentFilePath), "shown");
    }

    [TestMethod]
    public void RollsAndKeepsFiveFiles()
    {
        using var provider = new RollingFileLoggerProvider(_directory, new LogLevelSwitch(), TimeProvider.System, maxFileBytes: 200, maxFiles: 5);
        var logger = provider.CreateLogger("Tests");

        for (var i = 0; i < 40; i++)
        {
            logger.Log(LogLevel.Information, "line {Number} padded to take up room in the file", i);
        }

        var files = Directory.GetFiles(_directory).Select(Path.GetFileName).Order().ToList();
        CollectionAssert.AreEqual(
            new[] { "ConnectFlyout.1.log", "ConnectFlyout.2.log", "ConnectFlyout.3.log", "ConnectFlyout.4.log", "ConnectFlyout.log" },
            files);
        Assert.IsTrue(Directory.GetFiles(_directory).All(file => new FileInfo(file).Length <= 200));
        StringAssert.Contains(File.ReadAllText(provider.CurrentFilePath), "line 39");
    }
}

[TestClass]
public sealed class ThrottlerTests
{
    private readonly FakeTimeProvider _time = new();

    [TestMethod]
    public void RunsFirstActionRightAway()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        throttler.Run(Record(runs, 1));

        CollectionAssert.AreEqual(new[] { 1 }, runs);
    }

    [TestMethod]
    public void CollapsesBurstIntoLastValue()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        throttler.Run(Record(runs, 1));
        throttler.Run(Record(runs, 2));
        throttler.Run(Record(runs, 3));
        Assert.IsTrue(throttler.HasPending);

        _time.Advance(TimeSpan.FromMilliseconds(150));

        CollectionAssert.AreEqual(new[] { 1, 3 }, runs);
        Assert.IsFalse(throttler.HasPending);
    }

    [TestMethod]
    public void RunsImmediatelyAgainAfterQuietInterval()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        throttler.Run(Record(runs, 1));
        _time.Advance(TimeSpan.FromMilliseconds(200));
        throttler.Run(Record(runs, 2));

        CollectionAssert.AreEqual(new[] { 1, 2 }, runs);
    }

    [TestMethod]
    public void NeverRunsMoreThanOncePerInterval()
    {
        using var throttler = new Throttler(TimeSpan.FromMilliseconds(150), _time);
        var runs = new List<int>();

        for (var i = 0; i < 30; i++)
        {
            throttler.Run(Record(runs, i));
            _time.Advance(TimeSpan.FromMilliseconds(10));
        }
        _time.Advance(TimeSpan.FromMilliseconds(150));

        Assert.IsTrue(runs.Count <= 4, $"ran {runs.Count} times in 450 ms");
        Assert.AreEqual(29, runs[^1]);
    }

    private static Func<Task> Record(List<int> runs, int value) => () =>
    {
        runs.Add(value);
        return Task.CompletedTask;
    };
}
````

- [ ] **Step 2: Run the build to see it fail**

Expected: FAIL with `CS0234` for `Placement`, `Logging` and `Common.Throttler`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 3: Create `src/ConnectFlyout.Presentation/Placement/FlyoutPlacement.cs`**

````csharp
namespace ConnectFlyout.Presentation.Placement;

/// <summary>
/// Screen rectangle in physical pixels. Right and bottom are exclusive.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>
/// Screen edge the taskbar sits on.
/// </summary>
public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Where the flyout goes on the monitor that holds the tray icon.
/// </summary>
/// <remarks>
/// The flyout is always right-aligned. It sits above a bottom taskbar, below a top taskbar,
/// and at the bottom of the work area when the taskbar is on the left or right. It never leaves
/// the work area; when the work area is too short, the flyout gets shorter.
/// </remarks>
public static class FlyoutPlacement
{
    public const int WidthDip = 360;
    public const int HeightDip = 640;
    public const int MarginDip = 12;

    /// <summary>
    /// Finds the taskbar edge by comparing the monitor to its work area. An auto-hidden taskbar
    /// takes no work area, so it's treated as bottom.
    /// </summary>
    public static TaskbarEdge DetectEdge(PixelRect monitor, PixelRect workArea)
    {
        if (workArea.Bottom < monitor.Bottom)
        {
            return TaskbarEdge.Bottom;
        }
        if (workArea.Top > monitor.Top)
        {
            return TaskbarEdge.Top;
        }
        if (workArea.Left > monitor.Left)
        {
            return TaskbarEdge.Left;
        }
        if (workArea.Right < monitor.Right)
        {
            return TaskbarEdge.Right;
        }
        return TaskbarEdge.Bottom;
    }

    /// <param name="monitor">Full bounds of the monitor holding the tray icon.</param>
    /// <param name="workArea">That monitor's work area.</param>
    /// <param name="scale">Monitor DPI divided by 96.</param>
    public static PixelRect Calculate(PixelRect monitor, PixelRect workArea, double scale)
    {
        var edge = DetectEdge(monitor, workArea);
        var margin = (int)Math.Round(MarginDip * scale);
        var width = Math.Min((int)Math.Round(WidthDip * scale), workArea.Width - (2 * margin));
        var height = Math.Min((int)Math.Round(HeightDip * scale), workArea.Height - (2 * margin));

        var left = workArea.Right - margin - width;
        var top = edge == TaskbarEdge.Top
            ? workArea.Top + margin
            : workArea.Bottom - margin - height;

        return new PixelRect(left, top, left + width, top + height);
    }
}
````

- [ ] **Step 4: Create `src/ConnectFlyout.Presentation/Logging/RollingFileLoggerProvider.cs`**

````csharp
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ConnectFlyout.Presentation.Logging;

/// <summary>
/// Minimum level the file logger writes. Changed at runtime by the Debug logging setting.
/// </summary>
public sealed class LogLevelSwitch
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

/// <summary>
/// Writes log lines to ConnectFlyout.log and keeps the newest files.
/// </summary>
/// <remarks>
/// When the current file would pass <c>maxFileBytes</c>, it becomes ConnectFlyout.1.log, the
/// older numbered files shift up by one, and anything past <c>maxFiles</c> total is deleted.
/// Line format: <c>2026-09-23T15:04:05.123Z [Information] Category: message</c>.
/// </remarks>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private const string BaseName = "ConnectFlyout";

    private readonly string _directory;
    private readonly LogLevelSwitch _levelSwitch;
    private readonly long _maxFileBytes;
    private readonly int _maxFiles;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    public RollingFileLoggerProvider(string directory, LogLevelSwitch levelSwitch, TimeProvider timeProvider, long maxFileBytes = 1_048_576, int maxFiles = 5)
    {
        _directory = directory;
        _levelSwitch = levelSwitch;
        _timeProvider = timeProvider;
        _maxFileBytes = maxFileBytes;
        _maxFiles = maxFiles;
        Directory.CreateDirectory(directory);
    }

    public string CurrentFilePath => Path.Combine(_directory, BaseName + ".log");

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    public void Dispose()
    {
        // Every write opens and closes the file, so there's nothing to release.
    }

    internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _levelSwitch.MinimumLevel;

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(_timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(category).Append(": ")
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }
        line.AppendLine();
        var text = line.ToString();

        lock (_gate)
        {
            try
            {
                RollIfNeeded(Encoding.UTF8.GetByteCount(text));
                File.AppendAllText(CurrentFilePath, text, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private string NumberedPath(int number) => Path.Combine(_directory, $"{BaseName}.{number}.log");

    private void RollIfNeeded(int incomingBytes)
    {
        var current = new FileInfo(CurrentFilePath);
        if (!current.Exists || current.Length + incomingBytes <= _maxFileBytes)
        {
            return;
        }

        var oldest = NumberedPath(_maxFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        for (var number = _maxFiles - 2; number >= 1; number--)
        {
            var source = NumberedPath(number);
            if (File.Exists(source))
            {
                File.Move(source, NumberedPath(number + 1));
            }
        }
        File.Move(CurrentFilePath, NumberedPath(1));
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _category;

        public RollingFileLogger(RollingFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }
            _provider.Write(logLevel, _category, formatter(state, exception), exception);
        }
    }
}
````

- [ ] **Step 5: Create `src/ConnectFlyout.Presentation/Logging/LogMessages.cs`**

````csharp
using Microsoft.Extensions.Logging;

namespace ConnectFlyout.Presentation.Logging;

/// <summary>
/// Every log message the Presentation layer writes, as source-generated LoggerMessage delegates.
/// </summary>
internal static partial class LogMessages
{
    // =========================================================================
    // HEADSET MANAGER (1xx)
    // =========================================================================

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Windows connected {Name} ({Address})")]
    public static partial void WindowsConnected(ILogger logger, string name, string address);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Windows disconnected {Name}")]
    public static partial void WindowsDisconnected(ILogger logger, string name);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Control link to {Name} dropped")]
    public static partial void LinkDropped(ILogger logger, string name);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Control link to {Name} open")]
    public static partial void LinkOpen(ILogger logger, string name);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "Couldn't connect to {Name} (attempt {Attempt})")]
    public static partial void ConnectFailed(ILogger logger, Exception exception, string name, int attempt);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Reconnecting {Name}")]
    public static partial void Reconnecting(ILogger logger, string name);

    // =========================================================================
    // HEADSET CONTROLS (2xx)
    // =========================================================================

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "Couldn't change {Setting} on {Headset}")]
    public static partial void CommandFailed(ILogger logger, Exception exception, string setting, string headset);

    [LoggerMessage(EventId = 201, Level = LogLevel.Information, Message = "Battery refresh for {Headset} failed")]
    public static partial void BatteryRefreshFailed(ILogger logger, Exception exception, string headset);

    // =========================================================================
    // PLAYBACK (3xx)
    // =========================================================================

    [LoggerMessage(EventId = 300, Level = LogLevel.Information, Message = "Couldn't read media info")]
    public static partial void MediaReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 301, Level = LogLevel.Information, Message = "Media command failed")]
    public static partial void MediaCommandFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 302, Level = LogLevel.Information, Message = "Couldn't read the Windows volume")]
    public static partial void VolumeReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 303, Level = LogLevel.Warning, Message = "Couldn't set the Windows volume")]
    public static partial void VolumeSetFailed(ILogger logger, Exception exception);

    // =========================================================================
    // SETTINGS (4xx)
    // =========================================================================

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "Couldn't read the startup task")]
    public static partial void StartupTaskReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 401, Level = LogLevel.Warning, Message = "Couldn't change the startup task")]
    public static partial void StartupTaskChangeFailed(ILogger logger, Exception exception);
}
````

- [ ] **Step 6: Create `src/ConnectFlyout.Presentation/Common/Throttler.cs`**

````csharp
namespace ConnectFlyout.Presentation.Common;

/// <summary>
/// Runs at most one action per interval and always runs the last one it was given.
/// </summary>
/// <remarks>
/// Used for sliders: dragging sends a command right away, then at most one more per interval,
/// and the value where the drag stops is always sent. Actions run on the thread pool (or the
/// fake clock's thread in tests) and must handle their own errors.
/// </remarks>
public sealed class Throttler : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    private Func<Task>? _pending;
    private ITimer? _timer;
    private long _lastRun;
    private bool _hasRun;

    public Throttler(TimeSpan interval, TimeProvider timeProvider)
    {
        _interval = interval;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// True while an action is waiting for its turn.
    /// </summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    public void Run(Func<Task> action)
    {
        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            var elapsed = _hasRun ? _timeProvider.GetElapsedTime(_lastRun, now) : TimeSpan.MaxValue;

            if (_timer is null && elapsed >= _interval)
            {
                _lastRun = now;
                _hasRun = true;
                _ = action();
                return;
            }

            _pending = action;
            if (_timer is not null)
            {
                return;
            }
            _timer = _timeProvider.CreateTimer(_ => Flush(), null, _interval - elapsed, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _pending = null;
        }
    }

    private void Flush()
    {
        Func<Task>? action;
        lock (_gate)
        {
            action = _pending;
            _pending = null;
            _timer?.Dispose();
            _timer = null;
            _lastRun = _timeProvider.GetTimestamp();
            _hasRun = true;
        }
        _ = action?.Invoke();
    }
}
````

- [ ] **Step 7: Create `src/ConnectFlyout.Presentation/Common/UiContext.cs`**

````csharp
namespace ConnectFlyout.Presentation.Common;

/// <summary>
/// Runs work on the thread that created it (the UI thread in the app).
/// </summary>
/// <remarks>
/// Captures <see cref="SynchronizationContext.Current"/> at construction. Tests have no
/// context, so work runs inline.
/// </remarks>
public sealed class UiContext
{
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_context is null || SynchronizationContext.Current == _context)
        {
            action();
            return;
        }
        _context.Post(static state => ((Action)state!)(), action);
    }
}
````

- [ ] **Step 8: Build and run the tests**

Expected: `Passed: 50, Skipped: 1`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
dotnet test src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj --no-build -p:Platform=x64 --filter "TestCategory!=XM4"
````

---

### Task 10: Device Discovery, Reconnect Schedule, Media and Notifications

**Files:**
- Create: `src/ConnectFlyout.Presentation/Devices/{IBluetoothDeviceSource,BluetoothDeviceWatcher,ManagedHeadset,HeadsetManager}.cs`, `src/ConnectFlyout.Presentation/Media/{MediaControllers,CoreAudioVolumeController}.cs`, `src/ConnectFlyout.Presentation/Notifications/LowBatteryMonitor.cs`
- Test: `src/ConnectFlyout.Presentation.Tests/Fakes/FakeHeadset.cs`, `src/ConnectFlyout.Presentation.Tests/Fakes/Fakes.cs`, `src/ConnectFlyout.Presentation.Tests/HeadsetManagerTests.cs`

**Interfaces:**
- Consumes: `IHeadset`, `SonyDeviceNameFilter`, `LogMessages`, `AppSettings`.
- Produces: `BluetoothDeviceInfo(string Id, string Name, string Address, bool IsConnected)`; `IBluetoothDeviceSource` (`DeviceChanged`, `DeviceRemoved`, `StartWatching`, `StopWatching`); `BluetoothDeviceWatcher`; `ManagedHeadset` (`DeviceId`, `Id` = upper-case address, `Name`, `Headset`, `ConnectionState`); `HeadsetManager(source, createHeadset, isAutoConnectEnabled, timeProvider, logger)` with `Start()`, `Headsets`, `Reconnect(id)`, `ApplyAutoConnect(id)`, events `HeadsetAdded`, `HeadsetRemoved`, `ConnectionStateChanged`; `MediaInfo`, `IMediaController`, `IVolumeController`, `SmtcMediaController`, `CoreAudioVolumeController`; `INotificationService.ShowLowBattery(name, level)`, `LowBatteryMonitor(AppSettings, INotificationService)` with `Threshold = 20` and `Update(id, name, BatteryLevels)`.

- [ ] **Step 1: Create `src/ConnectFlyout.Presentation.Tests/Fakes/FakeHeadset.cs`**

````csharp
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Tests.Fakes;

/// <summary>
/// Scriptable <see cref="IHeadset"/>. Commands succeed and update <see cref="State"/> unless
/// <see cref="FailNextCommandWith"/> is set; connects succeed unless <see cref="ConnectFailures"/>
/// still has entries.
/// </summary>
internal sealed class FakeHeadset : IHeadset
{
    private readonly Lock _gate = new();

    public FakeHeadset(string deviceName = "WF-1000XM6", HeadsetFeatures? features = null)
    {
        DeviceName = deviceName;
        Features = features ?? Xm6Features;
    }

    public static HeadsetFeatures Xm6Features { get; } = new(true, true, true, true, true, true, true, true, true, true, true, true);

    public static HeadsetFeatures Xm4Features { get; } = new(false, true, true, true, true, true, false, false, false, false, true, true);

    public event EventHandler<HeadsetSnapshot>? StateChanged;

    public event EventHandler? Disconnected;

    public string DeviceName { get; }

    public string ModelName => DeviceName;

    public bool IsKnownModel => true;

    public HeadsetFeatures Features { get; }

    public HeadsetSnapshot State { get; set; } = HeadsetSnapshot.Empty with
    {
        Battery = new BatteryLevels(82, 85, 82, 95, false),
        NoiseControl = new NoiseControlSetting(NoiseMode.Ambient, 8, true),
        Codec = "LDAC",
    };

    public IReadOnlyList<EqualizerPresetOption> EqualizerPresets { get; } =
    [
        new EqualizerPresetOption(0x00, "Off"),
        new EqualizerPresetOption(0x10, "Bright"),
        new EqualizerPresetOption(0x16, "Bass Boost"),
        new EqualizerPresetOption(0xa0, "Manual"),
    ];

    /// <summary>
    /// Each connect attempt removes one entry and throws it. Empty means connects succeed.
    /// </summary>
    public Queue<Exception> ConnectFailures { get; } = new();

    public int ConnectAttempts { get; private set; }

    public int DisconnectCalls { get; private set; }

    public bool IsDisposed { get; private set; }

    public Exception? FailNextCommandWith { get; set; }

    public List<object> Commands { get; } = [];

    public Task ConnectAsync(string bluetoothAddress)
    {
        lock (_gate)
        {
            ConnectAttempts++;
            if (ConnectFailures.TryDequeue(out var failure))
            {
                return Task.FromException(failure);
            }
        }
        return Task.CompletedTask;
    }

    public void Disconnect() => DisconnectCalls++;

    public Task RefreshBatteryAsync() => Command("battery", state => state);

    public Task SetNoiseControlAsync(NoiseControlSetting value) => Command(value, state => state with { NoiseControl = value });

    public Task SetEqualizerPresetAsync(int preset) => Command(preset, state => state with { Equalizer = state.Equalizer with { Preset = preset } });

    public Task SetEqualizerCustomAsync(EqualizerSetting value) => Command(value, state => state with { Equalizer = value });

    public Task SetDseeAsync(bool enabled) => Command(("dsee", enabled), state => state with { Dsee = enabled });

    public Task SetSpeakToChatAsync(bool enabled) => Command(("speakToChat", enabled), state => state with { SpeakToChat = enabled });

    public Task SetAdaptiveVolumeAsync(bool enabled) => Command(("adaptiveVolume", enabled), state => state with { AdaptiveVolume = enabled });

    public Task SetAutoPowerOffAsync(int index) => Command(("autoPowerOff", index), state => state with { AutoPowerOff = index });

    public void RaiseStateChanged(HeadsetSnapshot snapshot)
    {
        State = snapshot;
        StateChanged?.Invoke(this, snapshot);
    }

    public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

    public void Dispose() => IsDisposed = true;

    private Task Command(object command, Func<HeadsetSnapshot, HeadsetSnapshot> apply)
    {
        lock (_gate)
        {
            Commands.Add(command);
            if (FailNextCommandWith is { } failure)
            {
                FailNextCommandWith = null;
                return Task.FromException(failure);
            }
            State = apply(State);
        }
        return Task.CompletedTask;
    }
}
````

- [ ] **Step 2: Create `src/ConnectFlyout.Presentation.Tests/Fakes/Fakes.cs`**

````csharp
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Media;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Tests.Fakes;

internal sealed class FakeDeviceSource : IBluetoothDeviceSource
{
    public event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    public event EventHandler<string>? DeviceRemoved;

    public bool IsWatching { get; private set; }

    public void StartWatching() => IsWatching = true;

    public void StopWatching() => IsWatching = false;

    public void Report(BluetoothDeviceInfo device) => DeviceChanged?.Invoke(this, device);

    public void Remove(string deviceId) => DeviceRemoved?.Invoke(this, deviceId);
}

internal sealed class FakeNotificationService : INotificationService
{
    public List<(string DeviceName, int Level)> Shown { get; } = [];

    public void ShowLowBattery(string deviceName, int level) => Shown.Add((deviceName, level));
}

internal sealed class FakeMediaController : IMediaController
{
    public MediaInfo? Current { get; set; }

    public int PlayPauseCalls { get; private set; }

    public int NextCalls { get; private set; }

    public int PreviousCalls { get; private set; }

    public Task<MediaInfo?> GetCurrentAsync() => Task.FromResult(Current);

    public Task PlayPauseAsync()
    {
        PlayPauseCalls++;
        if (Current is not null)
        {
            Current = Current with { IsPlaying = !Current.IsPlaying };
        }
        return Task.CompletedTask;
    }

    public Task NextAsync()
    {
        NextCalls++;
        return Task.CompletedTask;
    }

    public Task PreviousAsync()
    {
        PreviousCalls++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeVolumeController : IVolumeController
{
    public double Level { get; set; } = 40;

    public double GetVolume() => Level;

    public void SetVolume(double volume) => Level = volume;
}

internal sealed class FakeStartupTaskService : IStartupTaskService
{
    public bool Enabled { get; set; }

    /// <summary>
    /// When true, enabling is refused like a policy-disabled startup task.
    /// </summary>
    public bool RefuseEnable { get; set; }

    public Task<bool> IsEnabledAsync() => Task.FromResult(Enabled);

    public Task<bool> SetEnabledAsync(bool enabled)
    {
        Enabled = enabled && !RefuseEnable;
        return Task.FromResult(Enabled);
    }
}
````

- [ ] **Step 3: Write the failing tests `src/ConnectFlyout.Presentation.Tests/HeadsetManagerTests.cs`**

````csharp
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Tests.Fakes;

namespace ConnectFlyout.Presentation.Tests;

[TestClass]
public sealed class HeadsetManagerTests
{
    private static readonly BluetoothDeviceInfo Xm6 = new("device-xm6", "WF-1000XM6", "ac:80:0a:00:00:06", true);

    private readonly FakeDeviceSource _source = new();
    private readonly FakeTimeProvider _time = new();
    private readonly Dictionary<string, FakeHeadset> _created = [];
    private readonly HashSet<string> _autoConnectOff = [];
    private HeadsetManager _manager = null!;

    [TestInitialize]
    public void CreateManager()
    {
        _manager = new HeadsetManager(
            _source,
            name => _created[name] = new FakeHeadset(name),
            id => !_autoConnectOff.Contains(id),
            _time,
            NullLogger<HeadsetManager>.Instance);
        _manager.Start();
    }

    [TestCleanup]
    public void DisposeManager() => _manager.Dispose();

    [TestMethod]
    public void StartsWatchingWindowsDevices() => Assert.IsTrue(_source.IsWatching);

    [TestMethod]
    public async Task ConnectedSonyHeadsetIsAddedAndConnected()
    {
        ManagedHeadset? added = null;
        _manager.HeadsetAdded += (_, headset) => added = headset;

        _source.Report(Xm6);

        Assert.IsNotNull(added);
        Assert.AreEqual("AC:80:0A:00:00:06", added.Id);
        Assert.IsTrue(await TestWait.UntilAsync(() => added.ConnectionState == HeadsetConnectionState.Connected));
        Assert.AreEqual(1, _created["WF-1000XM6"].ConnectAttempts);
    }

    [TestMethod]
    public void OtherBrandsAreIgnored()
    {
        _source.Report(new BluetoothDeviceInfo("device-buds", "Galaxy Buds2", "00:11:22:33:44:55", true));

        Assert.AreEqual(0, _manager.Headsets.Count);
    }

    [TestMethod]
    public void PairedButDisconnectedHeadsetIsNotListed()
    {
        _source.Report(Xm6 with { IsConnected = false });

        Assert.AreEqual(0, _manager.Headsets.Count);
    }

    [TestMethod]
    public void WindowsDisconnectRemovesAndDisposesHeadset()
    {
        ManagedHeadset? removed = null;
        _manager.HeadsetRemoved += (_, headset) => removed = headset;
        _source.Report(Xm6);

        _source.Report(Xm6 with { IsConnected = false });

        Assert.IsNotNull(removed);
        Assert.AreEqual(0, _manager.Headsets.Count);
        Assert.IsTrue(_created["WF-1000XM6"].IsDisposed);
    }

    [TestMethod]
    public void UnpairingRemovesHeadset()
    {
        _source.Report(Xm6);

        _source.Remove(Xm6.Id);

        Assert.AreEqual(0, _manager.Headsets.Count);
    }

    [TestMethod]
    public async Task FailedConnectRetriesAtOneTwoFiveThenThirtySeconds()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        // First attempt already ran and succeeded; make the next ones fail.
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        for (var i = 0; i < 5; i++)
        {
            headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));
        }
        _manager.Reconnect("AC:80:0A:00:00:06");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 2));

        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(999), 2);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(1), 3);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(2), 4);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(5), 5);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(29), 5);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(1), 6);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromSeconds(30), 7);

        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task DroppedLinkReconnectsAfterOneSecond()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        headset.RaiseDisconnected();

        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(900), 1);
        await AdvanceAndExpectAttempts(headset, TimeSpan.FromMilliseconds(100), 2);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task WindowsDisconnectStopsRetrying()
    {
        _source.Report(Xm6);
        var headset = _created["WF-1000XM6"];
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 1));
        headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));
        headset.ConnectFailures.Enqueue(new COMException("timeout", HeadsetErrorMessages.TimeoutHResult));
        _manager.Reconnect("AC:80:0A:00:00:06");
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == 2));

        _source.Report(Xm6 with { IsConnected = false });
        _time.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        Assert.AreEqual(2, headset.ConnectAttempts);
    }

    [TestMethod]
    public async Task AutoConnectOffListsButDoesNotConnect()
    {
        _autoConnectOff.Add("AC:80:0A:00:00:06");

        _source.Report(Xm6);
        await Task.Delay(50);

        Assert.AreEqual(1, _manager.Headsets.Count);
        Assert.AreEqual(0, _created["WF-1000XM6"].ConnectAttempts);
        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
    }

    [TestMethod]
    public async Task ReconnectConnectsEvenWithAutoConnectOff()
    {
        _autoConnectOff.Add("AC:80:0A:00:00:06");
        _source.Report(Xm6);

        _manager.Reconnect("AC:80:0A:00:00:06");

        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));
    }

    [TestMethod]
    public async Task TurningAutoConnectOffDisconnects()
    {
        _source.Report(Xm6);
        Assert.IsTrue(await TestWait.UntilAsync(() => _manager.Headsets[0].ConnectionState == HeadsetConnectionState.Connected));

        _autoConnectOff.Add("AC:80:0A:00:00:06");
        _manager.ApplyAutoConnect("AC:80:0A:00:00:06");

        Assert.AreEqual(HeadsetConnectionState.Disconnected, _manager.Headsets[0].ConnectionState);
        Assert.AreEqual(1, _created["WF-1000XM6"].DisconnectCalls);
    }

    private async Task AdvanceAndExpectAttempts(FakeHeadset headset, TimeSpan by, int expected)
    {
        _time.Advance(by);
        Assert.IsTrue(await TestWait.UntilAsync(() => headset.ConnectAttempts == expected), $"expected {expected} attempts, saw {headset.ConnectAttempts}");
        await Task.Delay(20);
        Assert.AreEqual(expected, headset.ConnectAttempts);
    }
}
````

- [ ] **Step 4: Run the build to see it fail**

Expected: FAIL with `CS0246` for `HeadsetManager`, `IBluetoothDeviceSource`, `IMediaController` and `INotificationService`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 5: Create `src/ConnectFlyout.Presentation/Devices/IBluetoothDeviceSource.cs`**

````csharp
namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// A paired Bluetooth device as Windows reports it.
/// </summary>
/// <param name="Id">Windows device ID.</param>
/// <param name="Name">Friendly name, e.g. "WF-1000XM6".</param>
/// <param name="Address">Bluetooth address, e.g. "ac:80:0a:12:34:56".</param>
/// <param name="IsConnected">True while Windows has the device connected.</param>
public sealed record BluetoothDeviceInfo(string Id, string Name, string Address, bool IsConnected);

/// <summary>
/// Reports paired Bluetooth devices as they're found, connect, disconnect or get unpaired.
/// </summary>
public interface IBluetoothDeviceSource
{
    /// <summary>
    /// Raised when a device is found or any of its reported values change.
    /// </summary>
    event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    /// <summary>
    /// Raised with the Windows device ID when a device is unpaired.
    /// </summary>
    event EventHandler<string>? DeviceRemoved;

    void StartWatching();

    void StopWatching();
}
````

- [ ] **Step 6: Create `src/ConnectFlyout.Presentation/Devices/BluetoothDeviceWatcher.cs`**

````csharp
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// <see cref="IBluetoothDeviceSource"/> over a Windows <see cref="DeviceWatcher"/> for paired
/// Bluetooth Classic devices.
/// </summary>
public sealed class BluetoothDeviceWatcher : IBluetoothDeviceSource, IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, BluetoothDeviceInfo> _devices = [];
    private DeviceWatcher? _watcher;

    public event EventHandler<BluetoothDeviceInfo>? DeviceChanged;

    public event EventHandler<string>? DeviceRemoved;

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        _watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            [IsConnectedProperty, AddressProperty],
            DeviceInformationKind.AssociationEndpoint);
        _watcher.Added += OnAdded;
        _watcher.Updated += OnUpdated;
        _watcher.Removed += OnRemoved;
        _watcher.Start();
    }

    public void StopWatching()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.Added -= OnAdded;
        _watcher.Updated -= OnUpdated;
        _watcher.Removed -= OnRemoved;
        if (_watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            _watcher.Stop();
        }
        _watcher = null;
    }

    public void Dispose() => StopWatching();

    private static bool ReadBool(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is bool flag && flag;

    private static string ReadString(IReadOnlyDictionary<string, object> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is string text ? text : "";

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        var device = new BluetoothDeviceInfo(
            info.Id,
            info.Name,
            ReadString(info.Properties, AddressProperty),
            ReadBool(info.Properties, IsConnectedProperty));

        lock (_gate)
        {
            _devices[info.Id] = device;
        }
        DeviceChanged?.Invoke(this, device);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        BluetoothDeviceInfo? device;
        lock (_gate)
        {
            if (!_devices.TryGetValue(update.Id, out device))
            {
                return;
            }
            if (update.Properties.ContainsKey(IsConnectedProperty))
            {
                device = device with { IsConnected = ReadBool(update.Properties, IsConnectedProperty) };
                _devices[update.Id] = device;
            }
        }
        DeviceChanged?.Invoke(this, device);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_gate)
        {
            if (!_devices.Remove(update.Id))
            {
                return;
            }
        }
        DeviceRemoved?.Invoke(this, update.Id);
    }
}
````

- [ ] **Step 7: Create `src/ConnectFlyout.Presentation/Devices/ManagedHeadset.cs`**

````csharp
using ConnectFlyout.Presentation.Headsets;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// A Sony headset Windows reports as connected, plus the state of the app's control link to it.
/// </summary>
public sealed class ManagedHeadset
{
    internal ManagedHeadset(string deviceId, string address, string name, IHeadset headset)
    {
        DeviceId = deviceId;
        Id = address;
        Name = name;
        Headset = headset;
    }

    /// <summary>
    /// Windows device ID.
    /// </summary>
    public string DeviceId { get; }

    /// <summary>
    /// Upper-case Bluetooth address. Stable across restarts, so settings are keyed on it.
    /// </summary>
    public string Id { get; }

    public string Name { get; }

    public IHeadset Headset { get; }

    public HeadsetConnectionState ConnectionState { get; internal set; }

    internal CancellationTokenSource? ConnectLoop { get; set; }
}
````

- [ ] **Step 8: Create `src/ConnectFlyout.Presentation/Devices/HeadsetManager.cs`**

````csharp
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;

namespace ConnectFlyout.Presentation.Devices;

/// <summary>
/// Tracks the Sony headsets Windows reports as connected and keeps a control link open to each.
/// </summary>
/// <remarks>
/// A headset joins the list when Windows connects it and leaves when Windows disconnects or
/// unpairs it. While it's listed, the control link is (re)opened on a schedule of 0 s, 1 s, 2 s,
/// 5 s, then every 30 s until it succeeds. A link that drops while Windows still reports the
/// headset connected restarts that schedule at 1 s. Headsets with auto-connect turned off stay
/// listed but only connect when <see cref="Reconnect"/> is called.
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
    private readonly Func<string, bool> _isAutoConnectEnabled;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HeadsetManager> _logger;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, ManagedHeadset> _headsets = [];
    private bool _disposed;

    /// <param name="source">Paired Bluetooth devices from Windows.</param>
    /// <param name="createHeadset">Creates the control link for a device name.</param>
    /// <param name="isAutoConnectEnabled">Whether to connect a headset (by <see cref="ManagedHeadset.Id"/>) on its own.</param>
    /// <param name="timeProvider">Clock for the reconnect schedule.</param>
    /// <param name="logger">Logger.</param>
    public HeadsetManager(
        IBluetoothDeviceSource source,
        Func<string, IHeadset> createHeadset,
        Func<string, bool> isAutoConnectEnabled,
        TimeProvider timeProvider,
        ILogger<HeadsetManager> logger)
    {
        _source = source;
        _createHeadset = createHeadset;
        _isAutoConnectEnabled = isAutoConnectEnabled;
        _timeProvider = timeProvider;
        _logger = logger;
    }

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
    /// Drops the current link and connects again right away.
    /// </summary>
    public void Reconnect(string id)
    {
        var headset = Find(id);
        if (headset is null)
        {
            return;
        }

        LogMessages.Reconnecting(_logger, headset.Name);
        headset.Headset.Disconnect();
        StartConnectLoop(headset, 0);
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

        if (_isAutoConnectEnabled(id))
        {
            if (headset.ConnectionState == HeadsetConnectionState.Disconnected)
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
        if (!SonyDeviceNameFilter.IsSony(device.Name) || string.IsNullOrWhiteSpace(device.Address))
        {
            return;
        }

        if (device.IsConnected)
        {
            Track(device);
        }
        else
        {
            Untrack(device.Id);
        }
    }

    private void OnDeviceRemoved(object? sender, string deviceId) => Untrack(deviceId);

    private void Track(BluetoothDeviceInfo device)
    {
        ManagedHeadset headset;
        lock (_gate)
        {
            if (_disposed || _headsets.ContainsKey(device.Id))
            {
                return;
            }
            headset = new ManagedHeadset(device.Id, NormalizeAddress(device.Address), device.Name, _createHeadset(device.Name));
            _headsets[device.Id] = headset;
        }

        headset.Headset.Disconnected += (_, _) => OnLinkDropped(headset);
        LogMessages.WindowsConnected(_logger, headset.Name, headset.Id);
        HeadsetAdded?.Invoke(this, headset);

        if (_isAutoConnectEnabled(headset.Id))
        {
            StartConnectLoop(headset, 0);
        }
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

        LogMessages.WindowsDisconnected(_logger, headset.Name);
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
        }

        LogMessages.LinkDropped(_logger, headset.Name);
        SetState(headset, HeadsetConnectionState.Disconnected);
        if (_isAutoConnectEnabled(headset.Id))
        {
            StartConnectLoop(headset, 1);
        }
    }

    private void StartConnectLoop(ManagedHeadset headset, int firstDelayIndex)
    {
        var cancellation = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = headset.ConnectLoop;
            headset.ConnectLoop = cancellation;
        }
        previous?.Cancel();
        previous?.Dispose();

        _ = ConnectLoopAsync(headset, firstDelayIndex, cancellation.Token);
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

    private async Task ConnectLoopAsync(ManagedHeadset headset, int delayIndex, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var delay = delayIndex < ConnectDelays.Length ? ConnectDelays[delayIndex] : SteadyConnectDelay;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();

                SetState(headset, HeadsetConnectionState.Connecting);
                try
                {
                    await headset.Headset.ConnectAsync(headset.Id).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    SetState(headset, HeadsetConnectionState.Connected);
                    LogMessages.LinkOpen(_logger, headset.Name);
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMessages.ConnectFailed(_logger, ex, headset.Name, delayIndex + 1);
                    SetState(headset, HeadsetConnectionState.Disconnected);
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
````

- [ ] **Step 9: Create `src/ConnectFlyout.Presentation/Media/MediaControllers.cs`**

````csharp
using Windows.Media.Control;

namespace ConnectFlyout.Presentation.Media;

/// <summary>
/// What's playing right now.
/// </summary>
public sealed record MediaInfo(string Title, string Artist, bool IsPlaying);

/// <summary>
/// The app currently playing media on Windows.
/// </summary>
public interface IMediaController
{
    /// <summary>
    /// Returns null when nothing is playing.
    /// </summary>
    Task<MediaInfo?> GetCurrentAsync();

    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();
}

/// <summary>
/// Windows output volume, 0 to 100.
/// </summary>
public interface IVolumeController
{
    double GetVolume();

    void SetVolume(double volume);
}

/// <summary>
/// <see cref="IMediaController"/> over Windows' system media transport controls.
/// </summary>
public sealed class SmtcMediaController : IMediaController
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<MediaInfo?> GetCurrentAsync()
    {
        var session = await GetSessionAsync().ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var properties = await session.TryGetMediaPropertiesAsync();
        var playing = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        return new MediaInfo(properties.Title ?? "", properties.Artist ?? "", playing);
    }

    public async Task PlayPauseAsync()
    {
        var session = await GetSessionAsync().ConfigureAwait(false);
        if (session is not null)
        {
            await session.TryTogglePlayPauseAsync();
        }
    }

    public async Task NextAsync()
    {
        var session = await GetSessionAsync().ConfigureAwait(false);
        if (session is not null)
        {
            await session.TrySkipNextAsync();
        }
    }

    public async Task PreviousAsync()
    {
        var session = await GetSessionAsync().ConfigureAwait(false);
        if (session is not null)
        {
            await session.TrySkipPreviousAsync();
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> GetSessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetCurrentSession();
    }
}
````

- [ ] **Step 10: Create `src/ConnectFlyout.Presentation/Media/CoreAudioVolumeController.cs`**

````csharp
using System.Runtime.InteropServices;

namespace ConnectFlyout.Presentation.Media;

/// <summary>
/// <see cref="IVolumeController"/> for the default Windows playback device, over Core Audio.
/// </summary>
public sealed class CoreAudioVolumeController : IVolumeController
{
    private const int RenderFlow = 0;       // eRender
    private const int MultimediaRole = 1;   // eMultimedia
    private const int ClsctxAll = 0x17;     // CLSCTX_ALL

    private static readonly Guid AudioEndpointVolumeId = typeof(IAudioEndpointVolume).GUID;

    public double GetVolume()
    {
        GetEndpointVolume().GetMasterVolumeLevelScalar(out var level);
        return Math.Round(level * 100);
    }

    public void SetVolume(double volume)
    {
        var context = Guid.Empty;
        GetEndpointVolume().SetMasterVolumeLevelScalar((float)(Math.Clamp(volume, 0, 100) / 100), ref context);
    }

    private static IAudioEndpointVolume GetEndpointVolume()
    {
        var enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
        var device = enumerator.GetDefaultAudioEndpoint(RenderFlow, MultimediaRole);
        var iid = AudioEndpointVolumeId;
        return (IAudioEndpointVolume)device.Activate(ref iid, ClsctxAll, IntPtr.Zero);
    }

    // Only the leading vtable slots the app calls are declared, in the SDK's order.
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        IMMDevice GetDefaultAudioEndpoint(int dataFlow, int role);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object Activate(ref Guid iid, int clsCtx, IntPtr activationParams);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify);

        void UnregisterControlChangeNotify(IntPtr notify);

        void GetChannelCount(out uint channelCount);

        void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

        void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

        void GetMasterVolumeLevel(out float levelDb);

        void GetMasterVolumeLevelScalar(out float level);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumeratorComObject
    {
    }
}
````

- [ ] **Step 11: Create `src/ConnectFlyout.Presentation/Notifications/LowBatteryMonitor.cs`**

````csharp
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.Notifications;

/// <summary>
/// Shows Windows notifications.
/// </summary>
public interface INotificationService
{
    void ShowLowBattery(string deviceName, int level);
}

/// <summary>
/// Raises one low-battery notification each time a headset drops below
/// <see cref="Threshold"/> percent.
/// </summary>
/// <remarks>
/// The alert re-arms once the level is back at or above the threshold, or while charging.
/// </remarks>
public sealed class LowBatteryMonitor
{
    public const int Threshold = 20;

    private readonly AppSettings _settings;
    private readonly INotificationService _notifications;
    private readonly Dictionary<string, bool> _alerted = [];
    private readonly Lock _gate = new();

    public LowBatteryMonitor(AppSettings settings, INotificationService notifications)
    {
        _settings = settings;
        _notifications = notifications;
    }

    public void Update(string headsetId, string deviceName, BatteryLevels battery)
    {
        ArgumentNullException.ThrowIfNull(battery);

        var lowest = battery.Lowest;
        if (lowest is null)
        {
            return;
        }

        lock (_gate)
        {
            if (battery.Charging || lowest >= Threshold)
            {
                _alerted[headsetId] = false;
                return;
            }
            if (_alerted.GetValueOrDefault(headsetId) || !_settings.LowBatteryNotifications)
            {
                return;
            }
            _alerted[headsetId] = true;
        }
        _notifications.ShowLowBattery(deviceName, lowest.Value);
    }
}
````

- [ ] **Step 12: Build and run the tests**

Expected: `Passed: 62, Skipped: 1`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
dotnet test src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj --no-build -p:Platform=x64 --filter "TestCategory!=XM4"
````

---

### Task 11: View Models

**Files:**
- Create: `src/ConnectFlyout.Presentation/ViewModels/{HeadsetViewModel,PlaybackViewModel,FlyoutViewModel,SettingsViewModel}.cs`
- Test: `src/ConnectFlyout.Presentation.Tests/ViewModelTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 7–10.
- Produces (bound from XAML in Task 12): `HeadsetViewModel` (identity, battery texts/glyphs, `SetNoiseModeCommand` taking `"Off"`/`"NoiseCancelling"`/`"Ambient"`, `IsNoiseOff/IsNoiseCancelling/IsAmbient`, `AmbientLevel` (double, throttled), `FocusOnVoice`, `Scenes`, `ApplySceneCommand`, `EqualizerPresets`, `SelectedEqualizerIndex`, `DseeIndex` + static `DseeOptions`, `ClearBass`, `Band1..Band5`, `ApplyCustomEqualizerCommand`, `SpeakToChat`, `AdaptiveVolume`, `AutoPowerOffIndex` + static `AutoPowerOffOptions`, `AutoConnect`, `StatusText`, `IsConnected`, `IsConnecting`, `ErrorMessage`, `HasError`, `RefreshBatteryAsync()`); `PlaybackViewModel(IMediaController, IVolumeController, ILogger)` (`Title`, `Artist`, `HasTrack`, `NoTrack`, `PlayPauseGlyph`, `Volume`, three commands, `RefreshAsync()`); `FlyoutViewModel(HeadsetManager, FlyoutNavigator, PlaybackViewModel, Func<ManagedHeadset, HeadsetViewModel>)` (`Headsets`, `CurrentHeadset`, `IsEmptyVisible/IsPickerVisible/IsDeviceVisible`, `ShowBack`, `Playback`, `PickCommand`, `BackCommand`, `ReconnectCommand`, `OpenSettingsCommand`, `QuitCommand`, `SettingsRequested`, `QuitRequested`, `OnOpenedAsync()`); `SettingsViewModel(flyout, settings, logLevel, startup, applyTheme, applyNativeDebugLogging, openFolder, logFolder, logger)` and `SceneEditorViewModel`.

- [ ] **Step 1: Write the failing tests `src/ConnectFlyout.Presentation.Tests/ViewModelTests.cs`**

````csharp
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Media;
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
    private HeadsetViewModel _viewModel = null!;

    [TestInitialize]
    public void CreateViewModel()
    {
        var managed = new ManagedHeadset("device-xm6", "AC:80:0A:00:00:06", "WF-1000XM6", _headset)
        {
            ConnectionState = HeadsetConnectionState.Connected,
        };
        _viewModel = new HeadsetViewModel(managed, _settings, new LowBatteryMonitor(_settings, _notifications), _time, NullLogger.Instance);
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
        Assert.AreEqual("L 85% \u00b7 R 82%", _viewModel.BatterySummary);
        Assert.AreEqual("Connected \u00b7 LDAC", _viewModel.StatusText);
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

        _viewModel.UpdateConnectionState(HeadsetConnectionState.Disconnected);
        Assert.AreEqual("Disconnected", _viewModel.StatusText);
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

    private readonly FakeDeviceSource _source = new();
    private readonly FakeTimeProvider _time = new();
    private readonly AppSettings _settings = new(new InMemorySettingsStore());
    private HeadsetManager _manager = null!;
    private FlyoutViewModel _flyout = null!;

    [TestInitialize]
    public void CreateFlyout()
    {
        _manager = new HeadsetManager(_source, name => new FakeHeadset(name), _ => true, _time, NullLogger<HeadsetManager>.Instance);
        var monitor = new LowBatteryMonitor(_settings, new FakeNotificationService());
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            new PlaybackViewModel(new FakeMediaController(), new FakeVolumeController(), NullLogger.Instance),
            managed => new HeadsetViewModel(managed, _settings, monitor, _time, NullLogger.Instance));
        _manager.Start();
    }

    [TestCleanup]
    public void DisposeFlyout()
    {
        _flyout.Dispose();
        _manager.Dispose();
    }

    [TestMethod]
    public void StartsOnEmptyPage()
    {
        Assert.IsTrue(_flyout.IsEmptyVisible);
        Assert.IsNull(_flyout.CurrentHeadset);
    }

    [TestMethod]
    public void OneHeadsetOpensItsPageWithoutBack()
    {
        _source.Report(Xm6);

        Assert.IsTrue(_flyout.IsDeviceVisible);
        Assert.IsFalse(_flyout.ShowBack);
        Assert.AreEqual("WF-1000XM6", _flyout.CurrentHeadset?.DeviceName);
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
    public void RememberedHeadsetLeavingAndReturning()
    {
        _source.Report(Xm6);
        _source.Report(Xm4);
        _flyout.PickCommand.Execute(_flyout.Headsets[0]);

        _source.Report(Xm6 with { IsConnected = false });
        Assert.AreEqual("WH-1000XM4", _flyout.CurrentHeadset?.DeviceName);
        Assert.IsFalse(_flyout.ShowBack);

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
public sealed class PlaybackViewModelTests
{
    [TestMethod]
    public async Task RefreshShowsTrackAndVolume()
    {
        var media = new FakeMediaController { Current = new MediaInfo("Purple Haze", "Jimi Hendrix", true) };
        var playback = new PlaybackViewModel(media, new FakeVolumeController { Level = 55 }, NullLogger.Instance);

        await playback.RefreshAsync();

        Assert.AreEqual("Purple Haze", playback.Title);
        Assert.AreEqual("Jimi Hendrix", playback.Artist);
        Assert.IsTrue(playback.HasTrack);
        Assert.AreEqual("\uE769", playback.PlayPauseGlyph);
        Assert.AreEqual(55, playback.Volume);
    }

    [TestMethod]
    public async Task NothingPlayingShowsNoTrack()
    {
        var playback = new PlaybackViewModel(new FakeMediaController(), new FakeVolumeController(), NullLogger.Instance);

        await playback.RefreshAsync();

        Assert.IsTrue(playback.NoTrack);
        Assert.AreEqual("\uE768", playback.PlayPauseGlyph);
    }

    [TestMethod]
    public async Task PlayPauseTogglesAndRefreshes()
    {
        var media = new FakeMediaController { Current = new MediaInfo("Song", "Artist", false) };
        var playback = new PlaybackViewModel(media, new FakeVolumeController(), NullLogger.Instance);

        await playback.PlayPauseCommand.ExecuteAsync(null);

        Assert.AreEqual(1, media.PlayPauseCalls);
        Assert.IsTrue(playback.IsPlaying);
    }

    [TestMethod]
    public void VolumeSliderSetsWindowsVolume()
    {
        var volume = new FakeVolumeController();
        var playback = new PlaybackViewModel(new FakeMediaController(), volume, NullLogger.Instance)
        {
            Volume = 72.6,
        };

        Assert.AreEqual(73, volume.Level);
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
        _manager = new HeadsetManager(_source, name => new FakeHeadset(name), _ => true, time, NullLogger<HeadsetManager>.Instance);
        var monitor = new LowBatteryMonitor(_settings, new FakeNotificationService());
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            new PlaybackViewModel(new FakeMediaController(), new FakeVolumeController(), NullLogger.Instance),
            managed => new HeadsetViewModel(managed, _settings, monitor, time, NullLogger.Instance));
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
    public void ResettingScenesRestoresDefaults()
    {
        _viewModel.Scenes[0].Name = "Changed";
        _viewModel.SaveScenesCommand.Execute(null);

        _viewModel.ResetScenesCommand.Execute(null);

        CollectionAssert.AreEqual(Scene.Defaults.ToList(), _settings.Scenes.ToList());
    }
}
````

- [ ] **Step 2: Run the build to see it fail**

Expected: FAIL with `CS0234` for `ConnectFlyout.Presentation.ViewModels`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

- [ ] **Step 3: Create `src/ConnectFlyout.Presentation/ViewModels/HeadsetViewModel.cs`**

````csharp
using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.ViewModels;

/// <summary>
/// One headset's controls for the flyout device page and the settings window.
/// </summary>
/// <remarks>
/// Controls change right away when used, then send the command. When a command fails, every
/// control goes back to what the headset last confirmed and <see cref="ErrorMessage"/> shows
/// for five seconds. The ambient slider sends at most one command per 150 ms and always sends
/// where the drag stops. Headset notifications update the controls, except the noise controls
/// while slider commands are still queued.
/// </remarks>
public sealed class HeadsetViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SliderInterval = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);

    private const string NoValue = "—";

    private readonly ManagedHeadset _managed;
    private readonly IHeadset _headset;
    private readonly AppSettings _settings;
    private readonly LowBatteryMonitor _lowBattery;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly UiContext _ui = new();
    private readonly Throttler _noiseThrottler;

    private HeadsetSnapshot _snapshot = HeadsetSnapshot.Empty;
    private HeadsetConnectionState _connectionState;
    private NoiseMode _noiseMode;
    private double _ambientLevel = 10;
    private bool _focusOnVoice;
    private int _selectedEqualizerIndex = -1;
    private int _dseeIndex;
    private bool _speakToChat;
    private bool _adaptiveVolume;
    private int _autoPowerOffIndex;
    private double _clearBass;
    private double _band1;
    private double _band2;
    private double _band3;
    private double _band4;
    private double _band5;
    private string? _errorMessage;
    private ITimer? _errorTimer;
    private bool _applying;

    public HeadsetViewModel(
        ManagedHeadset managed,
        AppSettings settings,
        LowBatteryMonitor lowBattery,
        TimeProvider timeProvider,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(managed);

        _managed = managed;
        _headset = managed.Headset;
        _settings = settings;
        _lowBattery = lowBattery;
        _timeProvider = timeProvider;
        _logger = logger;
        _noiseThrottler = new Throttler(SliderInterval, timeProvider);
        _connectionState = managed.ConnectionState;

        SetNoiseModeCommand = new RelayCommand<string>(mode =>
        {
            if (Enum.TryParse<NoiseMode>(mode, out var parsed))
            {
                SelectNoiseMode(parsed);
            }
        });
        ApplySceneCommand = new RelayCommand<Scene>(scene =>
        {
            if (scene is not null)
            {
                ApplyScene(scene);
            }
        });
        ApplyCustomEqualizerCommand = new AsyncRelayCommand(ApplyCustomEqualizerAsync);

        _headset.StateChanged += OnHeadsetStateChanged;
        ApplySnapshot(_headset.State);
    }

    // =========================================================================
    // IDENTITY
    // =========================================================================

    public string Id => _managed.Id;

    public string DeviceName => _managed.Name;

    public string ModelName => _headset.ModelName;

    public bool IsKnownModel => _headset.IsKnownModel;

    public HeadsetFeatures Features => _headset.Features;

    public string Firmware => string.IsNullOrEmpty(_snapshot.Firmware) ? NoValue : _snapshot.Firmware;

    public string Codec => string.IsNullOrEmpty(_snapshot.Codec) ? NoValue : _snapshot.Codec;

    // =========================================================================
    // CONNECTION
    // =========================================================================

    public HeadsetConnectionState ConnectionState => _connectionState;

    public bool IsConnected => _connectionState == HeadsetConnectionState.Connected;

    public bool IsConnecting => _connectionState == HeadsetConnectionState.Connecting;

    public string StatusText => _connectionState switch
    {
        HeadsetConnectionState.Connected when !string.IsNullOrEmpty(_snapshot.Codec) => $"Connected · {_snapshot.Codec}",
        HeadsetConnectionState.Connected when !IsKnownModel => "Connected · Unverified model",
        HeadsetConnectionState.Connected => "Connected",
        HeadsetConnectionState.Connecting => "Connecting…",
        _ => AutoConnect ? "Disconnected" : "Auto-connect off",
    };

    public bool AutoConnect
    {
        get => _settings.IsAutoConnectEnabled(Id);
        set
        {
            if (value == AutoConnect)
            {
                return;
            }
            _settings.SetAutoConnectEnabled(Id, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            AutoConnectChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? AutoConnectChanged;

    // =========================================================================
    // BATTERY
    // =========================================================================

    public bool ShowDualBattery => Features.DualBattery && _snapshot.Battery.Left is not null;

    public bool ShowSingleBattery => !ShowDualBattery;

    public string LeftBatteryText => Percent(_snapshot.Battery.Left);

    public string RightBatteryText => Percent(_snapshot.Battery.Right);

    public string CaseBatteryText => Percent(_snapshot.Battery.Case);

    public bool ShowCaseBattery => _snapshot.Battery.Case is not null;

    public string MainBatteryText => Percent(_snapshot.Battery.Main);

    public string LeftBatteryGlyph => Glyphs.Battery(_snapshot.Battery.Left);

    public string RightBatteryGlyph => Glyphs.Battery(_snapshot.Battery.Right);

    public string CaseBatteryGlyph => Glyphs.Battery(_snapshot.Battery.Case);

    public string MainBatteryGlyph => Glyphs.Battery(_snapshot.Battery.Main);

    public string BatterySummary => ShowDualBattery
        ? $"L {LeftBatteryText} · R {RightBatteryText}"
        : MainBatteryText;

    // =========================================================================
    // NOISE CONTROL
    // =========================================================================

    public IRelayCommand<string> SetNoiseModeCommand { get; }

    public IRelayCommand<Scene> ApplySceneCommand { get; }

    public IReadOnlyList<Scene> Scenes => _settings.Scenes;

    public NoiseMode NoiseMode => _noiseMode;

    public bool IsNoiseOff => _noiseMode == NoiseMode.Off;

    public bool IsNoiseCancelling => _noiseMode == NoiseMode.NoiseCancelling;

    public bool IsAmbient => _noiseMode == NoiseMode.Ambient;

    public double AmbientLevel
    {
        get => _ambientLevel;
        set
        {
            var level = Math.Clamp(Math.Round(value), 1, 20);
            if (!SetProperty(ref _ambientLevel, level) || _applying)
            {
                return;
            }
            OnPropertyChanged(nameof(AmbientLevelText));
            _noiseThrottler.Run(SendNoiseControlAsync);
        }
    }

    public string AmbientLevelText => $"{_ambientLevel:0}";

    public bool FocusOnVoice
    {
        get => _focusOnVoice;
        set
        {
            if (!SetProperty(ref _focusOnVoice, value) || _applying)
            {
                return;
            }
            _ = SendNoiseControlAsync();
        }
    }

    // =========================================================================
    // SOUND
    // =========================================================================

    public IReadOnlyList<EqualizerPresetOption> EqualizerPresets => _headset.EqualizerPresets;

    public int SelectedEqualizerIndex
    {
        get => _selectedEqualizerIndex;
        set
        {
            if (!SetProperty(ref _selectedEqualizerIndex, value) || _applying || value < 0 || value >= EqualizerPresets.Count)
            {
                return;
            }
            var preset = EqualizerPresets[value].Value;
            _ = RunCommandAsync(() => _headset.SetEqualizerPresetAsync(preset), "equalizer");
        }
    }

    /// <summary>
    /// 0 is Off, 1 is Auto.
    /// </summary>
    public int DseeIndex
    {
        get => _dseeIndex;
        set
        {
            if (!SetProperty(ref _dseeIndex, value) || _applying)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetDseeAsync(value == 1), "DSEE");
        }
    }

    public static IReadOnlyList<string> DseeOptions { get; } = ["Off", "Auto"];

    public double ClearBass
    {
        get => _clearBass;
        set => SetProperty(ref _clearBass, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band1
    {
        get => _band1;
        set => SetProperty(ref _band1, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band2
    {
        get => _band2;
        set => SetProperty(ref _band2, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band3
    {
        get => _band3;
        set => SetProperty(ref _band3, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band4
    {
        get => _band4;
        set => SetProperty(ref _band4, Math.Clamp(Math.Round(value), -10, 10));
    }

    public double Band5
    {
        get => _band5;
        set => SetProperty(ref _band5, Math.Clamp(Math.Round(value), -10, 10));
    }

    public IAsyncRelayCommand ApplyCustomEqualizerCommand { get; }

    // =========================================================================
    // SYSTEM
    // =========================================================================

    public bool SpeakToChat
    {
        get => _speakToChat;
        set
        {
            if (!SetProperty(ref _speakToChat, value) || _applying)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetSpeakToChatAsync(value), "Speak-to-Chat");
        }
    }

    public bool AdaptiveVolume
    {
        get => _adaptiveVolume;
        set
        {
            if (!SetProperty(ref _adaptiveVolume, value) || _applying)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetAdaptiveVolumeAsync(value), "adaptive volume");
        }
    }

    public int AutoPowerOffIndex
    {
        get => _autoPowerOffIndex;
        set
        {
            if (!SetProperty(ref _autoPowerOffIndex, value) || _applying || value < 0)
            {
                return;
            }
            _ = RunCommandAsync(() => _headset.SetAutoPowerOffAsync(value), "auto power-off");
        }
    }

    public static IReadOnlyList<string> AutoPowerOffOptions { get; } =
        ["Off", "After 5 minutes", "After 30 minutes", "After 1 hour", "After 3 hours", "When taken off"];

    // =========================================================================
    // ERRORS
    // =========================================================================

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    // =========================================================================
    // METHODS
    // =========================================================================

    public void SelectNoiseMode(NoiseMode mode)
    {
        if (mode == _noiseMode)
        {
            return;
        }
        _noiseMode = mode;
        RaiseNoiseModeChanged();
        _ = SendNoiseControlAsync();
    }

    public void ApplyScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _applying = true;
        try
        {
            _noiseMode = scene.Setting.Mode;
            if (scene.Setting.Mode == NoiseMode.Ambient)
            {
                AmbientLevel = scene.Setting.AmbientLevel;
            }
            FocusOnVoice = scene.Setting.FocusOnVoice;
        }
        finally
        {
            _applying = false;
        }
        RaiseNoiseModeChanged();
        OnPropertyChanged(nameof(AmbientLevelText));
        _ = SendNoiseControlAsync();
    }

    /// <summary>
    /// Asks the headset for fresh battery levels. Failures are logged, not shown.
    /// </summary>
    public async Task RefreshBatteryAsync()
    {
        if (!IsConnected)
        {
            return;
        }
        try
        {
            await _headset.RefreshBatteryAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.BatteryRefreshFailed(_logger, ex, DeviceName);
        }
    }

    internal void UpdateConnectionState(HeadsetConnectionState state)
    {
        if (!SetProperty(ref _connectionState, state, nameof(ConnectionState)))
        {
            return;
        }
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(StatusText));
        if (state == HeadsetConnectionState.Connected)
        {
            ApplySnapshot(_headset.State);
        }
    }

    internal void RefreshScenes() => OnPropertyChanged(nameof(Scenes));

    public void Dispose()
    {
        _headset.StateChanged -= OnHeadsetStateChanged;
        _noiseThrottler.Dispose();
        _errorTimer?.Dispose();
    }

    private static string Percent(int? level) => level is null ? NoValue : $"{level}%";

    private void OnHeadsetStateChanged(object? sender, HeadsetSnapshot snapshot) => _ui.Post(() => ApplySnapshot(snapshot));

    private void ApplySnapshot(HeadsetSnapshot snapshot)
    {
        var skipNoise = _noiseThrottler.HasPending;
        _snapshot = snapshot;
        _applying = true;
        try
        {
            if (!skipNoise)
            {
                _noiseMode = snapshot.NoiseControl.Mode;
                if (snapshot.NoiseControl.Mode == NoiseMode.Ambient && snapshot.NoiseControl.AmbientLevel > 0)
                {
                    AmbientLevel = snapshot.NoiseControl.AmbientLevel;
                }
                FocusOnVoice = snapshot.NoiseControl.FocusOnVoice;
            }

            SelectedEqualizerIndex = EqualizerPresets
                .Select((option, index) => (option, index))
                .FirstOrDefault(pair => pair.option.Value == snapshot.Equalizer.Preset, (null!, -1)).index;
            ClearBass = snapshot.Equalizer.ClearBass;
            Band1 = snapshot.Equalizer.Bands[0];
            Band2 = snapshot.Equalizer.Bands[1];
            Band3 = snapshot.Equalizer.Bands[2];
            Band4 = snapshot.Equalizer.Bands[3];
            Band5 = snapshot.Equalizer.Bands[4];
            DseeIndex = snapshot.Dsee ? 1 : 0;
            SpeakToChat = snapshot.SpeakToChat;
            AdaptiveVolume = snapshot.AdaptiveVolume;
            AutoPowerOffIndex = snapshot.AutoPowerOff;
        }
        finally
        {
            _applying = false;
        }

        RaiseNoiseModeChanged();
        OnPropertyChanged(nameof(AmbientLevelText));
        OnPropertyChanged(nameof(ShowDualBattery));
        OnPropertyChanged(nameof(ShowSingleBattery));
        OnPropertyChanged(nameof(LeftBatteryText));
        OnPropertyChanged(nameof(RightBatteryText));
        OnPropertyChanged(nameof(CaseBatteryText));
        OnPropertyChanged(nameof(ShowCaseBattery));
        OnPropertyChanged(nameof(MainBatteryText));
        OnPropertyChanged(nameof(LeftBatteryGlyph));
        OnPropertyChanged(nameof(RightBatteryGlyph));
        OnPropertyChanged(nameof(CaseBatteryGlyph));
        OnPropertyChanged(nameof(MainBatteryGlyph));
        OnPropertyChanged(nameof(BatterySummary));
        OnPropertyChanged(nameof(Firmware));
        OnPropertyChanged(nameof(Codec));
        OnPropertyChanged(nameof(StatusText));

        _lowBattery.Update(Id, DeviceName, snapshot.Battery);
    }

    private void RaiseNoiseModeChanged()
    {
        OnPropertyChanged(nameof(NoiseMode));
        OnPropertyChanged(nameof(IsNoiseOff));
        OnPropertyChanged(nameof(IsNoiseCancelling));
        OnPropertyChanged(nameof(IsAmbient));
    }

    private Task SendNoiseControlAsync()
    {
        var setting = new NoiseControlSetting(_noiseMode, (int)_ambientLevel, _focusOnVoice);
        return RunCommandAsync(() => _headset.SetNoiseControlAsync(setting), "noise control");
    }

    private Task ApplyCustomEqualizerAsync()
    {
        var setting = new EqualizerSetting(
            EqualizerSetting.ManualPreset,
            (int)_clearBass,
            ImmutableArray.Create((int)_band1, (int)_band2, (int)_band3, (int)_band4, (int)_band5));
        return RunCommandAsync(() => _headset.SetEqualizerCustomAsync(setting), "custom equalizer");
    }

    private async Task RunCommandAsync(Func<Task> command, string setting)
    {
        try
        {
            await command().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.CommandFailed(_logger, ex, setting, DeviceName);
            _ui.Post(() =>
            {
                ApplySnapshot(_headset.State);
                ShowError(HeadsetErrorMessages.Describe(ex));
            });
        }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        _errorTimer?.Dispose();
        _errorTimer = _timeProvider.CreateTimer(_ => _ui.Post(() => ErrorMessage = null), null, ErrorDuration, Timeout.InfiniteTimeSpan);
    }
}
````

- [ ] **Step 4: Create `src/ConnectFlyout.Presentation/ViewModels/PlaybackViewModel.cs`**

````csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Common;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Media;

namespace ConnectFlyout.Presentation.ViewModels;

/// <summary>
/// Now-playing info, transport buttons and the Windows volume slider in the flyout.
/// </summary>
public sealed class PlaybackViewModel : ObservableObject
{
    private readonly IMediaController _media;
    private readonly IVolumeController _volume;
    private readonly ILogger _logger;
    private readonly UiContext _ui = new();

    private string _title = "";
    private string _artist = "";
    private bool _hasTrack;
    private bool _isPlaying;
    private double _volumeLevel;
    private bool _applying;

    public PlaybackViewModel(IMediaController media, IVolumeController volume, ILogger logger)
    {
        _media = media;
        _volume = volume;
        _logger = logger;

        PlayPauseCommand = new AsyncRelayCommand(() => RunAsync(_media.PlayPauseAsync));
        NextCommand = new AsyncRelayCommand(() => RunAsync(_media.NextAsync));
        PreviousCommand = new AsyncRelayCommand(() => RunAsync(_media.PreviousAsync));
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        private set => SetProperty(ref _artist, value);
    }

    public bool HasTrack
    {
        get => _hasTrack;
        private set
        {
            if (SetProperty(ref _hasTrack, value))
            {
                OnPropertyChanged(nameof(NoTrack));
            }
        }
    }

    public bool NoTrack => !_hasTrack;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public string PlayPauseGlyph => _isPlaying ? Glyphs.Pause : Glyphs.Play;

    public double Volume
    {
        get => _volumeLevel;
        set
        {
            if (!SetProperty(ref _volumeLevel, Math.Clamp(Math.Round(value), 0, 100)) || _applying)
            {
                return;
            }
            try
            {
                _volume.SetVolume(_volumeLevel);
            }
            catch (Exception ex)
            {
                LogMessages.VolumeSetFailed(_logger, ex);
            }
        }
    }

    public IAsyncRelayCommand PlayPauseCommand { get; }

    public IAsyncRelayCommand NextCommand { get; }

    public IAsyncRelayCommand PreviousCommand { get; }

    /// <summary>
    /// Reads the current track and volume. Called each time the flyout opens.
    /// </summary>
    public async Task RefreshAsync()
    {
        MediaInfo? info = null;
        try
        {
            info = await _media.GetCurrentAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.MediaReadFailed(_logger, ex);
        }

        double? volume = null;
        try
        {
            volume = _volume.GetVolume();
        }
        catch (Exception ex)
        {
            LogMessages.VolumeReadFailed(_logger, ex);
        }

        _ui.Post(() =>
        {
            Title = info?.Title ?? "";
            Artist = info?.Artist ?? "";
            HasTrack = info is not null && !string.IsNullOrEmpty(info.Title);
            IsPlaying = info?.IsPlaying ?? false;
            if (volume is not null)
            {
                _applying = true;
                Volume = volume.Value;
                _applying = false;
            }
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogMessages.MediaCommandFailed(_logger, ex);
        }
        await RefreshAsync().ConfigureAwait(false);
    }
}
````

- [ ] **Step 5: Create `src/ConnectFlyout.Presentation/ViewModels/FlyoutViewModel.cs`**

````csharp
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
    private readonly UiContext _ui = new();

    private FlyoutRoute _route = new(FlyoutPageKind.Empty, null, false);
    private HeadsetViewModel? _currentHeadset;

    public FlyoutViewModel(
        HeadsetManager manager,
        FlyoutNavigator navigator,
        PlaybackViewModel playback,
        Func<ManagedHeadset, HeadsetViewModel> createHeadsetViewModel)
    {
        ArgumentNullException.ThrowIfNull(manager);

        _manager = manager;
        _navigator = navigator;
        _createHeadsetViewModel = createHeadsetViewModel;
        Playback = playback;

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

    public PlaybackViewModel Playback { get; }

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

    public IRelayCommand<HeadsetViewModel> PickCommand { get; }

    public IRelayCommand BackCommand { get; }

    public IRelayCommand ReconnectCommand { get; }

    public IRelayCommand OpenSettingsCommand { get; }

    public IRelayCommand QuitCommand { get; }

    /// <summary>
    /// Refreshes playback and battery. Called each time the flyout opens.
    /// </summary>
    public Task OnOpenedAsync()
    {
        Refresh();
        var battery = CurrentHeadset?.RefreshBatteryAsync() ?? Task.CompletedTask;
        return Task.WhenAll(Playback.RefreshAsync(), battery);
    }

    public void Dispose()
    {
        _manager.HeadsetAdded -= OnHeadsetAdded;
        _manager.HeadsetRemoved -= OnHeadsetRemoved;
        _manager.ConnectionStateChanged -= OnConnectionStateChanged;
        foreach (var headset in Headsets)
        {
            headset.Dispose();
        }
        Headsets.Clear();
    }

    private void Pick(HeadsetViewModel? headset)
    {
        if (headset is null)
        {
            return;
        }
        _navigator.Pick(headset.Id);
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
            viewModel.Dispose();
        }
        Refresh();
    });

    private void OnConnectionStateChanged(object? sender, ManagedHeadset headset) => _ui.Post(() =>
        Headsets.FirstOrDefault(item => item.Id == headset.Id)?.UpdateConnectionState(headset.ConnectionState));

    private void OnAutoConnectChanged(object? sender, EventArgs e)
    {
        if (sender is HeadsetViewModel headset)
        {
            _manager.ApplyAutoConnect(headset.Id);
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
        Headsets.Add(viewModel);
    }

    private void Refresh()
    {
        _route = _navigator.Resolve([.. Headsets.Select(headset => headset.Id)]);
        CurrentHeadset = _route.HeadsetId is null ? null : Headsets.FirstOrDefault(headset => headset.Id == _route.HeadsetId);
        OnPropertyChanged(nameof(Page));
        OnPropertyChanged(nameof(IsEmptyVisible));
        OnPropertyChanged(nameof(IsPickerVisible));
        OnPropertyChanged(nameof(IsDeviceVisible));
        OnPropertyChanged(nameof(ShowBack));
    }
}
````

- [ ] **Step 6: Create `src/ConnectFlyout.Presentation/ViewModels/SettingsViewModel.cs`**

````csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.Settings;

namespace ConnectFlyout.Presentation.ViewModels;

/// <summary>
/// One editable scene in the Noise &amp; scenes settings page.
/// </summary>
public sealed class SceneEditorViewModel : ObservableObject
{
    private string _name;
    private int _modeIndex;
    private double _ambientLevel;
    private bool _focusOnVoice;

    public SceneEditorViewModel(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        Glyph = scene.Glyph;
        _name = scene.Name;
        _modeIndex = (int)scene.Setting.Mode;
        _ambientLevel = Math.Clamp(scene.Setting.AmbientLevel, 1, 20);
        _focusOnVoice = scene.Setting.FocusOnVoice;
    }

    public static IReadOnlyList<string> ModeOptions { get; } = ["Off", "Noise cancelling", "Ambient sound"];

    public string Glyph { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>
    /// Index into <see cref="ModeOptions"/>, matching <see cref="NoiseMode"/>.
    /// </summary>
    public int ModeIndex
    {
        get => _modeIndex;
        set
        {
            if (SetProperty(ref _modeIndex, value))
            {
                OnPropertyChanged(nameof(IsAmbient));
            }
        }
    }

    public bool IsAmbient => _modeIndex == (int)NoiseMode.Ambient;

    public double AmbientLevel
    {
        get => _ambientLevel;
        set => SetProperty(ref _ambientLevel, Math.Clamp(Math.Round(value), 1, 20));
    }

    public bool FocusOnVoice
    {
        get => _focusOnVoice;
        set => SetProperty(ref _focusOnVoice, value);
    }

    public Scene ToScene()
    {
        var mode = (NoiseMode)Math.Clamp(_modeIndex, 0, 2);
        var level = mode == NoiseMode.Ambient ? (int)_ambientLevel : 0;
        var name = string.IsNullOrWhiteSpace(_name) ? "Scene" : _name.Trim();
        return new Scene(name, Glyph, new NoiseControlSetting(mode, level, _focusOnVoice));
    }
}

/// <summary>
/// The settings window.
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly FlyoutViewModel _flyout;
    private readonly AppSettings _settings;
    private readonly LogLevelSwitch _logLevel;
    private readonly IStartupTaskService _startup;
    private readonly Action<AppTheme> _applyTheme;
    private readonly Action<bool> _applyNativeDebugLogging;
    private readonly Action<string> _openFolder;
    private readonly ILogger _logger;

    private int _selectedHeadsetIndex;
    private bool _launchAtSignIn;

    public SettingsViewModel(
        FlyoutViewModel flyout,
        AppSettings settings,
        LogLevelSwitch logLevel,
        IStartupTaskService startup,
        Action<AppTheme> applyTheme,
        Action<bool> applyNativeDebugLogging,
        Action<string> openFolder,
        string logFolder,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(flyout);

        _flyout = flyout;
        _settings = settings;
        _logLevel = logLevel;
        _startup = startup;
        _applyTheme = applyTheme;
        _applyNativeDebugLogging = applyNativeDebugLogging;
        _openFolder = openFolder;
        _logger = logger;
        LogFolder = logFolder;

        Scenes = [.. settings.Scenes.Select(scene => new SceneEditorViewModel(scene))];
        SaveScenesCommand = new RelayCommand(SaveScenes);
        ResetScenesCommand = new RelayCommand(ResetScenes);
        OpenLogFolderCommand = new RelayCommand(() => _openFolder(LogFolder));

        _flyout.Headsets.CollectionChanged += (_, _) => OnHeadsetsChanged();
        _selectedHeadsetIndex = Headsets.Count > 0 ? 0 : -1;
    }

    // =========================================================================
    // HEADSETS
    // =========================================================================

    public ObservableCollection<HeadsetViewModel> Headsets => _flyout.Headsets;

    public int SelectedHeadsetIndex
    {
        get => _selectedHeadsetIndex;
        set
        {
            if (SetProperty(ref _selectedHeadsetIndex, value))
            {
                OnPropertyChanged(nameof(SelectedHeadset));
                OnPropertyChanged(nameof(HasHeadset));
                OnPropertyChanged(nameof(NoHeadset));
            }
        }
    }

    public HeadsetViewModel? SelectedHeadset =>
        _selectedHeadsetIndex >= 0 && _selectedHeadsetIndex < Headsets.Count ? Headsets[_selectedHeadsetIndex] : null;

    public bool HasHeadset => SelectedHeadset is not null;

    public bool NoHeadset => SelectedHeadset is null;

    // =========================================================================
    // SCENES
    // =========================================================================

    public ObservableCollection<SceneEditorViewModel> Scenes { get; }

    public IRelayCommand SaveScenesCommand { get; }

    public IRelayCommand ResetScenesCommand { get; }

    // =========================================================================
    // APP
    // =========================================================================

    public bool LaunchAtSignIn
    {
        get => _launchAtSignIn;
        set
        {
            if (SetProperty(ref _launchAtSignIn, value))
            {
                _ = ApplyLaunchAtSignInAsync(value);
            }
        }
    }

    public bool LowBatteryNotifications
    {
        get => _settings.LowBatteryNotifications;
        set
        {
            if (value == _settings.LowBatteryNotifications)
            {
                return;
            }
            _settings.LowBatteryNotifications = value;
            OnPropertyChanged();
        }
    }

    public static IReadOnlyList<string> ThemeOptions { get; } = ["Use system setting", "Light", "Dark"];

    public int ThemeIndex
    {
        get => (int)_settings.Theme;
        set
        {
            if (value < 0 || value == (int)_settings.Theme)
            {
                return;
            }
            _settings.Theme = (AppTheme)value;
            _applyTheme(_settings.Theme);
            OnPropertyChanged();
        }
    }

    public bool DebugLogging
    {
        get => _settings.DebugLogging;
        set
        {
            if (value == _settings.DebugLogging)
            {
                return;
            }
            _settings.DebugLogging = value;
            ApplyLogLevel();
            OnPropertyChanged();
        }
    }

    public string LogFolder { get; }

    public IRelayCommand OpenLogFolderCommand { get; }

    /// <summary>
    /// Reads the startup task state. Called when the window opens.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            _launchAtSignIn = await _startup.IsEnabledAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(LaunchAtSignIn));
        }
        catch (Exception ex)
        {
            LogMessages.StartupTaskReadFailed(_logger, ex);
        }
    }

    /// <summary>
    /// Applies the saved Debug logging choice to both loggers. Called at startup.
    /// </summary>
    public void ApplyLogLevel()
    {
        _logLevel.MinimumLevel = _settings.DebugLogging ? LogLevel.Debug : LogLevel.Information;
        _applyNativeDebugLogging(_settings.DebugLogging);
    }

    private async Task ApplyLaunchAtSignInAsync(bool enabled)
    {
        try
        {
            var actual = await _startup.SetEnabledAsync(enabled).ConfigureAwait(true);
            if (actual != _launchAtSignIn)
            {
                _launchAtSignIn = actual;
                OnPropertyChanged(nameof(LaunchAtSignIn));
            }
        }
        catch (Exception ex)
        {
            LogMessages.StartupTaskChangeFailed(_logger, ex);
        }
    }

    private void SaveScenes()
    {
        _settings.Scenes = [.. Scenes.Select(scene => scene.ToScene())];
        foreach (var headset in Headsets)
        {
            headset.RefreshScenes();
        }
    }

    private void ResetScenes()
    {
        Scenes.Clear();
        foreach (var scene in Scene.Defaults)
        {
            Scenes.Add(new SceneEditorViewModel(scene));
        }
        SaveScenes();
    }

    private void OnHeadsetsChanged()
    {
        if (_selectedHeadsetIndex >= Headsets.Count || (_selectedHeadsetIndex < 0 && Headsets.Count > 0))
        {
            SelectedHeadsetIndex = Headsets.Count > 0 ? 0 : -1;
            return;
        }
        OnPropertyChanged(nameof(SelectedHeadset));
        OnPropertyChanged(nameof(HasHeadset));
        OnPropertyChanged(nameof(NoHeadset));
    }
}
````

- [ ] **Step 7: Build and run the full C# suite**

Expected: `Passed!  - Failed: 0, Passed: 95, Skipped: 1, Total: 96`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
dotnet test src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj --no-build -p:Platform=x64 --filter "TestCategory!=XM4"
````

---

### Task 12: WinUI App (Tray Icon, Flyout, Settings Window)

**Files:**
- Create: `scripts/New-AppAssets.ps1` (and the six PNGs it writes to `src/ConnectFlyout.App/Assets/`), `src/ConnectFlyout.App/ConnectFlyout.App.csproj`, `app.manifest`, `Package.appxmanifest`, `Program.cs`, `App.xaml`, `App.xaml.cs`, `AppLog.cs`, `NativeMethods.cs`, `TrayIcon.cs`, `ScreenGeometry.cs`, `AppNotificationService.cs`, `TileStyles.cs`, `Styles/AppStyles.xaml`, `FlyoutWindow.xaml(.cs)`, `Views/FlyoutView.xaml(.cs)`, `SettingsWindow.xaml(.cs)`, `Views/Settings/SettingsPageBase.cs`, `Views/Settings/HeadsetSelector.xaml(.cs)`, `Views/Settings/{Devices,Sound,NoiseScenes,System,App}Page.xaml(.cs)` (all under `src/ConnectFlyout.App/`)

**Interfaces:**
- Consumes: every Presentation type from Tasks 7–11 and `ConnectFlyout.Core.HeadsetClient.SetLogHandler/SetDebugLogging`.
- Produces: the runnable, packageable app. No unit tests here: every rule this shell uses is tested in Presentation, and the running app is checked by hand in Task 13.

- [ ] **Step 1: Create `scripts/New-AppAssets.ps1`**

````powershell
<#
    Outputs the PNG logos the MSIX manifest needs plus the two tray icons
    (TrayLight.png for light taskbars, TrayDark.png for dark ones) into
    src/ConnectFlyout.App/Assets.

    Draws the "Headphone" glyph (U+E7F6) from Segoe Fluent Icons, which ships
    with Windows 11. Uses System.Drawing: https://learn.microsoft.com/dotnet/api/system.drawing
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot '..\src\ConnectFlyout.App\Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

$glyph = [string][char]0xE7F6
$fontFamily = 'Segoe Fluent Icons'

# Draw the glyph centered on a canvas, optionally on a rounded tile
function New-GlyphImage {
    param(
        [int] $Width,
        [int] $Height,
        [double] $GlyphScale,
        [System.Drawing.Color] $Foreground,
        [System.Drawing.Color] $Tile,
        [string] $Path
    )

    $bitmap = New-Object System.Drawing.Bitmap $Width, $Height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([System.Drawing.Color]::Transparent)

    if ($Tile.A -gt 0) {
        $side = [Math]::Min($Width, $Height)
        $radius = [int]($side * 0.2)
        $x = [int](($Width - $side) / 2)
        $y = [int](($Height - $side) / 2)
        $shape = New-Object System.Drawing.Drawing2D.GraphicsPath
        $shape.AddArc($x, $y, $radius * 2, $radius * 2, 180, 90)
        $shape.AddArc($x + $side - $radius * 2, $y, $radius * 2, $radius * 2, 270, 90)
        $shape.AddArc($x + $side - $radius * 2, $y + $side - $radius * 2, $radius * 2, $radius * 2, 0, 90)
        $shape.AddArc($x, $y + $side - $radius * 2, $radius * 2, $radius * 2, 90, 90)
        $shape.CloseFigure()
        $graphics.FillPath((New-Object System.Drawing.SolidBrush $Tile), $shape)
    }

    $size = [Math]::Min($Width, $Height) * $GlyphScale
    $font = New-Object System.Drawing.Font $fontFamily, $size, ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $area = New-Object System.Drawing.RectangleF 0, 0, $Width, $Height
    $graphics.DrawString($glyph, $font, (New-Object System.Drawing.SolidBrush $Foreground), $area, $format)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    Write-Output "Wrote $Path"
}

$white = [System.Drawing.Color]::White
$black = [System.Drawing.Color]::Black
$tile = [System.Drawing.Color]::FromArgb(255, 32, 32, 32)
$none = [System.Drawing.Color]::Transparent

# Package Logos
New-GlyphImage -Width 44 -Height 44 -GlyphScale 0.6 -Foreground $white -Tile $tile -Path (Join-Path $assets 'Square44x44Logo.png')
New-GlyphImage -Width 150 -Height 150 -GlyphScale 0.5 -Foreground $white -Tile $tile -Path (Join-Path $assets 'Square150x150Logo.png')
New-GlyphImage -Width 310 -Height 150 -GlyphScale 0.5 -Foreground $white -Tile $tile -Path (Join-Path $assets 'Wide310x150Logo.png')
New-GlyphImage -Width 50 -Height 50 -GlyphScale 0.6 -Foreground $white -Tile $tile -Path (Join-Path $assets 'StoreLogo.png')

# Tray Icons
New-GlyphImage -Width 64 -Height 64 -GlyphScale 0.85 -Foreground $black -Tile $none -Path (Join-Path $assets 'TrayLight.png')
New-GlyphImage -Width 64 -Height 64 -GlyphScale 0.85 -Foreground $white -Tile $none -Path (Join-Path $assets 'TrayDark.png')
````

- [ ] **Step 2: Generate the assets**

Expected: six `Wrote ...` lines.

````powershell
.\scripts\New-AppAssets.ps1
````

- [ ] **Step 3: Create `src/ConnectFlyout.App/ConnectFlyout.App.csproj`**

````xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <RootNamespace>ConnectFlyout.App</RootNamespace>
    <AssemblyName>ConnectFlyout</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <SelfContained>true</SelfContained>
    <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Platform)' == 'x64'">
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Platform)' == 'ARM64'">
    <RuntimeIdentifier>win-arm64</RuntimeIdentifier>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="Assets\*.png" />
    <Manifest Include="$(ApplicationManifest)" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.12" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.260804001" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\ConnectFlyout.Presentation\ConnectFlyout.Presentation.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectCapability Include="Msix" />
  </ItemGroup>

</Project>
````

- [ ] **Step 4: Create `src/ConnectFlyout.App/app.manifest`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="ConnectFlyout.app" />

  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
````

- [ ] **Step 5: Create `src/ConnectFlyout.App/Package.appxmanifest`**

````xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap5="http://schemas.microsoft.com/appx/manifest/uap/windows10/5"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap5 rescap">

  <Identity
    Name="Artistro08.ConnectFlyout"
    Publisher="CN=Devin Green"
    Version="1.0.0.0" />

  <Properties>
    <DisplayName>ConnectFlyout</DisplayName>
    <PublisherDisplayName>Devin Green</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22621.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="x-generate" />
  </Resources>

  <Applications>
    <Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="$targetentrypoint$">
      <uap:VisualElements
        DisplayName="ConnectFlyout"
        Description="Control Sony headphones from the Windows tray."
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>

      <Extensions>
        <uap5:Extension Category="windows.startupTask">
          <uap5:StartupTask TaskId="ConnectFlyoutStartup" Enabled="false" DisplayName="ConnectFlyout" />
        </uap5:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <DeviceCapability Name="bluetooth" />
  </Capabilities>
</Package>
````

- [ ] **Step 6: Create `src/ConnectFlyout.App/Program.cs`**

````csharp
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ConnectFlyout.App;

/// <summary>
/// Entry point. Replaces the XAML-generated Main so only one copy of the tray app runs.
/// </summary>
public static class Program
{
    private const string InstanceKey = "ConnectFlyout";

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // A second launch (startup task plus a manual start) exits right away.
        var instance = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!instance.IsCurrent)
        {
            return;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
````

- [ ] **Step 7: Create `src/ConnectFlyout.App/App.xaml`**

````xml
<Application
    x:Class="ConnectFlyout.App.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
                <ResourceDictionary Source="Styles/AppStyles.xaml" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
````

- [ ] **Step 8: Create `src/ConnectFlyout.App/Styles/AppStyles.xaml`**

````xml
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Theme Brushes -->
    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="FlyoutFooterBackgroundBrush" Color="#0F000000" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="FlyoutFooterBackgroundBrush" Color="#33000000" />
        </ResourceDictionary>
        <ResourceDictionary x:Key="HighContrast">
            <SolidColorBrush x:Key="FlyoutFooterBackgroundBrush" Color="{ThemeResource SystemColorWindowColor}" />
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>

    <!-- Flyout Section Header -->
    <Style x:Key="FlyoutSectionHeaderStyle" TargetType="TextBlock" BasedOn="{StaticResource BodyStrongTextBlockStyle}">
        <Setter Property="Margin" Value="0,0,0,8" />
    </Style>

    <!-- Secondary Text -->
    <Style x:Key="SecondaryTextStyle" TargetType="TextBlock" BasedOn="{StaticResource CaptionTextBlockStyle}">
        <Setter Property="Foreground" Value="{ThemeResource TextFillColorSecondaryBrush}" />
    </Style>

    <!-- Noise Control Tiles -->
    <Style x:Key="TileButtonStyle" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="VerticalAlignment" Value="Stretch" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="Height" Value="64" />
        <Setter Property="Padding" Value="4" />
        <Setter Property="CornerRadius" Value="{StaticResource OverlayCornerRadius}" />
    </Style>

    <Style x:Key="TileButtonSelectedStyle" TargetType="Button" BasedOn="{StaticResource AccentButtonStyle}">
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="VerticalAlignment" Value="Stretch" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="Height" Value="64" />
        <Setter Property="Padding" Value="4" />
        <Setter Property="CornerRadius" Value="{StaticResource OverlayCornerRadius}" />
    </Style>

    <!-- Footer Buttons -->
    <Style x:Key="FooterIconButtonStyle" TargetType="Button" BasedOn="{StaticResource SubtleButtonStyle}">
        <Setter Property="Width" Value="36" />
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="0" />
    </Style>

    <Style x:Key="FooterTextButtonStyle" TargetType="Button" BasedOn="{StaticResource SubtleButtonStyle}">
        <Setter Property="Height" Value="36" />
        <Setter Property="Padding" Value="8,0" />
    </Style>

    <!-- Settings Cards -->
    <Style x:Key="SettingsCardStyle" TargetType="Border">
        <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
        <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="{StaticResource OverlayCornerRadius}" />
        <Setter Property="Padding" Value="16" />
        <Setter Property="MinHeight" Value="68" />
    </Style>

    <Style x:Key="SettingsPageTitleStyle" TargetType="TextBlock" BasedOn="{StaticResource TitleTextBlockStyle}">
        <Setter Property="Margin" Value="0,0,0,16" />
    </Style>
</ResourceDictionary>
````

- [ ] **Step 9: Create `src/ConnectFlyout.App/AppLog.cs`**

````csharp
using Microsoft.Extensions.Logging;

namespace ConnectFlyout.App;

/// <summary>
/// Every log message the app shell writes, as source-generated LoggerMessage delegates.
/// </summary>
internal static partial class AppLog
{
    [LoggerMessage(EventId = 900, Message = "{Message}")]
    public static partial void Native(ILogger logger, LogLevel level, string message);

    [LoggerMessage(EventId = 901, Level = LogLevel.Information, Message = "ConnectFlyout {Version} started")]
    public static partial void Started(ILogger logger, string version);

    [LoggerMessage(EventId = 902, Level = LogLevel.Information, Message = "ConnectFlyout quit")]
    public static partial void Quit(ILogger logger);

    [LoggerMessage(EventId = 903, Level = LogLevel.Critical, Message = "Unhandled exception")]
    public static partial void Unhandled(ILogger logger, Exception? exception);

    [LoggerMessage(EventId = 904, Level = LogLevel.Warning, Message = "Unobserved task exception")]
    public static partial void UnobservedTask(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 905, Level = LogLevel.Warning, Message = "App notifications aren't available")]
    public static partial void NotificationsUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 906, Level = LogLevel.Warning, Message = "Couldn't open the log folder")]
    public static partial void OpenFolderFailed(ILogger logger, Exception exception);
}
````

- [ ] **Step 10: Create `src/ConnectFlyout.App/NativeMethods.cs`**

````csharp
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace ConnectFlyout.App;

/// <summary>
/// Win32 calls for the tray icon, flyout placement and window chrome.
/// </summary>
internal static class NativeMethods
{
    // =========================================================================
    // MESSAGES AND FLAGS
    // =========================================================================

    public const uint WM_NULL = 0x0000;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_APP = 0x8000;

    public const uint NIM_ADD = 0x00;
    public const uint NIM_MODIFY = 0x01;
    public const uint NIM_DELETE = 0x02;
    public const uint NIM_SETVERSION = 0x04;

    public const uint NIF_MESSAGE = 0x01;
    public const uint NIF_ICON = 0x02;
    public const uint NIF_TIP = 0x04;
    public const uint NIF_SHOWTIP = 0x80;

    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = 0x0400;
    public const uint NIN_KEYSELECT = 0x0401;

    public const uint MF_STRING = 0x0000;
    public const uint MF_SEPARATOR = 0x0800;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_NONOTIFY = 0x0080;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int SM_CXSMICON = 49;

    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWCP_ROUND = 2;

    // =========================================================================
    // STRUCTS
    // =========================================================================

    public delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    // =========================================================================
    // USER32
    // =========================================================================

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        uint exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    // =========================================================================
    // SHELL, SHCORE, DWM, KERNEL32
    // =========================================================================

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? moduleName);
}
````

- [ ] **Step 11: Create `src/ConnectFlyout.App/TrayIcon.cs`**

````csharp
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ConnectFlyout.App;

/// <summary>
/// The notification-area icon.
/// </summary>
/// <remarks>
/// Owns a hidden top-level window that receives the icon's callback messages, the
/// "TaskbarCreated" broadcast (Explorer restarted, so the icon is added again) and theme
/// changes (the icon switches between its light- and dark-taskbar versions). Uses
/// NOTIFYICON_VERSION_4, so a click or Enter arrives as NIN_SELECT/NIN_KEYSELECT and a
/// right-click as WM_CONTEXTMENU with the anchor point in wParam.
/// </remarks>
internal sealed class TrayIcon : IDisposable
{
    private const string WindowClassName = "ConnectFlyoutTrayWindow";
    private const uint IconId = 1;
    private const uint CallbackMessage = NativeMethods.WM_APP + 1;
    private const int MenuOpen = 1;
    private const int MenuSettings = 2;
    private const int MenuQuit = 3;

    private readonly string _tooltip;
    private readonly NativeMethods.WndProc _windowProc;
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreatedMessage;
    private IntPtr _icon;
    private bool _disposed;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        _windowProc = WindowProc;

        var windowClass = new NativeMethods.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = WindowClassName,
        };
        if (NativeMethods.RegisterClassExW(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _hwnd = NativeMethods.CreateWindowExW(0, WindowClassName, tooltip, 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, windowClass.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
    }

    /// <summary>
    /// Left click, Enter, or "Open ConnectFlyout" in the menu.
    /// </summary>
    public event EventHandler? Invoked;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    public void Show()
    {
        LoadIcon();

        var data = CreateData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        // Fails when Explorer isn't up yet at sign-in; TaskbarCreated adds the icon later.
        if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_ADD, ref data))
        {
            return;
        }

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_SETVERSION, ref data);
    }

    /// <summary>
    /// Screen rectangle of the icon, or null when the shell can't report it.
    /// </summary>
    public NativeMethods.RECT? GetIconRect()
    {
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = IconId,
        };
        return NativeMethods.Shell_NotifyIconGetRect(ref identifier, out var rect) == 0 ? rect : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var data = CreateData(0);
        NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_DELETE, ref data);
        NativeMethods.DestroyWindow(_hwnd);
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }
    }

    private static bool TaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    private NativeMethods.NOTIFYICONDATAW CreateData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private void LoadIcon()
    {
        // Light taskbar gets the dark glyph and the other way around.
        var fileName = TaskbarUsesLightTheme() ? "TrayLight.png" : "TrayDark.png";
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", fileName));
        var size = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSMICON, NativeMethods.GetDpiForSystem());

        var icon = NativeMethods.CreateIconFromResourceEx(bytes, (uint)bytes.Length, true, 0x00030000, size, size, 0);
        if (icon == IntPtr.Zero)
        {
            return;
        }
        if (_icon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_icon);
        }
        _icon = icon;
    }

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    Invoked?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WM_CONTEXTMENU:
                    ShowContextMenu(wParam);
                    break;
            }
            return IntPtr.Zero;
        }

        if (message == _taskbarCreatedMessage)
        {
            Show();
            return IntPtr.Zero;
        }

        if (message == NativeMethods.WM_SETTINGCHANGE)
        {
            LoadIcon();
            var data = CreateData(NativeMethods.NIF_ICON);
            NativeMethods.Shell_NotifyIconW(NativeMethods.NIM_MODIFY, ref data);
        }

        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowContextMenu(IntPtr anchor)
    {
        var x = (short)(anchor.ToInt64() & 0xFFFF);
        var y = (short)((anchor.ToInt64() >> 16) & 0xFFFF);

        var menu = NativeMethods.CreatePopupMenu();
        NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, MenuOpen, "Open ConnectFlyout");
        NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, MenuSettings, "Settings");
        NativeMethods.AppendMenuW(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
        NativeMethods.AppendMenuW(menu, NativeMethods.MF_STRING, MenuQuit, "Quit");

        // Foreground first and a WM_NULL after, or the menu won't close on click-away.
        NativeMethods.SetForegroundWindow(_hwnd);
        var command = NativeMethods.TrackPopupMenuEx(
            menu,
            NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_NONOTIFY,
            x,
            y,
            _hwnd,
            IntPtr.Zero);
        NativeMethods.PostMessageW(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        NativeMethods.DestroyMenu(menu);

        switch (command)
        {
            case MenuOpen:
                Invoked?.Invoke(this, EventArgs.Empty);
                break;
            case MenuSettings:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case MenuQuit:
                QuitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
````

- [ ] **Step 12: Create `src/ConnectFlyout.App/ScreenGeometry.cs`**

````csharp
using System.Runtime.InteropServices;
using ConnectFlyout.Presentation.Placement;

namespace ConnectFlyout.App;

/// <summary>
/// Reads the monitor under the tray icon and turns it into a flyout rectangle.
/// </summary>
internal static class ScreenGeometry
{
    /// <param name="iconRect">Tray icon bounds, or null to use the cursor position.</param>
    public static (PixelRect Rect, TaskbarEdge Edge) GetFlyoutPlacement(NativeMethods.RECT? iconRect)
    {
        IntPtr monitor;
        if (iconRect is { } rect)
        {
            monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            NativeMethods.GetCursorPos(out var point);
            monitor = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        }

        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfoW(monitor, ref info);
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var bounds = ToPixelRect(info.rcMonitor);
        var workArea = ToPixelRect(info.rcWork);
        return (FlyoutPlacement.Calculate(bounds, workArea, scale), FlyoutPlacement.DetectEdge(bounds, workArea));
    }

    private static PixelRect ToPixelRect(NativeMethods.RECT rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
````

- [ ] **Step 13: Create `src/ConnectFlyout.App/AppNotificationService.cs`**

````csharp
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ConnectFlyout.Presentation.Notifications;

namespace ConnectFlyout.App;

/// <summary>
/// <see cref="INotificationService"/> over Windows App SDK app notifications.
/// </summary>
internal sealed class AppNotificationService : INotificationService, IDisposable
{
    private readonly ILogger _logger;
    private bool _registered;

    public AppNotificationService(ILogger logger)
    {
        _logger = logger;
    }

    public void Register()
    {
        try
        {
            // Handle clicks in this process so a click doesn't start a second copy.
            AppNotificationManager.Default.NotificationInvoked += (_, _) => { };
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            AppLog.NotificationsUnavailable(_logger, ex);
        }
    }

    public void ShowLowBattery(string deviceName, int level)
    {
        if (!_registered)
        {
            return;
        }

        var notification = new AppNotificationBuilder()
            .AddText($"{deviceName} battery is low")
            .AddText($"{level}% left. Charge them soon.")
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }

    public void Dispose()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
            _registered = false;
        }
    }
}
````

- [ ] **Step 14: Create `src/ConnectFlyout.App/TileStyles.cs`**

````csharp
using Microsoft.UI.Xaml;

namespace ConnectFlyout.App;

/// <summary>
/// x:Bind function for the noise control tiles: accent style when selected.
/// </summary>
public static class TileStyles
{
    public static Style Pick(bool selected) =>
        (Style)Application.Current.Resources[selected ? "TileButtonSelectedStyle" : "TileButtonStyle"];
}
````

- [ ] **Step 15: Create `src/ConnectFlyout.App/FlyoutWindow.xaml`**

````xml
<Window
    x:Class="ConnectFlyout.App.FlyoutWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="ConnectFlyout">

    <!-- Acrylic Background -->
    <Window.SystemBackdrop>
        <DesktopAcrylicBackdrop />
    </Window.SystemBackdrop>
</Window>
````

- [ ] **Step 16: Create `src/ConnectFlyout.App/FlyoutWindow.xaml.cs`**

````csharp
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using ConnectFlyout.App.Views;
using ConnectFlyout.Presentation.Placement;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace ConnectFlyout.App;

/// <summary>
/// Borderless, always-on-top acrylic window that acts as the tray flyout.
/// </summary>
/// <remarks>
/// Hidden instead of closed, so opening it again is instant. Hides when it loses focus, like
/// Windows' own flyouts. The click on the tray icon that takes focus away arrives right after
/// the hide, so a toggle within <see cref="ReopenGuardMilliseconds"/> of hiding is ignored.
/// </remarks>
public sealed partial class FlyoutWindow : Window
{
    private const long ReopenGuardMilliseconds = 300;

    private readonly FlyoutViewModel _viewModel;
    private readonly FlyoutView _view;
    private readonly IntPtr _hwnd;
    private long _lastHiddenAt;
    private bool _shuttingDown;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        _view = new FlyoutView(viewModel);
        _view.CloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        Content = _view;

        _hwnd = WindowNative.GetWindowHandle(this);

        // Window Chrome
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "ConnectFlyout";

        var corner = NativeMethods.DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // Behavior
        AppWindow.Closing += (_, e) =>
        {
            if (!_shuttingDown)
            {
                e.Cancel = true;
                HideFlyout();
            }
        };
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible)
            {
                HideFlyout();
            }
        };
    }

    /// <summary>
    /// Raised by Esc and by the view's close actions.
    /// </summary>
    public event EventHandler? CloseRequested;

    internal void Toggle(NativeMethods.RECT? iconRect)
    {
        if (AppWindow.IsVisible)
        {
            HideFlyout();
            return;
        }
        if (Environment.TickCount64 - _lastHiddenAt < ReopenGuardMilliseconds)
        {
            return;
        }
        ShowFlyout(iconRect);
    }

    public void HideFlyout()
    {
        if (!AppWindow.IsVisible)
        {
            return;
        }
        AppWindow.Hide();
        _lastHiddenAt = Environment.TickCount64;
    }

    public void ApplyTheme(AppTheme theme) => _view.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>
    /// Lets the window really close when the app quits.
    /// </summary>
    public void Shutdown()
    {
        _shuttingDown = true;
        Close();
    }

    private void ShowFlyout(NativeMethods.RECT? iconRect)
    {
        var (rect, edge) = ScreenGeometry.GetFlyoutPlacement(iconRect);
        var bounds = new RectInt32(rect.Left, rect.Top, rect.Width, rect.Height);

        // ponytail: sized twice because moving to a monitor with another DPI rescales the
        // window once it's shown; a WM_DPICHANGED hook would do it in one pass.
        AppWindow.MoveAndResize(bounds);
        AppWindow.Show(true);
        AppWindow.MoveAndResize(bounds);
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        _view.PlayEntrance(edge == TaskbarEdge.Top);
        _ = _viewModel.OnOpenedAsync();
    }
}
````

- [ ] **Step 17: Create `src/ConnectFlyout.App/Views/FlyoutView.xaml`**

````xml
<UserControl
    x:Class="ConnectFlyout.App.Views.FlyoutView"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:app="using:ConnectFlyout.App"
    xmlns:scenes="using:ConnectFlyout.Presentation.Scenes"
    xmlns:vm="using:ConnectFlyout.Presentation.ViewModels">

    <UserControl.Resources>
        <!-- Entrance Animation -->
        <Storyboard x:Name="EntranceStoryboard">
            <DoubleAnimation
                x:Name="EntranceSlide"
                Storyboard.TargetName="ContentTransform"
                Storyboard.TargetProperty="Y"
                From="16"
                To="0"
                Duration="0:0:0.25">
                <DoubleAnimation.EasingFunction>
                    <ExponentialEase EasingMode="EaseOut" Exponent="7" />
                </DoubleAnimation.EasingFunction>
            </DoubleAnimation>
            <DoubleAnimation
                Storyboard.TargetName="ContentRoot"
                Storyboard.TargetProperty="Opacity"
                From="0"
                To="1"
                Duration="0:0:0.15" />
        </Storyboard>
    </UserControl.Resources>

    <UserControl.KeyboardAccelerators>
        <KeyboardAccelerator Key="Escape" Invoked="OnEscapeInvoked" />
    </UserControl.KeyboardAccelerators>

    <Grid x:Name="ContentRoot" RowDefinitions="*,Auto">
        <Grid.RenderTransform>
            <TranslateTransform x:Name="ContentTransform" />
        </Grid.RenderTransform>

        <!-- Empty Page -->
        <StackPanel
            Grid.Row="0"
            Padding="24"
            VerticalAlignment="Center"
            Spacing="12"
            Visibility="{x:Bind ViewModel.IsEmptyVisible, Mode=OneWay}">
            <FontIcon
                HorizontalAlignment="Center"
                FontSize="48"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}"
                Glyph="&#xE7F6;" />
            <TextBlock
                HorizontalAlignment="Center"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Text="No Sony headphones connected" />
            <TextBlock
                HorizontalAlignment="Center"
                Style="{StaticResource SecondaryTextStyle}"
                Text="Connect them in Bluetooth settings and they'll show up here."
                TextAlignment="Center"
                TextWrapping="Wrap" />
            <HyperlinkButton
                HorizontalAlignment="Center"
                Content="Open Bluetooth settings"
                NavigateUri="ms-settings:bluetooth" />
        </StackPanel>
        <!-- /Empty Page -->

        <!-- Picker Page -->
        <Grid
            Grid.Row="0"
            Padding="20,20,20,0"
            RowDefinitions="Auto,*"
            Visibility="{x:Bind ViewModel.IsPickerVisible, Mode=OneWay}">
            <TextBlock
                Margin="0,0,0,12"
                Style="{StaticResource SubtitleTextBlockStyle}"
                Text="Headphones" />
            <ListView
                Grid.Row="1"
                Margin="-12,0"
                IsItemClickEnabled="True"
                ItemClick="OnHeadsetClick"
                ItemsSource="{x:Bind ViewModel.Headsets}"
                SelectionMode="None">
                <ListView.ItemTemplate>
                    <DataTemplate x:DataType="vm:HeadsetViewModel">
                        <Grid
                            Padding="0,12"
                            ColumnDefinitions="Auto,*,Auto,Auto"
                            ColumnSpacing="12"
                            AutomationProperties.Name="{x:Bind DeviceName}">
                            <FontIcon FontSize="20" Glyph="&#xE7F6;" />
                            <StackPanel Grid.Column="1">
                                <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind DeviceName}" />
                                <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="{x:Bind StatusText, Mode=OneWay}" />
                            </StackPanel>
                            <TextBlock
                                Grid.Column="2"
                                VerticalAlignment="Center"
                                Text="{x:Bind BatterySummary, Mode=OneWay}" />
                            <FontIcon
                                Grid.Column="3"
                                FontSize="12"
                                Glyph="&#xE76C;" />
                        </Grid>
                    </DataTemplate>
                </ListView.ItemTemplate>
            </ListView>
        </Grid>
        <!-- /Picker Page -->

        <!-- Device Page -->
        <ScrollViewer
            Grid.Row="0"
            HorizontalScrollMode="Disabled"
            VerticalScrollBarVisibility="Auto"
            Visibility="{x:Bind ViewModel.IsDeviceVisible, Mode=OneWay}">
            <StackPanel Padding="20" Spacing="16">

                <!-- Header -->
                <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8">
                    <Button
                        Style="{StaticResource FooterIconButtonStyle}"
                        VerticalAlignment="Top"
                        AutomationProperties.Name="Back to headphones"
                        Command="{x:Bind ViewModel.BackCommand}"
                        ToolTipService.ToolTip="Back"
                        Visibility="{x:Bind ViewModel.ShowBack, Mode=OneWay}">
                        <FontIcon FontSize="16" Glyph="&#xE72B;" />
                    </Button>
                    <StackPanel Grid.Column="1">
                        <TextBlock Style="{StaticResource SubtitleTextBlockStyle}" Text="{x:Bind ViewModel.CurrentHeadset.DeviceName, Mode=OneWay}" />
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="{x:Bind ViewModel.CurrentHeadset.StatusText, Mode=OneWay}" />
                    </StackPanel>
                    <ProgressRing
                        Grid.Column="2"
                        Width="20"
                        Height="20"
                        IsActive="{x:Bind ViewModel.CurrentHeadset.IsConnecting, Mode=OneWay}" />
                </Grid>

                <!-- Error -->
                <InfoBar
                    IsClosable="False"
                    IsOpen="{x:Bind ViewModel.CurrentHeadset.HasError, Mode=OneWay}"
                    Message="{x:Bind ViewModel.CurrentHeadset.ErrorMessage, Mode=OneWay}"
                    Severity="Error" />

                <!-- Battery -->
                <Grid
                    ColumnDefinitions="*,*,*"
                    Visibility="{x:Bind ViewModel.CurrentHeadset.ShowDualBattery, Mode=OneWay}">
                    <StackPanel Spacing="2">
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Left" />
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <FontIcon FontSize="16" Glyph="{x:Bind ViewModel.CurrentHeadset.LeftBatteryGlyph, Mode=OneWay}" />
                            <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind ViewModel.CurrentHeadset.LeftBatteryText, Mode=OneWay}" />
                        </StackPanel>
                    </StackPanel>
                    <StackPanel Grid.Column="1" Spacing="2">
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Right" />
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <FontIcon FontSize="16" Glyph="{x:Bind ViewModel.CurrentHeadset.RightBatteryGlyph, Mode=OneWay}" />
                            <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind ViewModel.CurrentHeadset.RightBatteryText, Mode=OneWay}" />
                        </StackPanel>
                    </StackPanel>
                    <StackPanel
                        Grid.Column="2"
                        Spacing="2"
                        Visibility="{x:Bind ViewModel.CurrentHeadset.ShowCaseBattery, Mode=OneWay}">
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Case" />
                        <StackPanel Orientation="Horizontal" Spacing="6">
                            <FontIcon FontSize="16" Glyph="{x:Bind ViewModel.CurrentHeadset.CaseBatteryGlyph, Mode=OneWay}" />
                            <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind ViewModel.CurrentHeadset.CaseBatteryText, Mode=OneWay}" />
                        </StackPanel>
                    </StackPanel>
                </Grid>
                <StackPanel Spacing="2" Visibility="{x:Bind ViewModel.CurrentHeadset.ShowSingleBattery, Mode=OneWay}">
                    <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Battery" />
                    <StackPanel Orientation="Horizontal" Spacing="6">
                        <FontIcon FontSize="16" Glyph="{x:Bind ViewModel.CurrentHeadset.MainBatteryGlyph, Mode=OneWay}" />
                        <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind ViewModel.CurrentHeadset.MainBatteryText, Mode=OneWay}" />
                    </StackPanel>
                </StackPanel>
                <!-- /Battery -->

                <MenuFlyoutSeparator />

                <!-- Noise Control -->
                <StackPanel IsHitTestVisible="{x:Bind ViewModel.CurrentHeadset.IsConnected, Mode=OneWay}" Spacing="12">
                    <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Noise control" />
                    <Grid ColumnDefinitions="*,*,*" ColumnSpacing="8">
                        <Button
                            AutomationProperties.Name="Noise control off"
                            Command="{x:Bind ViewModel.CurrentHeadset.SetNoiseModeCommand, Mode=OneWay}"
                            CommandParameter="Off"
                            Style="{x:Bind app:TileStyles.Pick(ViewModel.CurrentHeadset.IsNoiseOff), Mode=OneWay}">
                            <StackPanel Spacing="4">
                                <FontIcon FontSize="18" Glyph="&#xE74F;" />
                                <TextBlock HorizontalAlignment="Center" Text="Off" />
                            </StackPanel>
                        </Button>
                        <Button
                            Grid.Column="1"
                            AutomationProperties.Name="Noise cancelling"
                            Command="{x:Bind ViewModel.CurrentHeadset.SetNoiseModeCommand, Mode=OneWay}"
                            CommandParameter="NoiseCancelling"
                            Style="{x:Bind app:TileStyles.Pick(ViewModel.CurrentHeadset.IsNoiseCancelling), Mode=OneWay}">
                            <StackPanel Spacing="4">
                                <FontIcon FontSize="18" Glyph="&#xE7F6;" />
                                <TextBlock HorizontalAlignment="Center" Text="ANC" />
                            </StackPanel>
                        </Button>
                        <Button
                            Grid.Column="2"
                            AutomationProperties.Name="Ambient sound"
                            Command="{x:Bind ViewModel.CurrentHeadset.SetNoiseModeCommand, Mode=OneWay}"
                            CommandParameter="Ambient"
                            Style="{x:Bind app:TileStyles.Pick(ViewModel.CurrentHeadset.IsAmbient), Mode=OneWay}">
                            <StackPanel Spacing="4">
                                <FontIcon FontSize="18" Glyph="&#xE8D6;" />
                                <TextBlock HorizontalAlignment="Center" Text="Ambient" />
                            </StackPanel>
                        </Button>
                    </Grid>

                    <!-- Ambient Level -->
                    <StackPanel Visibility="{x:Bind ViewModel.CurrentHeadset.IsAmbient, Mode=OneWay}">
                        <Grid ColumnDefinitions="*,Auto">
                            <TextBlock Text="Ambient sound" />
                            <TextBlock Grid.Column="1" Text="{x:Bind ViewModel.CurrentHeadset.AmbientLevelText, Mode=OneWay}" />
                        </Grid>
                        <Slider
                            AutomationProperties.Name="Ambient sound level"
                            Maximum="20"
                            Minimum="1"
                            StepFrequency="1"
                            TickFrequency="1"
                            TickPlacement="Outside"
                            Value="{x:Bind ViewModel.CurrentHeadset.AmbientLevel, Mode=TwoWay}" />
                    </StackPanel>

                    <!-- Focus On Voice -->
                    <Grid ColumnDefinitions="*,Auto">
                        <TextBlock VerticalAlignment="Center" Text="Focus on voice" />
                        <ToggleSwitch
                            Grid.Column="1"
                            MinWidth="0"
                            AutomationProperties.Name="Focus on voice"
                            IsOn="{x:Bind ViewModel.CurrentHeadset.FocusOnVoice, Mode=TwoWay}"
                            OffContent=""
                            OnContent="" />
                    </Grid>

                    <!-- Scenes -->
                    <ItemsRepeater ItemsSource="{x:Bind ViewModel.CurrentHeadset.Scenes, Mode=OneWay}">
                        <ItemsRepeater.Layout>
                            <UniformGridLayout
                                ItemsStretch="Fill"
                                MaximumRowsOrColumns="3"
                                MinColumnSpacing="8"
                                MinItemWidth="96"
                                MinRowSpacing="8" />
                        </ItemsRepeater.Layout>
                        <ItemsRepeater.ItemTemplate>
                            <DataTemplate x:DataType="scenes:Scene">
                                <Button
                                    HorizontalAlignment="Stretch"
                                    AutomationProperties.Name="{x:Bind Name}"
                                    Click="OnSceneClick">
                                    <StackPanel Orientation="Horizontal" Spacing="8">
                                        <FontIcon FontSize="14" Glyph="{x:Bind Glyph}" />
                                        <TextBlock Text="{x:Bind Name}" TextTrimming="CharacterEllipsis" />
                                    </StackPanel>
                                </Button>
                            </DataTemplate>
                        </ItemsRepeater.ItemTemplate>
                    </ItemsRepeater>
                </StackPanel>
                <!-- /Noise Control -->

                <MenuFlyoutSeparator />

                <!-- Sound -->
                <StackPanel IsHitTestVisible="{x:Bind ViewModel.CurrentHeadset.IsConnected, Mode=OneWay}" Spacing="8">
                    <Grid ColumnDefinitions="*,Auto" Visibility="{x:Bind ViewModel.CurrentHeadset.Features.Equalizer, Mode=OneWay}">
                        <TextBlock VerticalAlignment="Center" Text="Equalizer" />
                        <ComboBox
                            Grid.Column="1"
                            MinWidth="150"
                            AutomationProperties.Name="Equalizer preset"
                            DisplayMemberPath="Name"
                            ItemsSource="{x:Bind ViewModel.CurrentHeadset.EqualizerPresets, Mode=OneWay}"
                            SelectedIndex="{x:Bind ViewModel.CurrentHeadset.SelectedEqualizerIndex, Mode=TwoWay}" />
                    </Grid>
                    <Grid ColumnDefinitions="*,Auto" Visibility="{x:Bind ViewModel.CurrentHeadset.Features.Dsee, Mode=OneWay}">
                        <TextBlock VerticalAlignment="Center" Text="DSEE Extreme" />
                        <ComboBox
                            Grid.Column="1"
                            MinWidth="150"
                            AutomationProperties.Name="DSEE Extreme"
                            ItemsSource="{x:Bind vm:HeadsetViewModel.DseeOptions}"
                            SelectedIndex="{x:Bind ViewModel.CurrentHeadset.DseeIndex, Mode=TwoWay}" />
                    </Grid>
                </StackPanel>
                <!-- /Sound -->

                <MenuFlyoutSeparator />

                <!-- Playback -->
                <StackPanel Spacing="8">
                    <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Playback" />
                    <StackPanel Visibility="{x:Bind ViewModel.Playback.HasTrack, Mode=OneWay}">
                        <TextBlock Text="{x:Bind ViewModel.Playback.Title, Mode=OneWay}" TextTrimming="CharacterEllipsis" />
                        <TextBlock
                            Style="{StaticResource SecondaryTextStyle}"
                            Text="{x:Bind ViewModel.Playback.Artist, Mode=OneWay}"
                            TextTrimming="CharacterEllipsis" />
                    </StackPanel>
                    <TextBlock
                        Style="{StaticResource SecondaryTextStyle}"
                        Text="No track information"
                        Visibility="{x:Bind ViewModel.Playback.NoTrack, Mode=OneWay}" />
                    <StackPanel
                        HorizontalAlignment="Center"
                        Orientation="Horizontal"
                        Spacing="8">
                        <Button
                            Style="{StaticResource FooterIconButtonStyle}"
                            AutomationProperties.Name="Previous track"
                            Command="{x:Bind ViewModel.Playback.PreviousCommand}">
                            <FontIcon FontSize="16" Glyph="&#xE892;" />
                        </Button>
                        <Button
                            Style="{StaticResource FooterIconButtonStyle}"
                            AutomationProperties.Name="Play or pause"
                            Command="{x:Bind ViewModel.Playback.PlayPauseCommand}">
                            <FontIcon FontSize="16" Glyph="{x:Bind ViewModel.Playback.PlayPauseGlyph, Mode=OneWay}" />
                        </Button>
                        <Button
                            Style="{StaticResource FooterIconButtonStyle}"
                            AutomationProperties.Name="Next track"
                            Command="{x:Bind ViewModel.Playback.NextCommand}">
                            <FontIcon FontSize="16" Glyph="&#xE893;" />
                        </Button>
                    </StackPanel>
                    <Grid ColumnDefinitions="Auto,*" ColumnSpacing="12">
                        <FontIcon
                            VerticalAlignment="Center"
                            FontSize="16"
                            Glyph="&#xE767;" />
                        <Slider
                            Grid.Column="1"
                            AutomationProperties.Name="Windows volume"
                            Maximum="100"
                            Minimum="0"
                            Value="{x:Bind ViewModel.Playback.Volume, Mode=TwoWay}" />
                    </Grid>
                </StackPanel>
                <!-- /Playback -->
            </StackPanel>
        </ScrollViewer>
        <!-- /Device Page -->

        <!-- Footer -->
        <Grid
            Grid.Row="1"
            Height="52"
            Padding="12,0"
            Background="{ThemeResource FlyoutFooterBackgroundBrush}"
            BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
            BorderThickness="0,1,0,0"
            ColumnDefinitions="*,Auto">
            <Button
                VerticalAlignment="Center"
                Command="{x:Bind ViewModel.OpenSettingsCommand}"
                Content="Headphone settings"
                Style="{StaticResource FooterTextButtonStyle}" />
            <StackPanel
                Grid.Column="1"
                VerticalAlignment="Center"
                Orientation="Horizontal"
                Spacing="4">
                <Button
                    Style="{StaticResource FooterIconButtonStyle}"
                    AutomationProperties.Name="Reconnect"
                    Command="{x:Bind ViewModel.ReconnectCommand}"
                    ToolTipService.ToolTip="Reconnect"
                    Visibility="{x:Bind ViewModel.IsDeviceVisible, Mode=OneWay}">
                    <FontIcon FontSize="16" Glyph="&#xE72C;" />
                </Button>
                <Button
                    Style="{StaticResource FooterIconButtonStyle}"
                    AutomationProperties.Name="Settings"
                    Command="{x:Bind ViewModel.OpenSettingsCommand}"
                    ToolTipService.ToolTip="Settings">
                    <FontIcon FontSize="16" Glyph="&#xE713;" />
                </Button>
                <Button
                    Style="{StaticResource FooterIconButtonStyle}"
                    AutomationProperties.Name="More options"
                    ToolTipService.ToolTip="More options">
                    <FontIcon FontSize="16" Glyph="&#xE712;" />
                    <Button.Flyout>
                        <MenuFlyout Placement="TopEdgeAlignedRight">
                            <MenuFlyoutItem Command="{x:Bind ViewModel.OpenSettingsCommand}" Text="Settings">
                                <MenuFlyoutItem.Icon>
                                    <FontIcon Glyph="&#xE713;" />
                                </MenuFlyoutItem.Icon>
                            </MenuFlyoutItem>
                            <MenuFlyoutSeparator />
                            <MenuFlyoutItem Command="{x:Bind ViewModel.QuitCommand}" Text="Quit ConnectFlyout">
                                <MenuFlyoutItem.Icon>
                                    <FontIcon Glyph="&#xE7E8;" />
                                </MenuFlyoutItem.Icon>
                            </MenuFlyoutItem>
                        </MenuFlyout>
                    </Button.Flyout>
                </Button>
            </StackPanel>
        </Grid>
        <!-- /Footer -->
    </Grid>
</UserControl>
````

- [ ] **Step 18: Create `src/ConnectFlyout.App/Views/FlyoutView.xaml.cs`**

````csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ConnectFlyout.Presentation.Scenes;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views;

/// <summary>
/// Everything inside the flyout window: the empty, picker and device pages plus the footer.
/// </summary>
public sealed partial class FlyoutView : UserControl
{
    public FlyoutView(FlyoutViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public event EventHandler? CloseRequested;

    public FlyoutViewModel ViewModel { get; }

    /// <summary>
    /// Slides the content in from the taskbar side.
    /// </summary>
    public void PlayEntrance(bool fromTop)
    {
        EntranceSlide.From = fromTop ? -16 : 16;
        EntranceStoryboard.Begin();
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeadsetClick(object sender, ItemClickEventArgs e) =>
        ViewModel.PickCommand.Execute(e.ClickedItem as HeadsetViewModel);

    private void OnSceneClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Scene scene })
        {
            ViewModel.CurrentHeadset?.ApplySceneCommand.Execute(scene);
        }
    }
}
````

- [ ] **Step 19: Create `src/ConnectFlyout.App/SettingsWindow.xaml`**

````xml
<Window
    x:Class="ConnectFlyout.App.SettingsWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="ConnectFlyout Settings">

    <!-- Mica Background -->
    <Window.SystemBackdrop>
        <MicaBackdrop />
    </Window.SystemBackdrop>

    <Grid x:Name="RootGrid" RowDefinitions="Auto,*">

        <!-- Title Bar -->
        <Grid
            x:Name="AppTitleBar"
            Height="48"
            Padding="16,0"
            ColumnDefinitions="Auto,*"
            ColumnSpacing="12">
            <FontIcon
                VerticalAlignment="Center"
                FontSize="16"
                Glyph="&#xE7F6;" />
            <TextBlock
                Grid.Column="1"
                VerticalAlignment="Center"
                Style="{StaticResource CaptionTextBlockStyle}"
                Text="ConnectFlyout Settings" />
        </Grid>

        <!-- Navigation -->
        <NavigationView
            x:Name="Navigation"
            Grid.Row="1"
            IsBackButtonVisible="Collapsed"
            IsPaneToggleButtonVisible="False"
            IsSettingsVisible="False"
            PaneDisplayMode="Left"
            SelectionChanged="OnSelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem Content="Devices" Tag="Devices">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE772;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Sound" Tag="Sound">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE8D6;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="Noise &amp; scenes" Tag="NoiseScenes">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE7F6;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="System" Tag="System">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE770;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
                <NavigationViewItem Content="App" Tag="App">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="&#xE713;" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </NavigationView.MenuItems>

            <Frame x:Name="ContentFrame" Padding="36,24" />
        </NavigationView>
        <!-- /Navigation -->
    </Grid>
</Window>
````

- [ ] **Step 20: Create `src/ConnectFlyout.App/SettingsWindow.xaml.cs`**

````csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Views.Settings;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App;

/// <summary>
/// Settings window with a left menu. One page per menu item, all sharing one view model.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["Devices"] = typeof(DevicesPage),
        ["Sound"] = typeof(SoundPage),
        ["NoiseScenes"] = typeof(NoiseScenesPage),
        ["System"] = typeof(SystemPage),
        ["App"] = typeof(AppPage),
    };

    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.png"));

        Navigation.SelectedItem = Navigation.MenuItems[0];
        _ = _viewModel.LoadAsync();
    }

    public void ApplyTheme(AppTheme theme) => RootGrid.RequestedTheme = theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Pages.TryGetValue(tag, out var page))
        {
            ContentFrame.Navigate(page, _viewModel);
        }
    }
}
````

- [ ] **Step 21: Create `src/ConnectFlyout.App/Views/Settings/SettingsPageBase.cs`**

````csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Settings page that receives the shared <see cref="SettingsViewModel"/> as its navigation parameter.
/// </summary>
public partial class SettingsPageBase : Page
{
    public SettingsViewModel ViewModel { get; private set; } = null!;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        ViewModel = (SettingsViewModel)e.Parameter;
        base.OnNavigatedTo(e);
    }
}
````

- [ ] **Step 22: Create `src/ConnectFlyout.App/Views/Settings/HeadsetSelector.xaml`**

````xml
<UserControl
    x:Class="ConnectFlyout.App.Views.Settings.HeadsetSelector"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- Headset Picker -->
    <StackPanel Margin="0,0,0,16" Spacing="8">
        <ComboBox
            MinWidth="240"
            AutomationProperties.Name="Headphones"
            DisplayMemberPath="DeviceName"
            Header="Headphones"
            ItemsSource="{x:Bind ViewModel.Headsets, Mode=OneWay}"
            SelectedIndex="{x:Bind ViewModel.SelectedHeadsetIndex, Mode=TwoWay}"
            Visibility="{x:Bind ViewModel.HasHeadset, Mode=OneWay}" />
        <InfoBar
            IsClosable="False"
            IsOpen="{x:Bind ViewModel.NoHeadset, Mode=OneWay}"
            Message="Connect your Sony headphones to change these settings."
            Severity="Informational" />
    </StackPanel>
</UserControl>
````

- [ ] **Step 23: Create `src/ConnectFlyout.App/Views/Settings/HeadsetSelector.xaml.cs`**

````csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.Presentation.ViewModels;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Headset picker shared by the Sound, Noise &amp; scenes and System pages.
/// </summary>
public sealed partial class HeadsetSelector : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel),
        typeof(SettingsViewModel),
        typeof(HeadsetSelector),
        new PropertyMetadata(null, (sender, _) => ((HeadsetSelector)sender).Bindings.Update()));

    public HeadsetSelector()
    {
        InitializeComponent();
    }

    public SettingsViewModel? ViewModel
    {
        get => (SettingsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
}
````

- [ ] **Step 24: Create `src/ConnectFlyout.App/Views/Settings/DevicesPage.xaml`**

````xml
<local:SettingsPageBase
    x:Class="ConnectFlyout.App.Views.Settings.DevicesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ConnectFlyout.App.Views.Settings"
    xmlns:vm="using:ConnectFlyout.Presentation.ViewModels">

    <ScrollViewer>
        <StackPanel MaxWidth="900" HorizontalAlignment="Stretch" Spacing="8">
            <TextBlock Style="{StaticResource SettingsPageTitleStyle}" Text="Devices" />

            <InfoBar
                IsClosable="False"
                IsOpen="{x:Bind ViewModel.NoHeadset, Mode=OneWay}"
                Message="Connect your Sony headphones in Bluetooth settings and they'll show up here."
                Severity="Informational" />

            <!-- Headset Cards -->
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Headsets}">
                <ItemsRepeater.Layout>
                    <StackLayout Spacing="8" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:HeadsetViewModel">
                        <Border Style="{StaticResource SettingsCardStyle}">
                            <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="16">
                                <FontIcon
                                    VerticalAlignment="Center"
                                    FontSize="24"
                                    Glyph="&#xE7F6;" />
                                <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                    <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="{x:Bind DeviceName}" />
                                    <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="{x:Bind StatusText, Mode=OneWay}" />
                                    <TextBlock Style="{StaticResource SecondaryTextStyle}">
                                        <Run Text="Model " /><Run Text="{x:Bind ModelName}" />
                                        <Run Text=" · Firmware " /><Run Text="{x:Bind Firmware, Mode=OneWay}" />
                                        <Run Text=" · Codec " /><Run Text="{x:Bind Codec, Mode=OneWay}" />
                                    </TextBlock>
                                </StackPanel>
                                <ToggleSwitch
                                    Grid.Column="2"
                                    VerticalAlignment="Center"
                                    AutomationProperties.Name="Connect automatically"
                                    Header="Connect automatically"
                                    IsOn="{x:Bind AutoConnect, Mode=TwoWay}" />
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
            <!-- /Headset Cards -->

            <!-- Bluetooth Settings -->
            <Border Style="{StaticResource SettingsCardStyle}">
                <Grid ColumnDefinitions="*,Auto">
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="Pair or forget headphones" />
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Pairing is handled in Windows Bluetooth settings." />
                    </StackPanel>
                    <HyperlinkButton
                        Grid.Column="1"
                        Content="Open Bluetooth settings"
                        NavigateUri="ms-settings:bluetooth" />
                </Grid>
            </Border>
        </StackPanel>
    </ScrollViewer>
</local:SettingsPageBase>
````

- [ ] **Step 25: Create `src/ConnectFlyout.App/Views/Settings/DevicesPage.xaml.cs`**

````csharp
namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Paired Sony headphones and how the app connects to them.
/// </summary>
public sealed partial class DevicesPage : SettingsPageBase
{
    public DevicesPage()
    {
        InitializeComponent();
    }
}
````

- [ ] **Step 26: Create `src/ConnectFlyout.App/Views/Settings/SoundPage.xaml`**

````xml
<local:SettingsPageBase
    x:Class="ConnectFlyout.App.Views.Settings.SoundPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ConnectFlyout.App.Views.Settings"
    xmlns:vm="using:ConnectFlyout.Presentation.ViewModels">

    <ScrollViewer>
        <StackPanel MaxWidth="900" HorizontalAlignment="Stretch" Spacing="8">
            <TextBlock Style="{StaticResource SettingsPageTitleStyle}" Text="Sound" />
            <local:HeadsetSelector ViewModel="{x:Bind ViewModel}" />

            <StackPanel IsHitTestVisible="{x:Bind ViewModel.SelectedHeadset.IsConnected, Mode=OneWay}" Spacing="8" Visibility="{x:Bind ViewModel.HasHeadset, Mode=OneWay}">

                <!-- Equalizer Preset -->
                <Border Style="{StaticResource SettingsCardStyle}" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.Equalizer, Mode=OneWay}">
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="Equalizer preset" />
                            <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Pick Manual to use the custom curve below." />
                        </StackPanel>
                        <ComboBox
                            Grid.Column="1"
                            MinWidth="180"
                            VerticalAlignment="Center"
                            AutomationProperties.Name="Equalizer preset"
                            DisplayMemberPath="Name"
                            ItemsSource="{x:Bind ViewModel.SelectedHeadset.EqualizerPresets, Mode=OneWay}"
                            SelectedIndex="{x:Bind ViewModel.SelectedHeadset.SelectedEqualizerIndex, Mode=TwoWay}" />
                    </Grid>
                </Border>

                <!-- Custom Equalizer -->
                <Border Style="{StaticResource SettingsCardStyle}" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.Equalizer, Mode=OneWay}">
                    <StackPanel Spacing="12">
                        <TextBlock Text="Custom equalizer" />
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Each band goes from -10 to +10. Apply sends the curve and switches the preset to Manual." />
                        <Grid ColumnDefinitions="Auto,*" ColumnSpacing="16" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto" RowSpacing="4">
                            <TextBlock Grid.Row="0" VerticalAlignment="Center" Text="Clear Bass" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.ClearBass, Mode=OneWay}" />
                            <Slider Grid.Row="0" Grid.Column="1" AutomationProperties.Name="Clear Bass" Maximum="10" Minimum="-10" StepFrequency="1" Value="{x:Bind ViewModel.SelectedHeadset.ClearBass, Mode=TwoWay}" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.ClearBass, Mode=OneWay}" />
                            <TextBlock Grid.Row="1" VerticalAlignment="Center" Text="400 Hz" />
                            <Slider Grid.Row="1" Grid.Column="1" AutomationProperties.Name="400 hertz" Maximum="10" Minimum="-10" StepFrequency="1" Value="{x:Bind ViewModel.SelectedHeadset.Band1, Mode=TwoWay}" />
                            <TextBlock Grid.Row="2" VerticalAlignment="Center" Text="1 kHz" />
                            <Slider Grid.Row="2" Grid.Column="1" AutomationProperties.Name="1 kilohertz" Maximum="10" Minimum="-10" StepFrequency="1" Value="{x:Bind ViewModel.SelectedHeadset.Band2, Mode=TwoWay}" />
                            <TextBlock Grid.Row="3" VerticalAlignment="Center" Text="2.5 kHz" />
                            <Slider Grid.Row="3" Grid.Column="1" AutomationProperties.Name="2.5 kilohertz" Maximum="10" Minimum="-10" StepFrequency="1" Value="{x:Bind ViewModel.SelectedHeadset.Band3, Mode=TwoWay}" />
                            <TextBlock Grid.Row="4" VerticalAlignment="Center" Text="6.3 kHz" />
                            <Slider Grid.Row="4" Grid.Column="1" AutomationProperties.Name="6.3 kilohertz" Maximum="10" Minimum="-10" StepFrequency="1" Value="{x:Bind ViewModel.SelectedHeadset.Band4, Mode=TwoWay}" />
                            <TextBlock Grid.Row="5" VerticalAlignment="Center" Text="16 kHz" />
                            <Slider Grid.Row="5" Grid.Column="1" AutomationProperties.Name="16 kilohertz" Maximum="10" Minimum="-10" StepFrequency="1" Value="{x:Bind ViewModel.SelectedHeadset.Band5, Mode=TwoWay}" />
                        </Grid>
                        <Button
                            HorizontalAlignment="Right"
                            Command="{x:Bind ViewModel.SelectedHeadset.ApplyCustomEqualizerCommand, Mode=OneWay}"
                            Content="Apply"
                            Style="{StaticResource AccentButtonStyle}" />
                    </StackPanel>
                </Border>
                <!-- /Custom Equalizer -->

                <!-- DSEE -->
                <Border Style="{StaticResource SettingsCardStyle}" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.Dsee, Mode=OneWay}">
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="DSEE Extreme" />
                            <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Upscales compressed music. Uses a little more battery." />
                        </StackPanel>
                        <ComboBox
                            Grid.Column="1"
                            MinWidth="180"
                            VerticalAlignment="Center"
                            AutomationProperties.Name="DSEE Extreme"
                            ItemsSource="{x:Bind vm:HeadsetViewModel.DseeOptions}"
                            SelectedIndex="{x:Bind ViewModel.SelectedHeadset.DseeIndex, Mode=TwoWay}" />
                    </Grid>
                </Border>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</local:SettingsPageBase>
````

- [ ] **Step 27: Create `src/ConnectFlyout.App/Views/Settings/SoundPage.xaml.cs`**

````csharp
namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Equalizer and DSEE for the selected headphones.
/// </summary>
public sealed partial class SoundPage : SettingsPageBase
{
    public SoundPage()
    {
        InitializeComponent();
    }
}
````

- [ ] **Step 28: Create `src/ConnectFlyout.App/Views/Settings/NoiseScenesPage.xaml`**

````xml
<local:SettingsPageBase
    x:Class="ConnectFlyout.App.Views.Settings.NoiseScenesPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ConnectFlyout.App.Views.Settings"
    xmlns:vm="using:ConnectFlyout.Presentation.ViewModels">

    <ScrollViewer>
        <StackPanel MaxWidth="900" HorizontalAlignment="Stretch" Spacing="8">
            <TextBlock Style="{StaticResource SettingsPageTitleStyle}" Text="Noise &amp; scenes" />

            <!-- Scenes -->
            <TextBlock Style="{StaticResource BodyStrongTextBlockStyle}" Text="Scenes" />
            <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Scenes are the quick buttons under Noise control in the flyout." />
            <ItemsRepeater ItemsSource="{x:Bind ViewModel.Scenes}">
                <ItemsRepeater.Layout>
                    <StackLayout Spacing="8" />
                </ItemsRepeater.Layout>
                <ItemsRepeater.ItemTemplate>
                    <DataTemplate x:DataType="vm:SceneEditorViewModel">
                        <Border Style="{StaticResource SettingsCardStyle}">
                            <Grid ColumnDefinitions="Auto,*,*,Auto" ColumnSpacing="16" RowDefinitions="Auto,Auto" RowSpacing="8">
                                <FontIcon
                                    Grid.RowSpan="2"
                                    VerticalAlignment="Center"
                                    FontSize="20"
                                    Glyph="{x:Bind Glyph}" />
                                <TextBox
                                    Grid.Column="1"
                                    AutomationProperties.Name="Scene name"
                                    Header="Name"
                                    Text="{x:Bind Name, Mode=TwoWay}" />
                                <ComboBox
                                    Grid.Column="2"
                                    HorizontalAlignment="Stretch"
                                    AutomationProperties.Name="Noise control mode"
                                    Header="Noise control"
                                    ItemsSource="{x:Bind vm:SceneEditorViewModel.ModeOptions}"
                                    SelectedIndex="{x:Bind ModeIndex, Mode=TwoWay}" />
                                <ToggleSwitch
                                    Grid.Column="3"
                                    AutomationProperties.Name="Focus on voice"
                                    Header="Focus on voice"
                                    IsOn="{x:Bind FocusOnVoice, Mode=TwoWay}" />
                                <Slider
                                    Grid.Row="1"
                                    Grid.Column="1"
                                    Grid.ColumnSpan="3"
                                    AutomationProperties.Name="Ambient sound level"
                                    Header="Ambient sound level"
                                    Maximum="20"
                                    Minimum="1"
                                    StepFrequency="1"
                                    Value="{x:Bind AmbientLevel, Mode=TwoWay}"
                                    Visibility="{x:Bind IsAmbient, Mode=OneWay}" />
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsRepeater.ItemTemplate>
            </ItemsRepeater>
            <StackPanel
                HorizontalAlignment="Right"
                Orientation="Horizontal"
                Spacing="8">
                <Button Command="{x:Bind ViewModel.ResetScenesCommand}" Content="Reset to defaults" />
                <Button
                    Command="{x:Bind ViewModel.SaveScenesCommand}"
                    Content="Save scenes"
                    Style="{StaticResource AccentButtonStyle}" />
            </StackPanel>
            <!-- /Scenes -->

            <!-- Speak-to-Chat -->
            <TextBlock
                Margin="0,16,0,0"
                Style="{StaticResource BodyStrongTextBlockStyle}"
                Text="Headphones" />
            <local:HeadsetSelector ViewModel="{x:Bind ViewModel}" />
            <Border
                IsHitTestVisible="{x:Bind ViewModel.SelectedHeadset.IsConnected, Mode=OneWay}"
                Style="{StaticResource SettingsCardStyle}"
                Visibility="{x:Bind ViewModel.SelectedHeadset.Features.SpeakToChat, Mode=OneWay}">
                <Grid ColumnDefinitions="*,Auto">
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="Speak-to-Chat" />
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Pauses music and lets in outside sound when you start talking." />
                    </StackPanel>
                    <ToggleSwitch
                        Grid.Column="1"
                        AutomationProperties.Name="Speak-to-Chat"
                        IsOn="{x:Bind ViewModel.SelectedHeadset.SpeakToChat, Mode=TwoWay}" />
                </Grid>
            </Border>
        </StackPanel>
    </ScrollViewer>
</local:SettingsPageBase>
````

- [ ] **Step 29: Create `src/ConnectFlyout.App/Views/Settings/NoiseScenesPage.xaml.cs`**

````csharp
namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Flyout scenes and Speak-to-Chat.
/// </summary>
public sealed partial class NoiseScenesPage : SettingsPageBase
{
    public NoiseScenesPage()
    {
        InitializeComponent();
    }
}
````

- [ ] **Step 30: Create `src/ConnectFlyout.App/Views/Settings/SystemPage.xaml`**

````xml
<local:SettingsPageBase
    x:Class="ConnectFlyout.App.Views.Settings.SystemPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ConnectFlyout.App.Views.Settings"
    xmlns:vm="using:ConnectFlyout.Presentation.ViewModels">

    <ScrollViewer>
        <StackPanel MaxWidth="900" HorizontalAlignment="Stretch" Spacing="8">
            <TextBlock Style="{StaticResource SettingsPageTitleStyle}" Text="System" />
            <local:HeadsetSelector ViewModel="{x:Bind ViewModel}" />

            <StackPanel IsHitTestVisible="{x:Bind ViewModel.SelectedHeadset.IsConnected, Mode=OneWay}" Spacing="8" Visibility="{x:Bind ViewModel.HasHeadset, Mode=OneWay}">

                <!-- Auto Power-Off -->
                <Border Style="{StaticResource SettingsCardStyle}" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.AutoPowerOff, Mode=OneWay}">
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="Auto power-off" />
                            <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Turns the headphones off after they sit unused." />
                        </StackPanel>
                        <ComboBox
                            Grid.Column="1"
                            MinWidth="180"
                            VerticalAlignment="Center"
                            AutomationProperties.Name="Auto power-off"
                            ItemsSource="{x:Bind vm:HeadsetViewModel.AutoPowerOffOptions}"
                            SelectedIndex="{x:Bind ViewModel.SelectedHeadset.AutoPowerOffIndex, Mode=TwoWay}" />
                    </Grid>
                </Border>

                <!-- Adaptive Volume -->
                <Border Style="{StaticResource SettingsCardStyle}" Visibility="{x:Bind ViewModel.SelectedHeadset.Features.AdaptiveVolume, Mode=OneWay}">
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="Adaptive volume" />
                            <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Adjusts the volume to the noise around you." />
                        </StackPanel>
                        <ToggleSwitch
                            Grid.Column="1"
                            AutomationProperties.Name="Adaptive volume"
                            IsOn="{x:Bind ViewModel.SelectedHeadset.AdaptiveVolume, Mode=TwoWay}" />
                    </Grid>
                </Border>
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</local:SettingsPageBase>
````

- [ ] **Step 31: Create `src/ConnectFlyout.App/Views/Settings/SystemPage.xaml.cs`**

````csharp
namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Auto power-off and adaptive volume for the selected headphones.
/// </summary>
public sealed partial class SystemPage : SettingsPageBase
{
    public SystemPage()
    {
        InitializeComponent();
    }
}
````

- [ ] **Step 32: Create `src/ConnectFlyout.App/Views/Settings/AppPage.xaml`**

````xml
<local:SettingsPageBase
    x:Class="ConnectFlyout.App.Views.Settings.AppPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:ConnectFlyout.App.Views.Settings"
    xmlns:vm="using:ConnectFlyout.Presentation.ViewModels">

    <ScrollViewer>
        <StackPanel MaxWidth="900" HorizontalAlignment="Stretch" Spacing="8">
            <TextBlock Style="{StaticResource SettingsPageTitleStyle}" Text="App" />

            <!-- Launch At Sign-In -->
            <Border Style="{StaticResource SettingsCardStyle}">
                <Grid ColumnDefinitions="*,Auto">
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="Start when I sign in" />
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="If this won't turn on, check Settings &gt; Apps &gt; Startup." />
                    </StackPanel>
                    <ToggleSwitch
                        Grid.Column="1"
                        AutomationProperties.Name="Start when I sign in"
                        IsOn="{x:Bind ViewModel.LaunchAtSignIn, Mode=TwoWay}" />
                </Grid>
            </Border>

            <!-- Low Battery -->
            <Border Style="{StaticResource SettingsCardStyle}">
                <Grid ColumnDefinitions="*,Auto">
                    <StackPanel VerticalAlignment="Center">
                        <TextBlock Text="Low battery notifications" />
                        <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Notifies you once when your headphones drop below 20%." />
                    </StackPanel>
                    <ToggleSwitch
                        Grid.Column="1"
                        AutomationProperties.Name="Low battery notifications"
                        IsOn="{x:Bind ViewModel.LowBatteryNotifications, Mode=TwoWay}" />
                </Grid>
            </Border>

            <!-- Theme -->
            <Border Style="{StaticResource SettingsCardStyle}">
                <Grid ColumnDefinitions="*,Auto">
                    <TextBlock VerticalAlignment="Center" Text="App theme" />
                    <ComboBox
                        Grid.Column="1"
                        MinWidth="180"
                        VerticalAlignment="Center"
                        AutomationProperties.Name="App theme"
                        ItemsSource="{x:Bind vm:SettingsViewModel.ThemeOptions}"
                        SelectedIndex="{x:Bind ViewModel.ThemeIndex, Mode=TwoWay}" />
                </Grid>
            </Border>

            <!-- Logging -->
            <Border Style="{StaticResource SettingsCardStyle}">
                <StackPanel Spacing="12">
                    <Grid ColumnDefinitions="*,Auto">
                        <StackPanel VerticalAlignment="Center">
                            <TextBlock Text="Debug logging" />
                            <TextBlock Style="{StaticResource SecondaryTextStyle}" Text="Also logs every byte sent to and from the headphones. Turn on only when troubleshooting." />
                        </StackPanel>
                        <ToggleSwitch
                            Grid.Column="1"
                            AutomationProperties.Name="Debug logging"
                            IsOn="{x:Bind ViewModel.DebugLogging, Mode=TwoWay}" />
                    </Grid>
                    <Grid ColumnDefinitions="*,Auto">
                        <TextBlock
                            VerticalAlignment="Center"
                            Style="{StaticResource SecondaryTextStyle}"
                            Text="{x:Bind ViewModel.LogFolder}"
                            TextTrimming="CharacterEllipsis" />
                        <Button
                            Grid.Column="1"
                            Command="{x:Bind ViewModel.OpenLogFolderCommand}"
                            Content="Open log folder" />
                    </Grid>
                </StackPanel>
            </Border>

            <!-- About -->
            <TextBlock
                Margin="0,16,0,0"
                Style="{StaticResource SecondaryTextStyle}"
                Text="ConnectFlyout. Protocol code based on sony-device-center (MIT)."
                TextWrapping="Wrap" />
        </StackPanel>
    </ScrollViewer>
</local:SettingsPageBase>
````

- [ ] **Step 33: Create `src/ConnectFlyout.App/Views/Settings/AppPage.xaml.cs`**

````csharp
namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// Startup, notifications, theme and logging.
/// </summary>
public sealed partial class AppPage : SettingsPageBase
{
    public AppPage()
    {
        InitializeComponent();
    }
}
````

- [ ] **Step 34: Create `src/ConnectFlyout.App/App.xaml.cs`**

````csharp
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ConnectFlyout.Core;
using ConnectFlyout.Presentation.Devices;
using ConnectFlyout.Presentation.Headsets;
using ConnectFlyout.Presentation.Logging;
using ConnectFlyout.Presentation.Media;
using ConnectFlyout.Presentation.Navigation;
using ConnectFlyout.Presentation.Notifications;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.ViewModels;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ConnectFlyout.App;

/// <summary>
/// Tray app composition root. No window opens at launch; the tray icon opens the flyout.
/// </summary>
public partial class App : Application, IDisposable
{
    private ILoggerFactory? _loggerFactory;
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private AppSettings? _settings;
    private AppNotificationService? _notifications;
    private HeadsetManager? _manager;
    private FlyoutViewModel? _flyout;
    private SettingsViewModel? _settingsViewModel;
    private FlyoutWindow? _flyoutWindow;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _trayIcon;

    public App()
    {
        InitializeComponent();

        // Closing the settings window must not end a tray app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => AppLog.Unhandled(_logger, e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Logging
        var logFolder = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs");
        var levelSwitch = new LogLevelSwitch();
        _loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new RollingFileLoggerProvider(logFolder, levelSwitch, TimeProvider.System)));
        _logger = _loggerFactory.CreateLogger("ConnectFlyout");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Unhandled(_logger, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.UnobservedTask(_logger, e.Exception);
            e.SetObserved();
        };

        var nativeLogger = _loggerFactory.CreateLogger("ConnectFlyout.Native");
        HeadsetClient.SetLogHandler((level, message) => AppLog.Native(nativeLogger, ToLogLevel(level), message));

        // Services
        var time = TimeProvider.System;
        _settings = new AppSettings(new LocalSettingsStore());
        _notifications = new AppNotificationService(_loggerFactory.CreateLogger<AppNotificationService>());
        _notifications.Register();
        var lowBattery = new LowBatteryMonitor(_settings, _notifications);

        _manager = new HeadsetManager(
            new BluetoothDeviceWatcher(),
            name => new WinRtHeadset(name),
            _settings.IsAutoConnectEnabled,
            time,
            _loggerFactory.CreateLogger<HeadsetManager>());

        // View Models
        var headsetLogger = _loggerFactory.CreateLogger<HeadsetViewModel>();
        var playback = new PlaybackViewModel(new SmtcMediaController(), new CoreAudioVolumeController(), _loggerFactory.CreateLogger<PlaybackViewModel>());
        _flyout = new FlyoutViewModel(
            _manager,
            new FlyoutNavigator(_settings),
            playback,
            managed => new HeadsetViewModel(managed, _settings, lowBattery, time, headsetLogger));
        _flyout.SettingsRequested += (_, _) => ShowSettings();
        _flyout.QuitRequested += (_, _) => Quit();

        _settingsViewModel = new SettingsViewModel(
            _flyout,
            _settings,
            levelSwitch,
            new StartupTaskService(),
            ApplyTheme,
            HeadsetClient.SetDebugLogging,
            OpenFolder,
            logFolder,
            _loggerFactory.CreateLogger<SettingsViewModel>());
        _settingsViewModel.ApplyLogLevel();

        // Tray Icon and Flyout
        _flyoutWindow = new FlyoutWindow(_flyout);
        _flyoutWindow.CloseRequested += (_, _) => _flyoutWindow.HideFlyout();

        _trayIcon = new TrayIcon("ConnectFlyout");
        _trayIcon.Invoked += (_, _) => _flyoutWindow.Toggle(_trayIcon.GetIconRect());
        _trayIcon.SettingsRequested += (_, _) => ShowSettings();
        _trayIcon.QuitRequested += (_, _) => Quit();
        _trayIcon.Show();

        ApplyTheme(_settings.Theme);
        _manager.Start();
        var version = Package.Current.Id.Version;
        AppLog.Started(_logger, $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");
    }

    private static LogLevel ToLogLevel(NativeLogLevel level) => level switch
    {
        NativeLogLevel.Debug => LogLevel.Debug,
        NativeLogLevel.Warning => LogLevel.Warning,
        NativeLogLevel.Error => LogLevel.Error,
        _ => LogLevel.Information,
    };

    private void ShowSettings()
    {
        _flyoutWindow?.HideFlyout();
        if (_settingsViewModel is null || _settings is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel);
            _settingsWindow.ApplyTheme(_settings.Theme);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private void ApplyTheme(AppTheme theme)
    {
        _flyoutWindow?.ApplyTheme(theme);
        _settingsWindow?.ApplyTheme(theme);
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.OpenFolderFailed(_logger, ex);
        }
    }

    public void Dispose()
    {
        _trayIcon?.Dispose();
        _settingsWindow?.Close();
        _flyoutWindow?.Shutdown();
        _flyout?.Dispose();
        _manager?.Dispose();
        _notifications?.Dispose();
        _loggerFactory?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Quit()
    {
        AppLog.Quit(_logger);
        Dispose();
        Exit();
    }
}
````

- [ ] **Step 35: Build the app**

Expected: `ConnectFlyout.App.csproj -> ...\ConnectFlyout.dll` with `0 Warning(s)` and `0 Error(s)`.

````powershell
$msbuild = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
& $msbuild src\ConnectFlyout.App\ConnectFlyout.App.csproj -restore -p:RestorePackagesConfig=true -p:Configuration=Debug -p:Platform=x64 -m -nologo -v:minimal
````

---

### Task 13: Solution, Scripts and First-Build Verification

**Files:**
- Create: `ConnectFlyout.sln`, `scripts/Build.ps1`, `scripts/Build-Package.ps1`, `scripts/New-DevCertificate.ps1`, `README.md`

**Interfaces:**
- Consumes: every project above.
- Produces: one-command build and test (`scripts/Build.ps1`), signed x64 and ARM64 MSIX packages (`scripts/Build-Package.ps1`), and the XM6 first-build sign-off.

- [ ] **Step 1: Create `ConnectFlyout.sln`**

````text

Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.14.36203.30
MinimumVisualStudioVersion = 10.0.40219.1
Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "SonyTransport", "native\SonyTransport\SonyTransport.vcxproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}"
EndProject
Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "SonyProtocol", "native\SonyProtocol\SonyProtocol.vcxproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}"
EndProject
Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "ConnectFlyout.Core", "native\ConnectFlyout.Core\ConnectFlyout.Core.vcxproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}"
EndProject
Project("{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}") = "ConnectFlyout.Native.Tests", "native\ConnectFlyout.Native.Tests\ConnectFlyout.Native.Tests.vcxproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "ConnectFlyout.Core.Projection", "src\ConnectFlyout.Core.Projection\ConnectFlyout.Core.Projection.csproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "ConnectFlyout.Presentation", "src\ConnectFlyout.Presentation\ConnectFlyout.Presentation.csproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "ConnectFlyout.App", "src\ConnectFlyout.App\ConnectFlyout.App.csproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}"
EndProject
Project("{9A19103F-16F7-4668-BE54-9A1E7A4F7556}") = "ConnectFlyout.Presentation.Tests", "src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj", "{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|ARM64 = Debug|ARM64
		Debug|x64 = Debug|x64
		Release|ARM64 = Release|ARM64
		Release|x64 = Release|x64
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Debug|ARM64.ActiveCfg = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Debug|ARM64.Build.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Release|ARM64.ActiveCfg = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Release|ARM64.Build.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F01}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Debug|ARM64.ActiveCfg = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Debug|ARM64.Build.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Release|ARM64.ActiveCfg = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Release|ARM64.Build.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F02}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Debug|ARM64.ActiveCfg = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Debug|ARM64.Build.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Release|ARM64.ActiveCfg = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Release|ARM64.Build.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F03}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}.Debug|ARM64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}.Release|ARM64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F04}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Debug|ARM64.ActiveCfg = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Debug|ARM64.Build.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Release|ARM64.ActiveCfg = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Release|ARM64.Build.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F05}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Debug|ARM64.ActiveCfg = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Debug|ARM64.Build.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Release|ARM64.ActiveCfg = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Release|ARM64.Build.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F06}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Debug|ARM64.ActiveCfg = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Debug|ARM64.Build.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Debug|ARM64.Deploy.0 = Debug|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Debug|x64.Deploy.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Release|ARM64.ActiveCfg = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Release|ARM64.Build.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Release|ARM64.Deploy.0 = Release|ARM64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Release|x64.Build.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F07}.Release|x64.Deploy.0 = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}.Debug|ARM64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}.Debug|x64.ActiveCfg = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}.Debug|x64.Build.0 = Debug|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}.Release|ARM64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}.Release|x64.ActiveCfg = Release|x64
		{3B7A1F10-1C2D-4E5F-8A90-0B1C2D3E4F08}.Release|x64.Build.0 = Release|x64
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
````

- [ ] **Step 2: Create `scripts/Build.ps1`**

````powershell
<#
    Builds the whole solution and runs every test suite.

    Outputs build results plus GoogleTest and MSTest summaries. Used for the day-to-day
    build-and-test loop and for the first-build check. XM4 tests are left out unless
    -IncludeXm4 is passed.

    Depends on Visual Studio 2026 with the workloads listed in README.md; MSBuild is found
    with vswhere: https://github.com/microsoft/vswhere
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [switch] $IncludeXm4
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# Find MSBuild
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    throw 'MSBuild not found. Install Visual Studio 2026 with the workloads in README.md.'
}

# Build
& $msbuild (Join-Path $root 'ConnectFlyout.sln') -restore -p:RestorePackagesConfig=true -p:Configuration=$Configuration -p:Platform=$Platform -m -nologo -v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed ($LASTEXITCODE)."
}

# Tests only run on x64 (the build machine's architecture)
if ($Platform -ne 'x64') {
    Write-Output 'Skipping tests for ARM64.'
    return
}

# Native Tests
$nativeTests = Join-Path $root "bin\native\x64\$Configuration\ConnectFlyout.Native.Tests.exe"
$gtestFilter = if ($IncludeXm4) { '*' } else { '-Xm4*' }
& $nativeTests "--gtest_filter=$gtestFilter"
if ($LASTEXITCODE -ne 0) {
    throw 'Native tests failed.'
}

# Managed Tests
$testFilter = if ($IncludeXm4) { @() } else { @('--filter', 'TestCategory!=XM4') }
dotnet test (Join-Path $root 'src\ConnectFlyout.Presentation.Tests\ConnectFlyout.Presentation.Tests.csproj') --no-build -c $Configuration -p:Platform=x64 @testFilter
if ($LASTEXITCODE -ne 0) {
    throw 'Managed tests failed.'
}

Write-Output 'Build and tests passed.'
````

- [ ] **Step 3: Create `scripts/New-DevCertificate.ps1`**

````powershell
<#
    Creates the self-signed certificate that signs the ConnectFlyout MSIX and trusts it
    on this PC.

    Outputs the certificate thumbprint (Build-Package.ps1 reads it from the store) and
    ConnectFlyout.cer in the repo root. Run once, from an elevated PowerShell: trusting a
    certificate for sideloading means adding it to Local Machine > Trusted People.

    Depends on the PKI module that ships with Windows:
    https://learn.microsoft.com/powershell/module/pki/new-selfsignedcertificate
#>

#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

# Must match Publisher in src/ConnectFlyout.App/Package.appxmanifest
$subject = 'CN=Devin Green'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# Reuse an existing signing certificate
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date) -and $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3' } |
    Select-Object -First 1

if (-not $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject $subject `
        -KeyUsage DigitalSignature `
        -FriendlyName 'ConnectFlyout package signing' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(3) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}

# Trust It For Sideloading
$cerPath = Join-Path $root 'ConnectFlyout.cer'
Export-Certificate -Cert $certificate -FilePath $cerPath | Out-Null
Import-Certificate -FilePath $cerPath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

Write-Output "Signing certificate: $($certificate.Thumbprint)"
````

- [ ] **Step 4: Create `scripts/Build-Package.ps1`**

````powershell
<#
    Builds the signed ConnectFlyout MSIX for x64 and ARM64.

    Outputs .msix files under src/ConnectFlyout.App/AppPackages. Used to produce the
    installable app. Run scripts/New-DevCertificate.ps1 once first.

    Depends on Visual Studio 2026 (MSBuild, found with vswhere:
    https://github.com/microsoft/vswhere) and the Windows App SDK MSIX tooling.
#>

[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string[]] $Platforms = @('x64', 'ARM64')
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

# Find The Signing Certificate
$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq 'CN=Devin Green' -and $_.NotAfter -gt (Get-Date) -and $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3' } |
    Select-Object -First 1
if (-not $certificate) {
    throw 'No signing certificate. Run scripts/New-DevCertificate.ps1 from an elevated PowerShell first.'
}

# Find MSBuild
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

# Build And Package
foreach ($platform in $Platforms) {
    & $msbuild (Join-Path $root 'ConnectFlyout.sln') -restore -p:RestorePackagesConfig=true `
        -p:Configuration=Release `
        -p:Platform=$platform `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxBundle=Never `
        -p:UapAppxPackageBuildMode=SideloadOnly `
        -p:AppxPackageSigningEnabled=true `
        -p:PackageCertificateThumbprint=$($certificate.Thumbprint) `
        -m -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Packaging failed for $platform ($LASTEXITCODE)."
    }
}

Get-ChildItem (Join-Path $root 'src\ConnectFlyout.App\AppPackages') -Recurse -Filter *.msix | ForEach-Object { Write-Output $_.FullName }
````

- [ ] **Step 5: Create `README.md`**

````markdown
# ConnectFlyout

A Windows 11 tray app that controls Sony headphones from a native WinUI flyout.

## Introduction

Click the headphones icon in the tray and you get battery, noise control, ambient sound, Focus on Voice, scenes, equalizer, DSEE and playback in a flyout that looks like Windows' own. Everything else lives in a settings window. The app talks to the headphones over Bluetooth with protocol code based on [sony-device-center](https://github.com/marconvcm/sony-device-center) (MIT).

Supported headphones:

- WF-1000XM6 (verified first)
- WH-1000XM4

### Prerequisites

- Windows 11 22H2 or newer
- Visual Studio 2026 with these workloads and components:
  - Desktop development with C++ (plus the ARM64 build tools)
  - .NET desktop development
  - Windows App SDK C# and C++ templates

You can install Visual Studio with everything in one go:

```powershell
winget install --id Microsoft.VisualStudio.Community --exact --override "--wait --passive --add Microsoft.VisualStudio.Workload.NativeDesktop --add Microsoft.VisualStudio.Workload.ManagedDesktop --add Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cs --add Microsoft.VisualStudio.ComponentGroup.WindowsAppSDK.Cpp --add Microsoft.VisualStudio.Component.VC.Tools.ARM64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --includeRecommended"
```

## Building

Build everything and run the tests:

```powershell
.\scripts\Build.ps1
```

That's it for day-to-day work. XM4 tests are left out by default; add `-IncludeXm4` to run them.

To test against real headphones, set their Bluetooth address first. The hardware tests are skipped without it:

```powershell
$env:SONY_TEST_XM6_ADDRESS = "AC:80:0A:12:34:56"
.\scripts\Build.ps1
```

## Installing

1. Once, from an elevated PowerShell, create and trust the signing certificate:

    ```powershell
    .\scripts\New-DevCertificate.ps1
    ```

2. Build the signed packages:

    ```powershell
    .\scripts\Build-Package.ps1
    ```

3. Double-click the `.msix` under `src\ConnectFlyout.App\AppPackages` for your PC's architecture.

> Logs live in the app's local data folder. Open them from Settings > App > Open log folder.

## Documentation

- Design: [docs/superpowers/specs/2026-09-23-ConnectFlyout-design.md](docs/superpowers/specs/2026-09-23-ConnectFlyout-design.md)
- Plan: [docs/superpowers/plans/2026-09-23-ConnectFlyout.md](docs/superpowers/plans/2026-09-23-ConnectFlyout.md)
````

- [ ] **Step 6: Build everything and run every non-XM4 test**

Expected: the build ends with `0 Error(s)`, GoogleTest prints `[  PASSED  ] 41 tests.`, MSTest prints `Passed: 95, Skipped: 1`, and the script ends with `Build and tests passed.`

````powershell
.\scripts\Build.ps1
````

- [ ] **Step 7: Build ARM64**

Expected: `0 Error(s)` and `Skipping tests for ARM64.`

````powershell
.\scripts\Build.ps1 -Platform ARM64
````

- [ ] **Step 8: XM6 hardware test (Devin, with the XM6 connected in Windows)**

Find the address in Settings > Bluetooth & devices > Devices > WF-1000XM6 > Properties, or ask the agent to read it. Expected: `Passed: 96, Skipped: 0`. Devin should hear noise cancelling switch on, then ambient sound, then the original mode.

````powershell
$env:SONY_TEST_XM6_ADDRESS = "<XM6 Bluetooth address>"
.\scripts\Build.ps1
````

- [ ] **Step 9: Devin creates and trusts the signing certificate**

Needs an elevated PowerShell because it adds the certificate to Local Machine > Trusted People. The agent must not run this. Expected: `Signing certificate: <thumbprint>`.

````powershell
.\scripts\New-DevCertificate.ps1
````

- [ ] **Step 10: Build the signed packages**

Expected: two `.msix` paths, one ending `_x64.msix` and one ending `_arm64.msix`.

````powershell
.\scripts\Build-Package.ps1
````

- [ ] **Step 11: Devin installs and checks the app on the XM6**

Double-click the x64 `.msix`, install it, then launch ConnectFlyout from Start. Check each item and report any that fail:

1. The headphones icon appears in the tray and matches the taskbar theme.
2. A left click opens the flyout at the bottom right, 12 px from the taskbar, with acrylic, rounded corners and the slide-up animation. Clicking the icon again, clicking elsewhere, or pressing Esc closes it.
3. With only the XM6 connected, the device page opens directly with no back arrow. It shows "Connected · <codec>" and left, right and case battery.
4. The Off, ANC and Ambient tiles change the headset. The ambient slider changes the level live. Focus on Voice, a scene button, the equalizer and DSEE all change the headset.
5. Changing noise control with the earbuds' own touch control updates the flyout within about a second.
6. Playback shows the current track, and the buttons and volume slider work.
7. Putting the earbuds in the case switches the flyout to the empty page. Taking them out reconnects within about 10 seconds.
8. The Settings button opens the Mica settings window. Every page loads. Theme changes apply to both windows.
9. Settings > App > Open log folder shows `ConnectFlyout.log` with "ConnectFlyout 1.0.0.0 started" and connect lines.
10. Right-clicking the tray icon shows Open, Settings and Quit. Quit removes the icon and ends the process.

> **If a step fails:** read `ConnectFlyout.log` first (turn on Debug logging in Settings > App for byte-level detail), then fix with superpowers:systematic-debugging. Don't change the protocol bytes without a failing test that shows the headset's actual reply.

- [ ] **Step 12: XM4 follow-up (after the XM6 sign-off, when Devin asks)**

````powershell
$env:SONY_TEST_XM4_ADDRESS = "<XM4 Bluetooth address>"
.\scripts\Build.ps1 -IncludeXm4
````

Expected: GoogleTest `[  PASSED  ] 50 tests.` and MSTest `Passed: 97` (including both hardware tests).

