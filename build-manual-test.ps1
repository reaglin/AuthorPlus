# Builds the hand-test exe into manual-test\ (gitignored). Same route as SMADA10.
#
#   .\build-manual-test.ps1                 framework-dependent single file (needs .NET 10 on the machine; ~25 MB)
#   .\build-manual-test.ps1 -SelfContained  no .NET needed on the target machine (~75 MB)
#
# Prerequisite on a fresh machine: ..\AiManager\build\pack.ps1 (the Eaglin.AiManager packages
# come from the local feed C:\nuget-local). The published folder carries those DLLs inside the
# single file, so the exe runs anywhere .NET 10 is present.
param(
    [switch] $SelfContained,
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'manual-test')
)
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'src\AuthorPlus.App\AuthorPlus.App.csproj'

$args = @('publish', $proj, '-c', 'Release', '-r', 'win-x64',
          '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
          '-o', $OutputDirectory, '--nologo', '-v', 'q')
if ($SelfContained) { $args += @('--self-contained', 'true', '-p:EnableCompressionInSingleFile=true') }
else                { $args += @('--self-contained', 'false') }

& dotnet @args
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

$exe = Join-Path $OutputDirectory 'AuthorPlus.exe'
$props = [xml](Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props'))
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.AuthorPlusVersion } | Where-Object { $_ }) | Select-Object -First 1
Write-Host ("AuthorPlus {0} -> {1} ({2:N1} MB, {3})" -f $version, $exe, ((Get-Item $exe).Length / 1MB), ($(if ($SelfContained) { 'self-contained' } else { 'needs .NET 10' })))
