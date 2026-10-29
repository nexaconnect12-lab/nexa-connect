# POS UX8 — cash-drawer preparation and remaining integration

29 October 2026: **UX8A server authority/audit is implemented in source; full UX8 is incomplete**.
[UX8A](POS-UX8A-Drawer-Authority.md) adds default-off contracts and owning migrations.
Native automatic/manual opening remains unavailable; no physical/payment hook is activated.

UX8B software preparation now supplies the native authority adapter, lazy local consumption,
disabled manual controls and guarded original-cash-only hook. No device driver is enabled;
the user deferred equipment facts and selected software preparation. See
[UX8B preparation and remaining activation](POS-UX8B-Native-Drawer.md).

[UX8C](POS-UX8C-Drawer-History.md) subsequently adds source-only authorized own-cashier/terminal
history and read-only original-record review. It does not enable physical opening, change
payment or resolve/reset consumed intents; the retained pilot remains unchanged.

## Equipment facts and prerequisites

The user reports that the equipment is available and supplied **PAISHUO, USB, 80 mm,
200 DPI** printer information. These details do not identify the exact model or establish
drawer protocol compatibility. The drawer is not connected or its connection is unknown.
The user subsequently supplied the cash-drawer model **PAISHUO 4141P**: advertised compatible
devices PC/Tablet; operating system listed as “no”; material 0.8 mm SPCC cold-rolled steel
sheet and ABS; warranty listed as unavailable; origin Guangdong, China. These are user-supplied
listing details, not verified protocol or electrical specifications. **4141P identifies the
drawer, not the printer model.** Connector/pinout, voltage, pulse command and driver-controlled
opening remain unconfirmed; PC/Tablet compatibility does not establish direct USB operation.
Do not infer that printer USB is the drawer interface, or choose a pulse/pin/voltage/ESC/POS
command from the brand or paper width alone. No printer-driver opening test is confirmed.

Before physical implementation, confirm the exact printer model, installed Windows driver,
drawer cable/port and vendor-supported opening command and electrical compatibility. These
are prerequisites, not successful hardware observations. Connect/test using vendor guidance.

## Delivered preparation

The source staff **Terminal & sync → Cash drawer** panel reports disabled automatic opening,
unavailable manual opening and missing setup facts. UX8B adds a disabled reason picker/action;
there is no enabling setting. Receipt queue selection cannot enable a drawer. Customer display is unchanged.

The isolated [Drawers component](../../src/Clients/NexaConnect.POS/Hardware/Drawers/README.md)
adds request validation, a testable authorization/driver/intent workflow and a separate SQLite
intent-store adapter. UX8B now composes its lazy store and server adapter with a disabled
driver and guarded original-cash-only post-payment hook. No default journal I/O, preference
or RAW command is activated. UX8A owns server contracts, permissions and migrations. The existing
`Hardware.ICashDrawer` remains unconfigured; the new `IDrawerDriver` distinguishes configured
status and submission rather than treating the existing throwing port as a working driver.

The preparatory workflow requires authorization, a current session and a durable reservation
before one submission. Local consumption now precedes server HTTP. Full-scope original-sale keys suppress repeated sale attempts across
operation IDs; manual requests require fixed reasons and an explicit operation ID. A driver
failure remains Unknown, never proof of no opening. An audit completion failure is distinct
from failure before reservation. It cannot repeat payment, post a cash movement or retry
the drawer. Local evidence cannot replace live server authority or immutable server audit.
See [intent storage](../Database/POS-Drawer-Intents.md).

## Remaining work to complete UX8

1. Verify the model/interface/driver and implement only its vendor-supported adapter/profile.
   Keep default off, require explicit verified setup and never fall back to another queue.
2. UX8A supplies POS-owned live scoped authorization and durable server audit/intent contracts
   in source. UX8B now supplies the disabled native adapter/coordinator in source; eventual
   activation must deploy matching server migrations/binaries and a verified physical profile.
   Manual opening needs a separate permission and reason; cashier sign-in alone is insufficient.
   Validate current organization/restaurant/branch/store/terminal, cashier/shift and open cash
   custody; bind authorizations to their original intent. Reuse UX8A's correlated boundary,
   migration/permission and owning-test contracts, and add native integration/recovery coverage.
3. For automatic opening, verify the original confirmed cash settlement through owning-service
   authority. Trigger only the original eligible completion, not QR submission/acceptance,
   receipt reprint, card/PromptPay payment or generic recovery. No historical-sale bulk replay.
4. Integrate manual controls, deliberate confirmation and clear submitted/unknown/error states.
   Preserve original cashier ownership, lock/privacy and financial recovery. Define explicit
   handling of printer failure, unavailable audit, late payment response and crash between
   reservation and submission; never promise exactly-once physical action.
5. Verify with the actual equipment: authorized/denied manual opening, cash-only automatic
   opening, lock/sign-out, repeated confirmations, unplugged printer, spooler/device failure,
   restart and unchanged payment/reconciliation. Record actual results before activation.

## Verification

`DrawerWorkflowTests` exercises disabled/denied/cancelled paths, manual reasons, changed
custody, durable duplicate/crash suppression, concurrent SQLite reservations and failures
before/after submission with fake authority/driver ports. The Windows input fixture checks
that the source panel stays disabled and no manual opening button appears. These checks do
not authenticate against a new server contract or open a drawer. Hardware and full UX8 remain
pending. Verification includes the full unit suite with current-user DPAPI acceptance enabled,
six architecture tests and the isolated Windows input/lock/diagnostics fixture. The first
parallel test build exhausted available host memory; a single-worker build and released build
servers resolved it. No retained services or native POS were stopped to run these checks.
