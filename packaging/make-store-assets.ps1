<#
.SYNOPSIS
    Draws every Author+ icon, tile and Store listing image.

.DESCRIPTION
    Author+ has no photographic logo to crop, so the mark is drawn here, once, at 2048 px, and
    everything else is a downscale of it. That keeps the small sizes sharp and means a change of
    mind about the artwork is a change to this file and one command.

    The mark is a manuscript page on ink-navy ground with a plus set into its lower corner: at
    16 px that reads as a pale page and a bright plus, which is as much as any icon gets at that
    size. The wordmark is only used where there is room for it — the wide tile, the splash
    screen and the listing images — because "Author+" at 44 px is illegible noise.

    Every package asset is written under WACK's 204800-byte limit for logo images: 256 colours
    first, then 128, 64, 32 until it fits. On flat artwork like this that is invisible. The
    Partner Center uploads are not capped and stay 24-bit.

    Outputs:
      packaging/Assets/              MSIX tiles - unqualified (scale-100) plus scale-* variants
      store/images/                  Partner Center upload images
      src/AuthorPlus.App/AuthorPlus.ico   the .exe icon (skip with -SkipIco)

    Requires ImageMagick 7 (magick.exe) on PATH.

.EXAMPLE
    ./packaging/make-store-assets.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipIco
)

$ErrorActionPreference = 'Stop'

$repoRoot  = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'packaging/Assets'
$storeDir  = Join-Path $repoRoot 'store/images'
$workDir   = Join-Path ([System.IO.Path]::GetTempPath()) 'authorplus-store-assets'

foreach ($d in @($assetsDir, $storeDir, $workDir)) { New-Item -ItemType Directory -Force $d | Out-Null }

function Invoke-Magick {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)
    & magick @Arguments
    if ($LASTEXITCODE -ne 0) { throw "magick failed: $($Arguments -join ' ')" }
}

if (-not (Get-Command magick -ErrorAction SilentlyContinue)) {
    throw 'ImageMagick 7 (magick.exe) is not on PATH. Install it, or run this on a machine that has it.'
}

