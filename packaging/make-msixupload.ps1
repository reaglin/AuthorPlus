<#
.SYNOPSIS
    Builds the .msixupload package that Partner Center accepts for a Store submission.

.DESCRIPTION
    A .msixupload is a zip holding an .msixbundle and, optionally, an .appxsym symbol archive
    for crash analysis. This script produces one:

      pack.ps1 -SkipSigning   ->  one .msix per runtime
      makeappx bundle         ->  AuthorPlus_<version>_<arch>.msixbundle
      zip                     ->  AuthorPlus_<version>_<arch>_bundle.msixupload

    The package is deliberately UNSIGNED. The Store re-signs every submission with the publisher
    certificate it issues; a package signed with a local or development certificate is rejected.

    IDENTITY. Three values must match Partner Center exactly or the upload is rejected:

      Package/Identity/Name          the name Partner Center issued for Author+ — copy it from
                                     Product management > Product identity. It is NOT the
                                     reserved display name; it looks like DeanEaglin.AuthorPlus.
      Package/Identity/Publisher     CN=E88392BA-A722-4B3A-8372-04403A55AA63   (per account)
      PublisherDisplayName           Dean Eaglin            (already in AppxManifest.xml)

    Verify a build with -VerifyFamilyName: the script derives the package family name from the
    identity and prints it, and it must equal what Partner Center shows (the account's hash
    suffix is _xs303fgqvwdg8, the same one SMADA and Statistle carry).

    Run without -IdentityName for a build check only. The output is a valid package with the
    development identity from AppxManifest.xml, which is fine for WACK and useless for
    submission.

.EXAMPLE
    ./packaging/make-msixupload.ps1
    Build check with the development identity.

