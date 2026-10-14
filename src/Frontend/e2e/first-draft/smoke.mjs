import { chromium } from '@playwright/test';
import { writeFile } from 'node:fs/promises';

const { NEXACONNECT_DRAFT_URL: base, NEXACONNECT_DRAFT_STATE: statePath,
  NEXACONNECT_DRAFT_SMOKE_PASSWORD: password, NEXACONNECT_DRAFT_SMOKE_SUBJECT: subject,
  NEXACONNECT_DRAFT_SMOKE_MODE: mode } = process.env;
const url = new URL(base);
if (url.hostname !== '127.0.0.1' || url.protocol !== 'https:' || !statePath || !subject ||
    !['login', 'resume'].includes(mode) || (mode === 'login' && !password)) {
  throw new Error('Invalid owned draft browser verification settings.');
}
const browser = await chromium.launch();
try {
  const context = await browser.newContext({ ignoreHTTPSErrors: true,
    ...(mode === 'resume' ? { storageState: statePath } : {}) });
  const page = await context.newPage();
  if (mode === 'login') {
    await page.goto(new URL('/bff/customer/login?returnUrl=/', base).href);
    await page.locator('#username').fill('draft-smoke');
    await page.locator('#password').fill(password);
    await page.locator('#kc-login').click();
    await page.waitForURL(candidate => candidate.origin === url.origin && candidate.pathname === '/', { timeout: 45000 });
  }
  const reply = await context.request.get(new URL('/bff/customer/me', base).href);
  if (reply.status() !== 200 || (await reply.json()).subjectId !== subject) {
    throw new Error('Draft authenticated session did not survive its required boundary.');
  }
  if (mode === 'login') await context.storageState({ path: statePath });
  await writeFile(statePath + '.' + mode + '.json', JSON.stringify({ mode, passed: true,
    realOidcVerified: true, productAuthorizationVerified: false, screenshotsEnabled: false }));
  console.log(`Owned draft ${mode} session verified.`);
  await context.close();
} finally { await browser.close(); }
