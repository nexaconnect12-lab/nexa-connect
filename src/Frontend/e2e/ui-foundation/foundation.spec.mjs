import { test, expect } from "@playwright/test";

const organizationId = "11111111-1111-1111-1111-111111111111";
async function setup(page, name, { fail = false, hold = false, role = "platform-owner" } = {}) {
  const owner = name === "product-owner";
  const prefix = owner ? "/bff/platform-admin" : "/bff/customer";
  const state = { fail, hold, requests: [], mutations: [] };
  await page.route("**/bff/**", async route => {
    const path = new URL(route.request().url()).pathname;
    state.requests.push(path);
    if (route.request().method() !== "GET") state.mutations.push(path);
    if (!path.startsWith(prefix)) return route.fulfill({ status: 403, json: {} });
    if (path === `${prefix}/me`) {
      if (state.hold) return; // The test closes the page while this synthetic read is pending.
      if (state.fail) return route.fulfill({ status: 503, json: { title: "Preview service unavailable" } });
      return route.fulfill({ json: { subjectId: "preview-operator", username: "Preview operator", roles: [role] } });
    }
    if (path === "/bff/customer/access") return route.fulfill({ json: { organizations: [
      { organizationId, organizationCode: "preview", organizationName: "Preview restaurant", applicationCode: "nexa_connect" },
    ] } });
    if (path === "/bff/customer/tenant") return route.fulfill({ json: { organizationId, applicationCode: "nexa_connect" } });
    if (path === "/bff/platform-admin/platform/summary") return route.fulfill({ json: {
      organizationCount: 2, activeOrganizationCount: 2, activeMembershipCount: 8,
      registeredProductCount: 1, enabledProductAccessCount: 2, activeSupportElevationCount: 0,
    } });
    return route.fulfill({ status: 404, json: { title: "No preview fixture" } });
  });
  return { state, owner, title: owner ? "Product Owner" : "Customer Portal" };
}

test("desktop shell preserves separate session boundaries and keyboard skip navigation", async ({ page }, info) => {
  const { state, title, owner } = await setup(page, info.project.name);
  await page.goto("/");
  await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
  await expect(page.getByText("Authenticated workspace", { exact: true })).toBeVisible();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Skip to workspace" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("main")).toBeFocused();
  await expect(page.getByRole("link", { name: "Sign out", exact: true })).toHaveAttribute("href", owner ? "/bff/platform-admin/logout" : "/bff/customer/logout");
  expect(state.mutations).toEqual([]);
  await page.screenshot({ path: info.outputPath("desktop.png"), fullPage: true });
});

test("mobile navigation opens, selects a section and returns without page overflow", async ({ page }, info) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const { owner, title } = await setup(page, info.project.name);
  await page.goto("/");
  await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Open navigation" }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog).toBeVisible();
  await dialog.getByRole("menuitem", { name: owner ? "Summary" : "Enabled products", exact: true }).click();
  await expect(dialog).not.toBeVisible();
  await expect(page.getByRole("main")).toBeFocused();
  await expect(page.getByRole("link", { name: "Skip to workspace" })).not.toBeInViewport();
  if (owner) await expect(page.locator(".ant-statistic-title").filter({ hasText: /^Organizations$/ })).toBeVisible();
  else await expect(page.getByRole("heading", { name: "Enabled products", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: info.outputPath("mobile.png"), fullPage: true });
});

test("session loading has an accessible status without claiming authentication", async ({ page }, info) => {
  await setup(page, info.project.name, { hold: true });
  await page.goto("/");
  await expect(page.getByRole("status")).toContainText("Opening");
  await expect(page.getByText("Authenticated workspace", { exact: true })).toHaveCount(0);
});

test("failed initial read provides a safe retry without any mutation", async ({ page }, info) => {
  const { state, title } = await setup(page, info.project.name, { fail: true });
  await page.goto("/");
  await expect(page.getByRole("alert")).toContainText("Preview service unavailable");
  await expect(page.getByRole("status")).toHaveCount(0);
  state.fail = false;
  await page.getByRole("button", { name: "Try again" }).click();
  await expect(page.getByRole("heading", { name: title, exact: true })).toBeVisible();
  expect(state.mutations).toEqual([]);
});

test("role-restricted owner section stays denied in the new shell", async ({ page }, info) => {
  const { owner, state } = await setup(page, info.project.name, { role: "platform-auditor" });
  await page.goto(owner ? "/#users" : "/#profile");
  if (owner) {
    await expect(page.getByRole("alert")).toContainText("not available for your platform role");
    await expect(page.getByRole("menuitem", { name: "Users", exact: true })).toHaveCount(0);
    expect(state.requests.some(path => path.endsWith("/platform/users"))).toBe(false);
  } else {
    await expect(page.getByRole("heading", { name: "Organization profile", exact: true })).toBeVisible();
    expect(state.requests.every(path => path.startsWith("/bff/customer"))).toBe(true);
  }
  expect(state.mutations).toEqual([]);
});
