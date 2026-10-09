import {defineConfig,devices} from '@playwright/test';
import {readSettings} from './e2e/cashier-day-cutoff-live/settings.mjs';
const s=readSettings(process.env);
export default defineConfig({testDir:'./e2e/cashier-day-cutoff-live',testMatch:'*.spec.mjs',workers:1,fullyParallel:false,retries:0,repeatEach:1,timeout:180_000,expect:{timeout:30_000},outputDir:`test-results/cashier-day-cutoff-live/${s.runId}`,reporter:[['./e2e/cashier-day-cutoff-live/safe-reporter.mjs',{runId:s.runId}]],use:{baseURL:s.baseURL,ignoreHTTPSErrors:true,trace:'off',screenshot:'off',video:'off',serviceWorkers:'block'},projects:[{name:'cashier-day-cutoff-live',use:{...devices['Desktop Chrome']}}]});
