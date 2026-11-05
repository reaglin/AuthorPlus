# The Store screenshots (plan 4.6): the hand-test exe opened on the sample book (tools/sample-book), driven
# through UI Automation and captured with PrintWindow by tools/tour-window.ps1 (from TrafficLab+; it never
# types into another window). Writes store/screenshots/*.png at the window size given (the Store wants at
# least 1366 x 768). The sample book is original text written for this; no one's manuscript is shown.
#
#   .\build-manual-test.ps1
#   pwsh tools/store-shots.ps1 [-Size 1440x900]
param([string]$Size = "1440x900")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$exe  = Join-Path $root "manual-test\AuthorPlus.exe"
$out  = Join-Path $root "store\screenshots"
$tour = Join-Path $PSScriptRoot "tour-window.ps1"
if (-not (Test-Path $exe)) { throw "Build the hand-test exe first: .\build-manual-test.ps1" }
New-Item -ItemType Directory -Force $out | Out-Null

# The status bar shows the folder the book was opened from, so it is a neutral one: no user name in it.
$lib = Join-Path ([Environment]::GetFolderPath("CommonDocuments")) "Author+ sample"
if (Test-Path $lib) { Remove-Item $lib -Recurse -Force }
$book = (& dotnet run (Join-Path $PSScriptRoot "sample-book\make-sample-book.cs") -- $lib | Select-Object -Last 1).Trim()
if (-not (Test-Path (Join-Path $book "book.json"))) { throw "The sample book was not made: $book" }

# Opening a book puts it on the recent list; keep this machine's own list out of the way and put it back.
$recent = Join-Path $env:APPDATA "AuthorPlus\recent_books.txt"
if (Test-Path $recent) { Copy-Item $recent "$recent.store-shot" -Force }
$open = "`"$book`""
try {
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "wait:1500", "shot:$out\01-write-a-chapter.png"
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "select:🔍 Analysis", "wait:1500", "shot:$out\02-ai-analysis.png"
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "select:✦ Summary", "wait:1500", "shot:$out\03-ai-summary.png"
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "select:Characters (4)", "wait:1500", "shot:$out\04-characters-chart.png"
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "select:Plotlines (3)", "wait:1500", "shot:$out\05-plotlines-chart.png"
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "select:Timeline (4)", "wait:1500", "shot:$out\06-timeline.png"
    & $tour -Exe $exe -Args $open -WaitMs 5000 -NoKeys -Steps "size:$Size", "select:👤 Nell Carrow", "wait:1500", "shot:$out\07-character.png"
}
finally {
    if (Test-Path "$recent.store-shot") { Move-Item "$recent.store-shot" $recent -Force }
    elseif (Test-Path $recent) { Remove-Item $recent }
    Remove-Item $lib -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem $out -Filter *.png | ForEach-Object { "{0}  {1:N0} KB" -f $_.Name, ($_.Length / 1KB) }
