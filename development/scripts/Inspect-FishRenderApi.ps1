[CmdletBinding()]
param([string]$GamePath)
$ErrorActionPreference = 'Stop'
# Read generated signatures only. No game methods or lifecycle hooks execute.
& (Join-Path $PSScriptRoot 'Inspect-GameApi.ps1') -GamePath $GamePath -ReportName 'fish-render-api.json' -TypeName @(
    'DR.AI.FishSpecData', 'DR.FishInfoData', 'DR.SpineAnimation.SpineAnimator',
    'DR.AI.FishAnimationSMBController', 'FishBehaviorControlFlag',
    'DR.AI.FishAISystem', 'DR.AI.SABaseFishSystem'
)
& (Join-Path $PSScriptRoot 'Inspect-GameApi.ps1') -GamePath $GamePath -AssemblyName 'spine-unity.dll' -ReportName 'spine-render-api.json' -TypeName @(
    'Spine.Unity.SkeletonAnimation', 'Spine.Unity.SkeletonRenderer',
    'Spine.Unity.SkeletonMecanim', 'Spine.Unity.SkeletonDataAsset',
    'Spine.AnimationState', 'Spine.TrackEntry', 'Spine.Skeleton',
    'Spine.Animation', 'Spine.Skin'
)
