param([string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path)
$ErrorActionPreference = 'Stop'
# EN: Compile actual Eve sources with test-only Unity substitutes; no NuGet restore required.
# VI: Biên dịch mã Eve thật với Unity thay thế dành cho kiểm thử; không cần NuGet restore.
$dotnetDirectory = Split-Path (Get-Command dotnet).Source
$sdk = Get-ChildItem (Join-Path $dotnetDirectory 'sdk') -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'Roslyn/bincore/csc.dll') } | Sort-Object Name -Descending | Select-Object -First 1
$referencePack = Get-ChildItem (Join-Path $dotnetDirectory 'packs/Microsoft.NETCore.App.Ref') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$version = [version]$referencePack.Name
$tfm = "net$($version.Major).$($version.Minor)"
$referencePath = Join-Path $referencePack.FullName "ref/$tfm"
$jsonDll = Get-ChildItem (Join-Path $ProjectRoot 'Library/PackageCache') -Directory -Filter 'com.unity.nuget.newtonsoft-json@*' | ForEach-Object { Join-Path $_.FullName 'Runtime/Newtonsoft.Json.dll' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (!$jsonDll) { throw 'Open the Unity project once to restore Newtonsoft.Json.' }
$outputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('EveSmokeBuild-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outputDirectory | Out-Null
$outputDll = Join-Path $outputDirectory 'EveSmoke.dll'
$references = @(Get-ChildItem -LiteralPath $referencePath -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' })
$sourceDirectories = @('Assets/EveEngine','Assets/Core','Assets/Game') | ForEach-Object { Join-Path $ProjectRoot $_ }
# PhysX-backed hitboxes are covered by PNHitboxEditorChecks in real Unity, not simulated stubs.
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName) + @(Get-ChildItem $sourceDirectories -Recurse -Filter '*.cs' | Where-Object { $_.Name -notlike '*AssemblyInfo.cs' -and $_.FullName -notmatch '[\\/]Hitbox[\\/]' } | ForEach-Object FullName)
$sources += Join-Path $ProjectRoot 'Assets/Tests/EveEngine/EveLifecycleModelChecks.cs'
$sources += Join-Path $ProjectRoot 'Assets/Tests/EveEngine/EveLifecycleCleanupChecks.cs'
$sources += Join-Path $ProjectRoot 'Assets/Tests/Core/PNPolicyFixturePlan.cs'
$options = @('/nologo','/target:exe','/nowarn:0649',('/out:"' + $outputDll + '"'),('/reference:"' + $jsonDll + '"')) + $references + @($sources | ForEach-Object { '"' + $_ + '"' })
$response = Join-Path $outputDirectory 'EveCompile.rsp'
$options | Set-Content -LiteralPath $response
& dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') ('@' + $response)
if ($LASTEXITCODE -ne 0) { throw 'Eve smoke compilation failed.' }
Copy-Item -LiteralPath $jsonDll -Destination $outputDirectory
@{ runtimeOptions = @{ tfm = $tfm; framework = @{ name = 'Microsoft.NETCore.App'; version = "$($version.Major).$($version.Minor).0" } } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputDirectory 'EveSmoke.runtimeconfig.json')
& dotnet $outputDll $ProjectRoot
if ($LASTEXITCODE -ne 0) { throw 'Eve smoke checks failed.' }

# EN: Compile independent mod/library DLLs and test the loader in a fresh process.
# VI: Biên dịch DLL mod/thư viện độc lập và kiểm tra loader trong tiến trình mới.
$fixtureOutput = Join-Path $outputDirectory 'ModFixtures'
New-Item -ItemType Directory -Path (Join-Path $fixtureOutput 'Conflict') -Force | Out-Null
function Build-SmokeLibrary($taskSource, $taskDestination, $taskExtraReferences) {
    $taskOptions = @('/nologo','/target:library',('/out:"' + $taskDestination + '"')) + $references + $taskExtraReferences + @('"' + $taskSource + '"')
    $taskResponse = $taskDestination + '.rsp'
    $taskOptions | Set-Content -LiteralPath $taskResponse
    & dotnet (Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll') ('@' + $taskResponse)
    if ($LASTEXITCODE -ne 0) { throw 'Mod fixture compilation failed.' }
}
$dependencySource = Join-Path $PSScriptRoot 'ModFixtures/SmokeDependency.cs'
$dependencyDll = Join-Path $fixtureOutput 'SmokeDependency.dll'
Build-SmokeLibrary $dependencySource $dependencyDll @()
$conflictSource = Join-Path $outputDirectory 'ConflictDependency.cs'
([IO.File]::ReadAllText($dependencySource).Replace('7f','8f')) | Set-Content -LiteralPath $conflictSource
Build-SmokeLibrary $conflictSource (Join-Path $fixtureOutput 'Conflict/SmokeDependency.dll') @()
Build-SmokeLibrary (Join-Path $PSScriptRoot 'ModFixtures/SmokeCodeMod.cs') (Join-Path $fixtureOutput 'SmokeCodeMod.dll') @('/reference:"' + $outputDll + '"','/reference:"' + $dependencyDll + '"')
& dotnet $outputDll $ProjectRoot '--dll-tests' $fixtureOutput
if ($LASTEXITCODE -ne 0) { throw 'Real DLL mod checks failed.' }
