param([switch]$LogicOnly,[string]$ExecutablePath = (Join-Path $PSScriptRoot 'bin\ChoiceMinimizeWindow.exe'))
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$ExecutablePath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$argument = if ($LogicOnly) {'--logic-test'} else {'--self-test'}
$reportName = if ($LogicOnly) {'logic-test.txt'} else {'self-test.txt'}
$result = Start-Process -FilePath $ExecutablePath -ArgumentList $argument -PassThru
if (!$result.WaitForExit(120000)) {
    & taskkill.exe /PID $result.Id /T /F | Out-Null
    throw 'Tests exceeded 120 seconds; terminated the test process tree.'
}
$reportDirectory = Join-Path (Split-Path -Parent (Split-Path -Parent ([IO.Path]::GetFullPath($ExecutablePath)))) 'test-results'
Get-Content (Join-Path $reportDirectory $reportName)
if ($result.ExitCode -ne 0) { throw 'Integration tests failed' }
