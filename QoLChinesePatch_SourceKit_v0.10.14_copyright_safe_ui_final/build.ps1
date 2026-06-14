param(
  [string]$GameDir = "D:\steam\steamapps\common\Casualties Unknown Demo"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $GameDir)) {
  Write-Error "GameDir not found: $GameDir"
  exit 1
}

$required = @(
  "$GameDir\BepInEx\core\BepInEx.dll",
  "$GameDir\BepInEx\core\0Harmony.dll",
  "$GameDir\CasualtiesUnknown_Data\Managed\UnityEngine.dll",
  "$GameDir\CasualtiesUnknown_Data\Managed\UnityEngine.CoreModule.dll",
  "$GameDir\CasualtiesUnknown_Data\Managed\UnityEngine.IMGUIModule.dll",
  "$GameDir\CasualtiesUnknown_Data\Managed\UnityEngine.UIModule.dll",
  "$GameDir\CasualtiesUnknown_Data\Managed\UnityEngine.UI.dll",
  "$GameDir\CasualtiesUnknown_Data\Managed\UnityEngine.InputLegacyModule.dll"
)

foreach ($file in $required) {
  if (-not (Test-Path $file)) {
    Write-Error "Required reference not found: $file"
    exit 1
  }
}

dotnet build .\src\QoLChinesePatchBootstrap\QoLChinesePatchBootstrap.csproj -c Release -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build .\src\QoLChinesePatch\QoLChinesePatch.csproj -c Release -p:GameDir="$GameDir"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$bootstrapDll = ".\src\QoLChinesePatchBootstrap\bin\Release\QoLChinesePatch.Bootstrap.dll"
$mainDll = ".\src\QoLChinesePatch\bin\Release\QoLChinesePatch.dll"
if (-not (Test-Path $bootstrapDll)) {
  Write-Error "Build finished but bootstrap DLL not found: $bootstrapDll"
  exit 1
}
if (-not (Test-Path $mainDll)) {
  Write-Error "Build finished but main DLL not found: $mainDll"
  exit 1
}

if (Test-Path .\release\QoLChinesePatch) { Remove-Item .\release\QoLChinesePatch -Recurse -Force }
New-Item -Force -ItemType Directory .\release\QoLChinesePatch | Out-Null
Copy-Item $bootstrapDll .\release\QoLChinesePatch\QoLChinesePatch.Bootstrap.dll -Force
Copy-Item $mainDll .\release\QoLChinesePatch\QoLChinesePatch.dll -Force
Copy-Item .\translations\*.json .\release\QoLChinesePatch\ -Force
Compress-Archive -Path .\release\QoLChinesePatch -DestinationPath .\QoLChinesePatch_release.zip -Force
Write-Host "Built .\QoLChinesePatch_release.zip"
Write-Host "Install both DLLs: QoLChinesePatch.Bootstrap.dll and QoLChinesePatch.dll"
