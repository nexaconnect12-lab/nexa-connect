import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir:"./e2e/financial-completeness",workers:1,timeout:30_000,outputDir:"test-results/financial-completeness",reporter:"list",
  use:{baseURL:"http://127.0.0.1:5181",trace:"off",screenshot:"off",video:"off"},
  globalSetup:"./e2e/financial-completeness/server.mjs",
  projects:[{name:"financial-completeness-chromium",use:{...devices["Desktop Chrome"]}}],
});
