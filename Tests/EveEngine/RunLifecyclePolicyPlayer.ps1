param(
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateSet('validate','baseline','micro','screen','full','endurance','combined','presentation','popup')][string]$Mode = 'screen',
    [ValidateRange(0,3)][int]$Peers = 3,
    [ValidateSet('all','wave32','wave64','wave128matched','wave128','overload256','player60','one100','normal20','normal6')][string]$Case = 'all',
    [ValidateSet('all','stable','timed','mixed','natural')][string]$Pattern = 'all',
    [int]$Seed = 20261012,
    [switch]$Countdown,
    [int]$Aoe = 0,
    [int]$AoeDepth = 2,
    [double]$AoePeriod = 2,
    [bool]$HotPaths = $true,
    [switch]$Stagger,
    [bool]$Popups = $true,
    [ValidateRange(0,3600)][double]$Seconds = 0,
    [ValidateRange(0,240)][int]$FrameCap = 0,
    [switch]$Events,
    [switch]$AllocationTrace,
    [switch]$Development,
    [switch]$PopupDiagnostic,
    [switch]$Camera,
    [ValidateSet('low','diverse','repeat')][string]$PopupDamage = 'low',
    [string]$PlayerExecutable
)
$ErrorActionPreference = 'Stop'
if ($AllocationTrace -and !$Development) { throw 'AllocationTrace requires Development.' }
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ($Output.StartsWith((Join-Path $workspace '.utmp\Step3b'), [StringComparison]::OrdinalIgnoreCase) -and
    (Test-Path -LiteralPath (Join-Path $workspace '.utmp\Step3b\PauseBeforeDiagnostic.request'))) {
    throw 'Step3b paused at a case boundary for validator correction; no running Player was interrupted.'
}
$executable = Join-Path $workspace $(if ($Development) { '.utmp\LifecyclePolicyPlayer\Development\LifecyclePolicy.exe' } else { '.utmp\LifecyclePolicyPlayer\Build\LifecyclePolicy.exe' })
if ($PlayerExecutable) { $executable = (Resolve-Path -LiteralPath $PlayerExecutable).Path }
if (!(Test-Path -LiteralPath $executable)) { throw 'Build PNLifecyclePolicyPlayerBuild first.' }
$resolvedOutput = [System.IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath (Join-Path $resolvedOutput 'Samples.jsonl')) { throw 'Use a fresh output directory; raw samples must not be mixed.' }
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
# Quote paths for the Windows process command line, rather than JSON/shell interpolation.
function Quote-Argument([string]$value) {
    if ($value.Contains('"')) { throw 'Argument containing a quote is unsupported.' }
    return '"' + $value + '"'
}
$arguments = @('-batchmode','-pn-lifecycle-policy','-pn-output',(Quote-Argument $resolvedOutput),'-pn-mode',$Mode,
    '-pn-peers',"$Peers",'-pn-case',$Case,'-pn-pattern',$Pattern,'-screen-width','1280','-screen-height','720',
    '-pn-hotpaths',$(if ($HotPaths) { '1' } else { '0' }),
    '-pn-stagger',$(if ($Stagger) { '1' } else { '0' }),
    '-pn-popups',$(if ($Popups) { '1' } else { '0' }),'-pn-seconds',$Seconds.ToString([Globalization.CultureInfo]::InvariantCulture),
    '-pn-cap',"$FrameCap",'-pn-seed',"$Seed",'-pn-countdown',$(if ($Countdown) { '1' } else { '0' }),'-pn-aoe',"$Aoe",'-pn-aoe-depth',"$AoeDepth",'-pn-aoe-period',$AoePeriod.ToString([Globalization.CultureInfo]::InvariantCulture),'-pn-events',$(if ($Events) { '1' } else { '0' }),
    '-pn-allocation-trace',$(if ($AllocationTrace) { '1' } else { '0' }),
    '-logFile',(Quote-Argument (Join-Path $resolvedOutput 'Player.log')))
$arguments += @('-pn-popup-diagnostic',$(if($PopupDiagnostic){'1'}else{'0'}),'-pn-popup-damage',$PopupDamage,'-pn-camera',$(if($Camera){'1'}else{'0'}))
$process = Start-Process -FilePath $executable -WorkingDirectory $workspace -WindowStyle Hidden -ArgumentList $arguments -PassThru
Write-Output "Lifecycle policy Player PID=$($process.Id); mode=$Mode; peers=$Peers; output=$resolvedOutput"
$process.WaitForExit()
if (!(Test-Path -LiteralPath (Join-Path $resolvedOutput 'Result.json'))) { throw "Player did not produce Result.json; exit=$($process.ExitCode)" }
$result = Get-Content -LiteralPath (Join-Path $resolvedOutput 'Result.json') -Raw | ConvertFrom-Json
if (!$result.success) { throw "Player failed; inspect $resolvedOutput\Failure.txt and Player.log" }
$sampleFile = Join-Path $resolvedOutput 'Samples.jsonl'
if (!(Test-Path -LiteralPath $sampleFile) -or (Get-Item -LiteralPath $sampleFile).Length -eq 0) {
    throw 'Player produced no samples: this case/pattern is not supported by the selected mode; empty runs are not a valid PASS.'
}
$result | ConvertTo-Json -Compress
