param([string]$PublishPath, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputPath) { $OutputPath = Join-Path $projectRoot 'outputs' }
if (-not $PublishPath) { $PublishPath = Join-Path $projectRoot 'outputs/OrbLink-win-x64-v0.3.0' }
$PublishPath = (Resolve-Path -LiteralPath $PublishPath).Path
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory($OutputPath) | Out-Null
$binary = Join-Path $PublishPath 'OrbLink.exe'
if (([Diagnostics.FileVersionInfo]::GetVersionInfo($binary)).FileVersion -ne '0.3.0.0') { throw 'Expected OrbLink 0.3.0.0 binary.' }
function New-Archive([string]$Destination, [string]$BasePath, [string[]]$Files, [string]$Prefix) {
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($relative in ($Files | Sort-Object -Unique)) {
            $path = [IO.Path]::GetFullPath((Join-Path $BasePath $relative))
            if (-not $path.StartsWith($BasePath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe archive path.' }
            $entry = $archive.CreateEntry($Prefix + $relative.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($path); $target = $entry.Open()
            try { $input.CopyTo($target) } finally { $input.Dispose(); $target.Dispose() }
        }
    }
    finally { $archive.Dispose(); $stream.Dispose() }
}
$files = @('OrbLink.exe', 'LEIA-ME.md', 'LICENSE.txt', 'CHECKLIST-TESTES.md', 'THIRD-PARTY-NOTICES.md', 'docs/licenses/DOTNET-LICENSE.txt', 'docs/licenses/DOTNET-NOTICES.txt', 'docs/licenses/SQLITEPCLRAW-LICENSE.txt')
foreach ($file in $files) { if (-not (Test-Path -LiteralPath (Join-Path $PublishPath $file) -PathType Leaf)) { throw "Missing $file; run build.ps1." } }
$binaryZip = Join-Path $OutputPath 'OrbLink-win-x64-v0.3.0.zip'
New-Archive $binaryZip $PublishPath $files 'OrbLink-win-x64-v0.3.0/'
Push-Location $projectRoot
try {
    $gitRoot = & git -c "safe.directory=$projectRoot" rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot) -ne $projectRoot) { throw 'Initialize Git in this source root before packaging; do not use a parent repository.' }
    $sourceFiles = & git -c "safe.directory=$projectRoot" -c core.quotepath=false ls-files --cached --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'Git is required to enforce .gitignore.' }
    $publicFiles = @()
    foreach ($file in $sourceFiles) {
        & git -c "safe.directory=$projectRoot" check-ignore --no-index --quiet -- $file
        if ($LASTEXITCODE -eq 0) { continue }
        if ($LASTEXITCODE -ne 1) { throw 'git check-ignore failed.' }
        if ($file -match '(?i)(^|/)(bin|obj|Received|Outgoing|Temp|logs|outputs|work|\.git|\.planning)/|\.(db|pfx|p12|key|pem|log|zip|dmp|trx)$') { throw "Private/generated source candidate: $file" }
        if (Test-Path -LiteralPath $file -PathType Leaf) { $publicFiles += $file }
    }
    & (Join-Path $PSScriptRoot 'audit-source.ps1') -Files $publicFiles
    $sourceZip = Join-Path $OutputPath 'OrbLink-source-v0.3.0.zip'
    New-Archive $sourceZip $projectRoot $publicFiles 'OrbLink/'
}
finally { Pop-Location }
$hashes = @($binaryZip, $sourceZip, $binary) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
}
[IO.File]::WriteAllLines((Join-Path $OutputPath 'SHA256SUMS-v0.3.0.txt'), $hashes, [Text.UTF8Encoding]::new($false))
$hashes
