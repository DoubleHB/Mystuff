# Makes the release signing key once: android\keystore\apiscout-release.jks + android\key.properties (both ignored by git).
#   .\make-release-key.ps1                  a random 28-character password, written to key.properties
#   .\make-release-key.ps1 -Password '...'  your own password
# Back up the .jks and key.properties somewhere safe: without them a later version cannot be signed as the same app.
# (With Play App Signing this is the upload key; Google can reset an upload key, but not without a support request.)
param([string]$Password, [string]$Alias = 'apiscout')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$keytool = Join-Path $env:LOCALAPPDATA 'Android\jdk\bin\keytool.exe'
if (-not (Test-Path $keytool)) { throw "keytool not found at $keytool" }
$dir = Join-Path $root 'android\keystore'
$jks = Join-Path $dir 'apiscout-release.jks'
$props = Join-Path $root 'android\key.properties'
if (Test-Path $jks) { throw "$jks already exists - delete it on purpose if you really want a new key" }
New-Item -ItemType Directory -Force $dir | Out-Null
if (-not $Password) {
    $chars = [char[]]((48..57) + (65..90) + (97..122))
    $Password = -join (1..28 | ForEach-Object { $chars[(Get-Random -Maximum $chars.Length)] })
}
# keytool chats on stderr, which PowerShell would report as an error: run it through cmd so only the exit code counts
& cmd /c "`"$keytool`" -genkeypair -v -keystore `"$jks`" -storetype PKCS12 -alias $Alias -keyalg RSA -keysize 2048 -validity 10000 -storepass $Password -keypass $Password -dname `"CN=ApiScout, O=kramn, C=GB`" 2>&1"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $jks)) { throw 'keytool failed' }
@"
storePassword=$Password
keyPassword=$Password
keyAlias=$Alias
storeFile=keystore/apiscout-release.jks
"@ | Set-Content -Encoding ascii $props
"Keystore: $jks"
"Passwords: $props  (keep both files backed up; neither is in git)"
