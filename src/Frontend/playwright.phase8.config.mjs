import { defineConfig, devices } from "@playwright/test";

const baseURL = process.env.NEXACONNECT_PHASE8_E2E_BASE_URL ?? "https://localhost:51829";
const target = new URL(baseURL);
const localHosts = new Set(["localhost", "127.0.0.1", "::1"]);
const retainSensitiveArtifacts = process.env.NEXACONNECT_PHASE8_E2E_RETAIN_SENSITIVE_ARTIFACTS === "1";

if (!localHosts.has(target.hostname) && process.env.NEXACONNECT_PHASE8_E2E_ALLOW_REMOTE !== "1") {
  throw new Error(
    "Phase 8 E2E targets must be local unless NEXACONNECT_PHASE8_E2E_ALLOW_REMOTE=1 is explicitly set for a disposable non-production environment.",
  );
}

export default defineConfig({
  testDir: "./e2e/phase8",
  fullyParallel: false,
  workers: 1,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  outputDir: "test-results/phase8",
  reporter: [["list"], ["html", { outputFolder: "playwright-report/phase8", open: "never" }]],
  use: {
    baseURL,
    ignoreHTTPSErrors: localHosts.has(target.hostname),
    screenshot: "only-on-failure",
    trace: retainSensitiveArtifacts ? "retain-on-failure" : "off",
    video: retainSensitiveArtifacts ? "retain-on-failure" : "off",
  },
  projects: [{ name: "phase8-chromium", use: { ...devices["Desktop Chrome"] } }],
});
