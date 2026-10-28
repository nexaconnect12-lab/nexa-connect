import {chromium,expect} from '@playwright/test';
import {createHash,randomUUID} from 'node:crypto';
import fs from 'node:fs/promises';
const {NEXACONNECT_DRAFT_URL:base,NEXACONNECT_DRAFT_MEDIA_PASSWORD:password,
 NEXACONNECT_DRAFT_MEDIA_CASHIER_PASSWORD:cashierPassword,NEXACONNECT_DRAFT_MEDIA_ORGANIZATION:organization,
 NEXACONNECT_DRAFT_MEDIA_PRODUCT:product,NEXACONNECT_DRAFT_MEDIA_BRANCH:branch,NEXACONNECT_DRAFT_MEDIA_RESULT:resultFile}=process.env;
if(new URL(base).hostname!=='127.0.0.1'||new URL(base).protocol!=='https:'||![password,cashierPassword,organization,product,branch,resultFile].every(Boolean))throw new Error('Invalid local Media acceptance settings.');
const browser=await chromium.launch();let stage='login',lastStatus=null,providerPutStatus=null,networkFailure=null;
try{
 async function login(username,secret){
  const context=await browser.newContext({ignoreHTTPSErrors:true});const page=await context.newPage();page.on('response',r=>{if(r.request().method()==='PUT')providerPutStatus=r.status();});page.on('requestfailed',r=>{const error=r.failure()?.errorText;networkFailure=/^net::ERR_[A-Z_]+$/.test(error??'')?error:'request-failed';});
  stage='oidc-navigation';await page.goto(new URL('/bff/customer/login?returnUrl=/',base).href);
  stage='oidc-credentials';await page.locator('#username').fill(username);await page.locator('#password').fill(secret);await page.locator('#kc-login').click();
  stage='oidc-callback';await page.waitForURL(u=>u.origin===new URL(base).origin&&u.pathname==='/',{timeout:45000});
  stage='workspace-selection';const selection=await context.request.post(new URL('/bff/customer/tenant',base).href,{data:{organizationId:organization,applicationCode:'nexa_connect'}});expect(selection.status()).toBe(200);await page.reload();
  return {context,page};
 }
 const {context,page}=await login('draft-store-manager',password);let assetId;
 try{
  stage='branch-gallery-read';const galleryResponse=await context.request.get(new URL(`/bff/customer/screensaver-gallery/branches/${branch}`,base).href);
  expect(galleryResponse.status()).toBe(200);const gallery=await galleryResponse.json();expect(gallery.organizationId).toBe(organization);expect(gallery.branchId).toBe(branch);
  expect((await context.request.get(new URL(`/bff/customer/screensaver-gallery/branches/${randomUUID()}`,base).href)).status()).toBe(403);
  stage='upload-page';await page.goto(new URL('/#media',base).href);
  await expect(page.getByRole('heading',{name:'Media management',exact:true})).toBeVisible();
  stage='upload-input';const image=Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=','base64');
  const name=`draft-media-${randomUUID()}.png`;
  await page.getByPlaceholder('Catalog product UUID').fill(product);
  await page.locator('input[type="file"]').setInputFiles({name,mimeType:'image/png',buffer:image});
  const start=page.waitForResponse(r=>r.url().endsWith('/bff/customer/media/uploads')&&r.request().method()==='POST').catch(()=>null);
  const complete=page.waitForResponse(r=>r.url().endsWith('/complete')&&r.request().method()==='POST').catch(()=>null);
  stage='upload-request';await page.getByRole('button',{name:'Upload',exact:true}).click();
  const response=await start;if(!response)throw new Error();lastStatus=response.status();expect(lastStatus).toBe(201);stage='upload-session-json';const session=await response.json();assetId=session.asset.id;
  const storageOrigin=new URL(session.uploadUrl).origin;const staffHeaders=(await context.request.get(new URL('/',base).href)).headers();const guestHeaders=(await context.request.get(new URL('/order',base).href)).headers();
  expect(staffHeaders['content-security-policy']).toContain(storageOrigin);expect(guestHeaders['content-security-policy']).not.toContain(storageOrigin);stage='upload-transfer';
  const completion=await complete;if(!completion)throw new Error();lastStatus=completion.status();expect(lastStatus).toBe(200);
  stage='processing';
  const get=async path=>{const r=await context.request.get(new URL(path,base).href);expect(r.status()).toBe(200);return r.json();};
  await expect.poll(async()=>(await get('/bff/customer/media')).find(i=>i.id===assetId)?.processingStatus,{timeout:90000}).toBe('ready');
  await expect.poll(async()=>(await get(`/bff/customer/media/${assetId}/variants`)).map(v=>v.name).sort(),{timeout:90000}).toEqual(['display','thumbnail']);
  const variants=await get(`/bff/customer/media/${assetId}/variants`);expect(variants.map(v=>v.name).sort()).toEqual(['display','thumbnail']);
  stage='downloads';let originalUrl;
  for(const suffix of ['download','variants/thumbnail/download','variants/display/download']){
   const link=await get(`/bff/customer/media/${assetId}/${suffix}`),download=await context.request.get(link.downloadUrl);
   expect(download.status()).toBe(200);const bytes=await download.body();expect(bytes.length).toBeGreaterThan(0);
   if(suffix==='download'){expect(createHash('sha256').update(bytes).digest('hex')).toBe(createHash('sha256').update(image).digest('hex'));originalUrl=link.downloadUrl;}
  }
  const unsigned=new URL(originalUrl);unsigned.search='';expect((await context.request.get(unsigned.href)).status()).toBe(403);
  stage='authorization';
  const badTenant=await context.request.post(new URL('/bff/customer/tenant',base).href,{data:{organizationId:randomUUID(),applicationCode:'nexa_connect'}});expect(badTenant.status()).toBe(403);
  const cashier=await login('draft-cashier',cashierPassword);
  try{expect((await cashier.context.request.get(new URL('/bff/customer/media',base).href)).status()).toBe(403);
   expect((await cashier.context.request.post(new URL('/bff/customer/media/uploads',base).href,{data:{ownerService:'catalog',ownerType:'product',ownerId:product,fileName:'denied-test.png',contentType:'image/png',sizeBytes:image.length,checksumSha256:createHash('sha256').update(image).digest('hex')}})).status()).toBe(403);
  }finally{await cashier.context.close();}
  stage='deletion';const assets=await get('/bff/customer/media'),asset=assets.find(a=>a.id===assetId);
  expect((await context.request.delete(new URL(`/bff/customer/media/${assetId}?expectedVersion=${asset.concurrencyVersion}`,base).href)).status()).toBe(200);
  await expect.poll(async()=>(await context.request.get(originalUrl)).status(),{timeout:90000}).toBe(404);assetId=undefined;
  await fs.writeFile(resultFile,JSON.stringify({version:1,passed:true,realOidc:true,branchGalleryRead:true,unavailableBranchDenied:true,browserUploadCors:true,staffStorageCsp:true,guestCspSelfOnly:true,originalChecksum:true,variants:2,unsignedStorageDenied:true,cashierReadDenied:true,cashierWriteDenied:true,wrongTenantDenied:true,deletionVerified:true,productionVerified:false})+'\n',{flag:'wx'});
  process.stdout.write('Local Media upload, processing, download, access denial and deletion verified.\n');
 }finally{
  // Only this generated test image may be deleted; existing user assets are never touched.
  if(assetId){try{const r=await context.request.get(new URL('/bff/customer/media',base).href);if(r.ok()){const a=(await r.json()).find(i=>i.id===assetId);if(a)await context.request.delete(new URL(`/bff/customer/media/${assetId}?expectedVersion=${a.concurrencyVersion}`,base).href);}}catch{}}
  await context.close();
 }
}catch(error){process.stderr.write(`Local Media acceptance failed at ${stage} (httpStatus=${lastStatus}, providerPutStatus=${providerPutStatus}, networkFailure=${networkFailure}, errorType=${error?.name}, strictLocator=${String(error?.message?.includes('strict mode violation')===true)}); no credentials or signed URLs emitted.\n`);process.exitCode=1;}
finally{await browser.close();}
