param(
    [string]$GameDir = "I:\SteamLibrary\steamapps\common\Solar Expanse",
    [switch]$IncludeSymbols
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "LogisticsMod\LogisticsMod.csproj"
$distDir = Join-Path $repoRoot "dist"

[xml]$project = Get-Content $projectPath
$version = $project.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = "0.0.0"
}

dotnet build $projectPath -c Release -t:Rebuild -p:GameDir="$GameDir" -p:DebugType=none -p:DebugSymbols=false

$stageName = "GhostFleet-LogisticsMod-$version"
$stageDir = Join-Path $distDir $stageName
$pluginDir = Join-Path $stageDir "BepInEx\plugins\logisticsmod"

if (Test-Path $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -LiteralPath (Join-Path $GameDir "BepInEx\plugins\logisticsmod\LogisticsMod.dll") -Destination $pluginDir

$pdbPath = Join-Path $GameDir "BepInEx\plugins\logisticsmod\LogisticsMod.pdb"
if (-not $IncludeSymbols -and (Test-Path $pdbPath)) {
    Remove-Item -LiteralPath $pdbPath -Force
}

if ($IncludeSymbols -and (Test-Path $pdbPath)) {
    Copy-Item -LiteralPath $pdbPath -Destination $pluginDir
}

Copy-Item -LiteralPath (Join-Path $repoRoot "README.md") -Destination $stageDir
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination $stageDir

$zipPath = Join-Path $distDir "$stageName.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath
if (-not (Test-Path $zipPath)) {
    throw "Package zip was not created: $zipPath"
}

Write-Host "Created $zipPath"
