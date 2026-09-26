import {defineConfig,devices} from '@playwright/test';
import {readSettings} from './e2e/cash-close-live/settings.mjs';
const settings=readSettings(process.env);
export default defineConfig({testDir:'./e2e/cash-close-live',testMatch:'*.spec.mjs',workers:1,fullyParallel:false,retries:0,repeatEach:1,
  timeout:120_000,expect:{timeout:20_000},outputDir:`test-results/cash-close-live/${settings.runId}`,
  reporter:[['./e2e/cash-close-live/safe-reporter.mjs',{runId:settings.runId}]],
  use:{baseURL:settings.baseURL,ignoreHTTPSErrors:true,trace:'off',screenshot:'off',video:'off',serviceWorkers:'block'},
  projects:[{name:'cash-close-live',use:{...devices['Desktop Chrome']}}]});
