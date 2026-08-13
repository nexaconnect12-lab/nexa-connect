# Media service

This service is the exclusive owner of media metadata and the future object-storage lifecycle. Apply Media migration 1 and configure `ConnectionStrings__Media`, `Services__PlatformDirectory`, and `Services__Authorization`. The development profile uses `https://localhost:51228`.

`GET /api/media/v1/customer/organizations/{organizationId}/assets` lists up to 500 non-deleted metadata rows after exact-organization and `media.asset.read` authorization. Bytes remain in planned S3-compatible storage. Upload, signed download, deletion, variants, and processing workers remain staged.

Service name: `nexaconnect-media`. Query the safe `Customer media authorization denied` event by `correlation_id`. Tokens, object keys, checksums, and filenames are not logged by that event.
Dependency rejection also emits `Media organization access dependency rejected request`. Authorization returns `403`; database/dependency failures fail closed. Verify with `dotnet build src/Services/NexaConnect.Services.Media/NexaConnect.Services.Media.csproj`, then apply Media migration 1 before an authenticated smoke test.
