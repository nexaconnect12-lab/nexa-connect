# Physical POS display placement follow-up

## Behavior and status

The [cashier full-screen follow-up](Next-Iteration-Phase-4.md#setup-and-recovery)
supersedes the original staff work-area sizing described below. The customer refit and
display-safety rules still apply.

The operator reports a display-safety lock after assigning staff/customer roles and
maximizing the customer view. Source review also reproduced the staff-frame geometry
failure: normal Win32 maximization can propose invisible resize borders outside its
monitor, and cross-monitor customer-window DPI/placement notifications were treated as
unconditional topology failures.

The 0.8.1 source follow-up keeps staff geometry inside the explicitly assigned work area.
Native maximize limits use physical monitor/work-area coordinates; ordinary move/resize
requests are constrained **before** Windows applies them. Invalid geometry, incompatible
minimum dimensions and unknown/changed topology still mask/lock private work. Locked
screensaver geometry keeps its existing separate safety path.

The non-interactive public customer window is already full-screen after role assignment.
Its maximize/restore/move/size and window-DPI notifications queue one refit to the original
customer monitor while the topology remains current and the private staff frame is safe.
The refit restores normal borderless full-monitor bounds without changing transaction data,
authentication, PINs or custody. It cannot open a new customer window after disposal/closure.
Actual monitor/settings changes, staff DPI changes, unexpected customer closure or failed
placement still fail closed. No screen assignment is automatically persisted or changed.

Unit and hidden-window tests cover 1366×768, maximize overscan, ordinary movement, attempted
boundary crossing, negative coordinates, native maximize limits, customer maximization/refit
and monitor-loss fencing. The full unit run passed **1,490**, skipped **16** existing conditional
cases, and the native touch/input/presentation suite passed. Physical retest remains required.
No business API, schema, grant or financial workflow changes. The private unsigned 0.8.1
installer and matching onboarding bundle were prepared. After the operator closed physical
POS, its separate backend was rebuilt and restored to Ready/provisioned. Actual-device
retest remains pending. This correction is retained in the current
[0.8.4 update bundle](../Identity/POS-Session-PIN.md#locked-session-renewal);
use that current package for the combined update rather than the historical 0.8.1 folder below.

## Prepared 0.8.1 update

This section records the earlier staging/integrity evidence. The current update instructions
and hashes are in the linked 0.8.4 guide above.

The six public update files are staged in
`\\192.168.0.191\Shared\NexaConnect-POS-0.8.1-b1bd29fd`. Copy that complete folder to
the physical POS while signed into Windows account **PC**. Keep POS closed, use the
ordinary account and follow the existing installer workflow; select the included
`terminal-profile.json`. The issuer, terminal and public CA are unchanged; no new CA or
PIN enrollment is required by this update. This is still a private unsigned test package.

Independently verify `onboarding.json` SHA-256:
`b0f288e137d3b913daa09f879e92566a481a270470fc3d9d9b0c3c09bee647e1`.
Before executing the checker, compare its hash and the CA hash against the verified
manifest, as required by the onboarding guide. From the copied folder, run:

```powershell
powershell.exe -NoProfile -File .\Test-DeviceOnboarding.ps1 -BundleDirectory . -ExpectedManifestSha256 b0f288e137d3b913daa09f879e92566a481a270470fc3d9d9b0c3c09bee647e1
```

Pass `-BundleDirectory .` explicitly: Windows PowerShell 5.1's default-directory invocation
failed during preparation; the unmodified checker passed with an explicit bundle directory.
Correcting that default is a tooling follow-up; no trust bypass is used.
Only after this check passes, run `NexaConnect-POS-setup.exe` and select the included profile.
Installer SHA-256: `9cc80350b2bb1377192304696076325160c6fd1f255e12044aaa6996ca5f3edf`.

Protected application/identity database backups and server-state snapshots were verified
before restart. After restart, 375 original business rows across 203 checked tables remained
present (volatile authorization, delivery and processing state excluded). Sixteen original
scope/key-ring files remained byte-identical; all 17 protected credential values were
unchanged despite DPAPI re-encryption. Original data volumes were retained. This does not
inspect or certify the physical machine's local recovery files.

Development-PC checks passed normal Windows TLS validation, exact issuer/PKCE, isolated
browser cashier sign-in, menu/history/PIN-status reads, wrong-terminal denial and hidden
admin/guest boundaries. The Windows PowerShell checker and all six copied share-file
hashes passed. No financial command, media write or PIN write was performed. Device
installation, display behavior and peripheral acceptance remain operator checks.

## Missing menu images in the new pilot

Read-only real-cashier diagnosis on the separate LAN pilot found three menu items, each
with HTTP 200 photo-selection metadata and **no assigned asset**. Public staff-edge and
direct Catalog photo delivery both return 404, as expected for no image. No media write,
approval or financial command was performed. The old pilot's approved media was not copied
into this fresh-data organization/branch.

On the development PC, use a separate/private browser window for the new manager portal
at `https://127.0.0.1:56464/`. Retrieve the new manager account locally:

```powershell
pwsh -File D:\DevOps\nexa-connect\scripts\get-first-draft-login.ps1 -RunId b1bd29fd3d334a2eaaa9cae18f1cd0e2 -Role store-manager
```

With the shared upload follow-up deployed, upload photos in **Media management** and wait
for processing. Open **Configuration → Menu management**, select a menu item, choose a
ready photo from the library, then preview, approve and save. Refresh the physical POS
menu after assigning images. Preserve branch context. The previous
portal on port 63417 manages the retained earlier pilot, not this new test branch.

## Update and retest

Use the [versioned onboarding process](POS-Physical-Device-Onboarding.md). Finish or
deliberately cancel unsaved work and close the physical POS before refreshing the separate
backend or running the newer compatible installer. Retain its original scope/issuer,
server key rings/databases and local recovery. No migrations or PIN re-enrollment are
required by this presentation change; normal restart authentication remains in force.

For this historical candidate, sign in as the original cashier, check the retained shift/work, apply the
two display roles again and verify customer fill, cashier maximize/restore/movement and
staff/customer privacy. The customer view should not need manual maximization. Then
verify assigned photos after Refresh menu. Do not infer device acceptance from fixture
passes or reset original financial recovery to bypass a warning.
