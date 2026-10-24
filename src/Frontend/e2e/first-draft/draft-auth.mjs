import {randomBytes,randomUUID,createHash} from 'node:crypto';

export async function draftToken(browser,api,state,reference,role,password){
 const redirect='nexaconnect-pos://oauth/callback',context=await browser.newContext(),page=await context.newPage();
 const verifier=randomBytes(32).toString('base64url'),nonce=randomUUID();let callback;
 await context.route('**/*',route=>new URL(route.request().url()).origin===new URL(state.authority).origin?route.continue():route.abort());
 await page.route('**/login-actions/authenticate*',async route=>{
  const response=await route.fetch({maxRedirects:0});const location=response.headers().location;
  if(location?.startsWith(redirect)){callback=new URL(location);await route.fulfill({status:200,body:'Authentication complete'});}
  else await route.fulfill({response});
 });
 try{
  await page.goto(state.authority+'/protocol/openid-connect/auth?'+new URLSearchParams({client_id:'nexaconnect-pos',redirect_uri:redirect,response_type:'code',scope:'openid',state:nonce,code_challenge:createHash('sha256').update(verifier).digest('base64url'),code_challenge_method:'S256'}));
  await page.locator('#username').fill('draft-'+role);await page.locator('#password').fill(password);await page.locator('#kc-login').click();
  await page.getByText('Authentication complete',{exact:true}).waitFor();
  if(callback?.searchParams.get('state')!==nonce)throw new Error('PKCE state mismatch');
  const response=await api.post(state.authority+'/protocol/openid-connect/token',{form:{grant_type:'authorization_code',client_id:'nexaconnect-pos',redirect_uri:redirect,code:callback.searchParams.get('code'),code_verifier:verifier}});
  if(response.status()!==200)throw new Error('PKCE exchange failed');const body=await response.json();
  if(JSON.parse(Buffer.from(body.access_token.split('.')[1],'base64url').toString()).sub!==reference.subjects[role])throw new Error('Stable identity mismatch');
  return body.access_token;
 }finally{await context.close();}
}
