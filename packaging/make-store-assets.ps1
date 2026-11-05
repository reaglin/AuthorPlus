<#
.SYNOPSIS
    Draws every Author+ icon, tile and Store listing image.

.DESCRIPTION
    Author+ has no photographic logo to crop, so the mark is drawn here, once, at 2048 px, and
    everything else is a downscale of it. That keeps the small sizes sharp and means a change of
    mind about the artwork is a change to this file and one command.

    The mark is a manuscript page on ink-navy ground with a gold plus over its lower corner (no
    plate behind the plus — Ron, 2026-11-05). Two versions are drawn:
      * the titled mark — a smaller page with "Author+" in white under it (Ron chose candidate B,
        2026-11-05) — for every tile of 71 px and up, the 50 px Store logo's larger scales, the
        listing images and the .ico's large frames;
      * the plain mark — the page and plus alone — for 16-48 px (taskbar, Start list, the .ico's
        small frames), where lettering is illegible noise.
    The wide tile, the splash screen and the hero images set the name beside the plain glyph.

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
# Drawn at 2048 so every emitted size is a downscale. The plus overhangs the page corner so the
# two shapes read as one object; a soft shadow under it (no plate) keeps it apart from the page.
$shadow = '#0D1526'

function Get-PlusArt([int]$cx, [int]$cy, [int]$len, [int]$bar) {
    $h = [int]($len / 2); $b = [int]($bar / 2); $o = [int]($bar * 0.22)
    @('-fill', $shadow, '-stroke', 'none',
      '-draw', "roundrectangle $($cx-$b+$o),$($cy-$h+$o) $($cx+$b+$o),$($cy+$h+$o) $b,$b",
      '-draw', "roundrectangle $($cx-$h+$o),$($cy-$b+$o) $($cx+$h+$o),$($cy+$b+$o) $b,$b",
      '-fill', $accent,
      '-draw', "roundrectangle $($cx-$b),$($cy-$h) $($cx+$b),$($cy+$h) $b,$b",
      '-draw', "roundrectangle $($cx-$h),$($cy-$b) $($cx+$h),$($cy+$b) $b,$b")
}

# Plain: the page with five lines of text, inset so the 44 px tile still shows navy around it.
$plainArt = @(
    '-fill', $shadow, '-stroke', 'none', '-draw', 'roundrectangle 604,436 1500,1668 44,44',
    '-fill', $page,   '-draw', 'roundrectangle 580,404 1476,1636 44,44',
    '-fill', $ink,
    '-draw', 'roundrectangle 680,560 1330,614 27,27',
    '-draw', 'roundrectangle 680,700 1376,754 27,27',
    '-draw', 'roundrectangle 680,840 1240,894 27,27',
    '-draw', 'roundrectangle 680,980 1376,1034 27,27',
    '-draw', 'roundrectangle 680,1120 1150,1174 27,27'
) + (Get-PlusArt 1490 1490 480 124)

# Titled: a smaller page high on the tile, "Author+" in white under it.
$titledArt = @(
    '-fill', $shadow, '-stroke', 'none', '-draw', 'roundrectangle 676,316 1412,1316 40,40',
    '-fill', $page,   '-draw', 'roundrectangle 656,290 1392,1290 40,40',
    '-fill', $ink,
    '-draw', 'roundrectangle 740,420 1280,466 23,23',
    '-draw', 'roundrectangle 740,536 1318,582 23,23',
    '-draw', 'roundrectangle 740,652 1200,698 23,23',
    '-draw', 'roundrectangle 740,768 1318,814 23,23',
    '-draw', 'roundrectangle 740,884 1100,930 23,23'
) + (Get-PlusArt 1400 1210 400 104)

$mark   = Join-Path $workDir 'mark-2048.png'         # plain, on the navy ground: 16-48 px
$titled = Join-Path $workDir 'titled-2048.png'       # titled, on the navy ground: 71 px and up
$glyph  = Join-Path $workDir 'glyph-2048.png'        # plain, transparent, for compositing beside the name

Invoke-Magick -size 2048x2048 "xc:$navy" @plainArt -strip $mark
Invoke-Magick -size 2048x2048 "xc:$navy" @titledArt -font $font -fill $white -pointsize 300 `
    -gravity south -annotate '+0+250' 'Author+' -strip $titled
Invoke-Magick -size 2048x2048 'xc:none'  @plainArt -strip $glyph

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
    # Each scale from the titled mark when it comes out at 71 px or more, else from the plain one.
    param([string]$Name, [int]$Width, [int]$Height, [string]$Src, [string]$Mode = 'Fit')
    foreach ($s in $scales) {
        $w = [int][math]::Round($Width  * $s.Factor)
        $h = [int][math]::Round($Height * $s.Factor)
        $dest = Join-Path $assetsDir "$Name$($s.Suffix).png"
        $from = if ($Src -eq $mark -and [math]::Min($w, $h) -ge 71) { $titled } else { $Src }
        Write-Capped $from $w $h $dest $Mode
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
# Square44x44 is the taskbar / Start list icon: plain at every scale, so it matches the 16-48 px target sizes.
foreach ($s in $scales) {
    $px = [int][math]::Round(44 * $s.Factor)
    Write-Capped $mark $px $px (Join-Path $assetsDir "Square44x44Logo$($s.Suffix).png")
}
Copy-Item (Join-Path $assetsDir 'Square44x44Logo.scale-100.png') (Join-Path $assetsDir 'Square44x44Logo.png') -Force
Write-Host '  Square44x44Logo' -ForegroundColor DarkGray
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
Write-Plain $titled 300  300  (Join-Path $storeDir 'StoreLogo-300x300.png')
Write-Plain $titled 1080 1080 (Join-Path $storeDir 'BoxArt-1080x1080.png')
Write-Plain $titled 720  1080 (Join-Path $storeDir 'PosterArt-720x1080.png') 'Pad'
Write-Hero 2400 1200 (Join-Path $storeDir 'SuperHeroArt-2400x1200.png')
Write-Hero 1920 1080 (Join-Path $storeDir 'HeroImage-1920x1080.png')

# --- the .exe icon ------------------------------------------------------------------------------
if (-not $SkipIco) {
    Write-Host 'Application icon -> src/AuthorPlus.App/AuthorPlus.ico' -ForegroundColor Cyan
    # Titled frames where the name can be read (64 px and up), plain ones below.
    $frames = foreach ($px in 256, 128, 64, 48, 40, 32, 24, 20, 16) {
        $f = Join-Path $workDir "ico-$px.png"
        Invoke-Magick $(if ($px -ge 64) { $titled } else { $mark }) -filter Lanczos -resize "${px}x${px}" -strip $f
        $f
    }
    Invoke-Magick @frames (Join-Path $repoRoot 'src/AuthorPlus.App/AuthorPlus.ico')

    # The same titled mark for the program's tile on softwareplus.ai, when that repo is beside this one.
    $siteIcon = Join-Path (Split-Path -Parent $repoRoot) 'SoftwarePlus/site/images/icons/author.png'
    if (Test-Path (Split-Path -Parent $siteIcon)) {
        Invoke-Magick $titled -filter Lanczos -resize 360x360 -strip $siteIcon
        Write-Host "  softwareplus.ai tile -> $siteIcon (deploy it from that repo)" -ForegroundColor DarkGray
    }
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
