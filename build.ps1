# ME desktop build + release helper.
# Usage:
#   powershell -ExecutionPolicy Bypass -File build.ps1            # build only
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Push      # build, commit, push
#
# Notes:
#   - Close the running ME.exe first, otherwise MSBuild cannot replace bin\...\ME.exe (MSB3027).
#   - Version lives in ME\ME.csproj <Version>. Bump it before releasing.

param(
    [switch]$Push,
    [string]$CommitMessage = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csproj = Join-Path $root "ME\ME.csproj"

function Get-Version {
    $line = Select-String -Path $csproj -Pattern "<Version>(.*?)</Version>" | Select-Object -First 1
    return $line.Matches[0].Groups[1].Value
}

Write-Host "== ME desktop build ==" -ForegroundColor Cyan
Write-Host ("Version: " + (Get-Version))

# Kill a running instance so the output exe is not locked.
$proc = Get-Process -Name "ME" -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "ME.exe is running, stopping it first..."
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 1
}

dotnet build $csproj -v q --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit 1
}
Write-Host "Build OK" -ForegroundColor Green

if (-not $Push) { return }

$ver = Get-Version
if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
    $CommitMessage = "v$($ver): build from build.ps1"
}

git add -A
git commit -m $CommitMessage
git push origin HEAD
if ($LASTEXITCODE -eq 0) {
    Write-Host ("Pushed v" + $ver) -ForegroundColor Green
}
