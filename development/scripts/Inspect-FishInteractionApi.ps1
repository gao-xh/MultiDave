[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'
# Generated metadata only. Never executes projectiles, damage, pickup or saves.
& (Join-Path $PSScriptRoot 'Inspect-GameApi.ps1') -GamePath $GamePath -ReportName 'fish-interaction-api.json' -TypeName @(
    'HarpoonProjectile', 'HarpoonWeaponHandler', 'ProjectileInfo', 'GunBullet',
    'CatchableObject', 'Damageable', 'Damager', 'AttackData', 'DefenseData',
    'DR.AI.FishAISystem', 'DR.AI.SABaseFishSystem', 'FishInteractionBody',
    'LootBox', 'SaveDataCaughtFishRouter', 'IngredientsStorage'
)
