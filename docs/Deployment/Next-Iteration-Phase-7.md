# Phase 7 — Windows POS packaging and installation

## Status and decision

23 October 2026: conventional per-user EXE packaging and read-only onboarding/maintenance
guards are implemented. The [decision](../Architecture/Decisions/ADR-042-pos-private-exe-installer.md)
selects an unpackaged installer before MSIX, preserving the current DPAPI/storage model.
The [build instructions](../../packaging/windows-pos/README.md) describe tool/runtime and
signing prerequisites. No Store account, certificate purchase, listing or submission is
performed. Phase 6 live hosting remains pending; installer preparation does not invent
hostnames, credentials, server readiness or purchased hardware specifications.

Microsoft's current [account guide](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
lists free registration through its new flow. [Store EXE requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)
still require trusted code signing and an immutable HTTPS installer URL. The private
candidate is not a Store-ready release. Code signing and distribution costs depend on
the selected provider, not a fee quoted by this implementation.

## Build a reviewable private candidate

Use an approved signature-verified Inno Setup compiler (6.7.3 verified locally) and a
fresh ignored output directory:

```powershell
./scripts/package-pos-installer.ps1 -CompilerPath .runstate/phase7-tools/inno/ISCC.exe -OutputDirectory .runstate/phase7-installer/candidate -PrivateUnsigned
```

The script publishes a self-contained .NET 10 Windows Desktop win-x64 payload, removes
all appsettings/PDB files, bundles code/runtime/native files and compiles an offline EXE.
`installer-candidate.json` pins exact payload hashes, setup/compiler/template hashes,
source input/revision/dirty identity and `installed=false`, `storeSubmitted=false`.
Fresh outputs only; failure leaves no success receipt. Do not edit executable source or
the installer template while building. The retained first-draft installer/releases are
not overwritten. A dirty local artifact is not a clean release or signed provenance.

For a signing-required build, omit `-PrivateUnsigned` and supply the existing certificate
thumbprint/SignTool path. The script requires valid payload and setup signatures and
configures a signed uninstaller. All otherwise unsigned PE dependencies are included in
this signing path; do not assume strong naming equals Authenticode. Trusted signing and
timestamp validation have not been exercised without the user's publisher credential.

