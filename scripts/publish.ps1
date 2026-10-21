param(
    [string]$OutputPath
)

# Compatibility entry point; publish now always follows the build/test gate.
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $OutputPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
