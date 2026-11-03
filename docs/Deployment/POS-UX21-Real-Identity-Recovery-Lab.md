# POS UX21 — disposable real-identity recovery lab

## Isolation and prerequisites

UX21 creates a new owned first-draft stack with real Keycloak, eleven loopback application
services, representative reference menu/stock and a separately packaged cash-only POS.
Prepare never adopts a supplied run ID, seeds financial transactions, launches POS or changes
its callback. It reuses existing service-owned provisioning and lifecycle ownership guards.
Omise, real transfers, physical drawer opening, Media and QR are outside this lab.

Files are under `.runstate/pos-recovery-lab/<lab-id>/` and the corresponding
`.runstate/first-draft/<lab-id>/`. Current-user DPAPI protects generated passwords. Native
state uses `%LOCALAPPDATA%\NexaConnect\POS-Pilots\<scope-hash>` with fresh scope identities;
normal credentials, custody, orders and recovery are neither used nor copied. Use the
[Windows draft prerequisites](First-Draft-Windows-POS.md), local Docker Desktop/images,
PowerShell7.4+, .NET10 Desktop and restored dependencies. Allow resources for a second stack.
Do not run competing publication/verification producers in this checkout.

The native app still permits only one POS per Windows account and uses the shared
`nexaconnect-pos` callback. The launcher conservatively requires no other POS process to be
running, including other Windows accounts. Close existing POS before Launch. It snapshots three
callback values, waits for lab POS to close, and restores only its own registration or an
already-restored snapshot. A changed owner is preserved for review. Verified backups are
archived; empty key containers may remain if no original values existed. Keep the launcher
shell open for automatic restoration. Launch does not force-kill POS.

Use a separate/private browser profile for the lab portal: cookies are scoped to hostnames,
not ports. Automated verification uses fresh browser contexts and intercepts its own callback
without registering protocols or opening native POS.

## Prepare and use

```powershell
pwsh -NoProfile -File scripts/test-pos-recovery-lab-guards.ps1
./scripts/pos-recovery-lab.ps1 -Action Prepare -ConfirmTestResources
$lab = '<fresh ID printed by Prepare>'
./scripts/pos-recovery-lab.ps1 -Action Status -LabId $lab
./scripts/pos-acceptance.ps1 -Action Prepare -RecoveryLabId $lab
```

Save the **new lab pack ID**. The existing tracker reuses its seventeen cases, explicit
observations, defects and retests. Version2 `recovery-lab-operator-observations` binds the
lab package/profile, reference scope, artifacts and live owned processes/listeners without a
QR prerequisite. Exports use `targetKind=recovery-lab`, `labId` and
`qrCandidateApplicable=false`. Version1 installed-pilot records remain supported. No existing
passes transfer between targets; readiness/source-verification flags remain false. Passing
observations require restored valid binding; failed/blocked observations remain possible
during an outage.

The common tracker retains all seventeen cases. This cash-only lab does not satisfy the
Omise, QR or approved-media prerequisites; record affected cases as blocked when those
prerequisites are absent. Do not mark them passed from unrelated lab smoke-test results or
transfer a pass from the normal pilot. Equipment observations still require the actual equipment.

Retrieve the required credential in a private local terminal only, never chat, CI or a transcript:

```powershell
./scripts/get-pos-recovery-lab-login.ps1 -LabId $lab -Account cashier
```

| Account argument | User | Purpose |
| --- | --- | --- |
| `cashier` | `draft-cashier` | Original cashier in the lab branch. |
| `cashier-other` | `lab-cashier-other` | Another cashier in that branch. |
| `manager` | `draft-store-manager` | Existing manager role/limits. |
| `restricted` | `draft-accountant` | Read-oriented accountant without placement authority. |
| `wrong-branch` | `lab-wrong-branch` | Cashier assigned only to the separate denied branch. |

Extra membership and assignment operations use their owning Application/repository paths
and runtime logins. Completed lab access provisioning is a no-op preserving revocations;
no ad hoc cross-service SQL or new grant is added.

After finishing the current normal POS action and closing that window:

```powershell
./scripts/pos-recovery-lab.ps1 -Action Launch -LabId $lab
```

The package runs directly; no installer or normal active-release change occurs. Use only
lab accounts/synthetic work and compare branch/terminal to the lab profile before proceeding.
When lab POS closes, the command restores the callback. If the shell was interrupted, close
lab POS then run `-Action RestoreCallback -LabId $lab`. Preserve a backup whose current owner
has changed and review it; never overwrite that owner. Native startup retains its existing
scope-before-registration guard.

## Controlled operator procedures

