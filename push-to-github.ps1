# Puts API Scout on GitHub: a PRIVATE repository, all commits and version tags, and the portable zip as a release.
# You run this yourself, once you are signed in - it never asks for or stores a password:
#   1. install the GitHub CLI:   winget install GitHub.cli
#   2. sign in (opens a browser): gh auth login
#   3. .\push-to-github.ps1            (or -Name my-repo-name, or -Public)
# Afterwards type "<you>/<repo>" into About > Updates. A private repository also needs a token there:
# github.com > Settings > Developer settings > Fine-grained tokens > only this repository > Contents: Read-only.
param([string]$Name = 'apiscout', [switch]$Public)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:PATH = "C:\Claude\tools\MinGit\cmd;$env:PATH"   # gh drives git
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI is not installed: winget install GitHub.cli' }
gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Not signed in to GitHub: run  gh auth login  first.' }

Set-Location $root
$version = ([xml](Get-Content (Join-Path $root 'ApiScout.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip = Join-Path (Split-Path $root -Parent) "ApiScout-Dist\ApiScout-$version-portable.zip"

if (-not (git remote)) {
    $visibility = if ($Public) { '--public' } else { '--private' }
    gh repo create $Name $visibility --source . --remote origin --description 'API Scout - free API finder (WPF)'
    if ($LASTEXITCODE -ne 0) { throw 'gh repo create failed' }
}
git push -u origin HEAD
git push origin --tags
if ($LASTEXITCODE -ne 0) { throw 'git push failed' }

if (Test-Path -LiteralPath $zip) {
    gh release view "v$version" *> $null
    if ($LASTEXITCODE -eq 0) { gh release upload "v$version" $zip --clobber }
    else { gh release create "v$version" $zip --title "API Scout $version" --notes "Portable build: unzip and run ApiScout.exe (no .NET install needed)." }
} else { Write-Warning "No portable zip for $version yet - run .\publish.ps1 first, then this script again." }

$repo = gh repo view --json nameWithOwner --jq .nameWithOwner
"Done. Update source for About > Updates:  $repo"
