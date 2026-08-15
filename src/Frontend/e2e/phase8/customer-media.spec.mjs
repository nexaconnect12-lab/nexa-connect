import { randomUUID } from "node:crypto";
import { expect, test } from "@playwright/test";

const settings = {
  enabled: process.env.NEXACONNECT_PHASE8_E2E === "1",
  username: process.env.NEXACONNECT_PHASE8_E2E_USERNAME,
  password: process.env.NEXACONNECT_PHASE8_E2E_PASSWORD,
  organizationId: process.env.NEXACONNECT_PHASE8_E2E_ORGANIZATION_ID,
  applicationCode: process.env.NEXACONNECT_PHASE8_E2E_APPLICATION_CODE ?? "nexa_connect",
  catalogProductId: process.env.NEXACONNECT_PHASE8_E2E_CATALOG_PRODUCT_ID,
  processingTimeout: Number(process.env.NEXACONNECT_PHASE8_E2E_PROCESSING_TIMEOUT_MS ?? "90000"),
};

const required = [
  settings.username,
  settings.password,
  settings.organizationId,
  settings.catalogProductId,
];
const configured = settings.enabled && required.every((value) => Boolean(value));
let createdAssetId;

test.skip(
  !configured,
  "Set NEXACONNECT_PHASE8_E2E=1 and the documented credentials and seed identifiers to run the joined Phase 8 acceptance test.",
);

test.afterEach(async ({ page }) => {
  if (!createdAssetId) return;
  try {
    await page.evaluate(async (assetId) => {
      const list = await fetch("/bff/customer/media", { credentials: "include" });
      if (!list.ok) return;
      const asset = (await list.json()).find((item) => item.id === assetId);
      if (!asset) return;
      await fetch(`/bff/customer/media/${assetId}?expectedVersion=${asset.concurrencyVersion}`, {
        method: "DELETE",
        credentials: "include",
      });
    }, createdAssetId);
  } catch {
    // Preserve the primary failure. The retained trace identifies any cleanup failure.
  } finally {
    createdAssetId = undefined;
  }
});

test("customer signs in, selects an authorized tenant, and completes the Media lifecycle", async ({ page, request }) => {
  const correlationId = `phase8-browser-${randomUUID()}`;
  await page.setExtraHTTPHeaders({ "X-Correlation-ID": correlationId });
  await signIn(page);

  const access = await page.evaluate(async () => {
    const response = await fetch("/bff/customer/access", { credentials: "include" });
    if (!response.ok) throw new Error(`Access request failed with ${response.status}.`);
    return response.json();
  });
  const selected = access.organizations.find(
    (item) => item.organizationId === settings.organizationId
      && item.applicationCode === settings.applicationCode,
  );
  expect(selected, "The signed-in user must have the seeded organization/product access.").toBeTruthy();

  await page.getByRole("combobox").first().click();
  await page.getByRole("option", {
    name: new RegExp(`${escapeRegex(selected.organizationName)}.*${escapeRegex(settings.applicationCode)}`),
  }).click();
  await expect(page.getByText(settings.organizationId, { exact: true })).toBeVisible();

  const rejectedTenantStatus = await page.evaluate(async ({ applicationCode }) => {
    const response = await fetch("/bff/customer/tenant", {
      method: "POST",
      credentials: "include",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ organizationId: crypto.randomUUID(), applicationCode }),
    });
    return response.status;
  }, { applicationCode: settings.applicationCode });
  expect(rejectedTenantStatus).toBe(403);

  await page.getByText("Media management", { exact: true }).first().click();
  await expect(page.getByRole("heading", { name: "Media management" })).toBeVisible();

  const png = Buffer.from(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=",
    "base64",
  );
  const fileName = `phase8-e2e-${randomUUID()}.png`;
  await page.getByPlaceholder("Catalog product UUID").fill(settings.catalogProductId);
  await page.locator('input[type="file"]').setInputFiles({ name: fileName, mimeType: "image/png", buffer: png });

  const uploadStarted = page.waitForResponse(
    (response) => response.url().includes("/bff/customer/media/uploads") && response.request().method() === "POST",
  );
  const uploadCompleted = page.waitForResponse(
    (response) => response.url().includes("/complete") && response.request().method() === "POST",
  );
  await page.getByRole("button", { name: "Upload", exact: true }).click();
  const startResponse = await uploadStarted;
  expect(startResponse.ok(), await startResponse.text()).toBeTruthy();
  const session = await startResponse.json();
  createdAssetId = session.asset.id;
  expect(session.asset.originalFileName).toBe(fileName);
  expect(session.asset.sizeBytes).toBe(png.length);
  const completionResponse = await uploadCompleted;
  expect(completionResponse.ok(), await completionResponse.text()).toBeTruthy();
  await expect(page.getByText("Upload completed; thumbnail and display variants are processing.")).toBeVisible();

  await expect.poll(
    async () => {
      const assets = await browserJson(page, "/bff/customer/media");
      return assets.find((asset) => asset.id === session.asset.id)?.processingStatus;
    },
    { timeout: settings.processingTimeout, message: "Media worker did not produce ready variants in time." },
  ).toBe("ready");

  const variants = await browserJson(page, `/bff/customer/media/${session.asset.id}/variants`);
  expect(variants.map((variant) => variant.name).sort()).toEqual(["display", "thumbnail"]);
  const original = await browserJson(page, `/bff/customer/media/${session.asset.id}/download`);
  const thumbnail = await browserJson(page, `/bff/customer/media/${session.asset.id}/variants/thumbnail/download`);
  const display = await browserJson(page, `/bff/customer/media/${session.asset.id}/variants/display/download`);
  for (const download of [original, thumbnail, display]) {
    const response = await request.get(download.downloadUrl);
    expect(response.ok()).toBeTruthy();
    expect((await response.body()).length).toBeGreaterThan(0);
  }

  await page.reload();
  await page.getByText("Media management", { exact: true }).first().click();
  const row = page.getByRole("row").filter({ hasText: fileName });
  await expect(row.getByText("ready", { exact: true })).toBeVisible();
  const deleted = page.waitForResponse(
    (response) => response.url().includes(`/bff/customer/media/${session.asset.id}`)
      && response.request().method() === "DELETE",
  );
  await row.getByRole("button", { name: "Delete" }).click();
  expect((await deleted).ok()).toBeTruthy();
  createdAssetId = undefined;
  await expect(row).toHaveCount(0);

  await expect.poll(async () => (await request.get(original.downloadUrl)).status(), {
    timeout: settings.processingTimeout,
    message: "Deleted original object remained available after the deletion worker timeout.",
  }).toBe(404);
});

async function signIn(page) {
  await page.goto("/");
  await page.locator("#username").fill(settings.username);
  await page.locator("#password").fill(settings.password);
  await page.locator("#kc-login").click();
  await expect(page.getByText("Customer Portal", { exact: true }).first()).toBeVisible();
}

async function browserJson(page, path) {
  return page.evaluate(async (requestPath) => {
    const response = await fetch(requestPath, { credentials: "include" });
    if (!response.ok) throw new Error(`${requestPath} failed with ${response.status}.`);
    return response.json();
  }, path);
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
