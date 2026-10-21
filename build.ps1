param([string]$OutputDirectory = 'bin')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) {[IO.Path]::GetFullPath($OutputDirectory)} else {[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $OutputDirectory))}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$executable = Join-Path $outputPath 'ChoiceMinimizeWindow.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$executable" /win32manifest:app.manifest /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src\*.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& icacls.exe $executable /setintegritylevel M | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cannot set executable integrity level' }
Write-Host "Built $executable"
