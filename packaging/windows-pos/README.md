# Windows POS private EXE candidate

The Inno Setup definition is compiled by `scripts/package-pos-installer.ps1`. It embeds
an offline ZIP of the actual self-contained win-x64 POS, with runtime/native dependencies.
No appsettings, deployment endpoints, terminal identities, tokens or local state are shipped.
Use [profile.example.json](profile.example.json) as an intentionally invalid onboarding
template; replace placeholders/zero IDs with provisioned hosted scope before setup.

See [Phase 7 operations and verification](../../docs/Deployment/Next-Iteration-Phase-7.md)
and [ADR-042](../../docs/Architecture/Decisions/ADR-042-pos-private-exe-installer.md).

Build prerequisites: Windows, PowerShell 7.4+, repository .NET 10 SDK/NuGet dependencies,
and an approved Inno Setup 6.7.3 compiler. The end-user setup needs Windows PowerShell 5.1
and .NET Framework ZIP support for staging, but no separately installed .NET 10 or PS7.
The initial candidate targets win-x64. ARM64-native installation is not implemented.

```powershell
./scripts/package-pos-installer.ps1 -CompilerPath C:/tools/Inno/ISCC.exe -OutputDirectory .runstate/pos-installer/test -PrivateUnsigned
```

The default build requires `-SignToolPath`, `-CertificateThumbprint` (CurrentUser store)
and a trusted HTTPS `-TimestampUrl`. It validates/signs unsigned payload PEs, instructs
Inno to sign setup/uninstaller and rejects invalid signatures. No signing credential is
supplied or purchased automatically. Do not publish the private unsigned output. Existing
outputs are never overwritten; receipts pin source hash/revision/dirty status, template,
compiler, payload inventory and setup hash. Hashes detect drift, not publisher authenticity.

The first local compiler was downloaded from the official release, verified using Windows
Authenticode publisher Pyrsys B.V., and extracted under ignored `.runstate` using Inno's
documented portable mode. Its download SHA-256 is
`9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732`.
The tool is not redistributed in the repository. Preserve Inno's copyright/license notices;
its included license permits commercial applications, while its website requests commercial
license purchases. No purchase or licensing account was made by this task.

Setup runs at lowest privilege and uses a fixed current-user program directory. Do not
run elevated/as a different Windows account, redirect `/DIR`, or share DPAPI data. Existing
unrelated install directories, wrong custody, legacy/newer schemas or missing verified
cashier bindings block migration. The setup mutex coordinates installers; the separate
maintenance/POS mutex pair coordinates startup in the same logon session. Do not maintain
the terminal from multiple simultaneous sessions of the same account.

Install and uninstall preserve `%LOCALAPPDATA%\NexaConnect\POS`, `Displays` and
`GalleryCache`. Public `appsettings.json` and `installer-owner.txt` remain under Terminal
after uninstall for a safe reinstall. Original unpackaged/draft releases are not removed.
Callback takeover is explicit; uninstall removes only a callback still owned by this path.

`--validate-package <profile> <previous-or-dash> <managed-directory>` is a read-only POS
preflight entry. It never opens the workspace/authenticates/migrates state. Normal startup
retains the existing auth and named-pipe callback flow; maintenance only adds a startup
guard. No HTTP service, business contract, data schema, role or permission is added.

```powershell
./scripts/test-pos-installer-guards.ps1
$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE='1'
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore --filter 'FullyQualifiedName~PosInstallerPolicyTests|FullyQualifiedName~PosInstallPreflightTests'
```

Real installer execution, cancellation/power loss, restart, callback activation from a
browser and clean-device runtime checks remain distinct VM/device acceptance gates.
## Phase 8 release binding

New installer receipts add the shared `releaseSource` field and check it before/after
packaging. Use the [integration/freeze tool](../../scripts/release/README.md) for a fresh
candidate: it rechecks actual setup/payload bytes and trusted signatures instead of using
an old Phase 7 receipt or local Boolean. Private unsigned output remains reviewable and
cannot be frozen as a signed release. No installed profile or protected state is changed.
