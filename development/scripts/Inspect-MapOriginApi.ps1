[CmdletBinding()]
param([string]$GamePath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$ErrorActionPreference = 'Stop'
$gameRoot = Resolve-DaveGamePath $GamePath
Add-Type -Path (Join-Path $gameRoot 'BepInEx\core\Mono.Cecil.dll')
function Get-OriginTypes($types) {
    foreach ($type in $types) {
        $type
        if ($type.NestedTypes.Count) { Get-OriginTypes $type.NestedTypes }
    }
}
function Get-OriginGetterFacts($method) {
    if ($null -eq $method -or !$method.HasBody) { return $null }
    $calls = @($method.Body.Instructions | Where-Object { $_.OpCode.FlowControl.ToString() -eq 'Call' } |
        ForEach-Object { $_.Operand.FullName })
    $fields = @($method.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } |
        ForEach-Object { $_.Operand.FullName })
    [pscustomobject]@{
        NativeFieldProxy = [bool](@($fields | Where-Object { $_ -match 'NativeFieldInfoPtr_' }).Count)
        RuntimeInvoke = [bool](@($calls | Where-Object { $_ -match 'il2cpp_runtime_invoke' }).Count)
    }
}
$queries = @(
    @{ File = 'Assembly-CSharp.dll'; Names = @('SceneLoader','InGameManager','SceneContext','IGPSetController','IGPSetInfo'); Nested = $true },
    @{ File = 'Unity.Addressables.dll'; Names = @('UnityEngine.AddressableAssets.Addressables'); Nested = $false },
    @{ File = 'Unity.ResourceManager.dll'; Names = @('UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle','UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle`1','UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationBase`1','UnityEngine.ResourceManagement.ResourceProviders.SceneInstance'); Nested = $false },
    @{ File = 'UnityEngine.CoreModule.dll'; Names = @('UnityEngine.SceneManagement.Scene'); Nested = $false }
)
$modules = @()
foreach ($query in $queries) {
    $path = Join-Path $gameRoot ('BepInEx\interop\' + $query.File)
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    try {
        $selected = @(Get-OriginTypes $assembly.MainModule.Types | Where-Object {
            $_.FullName -in $query.Names -or ($query.Nested -and $_.DeclaringType.FullName -in $query.Names)
        })
        $types = @($selected | ForEach-Object {
            [pscustomobject]@{
                Name = $_.FullName
                BaseType = $_.BaseType.FullName
                Fields = @($_.Fields | Where-Object { $_.Name -notmatch '^Native(Field|Method)InfoPtr_' } |
                    ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.FieldType.FullName; Static = $_.IsStatic } })
                Properties = @($_.Properties | ForEach-Object {
                    [pscustomobject]@{ Name = $_.Name; Type = $_.PropertyType.FullName; Getter = Get-OriginGetterFacts $_.GetMethod }
                })
                Methods = @($_.Methods | Where-Object { !$_.IsGetter -and !$_.IsSetter -and !$_.IsConstructor } |
                    ForEach-Object { [pscustomobject]@{ Signature = $_.FullName; Static = $_.IsStatic; Parameters = @($_.Parameters | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Type = $_.ParameterType.FullName } }) } })
            }
        })
        $modules += [pscustomobject]@{ AssemblyName = $query.File; SHA256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash; Types = $types }
    }
    finally { $assembly.Dispose() }
}
$outputPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.local\analysis\map-origin-hook-api.json'))
New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
[pscustomobject]@{
    GeneratedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    Evidence = 'Generated interop metadata and field-proxy classification only; no game execution, hook ABI, original control flow, saves, deployment, or runtime origin proven.'
    Modules = $modules
} | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Output "Local report: $outputPath"
