# Windows commercial code signing

The release workflow can publish unsigned builds as before, or sign every Windows executable before it is uploaded. No certificate or password is stored in this repository.

## Recommended: Azure Artifact Signing

Create an Artifact Signing account and certificate profile, then configure GitHub OpenID Connect for the repository. Give the application the **Artifact Signing Certificate Profile Signer** role.

Add these repository variables under **Settings → Secrets and variables → Actions → Variables**:

| Variable | Value |
| --- | --- |
| `CODE_SIGNING_METHOD` | `azure` |
| `AZURE_SIGNING_ENDPOINT` | The regional endpoint, such as `https://eus.codesigning.azure.net/` |
| `AZURE_SIGNING_ACCOUNT` | Artifact Signing account name |
| `AZURE_CERTIFICATE_PROFILE` | Certificate profile name |

Add these repository secrets under **Actions → Secrets**:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

## Alternative: exportable PFX certificate

Use this only when the certificate provider supplies an exportable Authenticode PFX that is permitted to run in GitHub-hosted CI.

1. Convert the PFX to Base64 locally:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\path\certificate.pfx")) | Set-Clipboard
   ```

2. Set the repository variable `CODE_SIGNING_METHOD` to `pfx`.
3. Add `CODE_SIGNING_PFX_BASE64` and `CODE_SIGNING_PFX_PASSWORD` as repository secrets.

The temporary PFX is created only on the isolated runner and removed immediately after signing. Never commit a PFX or its password.

## Disable signing

Delete `CODE_SIGNING_METHOD` or leave it empty. Releases will continue to build unsigned artifacts.

## Release order

For every release, the workflow:

1. Publishes the x64 and ARM64 applications.
2. Signs and verifies each `CodexTempo.exe`.
3. Creates the ZIP archives from the signed files.
4. Builds, signs, and verifies the x64 installer.
5. Uploads only the verified artifacts.
