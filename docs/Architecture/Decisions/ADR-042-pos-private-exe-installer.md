# ADR-042 — Private Windows POS EXE installer before Store publication

Status: Accepted for private candidate preparation, 23 October 2026.

## Context and current evidence

The unpackaged WPF POS uses current-user DPAPI, unvirtualized LocalAppData recovery and
an HKCU custom protocol. Financial recovery, original-cashier authenticated resume and
two-display/gallery state must survive deployment without new business commands.
The draft ZIP installer is loopback/run-specific and cannot serve as a hosted installer.

Microsoft's [account onboarding](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
currently lists no registration fee for either account type through the new onboarding
flow. This does not cover certificate purchases or commercial transaction charges.
[EXE/MSI requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements)
allow conventional installers but require trusted signatures, an immutable versioned
HTTPS download and an offline silent-install path. MSIX includes Store distribution
benefits but introduces package identity and virtualization considerations;
[Microsoft's virtualization documentation](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)
describes explicit exclusions and their Windows-version constraints. None of these sources
certifies this POS's storage, callbacks or update compatibility.

## Decision

Build a conventional per-user Inno Setup EXE with a self-contained win-x64 .NET 10 Windows
Desktop payload. Install into `%LOCALAPPDATA%\NexaConnect\Terminal`, outside the unchanged
`POS`, `Displays` and `GalleryCache` data directories. This retains unpackaged identity
and avoids introducing a new MSIX virtualization/migration boundary during the pilot.
MSI adds no needed scope here; MSIX remains a later separately rehearsed option.

No terminal config, fixed deployment address, tokens, confidential client or draft state
is embedded. The operator supplies a strict public hosted profile. Setup stages a frozen
copy, runs the actual POS read-only preflight and then uses the installer engine to copy
files/create a Start Menu shortcut/register HKCU callback. An existing other callback
requires explicit takeover authorization. The app still validates OAuth state and forwards
callbacks through its existing current-user pipe; setup never consumes/logs callback data.

A maintenance mutex prevents normal POS startup during maintenance in the same Windows
logon session. Setup also checks the existing POS mutex and never force-closes POS, closes
a shift or restarts it. Downgrades of the managed app are blocked. Compatible updates
preserve exact terminal/restaurant/tender/realm custody; endpoint relocation is allowed
with unchanged custody. Local schema 2, DPAPI scope and retained authentication bindings
are checked read-only. Installer preflight performs no schema migration or financial replay.
Existing realms/scopes or old/unreadable recovery require original-installation review.

The uninstaller removes installed programs/shortcut and removes the callback only while
its exact command still points to this installation. It retains public config/ownership
metadata and all external recovery, tokens, display preferences and gallery cache. It does
not restore another old installation's callback automatically. A wrong-account/device
recovery copy is unsupported; never delete/transplant DPAPI material to get setup to pass.

## Distribution gates and consequences

The build script defaults to signing-required mode: every payload PE must have a valid
Authenticode chain and the installer/uninstaller must be signed. Credentials are drawn
from an existing Windows certificate store, not a checked-in PFX/password. An explicit
`-PrivateUnsigned` path produces a visibly documented local test candidate with an unsigned
PE count and hashes. No trusted publisher certificate, Store account/listing or submission
is provisioned. Signing, publisher/legal/privacy/support data, immutable hosting and clean
candidate CI remain release gates; no unsigned build is called Store-ready.

Installer automation and OS reboot/install/update interruption still need a disposable
Windows VM and the actual intended account/device. Do not run competing sessions of the
same Windows account during maintenance: the existing POS single-instance mutex is logon
session scoped, and cross-session installation is not certified. Source tests and compile
success are separate from that device acceptance. Phase 6 hosting facts remain pending;
Phase 8 freezes the integrated release before Phase 9's consolidated user test.

See the [Phase 7 guide](../../Deployment/Next-Iteration-Phase-7.md) for build, onboarding,
migration restrictions, maintenance, rollback/recovery and test requirements.
