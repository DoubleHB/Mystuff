# Brings the latest commits of the three working folders into this combined repository, then pushes.
#   .\sync.ps1            pull the ApiScout/, ApiScoutMobile/ and ApiScout.Tests/ subtrees, push to origin with tags
#   .\sync.ps1 -NoPush    pull only
#   .\sync.ps1 -Tag desktop-v1.7.0    also tag the result (desktop-vX.Y.Z or mobile-vX.Y.Z)
# The working folders stay where they are (C:\Claude\ApiScout, C:\Claude\ApiScoutMobile, C:\Claude\ApiScout.Tests):
# commit there as usual; this repository is the published view of all three, with their histories.
param([switch]$NoPush, [string[]]$Tag)
$ErrorActionPreference = 'Stop'
$git = 'C:\Claude\tools\MinGit\cmd\git.exe'
Set-Location $PSScriptRoot
foreach ($name in 'ApiScout', 'ApiScoutMobile', 'ApiScout.Tests') {
    $path = "C:\Claude\$name"
    "== $name <- $path"
    & $git subtree pull --prefix=$name $path main -m "Sync $name from the working folder"
    if ($LASTEXITCODE -ne 0) { throw "subtree pull for $name failed" }
}
foreach ($t in $Tag) { & $git tag $t; "tagged $t" }
if (-not $NoPush) {
    & $git push origin main --tags
    if ($LASTEXITCODE -ne 0) { throw 'push failed' }
}
