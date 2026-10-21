param([string]$OutputPath, [switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputPath) { $OutputPath = Join-Path $projectRoot 'outputs/OrbLink-win-x64-v0.3.0' }
function Invoke-DotNet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed ($LASTEXITCODE)." }
}
Push-Location $projectRoot
try {
    Invoke-DotNet -Arguments @('restore', 'OrbLink.slnx')
    Invoke-DotNet -Arguments @('build', 'OrbLink.slnx', '-c', 'Release', '--no-restore', '--no-incremental')
    Invoke-DotNet -Arguments @('test', 'OrbLink.slnx', '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=beta.trx', '--results-directory', 'outputs/validacao-v0.3.0')
    if (-not $SkipPublish) {
        # Serial: publish changes intermediates and must not race the solution build/tests.
        Invoke-DotNet -Arguments @('publish', 'src/AMS.Transfer.App/AMS.Transfer.App.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-o', $OutputPath,
            '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false')
        Copy-Item -LiteralPath 'docs/GETTING-STARTED.md' -Destination (Join-Path $OutputPath 'LEIA-ME.md') -Force
        Copy-Item -LiteralPath 'LICENSE' -Destination (Join-Path $OutputPath 'LICENSE.txt') -Force
        Copy-Item -LiteralPath 'CHECKLIST-TESTES.md' -Destination $OutputPath -Force
        Copy-Item -LiteralPath 'THIRD-PARTY-NOTICES.md' -Destination $OutputPath -Force
        New-Item -ItemType Directory -Path (Join-Path $OutputPath 'docs/licenses') -Force | Out-Null
        Copy-Item -LiteralPath 'docs/licenses/DOTNET-LICENSE.txt', 'docs/licenses/DOTNET-NOTICES.txt', 'docs/licenses/SQLITEPCLRAW-LICENSE.txt' -Destination (Join-Path $OutputPath 'docs/licenses') -Force
    }
}
finally { Pop-Location }
