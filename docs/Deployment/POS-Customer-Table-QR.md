# Show a table QR on the POS customer screen

Use a QR-enabled Customer Portal. The exported entry must match the Windows POS
branch, and customers' phones must reach the exact guest ordering origin encoded in
the QR over trusted HTTPS.
The physical pilot's staff-only `:9443` listener is for staff sign-in and APIs.
Guest phones need the branch's separate [guest QR listener](POS-Physical-Device-Onboarding.md#enable-guest-table-qr-on-the-physical-pos-branch).

1. In Customer Portal, open **Configuration → Table QR entry**. When creating or
   deliberately reissuing a table QR, select **Download for POS** while the issued
   code is visible. The portal renders the same guest entry into the export locally.
2. Have the terminal administrator copy the unchanged `A1.nexa-table-qr.json` file
   (example table A1) into `%LOCALAPPDATA%\NexaConnect\Terminal\table-qr` under the
   Windows account that runs the managed POS. For an unpackaged build, the folder
   is `table-qr` next to the running executable. Create it if needed. Keep the exact
   `<table>.nexa-table-qr.json` name and replace the previous copy if the browser
   adds a duplicate suffix. POS accepts up to 100 files of at most 64 KiB each.
3. With both displays assigned, sign in and open **QR orders → Table QR for
   customer screen**. Select **Load table codes**, choose a table, then select
   **Show QR on customer screen**. Loading, showing and hiding the image leave
   issuance, rotation and expiry to the portal and guest server.
4. Check that ads remain on the left and **Order from your phone**, the table label
   and a scannable code are on the right. Scan with a phone and verify the table.
   Staff review follows the existing guest-order workflow. Only accepted requests
   are sent to Kitchen.

**Hide QR**, selecting a different table, leaving QR orders, loading a sale, locking,
or losing the display assignment clears the image. The display also clears after two
minutes or when the export expires, whichever comes first.

Treat the export like a printed table QR. The PNG itself contains the guest admission
capability. Provision files only from the authorized portal and restrict write access
to the terminal administrator. The client's file name, branch, expiry and image
checks validate file format and scope metadata. Authorized provisioning establishes
the file's origin, and the guest server validates the scanned capability. Replace the export
after rotating the table QR and remove it after revoking the entry. **Load table codes**
reloads the folder. Rotation invalidates earlier printed and exported copies. The
portal can only export the raw code from the visible issuance result.

To verify a deployment, check current and wrong-branch files, clear the display by
locking and loading an order, then test a real phone on the intended guest network.
