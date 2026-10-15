param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path,
    [string]$SdkDirectory = (Join-Path $ProjectRoot '.utmp/ModSdk'),
    [string]$OutputDirectory = (Join-Path $ProjectRoot '.utmp/ExampleCombatMod')
)
$ErrorActionPreference = 'Stop'
$dotnetDirectory = Split-Path (Get-Command dotnet).Source
$sdk = Get-ChildItem (Join-Path $dotnetDirectory 'sdk') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } | Sort-Object Name -Descending | Select-Object -First 1
if (!(Test-Path (Join-Path $SdkDirectory 'sdk.json'))) { throw 'Export the Mod SDK first.' }
$binaryDirectory = Join-Path $OutputDirectory 'bin'
New-Item -ItemType Directory -Path $binaryDirectory -Force | Out-Null
$referenceFiles = @(Get-ChildItem (Join-Path $SdkDirectory 'Framework') -Filter '*.dll') + @(Get-ChildItem (Join-Path $SdkDirectory 'References') -Filter '*.dll')
$options = @('/nologo','/nostdlib+','/target:library',('/out:"' + (Join-Path $binaryDirectory 'ExampleCombatMod.dll') + '"')) +
    @($referenceFiles | ForEach-Object { '/reference:"' + $_.FullName + '"' }) + @('"' + (Join-Path $ProjectRoot 'Examples/Mods/ExampleCombatMod/CombatMod.cs') + '"')
$response = Join-Path $OutputDirectory 'compile.rsp'
$options | Set-Content -LiteralPath $response
& dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Example mod compilation failed.' }
Copy-Item -LiteralPath (Join-Path $ProjectRoot 'Examples/Mods/ExampleCombatMod/manifest.json') -Destination $OutputDirectory
Write-Output "Built mod pack: $OutputDirectory"
