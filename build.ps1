param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x C# compiler not found.' }
$dist = Join-Path $project 'dist'
$testBin = Join-Path $project 'tests\bin'
$toolBin = Join-Path $project 'tools\bin'
New-Item -ItemType Directory -Force -Path $dist,$testBin,$toolBin | Out-Null
$core = Join-Path $project 'src\TrainerCore.cs'
$stability = Join-Path $project 'src\Stability.cs'
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Web.Extensions.dll "/out:$(Join-Path $toolBin 'Diagnostics.exe')" $core $stability (Join-Path $project 'tools\Diagnostics.cs')
if ($LASTEXITCODE -ne 0) { throw 'Diagnostics compilation failed.' }
& $compiler /nologo /optimize+ /platform:x64 /target:exe /reference:System.Web.Extensions.dll "/out:$(Join-Path $toolBin 'RestartMonitor.exe')" $core $stability (Join-Path $project 'tools\RestartMonitor.cs')
if ($LASTEXITCODE -ne 0) { throw 'Restart monitor compilation failed.' }
if (Test-Path -LiteralPath (Join-Path $project 'src\Program.cs')) {
    & $compiler /nologo /optimize+ /platform:x64 /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32manifest:$(Join-Path $project 'src\app.manifest')" "/out:$(Join-Path $dist 'ETS2Trainer.exe')" $core $stability (Join-Path $project 'src\Program.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Trainer compilation failed.' }
}
if ($Test) {
    & $compiler /nologo /optimize+ /platform:x64 /target:exe "/out:$(Join-Path $testBin 'Trainer.Tests.exe')" $core $stability (Join-Path $project 'tests\TrainerTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $testBin 'Trainer.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
Write-Output 'Build succeeded.'
