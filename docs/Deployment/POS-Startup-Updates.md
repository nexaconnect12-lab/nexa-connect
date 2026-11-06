# Windows POS shortcuts and startup updates

Setup creates **NexaConnect POS** on the current Windows account's desktop and under
**Start → All apps**. Both launch the managed executable and use its icon. Run Setup
from the same Windows account that runs the terminal.

## Enable a startup update source

Install an update-aware POS build manually once. Automatic updates are opt-in. They
check during an ordinary launch before the cashier workspace opens. An already
running POS and its browser callback skip the update check.
Use the managed current-user installation at `%LOCALAPPDATA%\NexaConnect\Terminal`
with normal directories and files. Junctions and symlinks on managed update paths
are rejected. Maintain POS from a single Windows logon session for that account.

1. Build a **signed** release with `scripts/package-pos-installer.ps1`. Use a fresh
   package version. The signed output's `installer` directory contains
   `NexaConnect-POS-<version>-win-x64-setup.exe` and `pos-update.json`. Use the
   existing release checks before publication. Publish only versions compatible with
   the target terminals' backend and retained recovery.
2. Publish the versioned EXE in a folder accessible to the intended Windows account.
   Replace `pos-update.json` atomically in that folder after the EXE is ready. The
   manifest contains the three-part version and the setup file SHA-256. Leave
   previously published EXE versions immutable. Give cashiers read access and reserve
   publish access for the release operator.
3. On the terminal copy
   [update-source.example.json](../../packaging/windows-pos/update-source.example.json)
   to `%LOCALAPPDATA%\NexaConnect\Terminal\update-source.json`. Set `manifest` to
   an HTTPS address such as `https://updates.example.invalid/windows-pos/pos-update.json`
   or a UNC path such as `\\server\share\windows-pos\pos-update.json` (escape each
   backslash when writing JSON). Use a direct HTTPS path without credentials, redirects,
   query parameters or fragments. For UNC use a trusted managed share. Windows uses
   the current account's SMB access, so a drive mapping is unnecessary.
4. Set `publisherCertificateSha256` to the SHA-256 fingerprint of the approved code
   signing certificate's DER bytes, obtained through an independent trusted channel.
   This is distinct from the SHA-1 thumbprint often shown in Windows certificate
   settings. Distribute a new pin securely before rotating the signing certificate.

To disable startup checks, remove the local `update-source.json`. Keep this file under
the intended user's control. It contains a public endpoint and certificate fingerprint.
Private unsigned packages can be installed manually for testing and cannot be distributed
through the startup update path.

## Update and recovery

The launch check takes up to 90 seconds and can be skipped with **Open installed POS**.
An unavailable source, malformed manifest, equal or older version, wrong file hash,
untrusted Authenticode signature, wrong signer, or wrong product/version opens the
installed POS. The worker stages the verified EXE locally and waits for POS to exit
before running Setup with the current profile. Setup's existing maintenance, version,
ownership, callback and recovery checks still apply. After Setup exits the worker
reopens POS once. On failure, POS reports it and attempts to open the installed
version. A failed preflight calls for reviewing the original installation and profile.
If the app cannot reopen following an interrupted install, use a reviewed compatible
forward repair and preserve the existing recovery folders.

Verify shortcuts under the actual cashier Windows account. Use a disposable test
terminal for signed end-to-end acceptance from HTTPS and the intended network share,
with retained recovery, offline fallback, repeated launches and interruption cases.
The unsigned candidate can verify manual installation and shortcuts. Signed
distribution still requires an approved signing certificate and a configured feed.