# --- palette -----------------------------------------------------------------------------------
$navy    = '#1B2A4A'   # the ground, and the splash screen colour in AppxManifest.xml
$navyLo  = '#111B30'   # the hero gradient's far end
$page    = '#F4EFE3'   # manuscript paper
$ink     = '#5A6B86'   # the lines of text on the page
$accent  = '#E8A33D'   # the plus
$white   = '#FFFFFF'
$muted   = '#A8BBD4'
# magick strips backslashes out of -font paths, so keep it forward-slashed.
$font    = $env:WINDIR.Replace('\', '/') + '/Fonts/segoeuib.ttf'

# WACK fails a package whose logo images exceed this; see Write-Capped.
$MaxAssetBytes = 204800

# --- the mark ----------------------------------------------------------------------------------
# Drawn at 2048 so every emitted size is a downscale. The page is inset far enough that the
# 44 px tile still shows navy around it; the plus overhangs the page corner so the two shapes
# read as one object rather than a sticker on a card.
$pageArt = @(
    # the page, with a soft shadow so it lifts off the navy
    '-fill', '#0D1526', '-stroke', 'none',
    '-draw', 'roundrectangle 604,436 1500,1668 44,44',
    '-fill', $page,
    '-draw', 'roundrectangle 580,404 1476,1636 44,44',
    # lines of text, left aligned, ragged right, stopping clear of the plus
    '-fill', $ink, '-stroke', 'none',
    '-draw', 'roundrectangle 680,560 1330,614 27,27',
    '-draw', 'roundrectangle 680,700 1376,754 27,27',
    '-draw', 'roundrectangle 680,840 1240,894 27,27',
    '-draw', 'roundrectangle 680,980 1376,1034 27,27',
    '-draw', 'roundrectangle 680,1120 1150,1174 27,27'
)
# The plus: a navy plate behind it so it stays separate wherever it overlaps the page.
$plusArt = @(
    '-fill', $navy, '-stroke', 'none',
    '-draw', 'roundrectangle 1180,1180 1800,1800 60,60',
    '-fill', $accent,
    '-draw', 'roundrectangle 1434,1274 1546,1706 56,56',
    '-draw', 'roundrectangle 1274,1434 1706,1546 56,56'
)

$mark  = Join-Path $workDir 'mark-2048.png'        # on the navy ground, for tiles
$glyph = Join-Path $workDir 'glyph-2048.png'       # transparent, for compositing

Invoke-Magick -size 2048x2048 "xc:$navy" @pageArt @plusArt -strip $mark
Invoke-Magick -size 2048x2048 'xc:none'  @pageArt @plusArt -strip $glyph

# --- emitters ----------------------------------------------------------------------------------
function Write-Capped {
    # Write Src at Width x Height, quantised only as far as it must be to clear the byte cap.
    param([string]$Src, [int]$Width, [int]$Height, [string]$Dest, [string]$Mode = 'Fit')

    foreach ($colors in 256, 128, 64, 32) {
        $cmd = if ($Mode -eq 'Pad') {
            @('-size', "${Width}x${Height}", "xc:$navy",
              '(', $Src, '-filter', 'Lanczos', '-resize', "${Width}x${Height}", ')',
              '-gravity', 'center', '-composite')
        } else {
            @($Src, '-filter', 'Lanczos', '-resize', "${Width}x${Height}^",
              '-gravity', 'center', '-extent', "${Width}x${Height}")
        }
        Invoke-Magick @cmd -colors $colors -strip $Dest
        if ((Get-Item $Dest).Length -le $MaxAssetBytes) { return }
    }
    Write-Warning ("{0} is {1} bytes, over WACK's {2} limit even at 32 colours." -f `
        (Split-Path -Leaf $Dest), (Get-Item $Dest).Length, $MaxAssetBytes)
}

function Write-Plain {
    # Uncapped, full colour - for the Partner Center uploads, which are not in the package.
    param([string]$Src, [int]$Width, [int]$Height, [string]$Dest, [string]$Mode = 'Fit')
    if ($Mode -eq 'Pad') {
        Invoke-Magick -size "${Width}x${Height}" "xc:$navy" `
            '(' $Src -filter Lanczos -resize "${Width}x${Height}" ')' `
            -gravity center -composite -strip $Dest
    } else {
        Invoke-Magick $Src -filter Lanczos -resize "${Width}x${Height}^" `
            -gravity center -extent "${Width}x${Height}" -strip $Dest
    }
}

$scales = @(
    @{ Suffix = '.scale-100'; Factor = 1.00 },
    @{ Suffix = '.scale-125'; Factor = 1.25 },
    @{ Suffix = '.scale-150'; Factor = 1.50 },
    @{ Suffix = '.scale-200'; Factor = 2.00 },
    @{ Suffix = '.scale-400'; Factor = 4.00 }
)

function Write-TileSet {
    param([string]$Name, [int]$Width, [int]$Height, [string]$Src, [string]$Mode = 'Fit')
    foreach ($s in $scales) {
        $w = [int][math]::Round($Width  * $s.Factor)
        $h = [int][math]::Round($Height * $s.Factor)
        $dest = Join-Path $assetsDir "$Name$($s.Suffix).png"
        Write-Capped $Src $w $h $dest $Mode
        if ($s.Suffix -eq '.scale-100') { Copy-Item $dest (Join-Path $assetsDir "$Name.png") -Force }
    }
    Write-Host "  $Name" -ForegroundColor DarkGray
}

function Write-TargetSizes {
    param([string]$Name, [string]$Src)
    foreach ($t in 16, 24, 32, 48, 256) {
        Write-Capped $Src $t $t (Join-Path $assetsDir "$Name.targetsize-$t.png")
        Write-Capped $Src $t $t (Join-Path $assetsDir ("$Name.targetsize-$t" + "_altform-unplated.png"))
    }
}

function Write-WideTile {
    # The glyph beside the name, on flat navy.
    param([int]$Width, [int]$Height, [string]$Dest)
    $margin = [int]($Width * 0.05)
    $artW   = [int]($Height * 0.78)
    $textX  = $margin + $artW + [int]($Width * 0.05)
    Invoke-Magick -size "${Width}x${Height}" "xc:$navy" `
        '(' $glyph -filter Lanczos -resize "${artW}x${artW}" ')' `
        -gravity west -geometry "+$margin+0" -compose over -composite `
        -gravity northwest -font $font -pointsize ([int]($Height * 0.21)) -fill $white `
        -annotate "+$textX+$([int]($Height * 0.37))" 'Author+' `
        -colors 256 -strip $Dest
}

function Write-Splash {
    # The mark centred with the name under it, on the navy the manifest declares.
    param([int]$Width, [int]$Height, [string]$Dest)
    $artH = [int]($Height * 0.52)
    Invoke-Magick -size "${Width}x${Height}" "xc:$navy" `
        '(' $glyph -filter Lanczos -resize "${artH}x${artH}" ')' `
        -gravity center -geometry "+0-$([int]($Height * 0.08))" -compose over -composite `
        -gravity center -font $font -pointsize ([int]($Height * 0.15)) -fill $white `
        -annotate "+0+$([int]($Height * 0.30))" 'Author+' `
        -colors 256 -strip $Dest
}

function Write-Hero {
    # The mark left, the name and a line of copy right. caption: fits the point size to the
    # column, so the wording can change without overflowing.
    param([int]$Width, [int]$Height, [string]$Dest)
    $margin = [int]($Width * 0.06)
    $artW   = [int]($Height * 0.62)
    $textX  = $margin + $artW + [int]($Width * 0.05)
    $textW  = $Width - $textX - $margin

    $name = Join-Path $workDir "hero-name-$Width.png"
    $tag  = Join-Path $workDir "hero-tag-$Width.png"
    Invoke-Magick -background none -fill $white -font $font `
        -size "${textW}x$([int]($Height * 0.13))" -gravity west 'caption:Author+' -strip $name
    Invoke-Magick -background none -fill $muted -font $font `
        -size "${textW}x$([int]($Height * 0.055))" -gravity west `
        'caption:Chapters, characters, timeline and plotlines in one place' -strip $tag

    Invoke-Magick -size "${Width}x${Height}" "gradient:$navy-$navyLo" `
        '(' $glyph -filter Lanczos -resize "${artW}x${artW}" ')' `
        -gravity west -geometry "+$margin+0" -compose over -composite `
        -gravity northwest `
        $name -geometry "+$textX+$([int]($Height * 0.33))" -composite `
        $tag  -geometry "+$textX+$([int]($Height * 0.53))" -composite `
        -strip $Dest
    Write-Host "  $(Split-Path -Leaf $Dest)" -ForegroundColor DarkGray
}

# --- MSIX tiles ----------------------------------------------------------------------------------
Write-Host 'MSIX tiles -> packaging/Assets' -ForegroundColor Cyan
Write-TileSet 'Square44x44Logo'   44  44  $mark
Write-TileSet 'Square71x71Logo'   71  71  $mark
Write-TileSet 'Square150x150Logo' 150 150 $mark
Write-TileSet 'Square310x310Logo' 310 310 $mark
Write-TileSet 'StoreLogo'         50  50  $mark
Write-TargetSizes 'Square44x44Logo' $mark

foreach ($s in $scales) {
    Write-WideTile ([int][math]::Round(310 * $s.Factor)) ([int][math]::Round(150 * $s.Factor)) `
        (Join-Path $assetsDir "Wide310x150Logo$($s.Suffix).png")
    Write-Splash   ([int][math]::Round(620 * $s.Factor)) ([int][math]::Round(300 * $s.Factor)) `
        (Join-Path $assetsDir "SplashScreen$($s.Suffix).png")
}
Copy-Item (Join-Path $assetsDir 'Wide310x150Logo.scale-100.png') (Join-Path $assetsDir 'Wide310x150Logo.png') -Force
Copy-Item (Join-Path $assetsDir 'SplashScreen.scale-100.png')    (Join-Path $assetsDir 'SplashScreen.png') -Force
Write-Host '  Wide310x150Logo' -ForegroundColor DarkGray
Write-Host '  SplashScreen' -ForegroundColor DarkGray

# --- Partner Center listing images -----------------------------------------------------------------
Write-Host 'Store listing -> store/images' -ForegroundColor Cyan
Write-Plain $mark 300  300  (Join-Path $storeDir 'StoreLogo-300x300.png')
Write-Plain $mark 1080 1080 (Join-Path $storeDir 'BoxArt-1080x1080.png')
Write-Plain $mark 720  1080 (Join-Path $storeDir 'PosterArt-720x1080.png') 'Pad'
Write-Hero 2400 1200 (Join-Path $storeDir 'SuperHeroArt-2400x1200.png')
Write-Hero 1920 1080 (Join-Path $storeDir 'HeroImage-1920x1080.png')

# --- the .exe icon ------------------------------------------------------------------------------
if (-not $SkipIco) {
    Write-Host 'Application icon -> src/AuthorPlus.App/AuthorPlus.ico' -ForegroundColor Cyan
    Invoke-Magick $mark -filter Lanczos `
        -define 'icon:auto-resize=256,128,64,48,40,32,24,20,16' `
        (Join-Path $repoRoot 'src/AuthorPlus.App/AuthorPlus.ico')
}

# --- contact sheet ------------------------------------------------------------------------------
$sheet = Join-Path $storeDir 'asset-contact-sheet.png'
Push-Location $assetsDir
try {
    & magick montage `
        'Square44x44Logo.targetsize-16.png' 'Square44x44Logo.targetsize-32.png' `
        'Square44x44Logo.png' 'Square71x71Logo.png' 'Square150x150Logo.png' `
        'Square310x310Logo.png' 'Wide310x150Logo.png' 'SplashScreen.png' `
        -tile 4x2 -geometry '+10+10<' -background '#909090' $sheet
    if ($LASTEXITCODE -ne 0) { throw 'montage failed.' }
}
finally { Pop-Location }

$pngs = Get-ChildItem $assetsDir -Filter *.png
$over = @($pngs | Where-Object Length -gt $MaxAssetBytes)
Write-Host ''
Write-Host ("Assets: {0} files, {1:N1} MB, {2} over the {3}-byte cap" -f `
    $pngs.Count, (($pngs | Measure-Object Length -Sum).Sum / 1MB), $over.Count, $MaxAssetBytes) `
    -ForegroundColor Green
Write-Host "Contact sheet: $sheet" -ForegroundColor Green
