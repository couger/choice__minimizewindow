param([switch]$LogicOnly,[switch]$LayoutOnly,[string]$ExecutablePath = (Join-Path $PSScriptRoot 'bin\ChoiceMinimizeWindow.exe'))
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
if($LogicOnly -and $LayoutOnly) {throw 'Choose either LogicOnly or LayoutOnly'}
$argument = if ($LogicOnly) {'--logic-test'} elseif($LayoutOnly) {'--layout-test'} else {'--self-test'}
$reportName = if ($LogicOnly) {'logic-test.txt'} elseif($LayoutOnly) {'layout-test.txt'} else {'self-test.txt'}
$result = Start-Process -FilePath $ExecutablePath -ArgumentList $argument -PassThru
if (!$result.WaitForExit(180000)) {
    & taskkill.exe /PID $result.Id /T /F | Out-Null
    throw 'Tests exceeded 180 seconds; terminated the test process tree.'
}
$reportDirectory = Join-Path (Split-Path -Parent (Split-Path -Parent ([IO.Path]::GetFullPath($ExecutablePath)))) 'test-results'
Get-Content (Join-Path $reportDirectory $reportName)
if ($result.ExitCode -ne 0) { throw 'Integration tests failed' }
