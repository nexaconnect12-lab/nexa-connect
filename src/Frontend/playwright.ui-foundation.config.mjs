import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e/ui-foundation", workers: 1, retries: 0, timeout: 30_000,
  outputDir: "test-results/ui-foundation", reporter: "list",
  use: { trace: "off", screenshot: "off", video: "off" },
  webServer: [
    { command: "npm run dev --workspace @nexaconnect/customer-portal -- --host 127.0.0.1 --port 5196 --strictPort", url: "http://127.0.0.1:5196", reuseExistingServer: false },
    { command: "npm run dev --workspace @nexaconnect/product-owner-portal -- --host 127.0.0.1 --port 5197 --strictPort", url: "http://127.0.0.1:5197", reuseExistingServer: false },
  ],
  projects: [
    { name: "customer", use: { ...devices["Desktop Chrome"], baseURL: "http://127.0.0.1:5196" } },
    { name: "product-owner", use: { ...devices["Desktop Chrome"], baseURL: "http://127.0.0.1:5197" } },
  ],
});
