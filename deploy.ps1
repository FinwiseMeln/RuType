# Deploy the portable build (exe + dict + assets) to a target folder WITHOUT
# touching user data (data\: config.json, my_words/stopwords/rules, ngram cache,
# log, corrections.jsonl) and without shipping debug symbols (*.pdb).
#
# Folder model:
#   RuType.exe, dict\, assets\  = APPLICATION (overwritten on update)
#   data\                       = YOUR SETTINGS (created on first run, preserved)
#
# ASCII-only on purpose: Windows PowerShell 5.1 mis-reads BOM-less Cyrillic in .ps1.
#
# Usage:
#   .\deploy.ps1                      # -> %LOCALAPPDATA%\Programs\RuType
#   .\deploy.ps1 -Target D:\Apps\RuType
#   .\deploy.ps1 -Publish             # rebuild the portable first, then deploy

param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA "Programs\RuType"),
    [switch]$Publish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "dist\RuType"

if ($Publish) {
    Write-Host "Rebuilding portable..." -ForegroundColor Cyan
    # Publish settings (single-file, self-contained, R2R) live in the csproj.
    # Always a CLEAN publish: an incremental one skips the up-to-date bundle and then
    # does not lay the loose native WPF dlls (*_cor3.dll) into the output folder.
    Remove-Item -Recurse -Force (Join-Path $root "src\RuType\bin\Release"), (Join-Path $root "src\RuType\obj\Release") -ErrorAction SilentlyContinue
    dotnet publish (Join-Path $root "src\RuType\RuType.csproj") -c Release -o $src
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
}

if (-not (Test-Path (Join-Path $src "RuType.exe"))) {
    throw "Not found: $src\RuType.exe - build the portable first (or run with -Publish)."
}

Write-Host "Copying application to $Target (data\ and *.pdb are left alone)..." -ForegroundColor Cyan
# /E - subdirs (dict, assets); /XD data - exclude the settings folder; /XF *.pdb - no symbols.
# /NFL /NDL /NP - quieter. No /MIR or /PURGE so nothing in the target is deleted.
robocopy $src $Target /E /XD (Join-Path $src "data") /XF *.pdb /NFL /NDL /NP | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) { throw "robocopy error (code $rc)" }

Write-Host "Done. Settings in $Target\data left untouched." -ForegroundColor Green
exit 0  # robocopy uses 1..7 for success; do not leak that as a failure code