Use [UX9's full expectations](POS-UX9-Acceptance.md) and Record/Defect/Resolve/Report/Worklist
with the lab pack, actual observations and privately indexed evidence. UUID generation alone
is not proof. No tool auto-records passes or promotes the retained pilot's results.

**Genuine session denial:** only this generated realm has thirty-second access tokens,
five-minute SSO idle and fifteen-minute SSO maximum. Native idle/absolute policy and normal
realms remain unchanged; the lab profile uses a fifteen-second refresh lead. Sign in as
original lab cashier and establish test custody. In another shell run
`./scripts/pos-recovery-lab.ps1 -Action ExpireCashierSession -LabId $lab -ConfirmTestResources`.
Remain active with harmless navigation until real refresh rejection locks private work.
Observe preserved custody/recovery, cancelled sign-in, another-cashier denial, then successful
original-cashier authentication. No tokens are edited; cashier authentication remains PKCE.

**Network retry:** place one synthetic cash order while Order is available and review its
server total. Run `-Action CutOrder -LabId $lab -ConfirmTestResources`. Confirm payment once;
the client must save its original request before the failed network operation and retain
uncertainty. Run `-Action RestoreOrder -LabId $lab -ConfirmTestResources`, then explicitly
review/retry that same request and verify one settlement, exact method/amount and receipt.

**Pending restart:** use a different synthetic order and repeat the interruption through
uncertain confirmation. Close lab POS normally, restore Order, then Launch and authenticate
as the original cashier. Recover the same saved request and verify one outcome. Launch
requires a complete Ready target, so restore Order before reopening. Normal restart alone
does not pass pending-payment recovery. These faults test unavailability; they do not prove
response loss after a server commit or external provider capture. UX20 supplies complementary
automated recovery evidence.

**Permissions/history:** use restricted and wrong-branch identities in an empty lab context
or separate private portal context. Never clear original custody to switch its owner.
Observe privileged denial and suppression of delayed private replies. History-only reads
must stay actor/terminal scoped; empty history is valid. Check lock/sign-out/identity-change,
denial and outage clearing without manufacturing drawer attempts. In this generated context,
the existing first-draft StopService/Restart actions may target `Authorization` with this
same `$lab` run ID to exercise history dependency failure. Never substitute the retained
run ID; restore the dependency before passing observations or financial work. Drawer command
authority and the physical driver remain disabled.

## Automated real-identity verification

With lab POS closed (normal POS may stay running):

```powershell
./scripts/test-pos-recovery-lab.ps1 -LabId $lab -ConfirmTestResources
```

The check signs in all five users via real browser authorization-code/PKCE, verifies provider
userinfo, refresh, live scoped decisions and branch denial, ends only original lab cashier
sessions and verifies rejected refresh. It cuts/restores only the owned Order process and
checks target rejection/restoration, including recovery in `finally` after a fault-test error.
Private bounded evidence is in `verification/<run-id>/`; passwords/tokens, screenshots and raw
response bodies are not exported. No financial transactions or operator observations are
created. Failed receipts remain failed. Native interaction and production readiness remain
separate from this real-identity smoke test.

## Stop, resume and cleanup

```powershell
./scripts/pos-recovery-lab.ps1 -Action Stop -LabId $lab -ConfirmTestResources
./scripts/pos-recovery-lab.ps1 -Action Resume -LabId $lab -ConfirmTestResources
# Only after reviewing all synthetic work/evidence and closing lab POS:
./scripts/pos-recovery-lab.ps1 -Action Reset -LabId $lab -ConfirmTestResources
```

Stop/Resume retain generated volumes and local recovery. Resume/fault controls reject source
changes; prepare a fresh lab for changed code. Reset is explicit because it discards lab
databases. It reuses project-label/process-start/executable/volume ownership checks and
verifies removal of only that project's resources. Protected diagnostics and native scope
recovery remain for review; no recursive local recovery deletion is performed. Normal POS,
the retained project, production accounts and domain/DNS configuration are never selected.

## Prepared target and measured verification

Lab `85ab1ba42fd245da83ab99bbb9761603` is prepared with eleven application services and a
separate version2 acceptance pack `b4fe3dcc34f04b8fbd8741bae9e18daa`, all seventeen cases pending.
The current local pointer is `.runstate/ux21-current.json`; no credentials are stored there.

Real-identity verification `6519a98e89f747109a3b340ac048f9c1` passed 29 checks across five
browser PKCE logins, provider userinfo, refresh, scope decisions, branch denial and ended-session
refresh rejection. Owned Order interruption/restoration passed. Stop/Resume subsequently retained
the same valid lab evidence binding. The native read-only `--validate-install` preflight exited 0.
Completed lab access provisioning was rechecked as a reference-preserving no-op.

All 1,390 unit tests and seven architecture tests executed and passed without skips. Forty-nine
legacy acceptance/worklist guards and twenty-six lab/target/profile/callback guards passed.
The normal installed pack remains nine passed, six pending and two blocked, with valid binding
and unchanged observation record. The source-bound lab uses revision
`e26bca219f85ccf7343c5e1ec5723939dbb524f1` (dirty), input hash
`6922c2f599fb5e49cd32ca2838efba9f3538250e47b956b6aa5dab0af684997a`.

After user-confirmed POS closure, the controlled signed-out lab startup/normal-close cycle
passed: the window was responsive, both native POS and launcher exited 0, and all three original
callback values were restored. Two checked normal-profile files remained byte-identical.
Evidence is retained in `.runstate/ux21-native-check.json`; no sign-in or financial action was
performed and no operator case was passed. The lab window is closed after this check.
The earlier attempt was blocked before launch while another POS process was running.
The incomplete initial lab `26537e327f0247dc9947730ece5197eb` was reset through owned cleanup
after correcting launcher pipe-lifetime handling; it is not passing verification evidence.
