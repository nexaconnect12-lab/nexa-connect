# NexaConnect branding

`assets/branding` is the source for the SVG, PNG and multi-size Windows ICO.
Keep the SVG copies in the Omise test helper and the Keycloak login theme in
sync with the canonical file. The two portal builds and Windows packaging read
the canonical files directly.

Publish both portal builds for the tab and sidebar icons. Kitchen and guest ordering
use the Customer Portal tab icon. In the LAN guest pilot, publish the matching
QR edge so it forwards the exact favicon GET. On Windows, install a newly built
POS package for the cashier, executable and Setup icons. The merchant display
continues to use its shop identity and approved media.

Rebuild and redeploy the production Keycloak image, or recreate the joined cashier
pilot Keycloak container to add the theme mount. Then follow the [Keycloak theme
setup](../../docker/keycloak/README.md#production-image). Existing realms retain
their selected login theme until an administrator changes it.

After publishing, check the two portal sidebars and browser tabs, the Kitchen and
guest browser tabs, the cashier window, the Windows executable and Setup icons,
and a fresh Keycloak sign-in page in the intended realm. The QR edge should return
the SVG for the exact `GET /nexaconnect-icon.svg`.
