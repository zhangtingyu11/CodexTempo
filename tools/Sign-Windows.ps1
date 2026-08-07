[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter(Mandatory = $true)]
    [string]$PfxBase64,

    [Parameter(Mandatory = $true)]
    [string]$PfxPassword,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedFile = (Resolve-Path -LiteralPath $FilePath).Path
$signTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
    Sort-Object -Property FullName -Descending |
    Select-Object -First 1

if (-not $signTool) {
    throw "SignTool was not found in the Windows SDK."
}

if ([string]::IsNullOrWhiteSpace($PfxBase64) -or [string]::IsNullOrWhiteSpace($PfxPassword)) {
    throw "The PFX certificate or password GitHub secret is empty."
}

$temporaryPfx = Join-Path $env:RUNNER_TEMP ("codextempo-signing-{0}.pfx" -f [guid]::NewGuid())

try {
    [IO.File]::WriteAllBytes($temporaryPfx, [Convert]::FromBase64String($PfxBase64))

    & $signTool.FullName sign `
        /f $temporaryPfx `
        /p $PfxPassword `
        /fd SHA256 `
        /tr $TimestampUrl `
        /td SHA256 `
        /d "Codex Tempo" `
        /du "https://github.com/zhangtingyu11/CodexTempo" `
        $resolvedFile

    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryPfx) {
        Remove-Item -LiteralPath $temporaryPfx -Force
    }
}
