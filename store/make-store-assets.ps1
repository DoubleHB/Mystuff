# Builds the Google Play listing images from raw 1080x2400 emulator screenshots.
#   .\store\make-store-assets.ps1 -Shots <folder with the raw PNGs>
# Output (in .\store): screenshots\01..08-*.png (1080x1920, 9:16), feature-graphic.png (1024x500), icon-512.png (full bleed).
param([Parameter(Mandatory)][string]$Shots)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot 'screenshots'
New-Item -ItemType Directory -Force $out | Out-Null

$blue = [System.Drawing.Color]::FromArgb(74, 140, 245)
$teal = [System.Drawing.Color]::FromArgb(62, 205, 184)
$white = [System.Drawing.Color]::White

function New-Canvas([int]$w, [int]$h) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.Point]::new(0, 0)), ([System.Drawing.Point]::new($w, $h)), $blue, $teal
    $g.FillRectangle($brush, 0, 0, $w, $h); $brush.Dispose()
    return $bmp, $g
}

function Get-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function Draw-Text($g, [string]$text, [string]$font, [float]$size, [System.Drawing.FontStyle]$style, [System.Drawing.RectangleF]$rect, [string]$align = 'Center') {
    $f = New-Object System.Drawing.Font $font, $size, $style, ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = $align; $sf.LineAlignment = 'Near'; $sf.Trimming = 'Word'
    $b = New-Object System.Drawing.SolidBrush $white
    $g.DrawString($text, $f, $b, $rect, $sf)
    $f.Dispose(); $b.Dispose(); $sf.Dispose()
}

function Frame-Shot([string]$name, [string]$source, [string]$headline, [string]$sub) {
    $bmp, $g = New-Canvas 1080 1920
    Draw-Text $g $headline 'Segoe UI' 66 'Bold' ([System.Drawing.RectangleF]::new(60, 90, 960, 180))
    Draw-Text $g $sub 'Segoe UI' 36 'Regular' ([System.Drawing.RectangleF]::new(90, 270, 900, 130))
    $shot = [System.Drawing.Bitmap]::FromFile((Join-Path $Shots "$source.png"))
    $pw = 780; $ph = [int]($pw * $shot.Height / $shot.Width); $px = (1080 - $pw) / 2; $py = 430
    $bezel = 22
    # shadow
    $sh = Get-RoundedPath ($px - $bezel + 10) ($py - $bezel + 24) ($pw + 2 * $bezel) ($ph + 2 * $bezel) 90
    $sb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0, 0, 0)); $g.FillPath($sb, $sh); $sb.Dispose()
    # bezel
    $bp = Get-RoundedPath ($px - $bezel) ($py - $bezel) ($pw + 2 * $bezel) ($ph + 2 * $bezel) 90
    $bb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(24, 24, 28)); $g.FillPath($bb, $bp); $bb.Dispose()
    # screen, clipped to rounded corners
    $clip = Get-RoundedPath $px $py $pw $ph 70
    $g.SetClip($clip); $g.DrawImage($shot, $px, $py, $pw, $ph); $g.ResetClip()
    $shot.Dispose(); $g.Dispose()
    $path = Join-Path $out "$name.png"; $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $path
}

function Draw-Icon($g, [float]$x, [float]$y, [float]$size, [bool]$rounded) {
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new($x, $y)), ([System.Drawing.PointF]::new($x + $size, $y + $size)), $blue, $teal
    if ($rounded) { $g.FillPath($brush, (Get-RoundedPath $x $y $size $size ($size * 0.22))) } else { $g.FillRectangle($brush, $x, $y, $size, $size) }
    $brush.Dispose()
    $f = New-Object System.Drawing.Font 'Segoe UI', ($size * 0.62), 'Regular', ([System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'; $sf.LineAlignment = 'Center'
    $b = New-Object System.Drawing.SolidBrush $white
    $g.DrawString('{ }', $f, $b, ([System.Drawing.RectangleF]::new($x, $y - $size * 0.04, $size, $size)), $sf)
    $f.Dispose(); $b.Dispose(); $sf.Dispose()
}

$shotsList = @(
    @('01-home',     'home',      '3,591 free APIs in your pocket',           'Scanned from five public directories. Nothing to sign up for, no key needed to browse.'),
    @('02-demo-key', 'nasa1',     'See the demo key before you sign up',      'Provider-published test keys, a request that works as it is, and how to get your own key.'),
    @('03-try-it',   'tryit',     'Try any endpoint from your phone',         'Variables, {key} filled in only when you press send, and a history of your last requests.'),
    @('04-docs-scan','ctdb3',     'Scan the docs for key info',               'Sign-up links, sample keys and example endpoints read live from the provider''s own pages.'),
    @('05-insight',  'insight',   'More about this API',                      'What the known facts mean for you, then the provider''s own words. Nothing is made up.'),
    @('06-filters',  'filter',    'Filter by what you need',                  'Auth, how much is free, HTTPS and CORS. Find an API you can call today.'),
    @('07-categories','drawer',   '42 categories, favourites, collections',   'Tag, note and group APIs, and move it all to and from the desktop app.'),
    @('08-dark',     'dark-home', 'Light, dark or system theme',              'Your keys stay encrypted in the Android keystore, on this phone only.')
)
foreach ($s in $shotsList) { Frame-Shot $s[0] $s[1] $s[2] $s[3] }

# Feature graphic 1024x500
$bmp, $g = New-Canvas 1024 500
$card = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))
$g.FillPath($card, (Get-RoundedPath 60 100 300 300 66)); $card.Dispose()
Draw-Icon $g 80 120 260 $true
$b = New-Object System.Drawing.SolidBrush $white
$f1 = New-Object System.Drawing.Font 'Segoe UI', 96, 'Bold', ([System.Drawing.GraphicsUnit]::Pixel)
$f2 = New-Object System.Drawing.Font 'Segoe UI', 40, 'Regular', ([System.Drawing.GraphicsUnit]::Pixel)
$f3 = New-Object System.Drawing.Font 'Segoe UI', 30, 'Regular', ([System.Drawing.GraphicsUnit]::Pixel)
$g.DrawString('ApiScout', $f1, $b, 400, 105)
$g.DrawString('free API finder', $f2, $b, 408, 225)
$g.DrawString("Thousands of free APIs, their demo keys,`na docs scanner and a request tester.", $f3, $b, 408, 300)
$f1.Dispose(); $f2.Dispose(); $f3.Dispose(); $b.Dispose(); $g.Dispose()
$bmp.Save((Join-Path $PSScriptRoot 'feature-graphic.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

# Full-bleed 512x512 icon (Play applies its own rounded mask)
$bmp, $g = New-Canvas 512 512
Draw-Icon $g 0 0 512 $false
$g.Dispose(); $bmp.Save((Join-Path $PSScriptRoot 'icon-512.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

Get-ChildItem $PSScriptRoot -Recurse -Filter *.png | ForEach-Object {
    $i = [System.Drawing.Image]::FromFile($_.FullName); "{0}  {1}x{2}  {3:N0} bytes" -f $_.Name, $i.Width, $i.Height, $_.Length; $i.Dispose()
}
