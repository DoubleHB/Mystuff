# Creates (or refreshes) the GitHub releases for the versions currently in this repository:
#   desktop-vX.Y.Z  with  C:\Claude\ApiScout-Dist\ApiScout-X.Y.Z-portable.zip   (made by ApiScout\publish.ps1)
#   mobile-vX.Y.Z   with  C:\Claude\ApiScoutMobile\ApiScout.apk                   (made by ApiScoutMobile\build-apk.ps1)
# Run it yourself, signed in:  winget install GitHub.cli ; gh auth login   - then  .\release.ps1
# It needs the tags to be on GitHub already (sync.ps1 pushes them). Nothing here asks for or stores a password.
param([switch]$Desktop, [switch]$Mobile)
$ErrorActionPreference = 'Stop'
if (-not $Desktop -and -not $Mobile) { $Desktop = $Mobile = $true }
$env:PATH = "C:\Claude\tools\MinGit\cmd;$env:PATH"
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'The GitHub CLI is not installed: winget install GitHub.cli' }
gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Not signed in to GitHub: run  gh auth login  first.' }
Set-Location $PSScriptRoot
$repo = gh repo view --json nameWithOwner --jq .nameWithOwner
if (-not $repo) { throw 'No GitHub remote yet: add origin and push (see README) first.' }

function Publish-Release([string]$tag, [string]$title, [string]$notes, [string]$file) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing $file - build it first." }
    $notesFile = Join-Path $env:TEMP "apiscout-release-notes.md"
    Set-Content -Path $notesFile -Value $notes -Encoding utf8
    gh release view $tag *> $null
    if ($LASTEXITCODE -eq 0) {
        gh release upload $tag $file --clobber
        gh release edit $tag --title $title --notes-file $notesFile | Out-Null
        "refreshed $tag"
    } else {
        gh release create $tag $file --title $title --notes-file $notesFile
        if ($LASTEXITCODE -ne 0) { throw "gh release create $tag failed" }
        "created $tag"
    }
}

if ($Desktop) {
    $v = ([xml](Get-Content 'ApiScout\ApiScout.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    $zip = "C:\Claude\ApiScout-Dist\ApiScout-$v-portable.zip"
    $notes = @"
Portable build for Windows: unzip and run ApiScout.exe - nothing to install (the .NET runtime is inside).
Keep portable.txt beside the exe to keep all data in a folder next to it.

In the app, About > Updates: enter ``$repo`` and later versions are found from this repository's desktop-v tags;
"Update and restart" swaps in the new exe.
"@
    Publish-Release "desktop-v$v" "API Free $v (Windows)" $notes $zip
}

if ($Mobile) {
    $v = ((Select-String -Path 'ApiScoutMobile\pubspec.yaml' -Pattern '^version:\s*([\d.]+)').Matches[0].Groups[1].Value)
    $apk = 'C:\Claude\ApiScoutMobile\ApiScout.apk'
    # the release notes are the section for this version in ApiScoutMobile\RELEASE-NOTES.md
    $md = Get-Content 'ApiScoutMobile\RELEASE-NOTES.md' -Raw
    $section = [regex]::Match($md, "(?s)## $([regex]::Escape($v))\s*\r?\n(.*?)(?=\r?\n## |\z)").Groups[1].Value.Trim()
    if (-not $section) { $section = "API Free for Android $v." }
    $notes = "$section`n`nInstall: allow 'install unknown apps' for your browser or file manager, open ApiScout.apk. A build older than 1.5.0 was signed with a debug key: export your data in the app, uninstall it, install this, import."
    Publish-Release "mobile-v$v" "API Free $v (Android)" $notes $apk
}
"Releases: https://github.com/$repo/releases"
