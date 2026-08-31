import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir:"./e2e/payment-review", workers:1, timeout:30_000,
  outputDir:"test-results/payment-review", reporter:"list",
  use:{baseURL:"http://127.0.0.1:5177",trace:"off",screenshot:"off",video:"off"},
  webServer:{command:"npm run dev --workspace @nexaconnect/customer-portal -- --host 127.0.0.1 --port 5177 --strictPort",url:"http://127.0.0.1:5177",reuseExistingServer:false},
  projects:[{name:"payment-review-chromium",use:{...devices["Desktop Chrome"]}}],
});
