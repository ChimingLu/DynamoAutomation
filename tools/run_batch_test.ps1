<#
  Runs Batch.RunGraph outside Dynamo (the same code path the node uses) for a quick end-to-end test.
    powershell -File tools\run_batch_test.ps1 -RevitVersion 2021 -ModelDir C:\temp\models -OutputDir C:\temp\out
#>
param(
    [Parameter(Mandatory)][int]$RevitVersion,
    [Parameter(Mandatory)][string]$ModelDir,
    [Parameter(Mandatory)][string]$OutputDir,
    [string]$Worker,
    [string]$SaveMode = "none",
    [int]$MaxParallel = 1,
    [int]$TimeoutMinutes = 15
)
$ErrorActionPreference = "Stop"
# $PSScriptRoot is empty inside param defaults on Windows PowerShell 5.1
if (-not $Worker) { $Worker = "$PSScriptRoot\..\dist\DynamoAutomation\extra\samples\CountWalls_Worker.dyn" }
# Load a copy so the dist DLL stays unlocked for the next build.
$copy = Join-Path $env:TEMP ("DynamoAutomation_test_" + [guid]::NewGuid().ToString("N") + ".dll")
Copy-Item "$PSScriptRoot\..\dist\DynamoAutomation\bin\DynamoAutomation.dll" $copy
Add-Type -Path $copy
$models = [DynamoAutomation.Batch]::FindModels($ModelDir, $false, $false)
$r = [DynamoAutomation.Batch]::RunGraph($models, (Resolve-Path $Worker).Path, $OutputDir, "$RevitVersion",
    $SaveMode, $false, $MaxParallel, $TimeoutMinutes, $null, $null, $true)
"status   : " + ($r["status"] -join ", ")
"messages : " + ($r["messages"] -join " | ")
"seconds  : " + $r["durationSeconds"]
"summary  : " + $r["summaryFile"]