.EXAMPLE
    ./packaging/make-msixupload.ps1 -IdentityName 'DeanEaglin.AuthorPlus' `
        -Publisher 'CN=E88392BA-A722-4B3A-8372-04403A55AA63' -VerifyFamilyName
    The real submission package.
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [string]$Configuration = 'Release',
    [string[]]$Runtimes = @('win-x64'),
    [string]$OutputDirectory,
    [switch]$SkipSymbols,
    [switch]$VerifyFamilyName
)

$ErrorActionPreference = 'Stop'

$repoRoot     = Split-Path -Parent $PSScriptRoot
$packagingDir = $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts' }

function Find-WindowsKitTool {
    param([Parameter(Mandatory)][string]$Name)
    $binRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (-not (Test-Path $binRoot)) { throw "Windows SDK not found at $binRoot." }
    $tool = Get-ChildItem -Path $binRoot -Filter $Name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending |
        Select-Object -First 1
    if (-not $tool) { throw "$Name not found under $binRoot. Install the Windows 10/11 SDK." }
    return $tool.FullName
}

function Get-PackageFamilyName {
    # The Store's own rule: SHA-256 of the publisher string in UTF-16LE, first 8 bytes, encoded
    # in Crockford-style base32 over "0123456789abcdefghjkmnpqrstvwxyz". Comparing the result
    # with what Partner Center shows catches a typo in either half of the identity before an
    # upload is rejected for it.
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$PublisherId)

    $bytes = [System.Text.Encoding]::Unicode.GetBytes($PublisherId)
    $hash  = [System.Security.Cryptography.SHA256]::HashData($bytes)[0..7]

    $bits = -join ($hash | ForEach-Object { [Convert]::ToString($_, 2).PadLeft(8, '0') })
    $bits = $bits.PadRight(65, '0')                      # 13 groups of 5 bits
    $alphabet = '0123456789abcdefghjkmnpqrstvwxyz'
    $suffix = -join (0..12 | ForEach-Object { $alphabet[[Convert]::ToInt32($bits.Substring($_ * 5, 5), 2)] })
    return "${Name}_$suffix"
}

# --- version -------------------------------------------------------------------------------
if (-not $Version) {
    $props = Join-Path $repoRoot 'Directory.Build.props'
    $Version = ([xml](Get-Content $props)).Project.PropertyGroup.AuthorPlusVersion |
        Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "AuthorPlusVersion not found in $props." }
}
$parts = @($Version.Split('.'))
while ($parts.Count -lt 4) { $parts += '0' }
# The Store requires the fourth part to be 0; it reserves it for its own use.
$parts[3] = '0'
$packageVersion = ($parts[0..3] -join '.')

Write-Host "Author+ $packageVersion -> .msixupload" -ForegroundColor Cyan
if (-not $IdentityName) {
    Write-Warning 'No -IdentityName given: this package carries the DEVELOPMENT identity and cannot be submitted.'
}

if ($VerifyFamilyName) {
    $checkName = if ($IdentityName) { $IdentityName } else { ([xml](Get-Content (Join-Path $packagingDir 'AppxManifest.xml'))).Package.Identity.Name }
    $checkPub  = if ($Publisher)    { $Publisher }    else { ([xml](Get-Content (Join-Path $packagingDir 'AppxManifest.xml'))).Package.Identity.Publisher }
    Write-Host ("  package family name: {0}" -f (Get-PackageFamilyName -Name $checkName -PublisherId $checkPub)) -ForegroundColor Cyan
    Write-Host '  It must equal what Partner Center shows under Product identity.' -ForegroundColor DarkGray
}

# --- one .msix per architecture --------------------------------------------------------------
$bundleDir = Join-Path $repoRoot 'artifacts/bundle'
Remove-Item $bundleDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $bundleDir | Out-Null

$packArgs = @{ Configuration = $Configuration; Version = $packageVersion; OutputDirectory = $bundleDir; SkipSigning = $true }
if ($IdentityName)         { $packArgs.IdentityName         = $IdentityName }
if ($Publisher)            { $packArgs.Publisher            = $Publisher }
if ($PublisherDisplayName) { $packArgs.PublisherDisplayName = $PublisherDisplayName }

foreach ($runtime in $Runtimes) {
    Write-Host "  packing $runtime" -ForegroundColor DarkGray
    & (Join-Path $packagingDir 'pack.ps1') @packArgs -Runtime $runtime | Out-Null
}

$msixFiles = Get-ChildItem $bundleDir -Filter *.msix
if (-not $msixFiles) { throw "pack.ps1 produced no .msix in $bundleDir." }

# --- bundle ----------------------------------------------------------------------------------
$arch       = if ($Runtimes.Count -gt 1) { 'multi' } else { $Runtimes[0].Replace('win-', '') }
$stem       = "AuthorPlus_${packageVersion}_$arch"
$bundlePath = Join-Path $OutputDirectory "$stem.msixbundle"
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
Remove-Item $bundlePath -Force -ErrorAction SilentlyContinue

$makeappx = Find-WindowsKitTool 'makeappx.exe'
& $makeappx bundle /d $bundleDir /p $bundlePath /bv $packageVersion /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx bundle failed.' }

# --- symbols ---------------------------------------------------------------------------------
# .appxsym is a zip of the build's .pdb files. pack.ps1 strips them from the package itself
# (they trip Store certification); Partner Center wants them separately for crash reports.
$uploadParts = @($bundlePath)
if (-not $SkipSymbols) {
    foreach ($runtime in $Runtimes) {
        $publishDir = Join-Path $repoRoot "artifacts/publish/$runtime"
        $pdbs = Get-ChildItem $publishDir -Filter *.pdb -Recurse -ErrorAction SilentlyContinue
        if (-not $pdbs) { continue }
        $sym = Join-Path $OutputDirectory "$stem.appxsym"
        Remove-Item $sym -Force -ErrorAction SilentlyContinue
        Compress-Archive -Path $pdbs.FullName -DestinationPath "$sym.zip" -Force
        Move-Item "$sym.zip" $sym -Force
        $uploadParts += $sym
    }
}

# --- .msixupload -------------------------------------------------------------------------------
$upload = Join-Path $OutputDirectory "${stem}_bundle.msixupload"
Remove-Item $upload -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $uploadParts -DestinationPath "$upload.zip" -Force
Move-Item "$upload.zip" $upload -Force

Write-Host ''
foreach ($f in @($upload) + $uploadParts) {
    $item = Get-Item $f
    Write-Host ("  {0,-46} {1,8:N1} MB" -f $item.Name, ($item.Length / 1MB)) -ForegroundColor Green
}
Write-Host ''
Write-Host 'Upload the .msixupload at Partner Center -> Submission -> Packages.' -ForegroundColor Cyan

Write-Output $upload
