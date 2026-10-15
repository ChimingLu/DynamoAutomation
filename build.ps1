<#
  Builds the DynamoAutomation package into .\dist\DynamoAutomation

  dist\DynamoAutomation\
    pkg.json
    bin\DynamoAutomation.dll (+ .xml)     zero-touch nodes; one build for every Dynamo 2.3 - 4.x host
    extra\samples\*.dyn                   master + worker sample graphs
    extra\install.ps1, install.cmd, uninstall.ps1
#>
param(
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist\DynamoAutomation"
$version = "2.0.0"

dotnet build "$root\src\DynamoAutomation\DynamoAutomation.csproj" -c $Configuration -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Node library build failed" }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force "$dist\bin", "$dist\extra\samples" | Out-Null

$lib = "$root\src\DynamoAutomation\bin\$Configuration\netstandard2.0"
Copy-Item "$lib\DynamoAutomation.dll", "$lib\DynamoAutomation.xml" "$dist\bin"
Copy-Item "$root\installer\install.ps1", "$root\installer\uninstall.ps1", "$root\installer\install.cmd" "$dist\extra"
python -I "$root\tools\make_samples.py" "$dist\extra\samples"
if ($LASTEXITCODE -ne 0) { throw "Sample generation failed" }

$pkg = [ordered]@{
    license           = "MIT"
    file_hash         = $null
    name              = "DynamoAutomation"
    version           = $version
    description       = "Batch-run one Dynamo graph on many Revit models (Revit 2020-2027 / Dynamo 2.3-4.x). Pure Dynamo nodes that drive Revit through journal files; nothing is installed into Revit."
    group             = ""
    keywords          = @("batch", "automation", "journal", "revit")
    dependencies      = @()
    host_dependencies = @()
    contents          = "Batch.RunGraph, Batch.FindModels, Job.Finish, Job.*, Journal.*, Report.*, Config.*, TextMatch.*, RevitInstall.*, ExternalProcess.Run"
    engine_version    = "2.3.0"
    engine            = "dynamo"
    engine_metadata   = ""
    site_url          = "https://github.com/ChimingLu/DynamoAutomation"
    repository_url    = "https://github.com/ChimingLu/DynamoAutomation"
    contains_binaries = $true
    node_libraries    = @("DynamoAutomation, Version=2.0.0.0, Culture=neutral, PublicKeyToken=null")
}
$pkg | ConvertTo-Json -Depth 4 | Set-Content "$dist\pkg.json" -Encoding UTF8

Write-Host "Package ready: $dist"
