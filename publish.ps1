# API Free release build.
#   .\publish.ps1            framework-dependent build into ..\ApiScout-App (close ApiScout.exe first) + the portable zip
#   .\publish.ps1 -SkipApp   only the portable zip
# The portable zip is one self-contained ApiScout.exe (no .NET install needed), README.md and portable.txt; with
# portable.txt beside the exe all data lives in a "data" folder next to it. latest.json is what "Check for updates"
# reads when the update source is the Dist folder (or a URL the file is copied to).
param([switch]$SkipApp)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'ApiScout.csproj'
$version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$dist = Join-Path (Split-Path $root -Parent) 'ApiScout-Dist'
$stage = Join-Path $dist "ApiScout-$version-portable"
$zip = "$stage.zip"
if (-not $version -or $stage -notlike '*\ApiScout-Dist\ApiScout-*-portable') { throw "Unexpected staging path: $stage" }

if (-not $SkipApp) {
    if (Get-Process ApiScout -ErrorAction SilentlyContinue) { throw 'ApiScout.exe is running - close it first (or use -SkipApp).' }
    dotnet publish $proj -c Release -r win-x64 --self-contained false -o (Join-Path (Split-Path $root -Parent) 'ApiScout-App') -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'publish (app) failed' }
}

# a fresh staging folder each time: build into a temp name, keep only the exe
$build = "$stage.build"
if (Test-Path -LiteralPath $build) { [IO.Directory]::Delete($build, $true) }
dotnet publish $proj -c Release -r win-x64 --self-contained true -o $build -nologo -v q `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'publish (portable) failed' }

if (Test-Path -LiteralPath $stage) { [IO.Directory]::Delete($stage, $true) }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $build 'ApiScout.exe') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $stage
[IO.Directory]::Delete($build, $true)
@"
API Free $version - portable copy

Run ApiScout.exe; nothing needs installing (the .NET runtime is inside the exe).
While this file sits beside ApiScout.exe, everything API Free keeps - the scanned catalogue, favourites,
tags, collections, notes - is stored in the "data" folder next to it. Delete this file and API Free uses
%LOCALAPPDATA%\ApiScout instead.

Saved API keys are encrypted for the Windows account that saved them, so they do not travel with the
folder: use About > Export my data (with a passphrase) and Import on the other PC.
"@ | Out-File (Join-Path $stage 'portable.txt') -Encoding utf8

if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
[pscustomobject]@{ version = "$version"; tag = "v$version"; download = (Split-Path $zip -Leaf); sha256 = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash; built = (Get-Date -Format s) } |
    ConvertTo-Json | Out-File (Join-Path $dist 'latest.json') -Encoding utf8
'{0}  ({1:N1} MB)' -f $zip, ((Get-Item $zip).Length / 1MB)
