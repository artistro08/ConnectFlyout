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
$msbuild = & $vswhere -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
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
