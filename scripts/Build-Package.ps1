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

# Find The Signing Certificate And SignTool
. (Join-Path $PSScriptRoot 'Signing.ps1')
$certificate = Get-SigningCertificate
$signtool = Get-SignTool

# Find MSBuild
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) {
    throw 'MSBuild not found. Install Visual Studio 2026 with the workloads in README.md.'
}

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
        -p:AppxPackageSigningTimestampServerUrl=$TimestampUrl `
        -p:AppxPackageSigningTimestampDigestAlgorithm=SHA256 `
        -p:AppExeSigningThumbprint=$($certificate.Thumbprint) `
        -p:AppExeSignTool=$($signtool.FullName) `
        -p:AppExeTimestampUrl=$TimestampUrl `
        -m -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Packaging failed for $platform ($LASTEXITCODE)."
    }
}

Get-ChildItem (Join-Path $root 'src\ConnectFlyout.App\AppPackages') -Recurse -Filter *.msix | ForEach-Object { Write-Output $_.FullName }
