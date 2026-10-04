[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'
# Read generated metadata only; this does not execute these game methods.
& (Join-Path $PSScriptRoot 'Inspect-GameApi.ps1') -GamePath $GamePath -ReportName 'world-api.json' -TypeName @(
    'InGameManager', 'DynamicIngameNodeLoader', 'IGPSetController', 'IGPSetInfo', 'IGPSetObject',
    'FishGroupsSelector', 'FishAllocator', 'DR.AI.FishAISystem', 'DR.AI.SpecialAttackerFishAISystem',
    'DR.AI.SABaseFishSystem', 'FishInteractionBody', 'FishBehaviorControlFlag',
    'Damageable', 'Damager', 'DamageResult', 'CatchableObject', 'CatchableByItem',
    'PickupInstanceItem', 'InstanceItem', 'IngameSaveDataManager'
)
