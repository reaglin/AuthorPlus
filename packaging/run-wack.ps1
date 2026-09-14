<#
.SYNOPSIS
    Runs the Windows App Certification Kit against the built package and summarises the report.

.DESCRIPTION
    appcert.exe requires elevation. Two things go wrong when it is run by hand:

      * From a normal prompt Windows refuses to start it, or spawns an elevated console that
        closes the moment it exits - so nothing is written and there is nothing to read.
      * An elevated console starts in C:\Windows\System32, so a relative package path does not
        resolve, appcert exits immediately, and the window closes before the error can be read.

    This script resolves both paths to absolute ones first, then relaunches itself elevated with
    -NoExit so the window stays open. It prints the pass/fail summary at the end rather than
    leaving an XML file to read.

    WACK is not a gate on submission - Store certification runs its own equivalent - but a local
    pass avoids a failed round trip. What to expect is in docs/STORE-SUBMISSION.md.

.EXAMPLE
    ./packaging/run-wack.ps1
    Tests the newest .msixbundle in artifacts/.

.EXAMPLE
    ./packaging/run-wack.ps1 -Package artifacts\AuthorPlus_1.0.0.0_x64.msixbundle
#>
[CmdletBinding()]
param(
    [string]$Package,
    [string]$Report
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# --- resolve paths before elevating, while the working directory is still the repo -----------
if (-not $Package) {
    $Package = Get-ChildItem (Join-Path $repoRoot 'artifacts') -Filter *.msixbundle -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $Package) {
        throw "No .msixbundle in artifacts\. Run packaging/make-msixupload.ps1 first."
    }
}
$Package = (Resolve-Path $Package).Path
if (-not $Report) { $Report = Join-Path $repoRoot 'artifacts/wack-report.xml' }
# The report file does not exist yet, so resolve its directory instead.
$Report = Join-Path ((Resolve-Path (Split-Path -Parent $Report)).Path) (Split-Path -Leaf $Report)

# --- elevate -----------------------------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal(
    [Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'appcert.exe needs elevation - relaunching. Approve the UAC prompt.' -ForegroundColor Yellow
    Write-Host 'The new window stays open when it finishes; read the summary there.' -ForegroundColor Yellow
    # Relaunch through Windows PowerShell, not whichever shell is running: a Store-installed
    # pwsh lives under C:\Program Files\WindowsApps, whose ACLs block launching it by path.
    # Nothing in this script needs PowerShell 7.
    $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    Start-Process $shell -Verb RunAs -ArgumentList @(
        '-NoExit', '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-Package', "`"$Package`"",
        '-Report',  "`"$Report`""
    )
    return
}

# --- run ------------------------------------------------------------------------------------
$wack = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\App Certification Kit\appcert.exe'
if (-not (Test-Path $wack)) {
    throw "appcert.exe not found at $wack. Install the Windows App Certification Kit (part of the Windows SDK)."
}

Write-Host "Package : $Package"
Write-Host "Report  : $Report"
Write-Host ''
Write-Host 'WACK drives the app on screen for several minutes. Leave the machine alone.' -ForegroundColor Cyan
Write-Host ''

Remove-Item $Report -Force -ErrorAction SilentlyContinue
& $wack reset | Out-Null
& $wack test -appxpackagepath $Package -reportoutputpath $Report
$appcertExit = $LASTEXITCODE

if (-not (Test-Path $Report)) {
    throw "appcert exited $appcertExit and wrote no report. Run '$wack test -appxpackagepath `"$Package`" -reportoutputpath `"$Report`"' here to see its own error."
}

# --- summarise ------------------------------------------------------------------------------
# In the kit's report RESULT is a CDATA child element of TEST, not an attribute - reading it as
# an attribute silently finds nothing and reports a clean run over a failing one.
$xml = [xml](Get-Content $Report)

$overallNode = $xml.SelectSingleNode('//@OVERALL_RESULT')
$overall = if ($overallNode) { $overallNode.Value } else { '(not stated - open the report)' }
$colour  = switch ($overall) { 'PASS' { 'Green' } 'WARNING' { 'Yellow' } default { 'Red' } }
Write-Host ''
Write-Host "OVERALL: $overall" -ForegroundColor $colour

$tests = @($xml.SelectNodes('//TEST') | ForEach-Object {
    $r = $_.SelectSingleNode('RESULT')
    [pscustomobject]@{
        Name     = $_.NAME
        Result   = if ($r) { $r.InnerText.Trim() } else { 'UNKNOWN' }
        Optional = $_.OPTIONAL -eq 'TRUE'
        Messages = @($_.SelectNodes('.//MESSAGE') | ForEach-Object { $_.TEXT })
    }
})
Write-Host ("{0} tests: {1} pass" -f $tests.Count, (@($tests | Where-Object Result -eq 'PASS').Count)) -ForegroundColor DarkGray

foreach ($t in $tests | Where-Object { $_.Result -ne 'PASS' }) {
    $tag = if ($t.Optional) { 'optional' } else { 'required' }
    $c   = if ($t.Result -eq 'WARNING') { 'Yellow' } else { 'Red' }
    Write-Host ''
    Write-Host ("  [{0}] {1}  ({2})" -f $t.Result, $t.Name, $tag) -ForegroundColor $c
    $shown = 0
    foreach ($m in $t.Messages) {
        if ($shown -ge 6) { Write-Host ("        ... and {0} more, see the report" -f ($t.Messages.Count - 6)) -ForegroundColor DarkGray; break }
        Write-Host "        $m" -ForegroundColor DarkGray
        $shown++
    }
}

Write-Host ''
Write-Host "Full report: $Report" -ForegroundColor Cyan
Write-Host 'Record the outcome in docs/STORE-SUBMISSION.md.' -ForegroundColor Cyan
