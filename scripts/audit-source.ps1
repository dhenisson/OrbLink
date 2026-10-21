param([string[]]$Files)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $projectRoot
try {
    if (-not $Files) {
        $Files = & git -c "safe.directory=$projectRoot" -c core.quotepath=false ls-files --cached --others --exclude-standard
        if ($LASTEXITCODE -ne 0) { throw 'Source audit requires Git.' }
    }
    $failures = @()
    foreach ($file in $Files) {
        & git -c "safe.directory=$projectRoot" check-ignore --no-index --quiet -- $file
        if ($LASTEXITCODE -eq 0) { continue }
        if ($LASTEXITCODE -ne 1) { throw 'git check-ignore failed.' }
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
        if ($file -match '(?i)\.(pfx|p12|key|pem|db|db-wal|db-shm|sqlite|dmp|log|zip|exe|dll|pdb|mdb)$') { $failures += "$file (private/generated extension)"; continue }
        if ($file -match '(?i)\.(png|ico|jpg|jpeg|gif)$') { continue }
        $content = [IO.File]::ReadAllText((Join-Path $projectRoot $file))
        # Report filename/category only, never the matched credential.
        if ($content -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[0-9A-Z]{16}|sk-(?:proj-)?[A-Za-z0-9_-]{32,}') { $failures += "$file (credential signature)" }
    }
    if ($failures.Count) { throw "Source audit blocked packaging:`n$($failures -join [Environment]::NewLine)" }
    Write-Host "Source audit: $($Files.Count) candidates checked; no private artifacts or recognized credential signatures. Human review remains required."
}
finally { Pop-Location }
# git check-ignore returns 1 for a non-ignored file; that is expected, not an audit failure.
# Reset the native command exit status so pwsh-based CI reports success after a clean audit.
$global:LASTEXITCODE = 0
