import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir:"./e2e/kitchen", workers:1, timeout:30_000,
  outputDir:"test-results/kitchen", reporter:"list",
  use:{baseURL:"http://127.0.0.1:5178",trace:"off",screenshot:"off",video:"off"},
  webServer:{command:"npm run dev --workspace @nexaconnect/customer-portal -- --host 127.0.0.1 --port 5178 --strictPort",url:"http://127.0.0.1:5178",reuseExistingServer:false},
  projects:[{name:"kitchen-chromium",use:{...devices["Desktop Chrome"]}}],
});
