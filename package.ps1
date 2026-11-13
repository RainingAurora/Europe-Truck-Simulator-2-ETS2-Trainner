$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$project = $PSScriptRoot
$dist = Join-Path $project 'dist'
$release = Join-Path $project 'release'
New-Item -ItemType Directory -Force -Path $release | Out-Null
foreach ($name in @('README.md','TEST_RESULTS.md')) {
    Copy-Item -LiteralPath (Join-Path $project $name) -Destination (Join-Path $dist $name)
}
$binary = Join-Path $dist 'ETS2Trainer.exe'
$hash = (Get-FileHash -LiteralPath $binary -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Value "$hash  ETS2Trainer.exe" -Encoding ascii

function Write-Zip([string]$Path, [string]$Root, [string[]]$Files) {
    $stream = [System.IO.File]::Create($Path)
    $zip = New-Object System.IO.Compression.ZipArchive($stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in $Files) {
            $source = Join-Path $Root $relative
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing package file: $source" }
            $entry = $zip.CreateEntry($relative.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new([DateTime]::new(2000, 1, 1), [TimeSpan]::Zero)
            $entryStream = $entry.Open()
            try {
                $sourceStream = [System.IO.File]::OpenRead($source)
                try { $sourceStream.CopyTo($entryStream) } finally { $sourceStream.Dispose() }
            } finally { $entryStream.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
Write-Zip (Join-Path $release 'ETS2Trainer-1.62.1.0-win64.zip') $dist @('ETS2Trainer.exe','README.md','TEST_RESULTS.md','SHA256SUMS.txt')
$sources = @('README.md','IMPLEMENTATION.md','TEST_RESULTS.md','build.ps1','package.ps1','.gitignore','src\TrainerCore.cs','src\Stability.cs','src\Program.cs','src\app.manifest','tests\TrainerTests.cs','tools\Diagnostics.cs','tools\RestartMonitor.cs')
Write-Zip (Join-Path $release 'ETS2Trainer-source.zip') $project $sources
Get-ChildItem -LiteralPath $release -File | Select-Object Name,Length
