<#
    Shared signing setup, dot-sourced by Build-Package.ps1 and Build-Release.ps1: finds the
    code signing certificate and SignTool, and names the timestamp server.

    Every signature is timestamped, so packages and installers stay valid after the certificate
    expires. SignTool comes from the Windows SDK build tools NuGet package
    (https://www.nuget.org/packages/Microsoft.Windows.SDK.BuildTools).
#>

# RFC 3161 timestamp server
$TimestampUrl = 'http://timestamp.digicert.com'

# The code signing certificate: the one pinned in SONY_SIGNING_THUMBPRINT, or the only valid
# CN=BF59F607-536B-4252-888F-7B3ED9D56F96 code signing certificate in the personal store. Several matches is an error
# rather than a guess.
function Get-SigningCertificate {
    $valid = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.NotAfter -gt (Get-Date) -and $_.EnhancedKeyUsageList.ObjectId -contains '1.3.6.1.5.5.7.3.3' }

    if ($env:SONY_SIGNING_THUMBPRINT) {
        $pinned = $valid | Where-Object { $_.Thumbprint -eq $env:SONY_SIGNING_THUMBPRINT }
        if (-not $pinned) {
            throw "No valid code signing certificate with thumbprint $env:SONY_SIGNING_THUMBPRINT."
        }
        return $pinned
    }

    $candidates = @($valid | Where-Object { $_.Subject -eq 'CN=BF59F607-536B-4252-888F-7B3ED9D56F96' })
    if ($candidates.Count -eq 0) {
        throw 'No signing certificate. Run scripts/New-DevCertificate.ps1 from an elevated PowerShell first.'
    }
    if ($candidates.Count -gt 1) {
        throw 'Several CN=BF59F607-536B-4252-888F-7B3ED9D56F96 signing certificates. Set SONY_SIGNING_THUMBPRINT to the one to use.'
    }
    return $candidates[0]
}

# SignTool from the newest Windows SDK in the build tools package, compared as versions
function Get-SignTool {
    $signtool = Get-ChildItem (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools') -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' -and $_.Directory.Parent.Name -as [version] } |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if (-not $signtool) {
        throw 'SignTool not found. Restore the solution once so the Windows SDK build tools package is downloaded.'
    }
    return $signtool
}

# Signs and timestamps one file
function Invoke-Sign([string] $Path, $Certificate, $SignTool) {
    & $SignTool.FullName sign /q /fd SHA256 /sha1 $Certificate.Thumbprint /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) {
        throw "Signing $Path failed ($LASTEXITCODE)."
    }
}
