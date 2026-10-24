import {defineConfig,devices} from "@playwright/test";
export default defineConfig({testDir:"./e2e/qr-entry",workers:1,retries:0,timeout:30000,outputDir:"test-results/qr-entry",reporter:"list",
use:{baseURL:"http://127.0.0.1:5199",trace:"off",screenshot:"off",video:"off"},
webServer:{command:"npm run dev --workspace @nexaconnect/customer-portal -- --host 127.0.0.1 --port 5199 --strictPort",url:"http://127.0.0.1:5199",reuseExistingServer:false},
projects:[{name:"phone-chromium",use:{...devices["Pixel 7"]}},{name:"phone-webkit",use:{...devices["iPhone 13"]}},{name:"desktop",use:{...devices["Desktop Chrome"]}}]});
