param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$UnityEditorPath,
    [string]$OutputDirectory = (Join-Path $ProjectRoot '.utmp/ModSdk')
)
$ErrorActionPreference = 'Stop'
# EN: Export compile-time references only; never bundle these API copies inside a mod.
# VI: Chỉ xuất reference lúc biên dịch; không đóng kèm các bản API này vào mod.
if (!$UnityEditorPath) {
    $UnityEditorPath = Get-Process Unity -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path -Unique | Select-Object -First 1
}
if (!$UnityEditorPath) { throw 'Supply -UnityEditorPath pointing to the Unity Editor executable.' }
$editorData = Join-Path (Split-Path $UnityEditorPath) 'Data'
$referenceDirectory = Join-Path $OutputDirectory 'References'
New-Item -ItemType Directory -Path $referenceDirectory -Force | Out-Null
$names = @('EveEngine','ProjectN.Core','ProjectN.Game','ProjectN.Input','Unity.InputSystem','Unity.TextMeshPro','UnityEngine.UI','Unity.RenderPipelines.Core.Runtime','Unity.RenderPipelines.Universal.Runtime')
foreach ($name in $names) {
    $source = Join-Path $ProjectRoot ('Library/ScriptAssemblies/' + $name + '.dll')
    if (!(Test-Path -LiteralPath $source)) { throw "Missing $source. Compile the Unity project first." }
    Copy-Item -LiteralPath $source -Destination $referenceDirectory
}
foreach ($name in @('UnityEngine.CoreModule','UnityEngine.PhysicsModule','UnityEngine.Physics2DModule','UnityEngine.InputLegacyModule','UnityEngine.TextRenderingModule','UnityEngine.UIModule','UnityEngine.UIElementsModule')) {
    Copy-Item -LiteralPath (Join-Path $editorData ('Managed/UnityEngine/' + $name + '.dll')) -Destination $referenceDirectory
}
$jsonDll = Get-ChildItem (Join-Path $ProjectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' |
    ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (!$jsonDll) { throw 'Missing Unity Newtonsoft.Json dependency.' }
Copy-Item -LiteralPath $jsonDll -Destination $referenceDirectory
$frameworkDirectory = Join-Path $OutputDirectory 'Framework'
New-Item -ItemType Directory -Path $frameworkDirectory -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $editorData 'NetStandard/ref/2.1.0') -Filter '*.dll' |
    ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $frameworkDirectory }
@{ apiVersion = '6'; platform = 'Windows'; backend = 'Mono'; unityVersion = (Get-Content (Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt') -First 1) } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'sdk.json')
Write-Output "Exported compile-time SDK: $OutputDirectory"

