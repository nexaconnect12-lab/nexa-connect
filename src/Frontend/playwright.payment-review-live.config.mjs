import {defineConfig,devices} from "@playwright/test";
import {readSettings} from "./e2e/payment-review-live/settings.mjs";
const settings=readSettings(process.env);
export default defineConfig({
  testDir:"./e2e/payment-review-live", testMatch:"*.spec.mjs",workers:1,fullyParallel:false,
  retries:0,repeatEach:1,timeout:60_000,expect:{timeout:15_000},
  outputDir:`test-results/payment-review-live/${settings.runId}`,
  reporter:[["./e2e/payment-review-live/safe-reporter.mjs",{runId:settings.runId}]],
  use:{baseURL:settings.baseURL,ignoreHTTPSErrors:true,trace:"off",screenshot:"off",video:"off",serviceWorkers:"block"},
  projects:[{name:"payment-review-live",use:{...devices["Desktop Chrome"]}}],
});
