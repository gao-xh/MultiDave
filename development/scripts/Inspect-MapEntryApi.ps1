[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'
# Generated interop signatures only. Does not execute selection/load/save APIs.
& (Join-Path $PSScriptRoot 'Inspect-GameApi.ps1') -GamePath $GamePath -ReportName 'map-entry-api.json' -TypeName @(
    'SceneContext', 'SceneMapLayerData', 'SceneMapLayerDataCache', 'SceneLoader',
    'DynamicIngameNodeLoader', 'IGPSetController', 'IGPSetInfo',
    'DR.Save.ISaveableInstanceData', 'DR.Save.SaveSystemPlayerDataManager'
)
