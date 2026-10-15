<#
.SYNOPSIS
  Installs the DynamoAutomation package for every Revit 2020-2027 found on this computer.

.DESCRIPTION
  Copies the Dynamo package into each Dynamo for Revit (and Dynamo Sandbox) packages folder,
  so the same nodes are available in every Dynamo version. Nothing is installed into Revit.
  An older DynamoAutomation 1.x package is moved to %AppData%\Dynamo\DynamoAutomation_backup.

  Close Revit and Dynamo before running. Run from the package's "extra" folder:
      powershell -ExecutionPolicy Bypass -File install.ps1
  or double-click install.cmd.

.PARAMETER RevitVersions  Only install for these years, e.g. -RevitVersions 2021,2024 (comma-separated)
#>
param(
    [string]$RevitVersions    # text, so "2021,2024" also works through powershell -File / install.cmd
)
$ErrorActionPreference = "Stop"
$packageRoot = Split-Path $PSScriptRoot -Parent          # ...\DynamoAutomation
$packageName = "DynamoAutomation"
$autodesk = Join-Path $env:ProgramFiles "Autodesk"
$dynamoRevitData = Join-Path $env:APPDATA "Dynamo\Dynamo Revit"
$dynamoCoreData = Join-Path $env:APPDATA "Dynamo\Dynamo Core"
$backupRoot = Join-Path $env:APPDATA "Dynamo\DynamoAutomation_backup\$(Get-Date -Format yyyyMMdd-HHmmss)"

if (-not (Test-Path (Join-Path $packageRoot "pkg.json"))) {
    throw "Run this script from the 'extra' folder of the DynamoAutomation package (pkg.json not found in $packageRoot)."
}
if (Get-Process Revit -ErrorAction SilentlyContinue) {
    Write-Warning "Revit is running. Files in use cannot be replaced; close Revit if the install reports errors."
}

# --- which Revit versions --------------------------------------------------------
$installed = 2020..2027 | Where-Object { Test-Path (Join-Path $autodesk "Revit $_\Revit.exe") }
if ($RevitVersions) {
    $wanted = $RevitVersions -split "[,; ]+" | Where-Object { $_ } | ForEach-Object { [int]$_ }
    $installed = $installed | Where-Object { $wanted -contains $_ }
}
if (-not $installed) { throw "No supported Revit (2020-2027) found." }
Write-Host "Revit versions: $($installed -join ', ')"

# --- Dynamo packages folders -------------------------------------------------------
$packageDirs = New-Object System.Collections.Generic.List[string]
foreach ($year in $installed) {
    $core = Join-Path $autodesk "Revit $year\AddIns\DynamoForRevit\DynamoCore.dll"
    if (-not (Test-Path $core)) { Write-Warning "Revit $year has no Dynamo for Revit; skipping."; continue }
    $v = [Version](Get-Item $core).VersionInfo.FileVersion
    $packageDirs.Add((Join-Path $dynamoRevitData "$($v.Major).$($v.Minor)\packages"))
    # The user folder is keyed by the Dynamo for Revit version, which can differ from DynamoCore
    # (Revit 2026: "3.7", Revit 2027: "27.0").
    $ds = Join-Path $autodesk "Revit $year\AddIns\DynamoForRevit\Revit\DynamoRevitDS.dll"
    if (Test-Path $ds) {
        $d = [Version](Get-Item $ds).VersionInfo.FileVersion
        $packageDirs.Add((Join-Path $dynamoRevitData "$($d.Major).$($d.Minor)\packages"))
    }
}
# Folders Dynamo already created (also covers hosts we cannot map) and Dynamo Sandbox/CLI.
foreach ($base in $dynamoRevitData, $dynamoCoreData) {
    if (Test-Path $base) {
        Get-ChildItem $base -Directory | Where-Object { $_.Name -match '^\d+\.\d+$' } |
            ForEach-Object { $packageDirs.Add((Join-Path $_.FullName "packages")) }
    }
}
$packageDirs = $packageDirs | Sort-Object -Unique

# --- install the package ------------------------------------------------------------
foreach ($dir in $packageDirs) {
    $target = Join-Path $dir $packageName
    if (Test-Path $target) {
        $old = Get-Content (Join-Path $target "pkg.json") -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($old -and $old.version -and ([Version]$old.version).Major -lt 2) {
            $backup = Join-Path $backupRoot (Split-Path (Split-Path $dir -Parent) -Leaf)
            New-Item -ItemType Directory -Force $backup | Out-Null
            Move-Item $target $backup
            Write-Host "  moved DynamoAutomation $($old.version) to $backup"
        } else {
            Remove-Item $target -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item (Join-Path $packageRoot "*") $target -Recurse -Force
    Write-Host "Package  -> $target"
}
Write-Host "Done."
