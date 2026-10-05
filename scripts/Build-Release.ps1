<#
    Builds every ConnectFlyout release file for one version into artifacts\release\v<Version>:

    - ConnectFlyout_<Version>_x64.msix and _arm64.msix, signed, plus ConnectFlyout.cer to trust them
    - ConnectFlyout_<Version>_x64.msi and _arm64.msi, the classic installer (no certificate
      needed; installs just for you, or for everyone from the wizard's Advanced step)

    Used to produce a GitHub release. Run scripts/New-DevCertificate.ps1 once first.
    Depends on Visual Studio 2026 (MSBuild) and the .NET SDK; the WiX Toolset comes in as a
    NuGet package: https://wixtoolset.org/docs/intro/
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    # The Store rejects any revision (4th number) other than 0
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$artifacts = Join-Path $root 'artifacts'
$release = Join-Path $artifacts "release\v$Version"
New-Item -ItemType Directory -Force -Path $release | Out-Null

# Find MSBuild
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) {
    throw 'MSBuild not found. Install Visual Studio 2026 with the workloads in README.md.'
}

# Signing For The Classic Exe And MSI (same certificate and SignTool as Build-Package.ps1), so
# Windows keeps the tray icon's taskbar spot across updates and the installer shows its publisher
. (Join-Path $PSScriptRoot 'Signing.ps1')
$certificate = Get-SigningCertificate
$signtool = Get-SignTool

# Stamp The Version Into The MSIX Manifest
$manifest = Join-Path $root 'src\ConnectFlyout.App\Package.appxmanifest'
$manifestText = Get-Content $manifest -Raw
$manifestText = $manifestText -replace '(<Identity[^>]*\sVersion=")[^"]+(")', "`${1}$Version`${2}"
Set-Content $manifest $manifestText -NoNewline

# MSIX Packages
& (Join-Path $PSScriptRoot 'Build-Package.ps1')
foreach ($platform in 'x64', 'ARM64') {
    $package = Get-ChildItem (Join-Path $root 'src\ConnectFlyout.App\AppPackages') -Recurse -Filter "ConnectFlyout.App_${Version}_$platform.msix" | Select-Object -First 1
    Copy-Item $package.FullName (Join-Path $release "ConnectFlyout_${Version}_$($platform.ToLower()).msix")
}
$publicCertificate = Get-ChildItem (Join-Path $root 'src\ConnectFlyout.App\AppPackages') -Recurse -Filter "ConnectFlyout.App_${Version}_x64.cer" | Select-Object -First 1
Copy-Item $publicCertificate.FullName (Join-Path $release 'ConnectFlyout.cer')

# License Page For The Installer (plain text to RTF)
$licenseText = (Get-ChildItem $root -Filter 'LICENSE*' | Sort-Object Name | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n`n"
$rtfBody = $licenseText -replace '\\', '\\' -replace '\{', '\{' -replace '\}', '\}' -replace "`r?`n", "\par`n"
$licenseRtf = Join-Path $artifacts 'License.rtf'
Set-Content $licenseRtf "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 $rtfBody}" -Encoding ascii

# Classic Installers: unpackaged publish, then the MSI around it
foreach ($platform in 'x64', 'ARM64') {
    $publish = Join-Path $artifacts "publish\$platform"
    if (Test-Path $publish) {
        Remove-Item $publish -Recurse -Force
    }

    & $msbuild (Join-Path $root 'src\ConnectFlyout.App\ConnectFlyout.App.csproj') -restore -t:Publish `
        -p:Configuration=Release `
        -p:Platform=$platform `
        -p:WindowsPackageType=None `
        -p:Version=$Version `
        -p:PublishDir="$publish\\" `
        -m -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing the classic build failed for $platform ($LASTEXITCODE)."
    }

    Invoke-Sign (Join-Path $publish 'ConnectFlyout.exe') $certificate $signtool

    $msiOut = Join-Path $artifacts "msi\$platform"
    dotnet build (Join-Path $root 'installer\ConnectFlyout.Installer.wixproj') -c Release `
        -p:Platform=$platform `
        -p:ProductVersion=$Version `
        -p:PublishDir="$publish\\" `
        -p:LicenseRtf=$licenseRtf `
        -o $msiOut -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Building the MSI failed for $platform ($LASTEXITCODE)."
    }
    Invoke-Sign (Join-Path $msiOut 'ConnectFlyout.msi') $certificate $signtool
    Copy-Item (Join-Path $msiOut 'ConnectFlyout.msi') (Join-Path $release "ConnectFlyout_${Version}_$($platform.ToLower()).msi")
}

Get-ChildItem $release | ForEach-Object { Write-Output $_.FullName }
