param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$SdkDirectory = (Join-Path $ProjectRoot '.utmp/ModSdk'),
    [string]$OutputDirectory = (Join-Path $ProjectRoot '.utmp/NativeModChecks/Mods/native_probe')
)
$ErrorActionPreference = 'Stop'
$dotnetDirectory = Split-Path (Get-Command dotnet).Source
$sdk = Get-ChildItem (Join-Path $dotnetDirectory 'sdk') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } | Sort-Object Name -Descending | Select-Object -First 1
$binaryDirectory = Join-Path $OutputDirectory 'bin'
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
$referenceFiles = @(Get-ChildItem (Join-Path $SdkDirectory 'Framework') -Filter '*.dll') + @(Get-ChildItem (Join-Path $SdkDirectory 'References') -Filter '*.dll')
$references = @($referenceFiles | ForEach-Object { '/reference:"' + $_.FullName + '"' })
foreach ($name in @('SmokeDependency','SmokeCodeMod')) {
    $options = @('/nologo','/nostdlib+','/target:library',('/out:"' + (Join-Path $binaryDirectory ($name + '.dll')) + '"')) + $references
    if ($name -eq 'SmokeCodeMod') { $options += '/reference:"' + (Join-Path $binaryDirectory 'SmokeDependency.dll') + '"' }
    $options += '"' + (Join-Path $PSScriptRoot ('ModFixtures/' + $name + '.cs')) + '"'
    $response = Join-Path $OutputDirectory ($name + '.rsp')
    $options | Set-Content -LiteralPath $response
    & dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') ('@' + $response)
    if ($LASTEXITCODE -ne 0) { throw 'Native fixture compilation failed.' }
}
@{
    schemaVersion = 1; namespace = 'native_probe'; version = '1.0.0'; dependencies = @{ project_n = '1.0.0' }
    code = @{ apiVersion = '6'; assembly = 'bin/SmokeCodeMod.dll'; entryPoint = 'SmokeCodeMod'; libraries = @('bin/SmokeDependency.dll') }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'manifest.json')
Write-Output "Built native DLL fixture: $OutputDirectory"
