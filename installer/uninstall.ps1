<#
.SYNOPSIS
  Removes the DynamoAutomation 2.x package and Revit add-in for Revit 2020-2027.
  Backups of older 1.x packages in %AppData%\Dynamo\DynamoAutomation_backup are left untouched.
#>
$ErrorActionPreference = "Stop"
foreach ($base in (Join-Path $env:APPDATA "Dynamo\Dynamo Revit"), (Join-Path $env:APPDATA "Dynamo\Dynamo Core")) {
    if (-not (Test-Path $base)) { continue }
    Get-ChildItem $base -Directory | ForEach-Object {
        $pkg = Join-Path $_.FullName "packages\DynamoAutomation"
        if (Test-Path (Join-Path $pkg "pkg.json")) {
            $info = Get-Content (Join-Path $pkg "pkg.json") -Raw | ConvertFrom-Json
            if (([Version]$info.version).Major -ge 2) {
                Remove-Item $pkg -Recurse -Force
                Write-Host "Removed package $pkg"
            }
        }
    }
}
foreach ($year in 2020..2027) {
    $addinsDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $manifest = Join-Path $addinsDir "DynamoAutomation.addin"
    if (Test-Path $manifest) {
        Remove-Item $manifest -Force
        Remove-Item (Join-Path $addinsDir "DynamoAutomation") -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Removed add-in for Revit $year"
    }
}
Write-Host "Done."
