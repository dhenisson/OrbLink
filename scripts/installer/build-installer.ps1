param([Parameter(Mandatory=$true)][string]$PublishPath)
$ErrorActionPreference = 'Stop'
$compiler = Get-Command ISCC.exe -ErrorAction Stop
$publish = (Resolve-Path -LiteralPath $PublishPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $publish 'OrbLink.exe'))) { throw 'Run build.ps1 first.' }
& $compiler.Source "/DSourceDir=$publish" (Join-Path $PSScriptRoot 'OrbLink.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Warning 'Compilation is not acceptance testing: verify clean install, update, startup and uninstall/data preservation before release.'