Bundling the runtime removes a separate Desktop Runtime prerequisite; it also makes
runtime security updates the responsibility of a new app build. Target supported Windows
editions/versions according to [Microsoft's .NET guidance](https://learn.microsoft.com/en-us/dotnet/core/install/windows),
with actual clean-device checks before acceptance. The compiler's minimum Windows build
is not a certification of every edition/device. Setup uses built-in Windows PowerShell
5.1/ZIP support; restrictive application-control policies that prevent staging must be
reviewed before installation. No ARM64-native package is currently supplied.

## Prepare hosted terminal onboarding

1. Provision live organization/restaurant/branch/store/terminal membership and original
   cashier authority in the owning services. Configure exact hosted identity issuer and
   public `nexaconnect-pos` PKCE callback `nexaconnect-pos://oauth/callback` in Keycloak.
   Preserve existing fine-grained permissions; setup grants none.
2. Copy the [public example profile](../../packaging/windows-pos/profile.example.json)
   into a private operator-managed location. Replace every reserved hostname/zero GUID.
   Use trusted non-loopback HTTPS API origins, manual THB cash/fictional PromptPay, and
   the existing openid/profile/email/API scopes. No client secret, password, token or
   arbitrary configuration section is accepted. QR image paths must be absolute or blank;
   approved external public imagery is managed separately and never copied from recovery.
3. Keep the original configuration of any unpackaged client. Archive its safe hash,
   candidate and intended Windows account/device; never export raw tokens, recovery or
   callback codes. First installation without previous config is allowed only when the
   current user's POS recovery directory has no retained entries.
4. Resolve active work using its original commands, then close POS yourself during an
   agreed maintenance window. Pending durable work can remain for compatible recovery;
   do not confuse closing the window with closing a shift. Unsaved in-memory cart edits
   must be completed or deliberately cancelled before closing, as before this change.
5. Run setup as the intended ordinary Windows account. Select the new public profile;
   for unpackaged migration select the previous profile too. Installed upgrades reuse
   their existing previous profile automatically. Setup stages a frozen copy and runs
   the actual POS's read-only preflight before any installed file/registry changes.

For controlled silent private installation (only on a disposable staging VM until tested):

```text
NexaConnect-POS-0.7.0-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PROFILE="C:\operator\hosted.json" /PREVIOUSCONFIG="C:\operator\original.json"
```

If another installation owns the callback, setup refuses until explicit takeover is
selected or `/TAKEOVERCALLBACK=1` is supplied after migration review. The old draft remains
installed and preserved. Do not run both installations interchangeably after takeover;
the old POS can re-register itself when launched. Keep its recovery/config for support.
The installer creates a Start Menu shortcut, no startup task, auto-launch, network download
or financial command. It never force-closes/restarts POS or schedules a forced reboot.

## Compatibility and unsupported migration

Compatible upgrades/reinstalls require exact organization, restaurant, branch, store,
terminal, realm, public client/callback, currency and tender/card-mode custody. API hostname
relocation and bounded session setting changes can be allowed under the same custody.
The actual protected terminal scope, pending checkout and saved cashier authority/subject
are checked read-only with current-user DPAPI. Missing/foreign/corrupt/legacy bindings and
non-2 local SQLite schemas fail closed; setup never migrates/deletes that state.

The local HTTP draft issuer cannot simply become a different hosted HTTPS issuer. New
scope/realm/account/device migration is **unsupported** until separately reconciled with
the original installation and identity/data owners. Do not edit the claimed previous
profile, delete pos-state.db/tokens, copy protected files between users, or change IDs to
bypass refusal. For an independent pilot scope, use a separately approved fresh Windows
profile/terminal identity with no copied recovery, while retaining the old draft intact.
Actual first-draft migration readiness remains a support/phase-8 decision.

Setup holds a maintenance mutex before checking the POS mutex. Normal startup creates
the POS mutex before checking maintenance; existing-instance callbacks still forward to
their original waiting app. This closes the same-logon-session startup race. Cross-session
maintenance of the same Windows account is not certified: log off other sessions first.
Never use an elevated/different account to install on behalf of the cashier.

## Updates, interrupted setup and uninstall

The program directory is fixed at `%LOCALAPPDATA%\NexaConnect\Terminal`, separate from
all protected data. An unrelated existing directory is rejected using installer ownership
metadata; links/junctions along the managed destination are rejected by POS preflight.
Managed binary downgrades are refused; equal-version reinstall is supported. Updates
use the installer engine's file/registry handling after successful preflight. Do not
claim recovery after power loss from a compiled setup: test that scenario in isolation.

Uninstall removes installed program files/shortcut. It removes `nexaconnect-pos` only
when the command exactly matches this installation; a newly assigned callback stays.
It keeps public `appsettings.json` and `installer-owner.txt` for reinstall, plus all
`%LOCALAPPDATA%\NexaConnect\POS`, `Displays` and `GalleryCache` content. No data reset
action exists. Public profiles contain no secrets, but keep operational IDs private.
Original draft releases and its UAT identity remain untouched. Uninstall is blocked while
POS is open; do not treat uninstall as a cashier session sign-out or financial rollback.

After a preflight/setup failure, preserve the old installation/configuration and original
operations. Inspect only safe error/exit codes and package/config hashes. Do not share
raw Inno logs if user paths/identifiers are sensitive, and never log callback URIs/tokens.
After interrupted file replacement, rerun the same or newer compatible installer while
POS is closed; reconcile any original pending operation once after restart. Do not install
an older binary over a newer schema or restore financial history to make a binary fit.

## Required VM/device and Store readiness checklist

| Check | Required observation |
| --- | --- |
| Clean install | Intended ordinary Windows account; no preinstalled .NET10/PS7; bundled runtime loads; profile validated; Start Menu/uninstaller visible |
| Login | System browser PKCE callback reaches the same primary POS instance once; cancel/timeout/wrong-user resume remain safe |
| Reinstall/upgrade | Same custody, saved shift/drawer/pending operation/receipt survives; original command reconciles once; changed scope/issuer denied |
| Running maintenance | Setup/uninstall/startup race blocked; no forced process close, shift close or reboot; other logon sessions absent |
| Interruption | Cancel before install, terminate a disposable setup mid-copy and reboot the VM; verify existing/repairable install plus unchanged protected data |
| Downgrade/schema | Older managed version and future/legacy SQLite state refused without reset; supported forward repair succeeds |
| Uninstall | Program removed, recovery/config retained; foreign callback owner preserved; reinstall recovers original work |
| Device | Two displays/hotplug, gallery, lock/resume, Kitchen/API reachability and PDF print checked on actual hardware |

Record actual results with exact setup/profile hashes and candidate identity in Phase 8/9;
never mark these as passed from source/unit/compile checks. A disposable VM can exercise
installation changes without mutating the retained user test environment.

Before Store submission: establish legal publisher/account type, trusted signing and
renewal/escrow ownership, privacy/support URLs and contacts, branding/screenshots/age and
market declarations, exact versioned HTTPS installer hosting, and a reviewed silent
install/onboarding model. Current profile selection is private deployment UX; a public
Store product cannot depend on an administrator passing unpublished machine-specific
profile arguments. Rework onboarding and run certification/privacy review before listing.
MSIX remains optional future work with an explicit identity/virtualization/DPAPI migration
drill. No purchase, developer registration, DNS publication or Store submission occurs here.

## Recorded verification

The final full unit regression passed 937 tests with no failures/skips, including 32
installer/recovery cases with actual Windows DPAPI pending checkout, original-cashier
binding and missing-binding refusal fixtures. Architecture regression: 6 passed. Installer tooling
guards: 15 passed. The actual Inno template compiled with a synthetic payload, followed
by a complete private candidate build using Inno Setup 6.7.3. The published self-contained
POS preflight rejected a synthetic profile containing a forbidden `ClientSecret` with
exit code 2 before recovery access, registry registration or workspace startup. This
checks the compiled preflight entry, not installation or browser callback activation.

The ignored local artifact is
`.runstate/phase7-installer/final-candidate/installer/NexaConnect-POS-0.7.0-win-x64-setup.exe`
(94,936,569 bytes), SHA-256
`AAADEBC7D0CFB5B313D74051350E6CFF85FE8F7A9B0BA231D6198A91B39911D8`.
Its receipt identifies source revision `636af028aab244835210da78ecf2ce7adae064c6` with
`sourceDirty=true`, 554 payload files and 16 unsigned payload PEs. Every payload hash
was verified; no appsettings, PDB, tokens or recovery state is included. The actual
runtime configuration includes .NETCore, WindowsDesktop and ASP.NETCore 10.0.11.
This is a local unsigned candidate, not a clean, signed release. The documentation
audit checks these claims against the installer, preflight, tests and candidate receipt.

Reproduce the source gates with the focused commands in the
[packaging README](../../packaging/windows-pos/README.md), then run:

```powershell
$env:NEXACONNECT_POS_DPAPI_ACCEPTANCE='1'
dotnet test tests/Unit/NexaConnect.UnitTests/NexaConnect.UnitTests.csproj --no-restore
dotnet test tests/Architecture/NexaConnect.ArchitectureTests/NexaConnect.ArchitectureTests.csproj --no-restore
```

Signing, real setup/upgrade/uninstall, restart/power interruption, browser activation
and clean/device acceptance remain pending. The candidate has not been installed.
