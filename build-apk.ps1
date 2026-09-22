# ApiScout Mobile: refresh the shared rules from the desktop app, test, build the APK.
#   .\build-apk.ps1            knowledge + tests (offline) + release APK  ->  .\ApiScout.apk
#   .\build-apk.ps1 -Live      also runs the live scan test
#   .\build-apk.ps1 -Bundle    also builds the Play app bundle       ->  .\ApiScout.aab
# Both are signed with the release key when android\key.properties exists (see .\make-release-key.ps1), else debug.
# Flutter lives in C:\Claude\tools\flutter (not on the system PATH); the Android SDK and JDK are the ones in
# %LOCALAPPDATA%\Android that the .NET Android apps use.
param([switch]$Live, [switch]$SkipKnowledge, [switch]$Bundle)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$env:PATH = "C:\Claude\tools\flutter\bin;C:\Claude\tools\MinGit\cmd;$env:PATH"
$env:JAVA_HOME = "$env:LOCALAPPDATA\Android\jdk"
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"; $env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
$env:PUB_CACHE = 'C:\Claude\tools\pub-cache'; $env:GRADLE_USER_HOME = 'C:\Claude\tools\gradle-home'; $env:CI = 'true'
Set-Location $root

if (-not $SkipKnowledge) {
    # category rules, keyword rules and key hints come from the desktop app, so both apps agree
    dotnet run --project C:\Claude\ApiScout.Tests -c Release -- --export-knowledge (Join-Path $root 'assets\knowledge.json')
    if ($LASTEXITCODE -ne 0) { throw 'knowledge export failed' }
}
if ($Live) { flutter test --dart-define=LIVE=true } else { flutter test }
if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
flutter build apk --release
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Copy-Item (Join-Path $root 'build\app\outputs\flutter-apk\app-release.apk') (Join-Path $root 'ApiScout.apk') -Force
'{0}  ({1:N1} MB)' -f (Join-Path $root 'ApiScout.apk'), ((Get-Item (Join-Path $root 'ApiScout.apk')).Length / 1MB)
if ($Bundle) {
    flutter build appbundle --release
    if ($LASTEXITCODE -ne 0) { throw 'bundle build failed' }
    Copy-Item (Join-Path $root 'build\app\outputs\bundle\release\app-release.aab') (Join-Path $root 'ApiScout.aab') -Force
    '{0}  ({1:N1} MB)' -f (Join-Path $root 'ApiScout.aab'), ((Get-Item (Join-Path $root 'ApiScout.aab')).Length / 1MB)
}
if (-not (Test-Path (Join-Path $root 'android\key.properties'))) { Write-Warning 'No android\key.properties: signed with the debug key. Run .\make-release-key.ps1 first for a Play build.' }
