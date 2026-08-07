[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$resolvedFile = (Resolve-Path -LiteralPath $FilePath).Path
$signature = Get-AuthenticodeSignature -LiteralPath $resolvedFile

if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "Invalid Authenticode signature on '$resolvedFile': $($signature.Status) - $($signature.StatusMessage)"
}

Write-Host "Valid Authenticode signature: $($signature.SignerCertificate.Subject)"
