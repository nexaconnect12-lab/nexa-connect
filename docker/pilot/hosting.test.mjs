import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { inventory, template, validatePlan, settings, validateSettings, initialize, compose, proxyConfig, files, verifyBundle, render, probe, validateInputs } from './hosting.mjs';

const secret = '0123456789abcdef0123456789abcdef0123456789abcdef';
function goodPlan() {
  const p = template(); p.project = 'nexa-pilot-isolated'; p.issuer = 'https://identity.nexa.test/realms/pilot';
  p.server = { confirmed: true, os: 'Synthetic Linux container host', cpu: 'Synthetic 16-core', ramGiB: 32, freeDiskGiB: 100, location: 'Fixture', uploadMbps: 100, existingWorkloads: 'None in synthetic fixture' };
  p.operations = { owner: 'Fixture', backupDestination: 'sftp://backup.nexa.test/pilot', retentionDays: 7, rpoMinutes: 60, rtoMinutes: 120, alertRecipient: 'fixture@nexa.test', maintenanceWindow: 'Fixture only' };
  p.runtimeImage = 'registry.nexa.test/aspnet@sha256:' + 'a'.repeat(64); p.proxyImage = 'registry.nexa.test/nginx@sha256:' + 'b'.repeat(64);
  for (const [id, s] of Object.entries(p.services)) s.host = `${id}.nexa.test`;
  for (const key of Object.keys(p.acceptance)) p.acceptance[key] = true;
  return p;
}
function goodSettings(s, p) {
  const c = settings(s, p);
  function replace(v) {
    for (const [key, value] of Object.entries(v)) {
      if (value && typeof value === 'object') replace(value);
      else if (typeof value === 'string') v[key] = value.replaceAll('REPLACE_FROM_SECRET_MANAGER', secret).replaceAll('example.invalid', 'nexa.test');
    }
  }
  replace(c);
  const scoped = name => createHash('sha256').update(`${s.id}:${name}`).digest('hex');
  for (const k of ['Bff', 'WorkloadIdentity', 'KeycloakAdmin', 'Authentication']) if (c[k]?.ClientSecret) c[k].ClientSecret = scoped('client');
  if (!s.spa) c.ConnectionStrings[s.name] = c.ConnectionStrings[s.name].replace(secret, scoped('database'));
  for (const [key, v] of Object.entries(c)) if (key === 'Outbox' || key.endsWith('Consumer')) if (v.ConnectionString) v.ConnectionString = `amqps://service:${secret}@broker.nexa.test/pilot`;
  return c;
}
function temp(t) { const d = fs.mkdtempSync(path.join(os.tmpdir(), 'nexa-hosting-test-')); t.after(() => fs.rmSync(d, { recursive: true, force: true })); return d; }
function secureDir(dir, p) {
  fs.mkdirSync(dir);
  for (const s of inventory) {
    const d = path.join(dir, s.id); fs.mkdirSync(d);
    fs.writeFileSync(path.join(d, 'appsettings.Production.json'), JSON.stringify(goodSettings(s, p)));
    for (const f of ['tls.pfx', 'data-protection.pfx']) fs.writeFileSync(path.join(d, f), 'Synthetic certificate existence fixture, not a real PFX');
  }
  for (const f of ['proxy.crt', 'proxy.key']) fs.writeFileSync(path.join(dir, f), 'Synthetic existence fixture');
}
function bundleDir(dir) {
  fs.mkdirSync(dir);
  for (const s of inventory) { const d = path.join(dir, s.id); fs.mkdirSync(d); fs.writeFileSync(path.join(d, s.assembly + '.dll'), 'Synthetic checksum fixture'); if (s.spa) { fs.mkdirSync(path.join(d, 'wwwroot')); fs.writeFileSync(path.join(d, 'wwwroot/index.html'), '<html>synthetic</html>'); } }
  const m = { version: 1, sourceRevision: 'a'.repeat(40), sourceDirty: true, sourceInputSha256: 'c'.repeat(64), runtime: 'linux-x64', schemas: { POS: 23 }, files: files(dir) };
  fs.writeFileSync(path.join(dir, 'manifest.json'), JSON.stringify(m)); return m;
}

test('unconfirmed server template cannot pass deployment validation', () => assert.throws(() => validatePlan(template()), /project|unconfirmed/));
test('complete synthetic prerequisites pass without assuming actual server facts', () => assert.equal(validatePlan(goodPlan()).version, 1));
for (const mutate of [p => p.server.confirmed = false, p => p.server.uploadMbps = 0, p => p.operations.rpoMinutes = 0, p => p.operations.backupDestination = '/same-server/backups', p => p.runtimeImage = 'aspnet:latest', p => p.acceptance.queueBindingsReady = false, p => p.acceptance.linuxContainerHostReady = false, p => p.services.pos.host = p.services['owner-portal'].host, p => p.services.pos.host = 'pos.nexa.test;evil', p => p.secretRoot = p.deploymentRoot + '/private', p => p.server.ramGiB = 1, p => p.issuer = 'http://identity.nexa.test/realms/pilot']) {
  test(`unsafe or missing plan prerequisite rejected (${mutate.toString()})`, () => { const p = goodPlan(); mutate(p); assert.throws(() => validatePlan(p)); });
}
test('all fifteen service configurations validate with independent runtime scopes', () => {
  const p = goodPlan(); assert.equal(inventory.length, 15);
  for (const s of inventory) assert.equal(validateSettings(s, goodSettings(s, p), p).AllowedHosts, p.services[s.id].host);
});
test('Platform Directory retains its actual database and runtime role naming', () => {
  const c = settings(inventory[0], goodPlan()); assert.match(c.ConnectionStrings.PlatformDirectory, /Database=PlatformDirectory;Username=platform_directory_app;/);
});
test('no org-wide secrets or POS confidential client are emitted into remote terminal settings', () => {
  const p = goodPlan(); const d = compose(p);
  assert(!('pos-client' in d.services)); assert(!d.services.pos.environment.WorkloadIdentity__ClientSecret);
});
for (const mutate of [c => c.Persistence.Provider = 'InMemory', c => c.ConnectionStrings.Order = c.ConnectionStrings.Order.replace('nexaconnect_order_app', 'postgres'), c => c.ConnectionStrings.Order = c.ConnectionStrings.Order.replace('VerifyFull', 'Disable'), c => c.WorkflowRecovery.Enabled = false, c => c.CardCheckout.EnableOmiseTestCheckout = true, c => c.Authentication.RequireHttpsMetadata = false, c => c.Authentication.OutboundToken = 'unsafe', c => c.Authentication.authority = 'https://evil.nexa.test', c => c['Authentication:Authority'] = 'https://evil.nexa.test', c => c.Kestrel = { Endpoints: { Http: { Url: 'http://+:80' } } }, c => c.WorkloadIdentity.ClientSecret = 'local-dev-only-change-me', c => c.Services.POS = 'https://foreign.nexa.test/', c => c.Outbox.Enabled = false]) {
  test(`dangerous Order configuration rejected (${mutate.toString()})`, () => { const p = goodPlan(), s = inventory.find(v => v.name === 'Order'), c = goodSettings(s, p); mutate(c); assert.throws(() => validateSettings(s, c, p)); });
}
test('plaintext BFF Redis and switched cookie-protection scope rejected', () => {
  const p = goodPlan(), s = inventory.find(v => v.spa === 'customer-portal'), c = goodSettings(s, p);
  c.ConnectionStrings.BffSessionCache = c.ConnectionStrings.BffSessionCache.replace('ssl=true', 'ssl=false'); assert.throws(() => validateSettings(s, c, p));
  const c2 = goodSettings(s, p); c2.DataProtection.ApplicationName = 'foreign'; assert.throws(() => validateSettings(s, c2, p));
});
test('media scanning cannot be disabled and provider delivery remains disabled', () => {
  const p = goodPlan(), s = inventory.find(v => v.name === 'Media'), c = goodSettings(s, p); c.MediaSafety.MalwareScanEnabled = false; assert.throws(() => validateSettings(s, c, p));
  const n = inventory.find(v => v.name === 'Notification'), nc = goodSettings(n, p); nc.NotificationDelivery.Enabled = true; assert.throws(() => validateSettings(n, nc, p));
});
test('compose isolates services, limits resources and gives only the edge a published port', () => {
  const p = goodPlan(), d = compose(p); assert.equal(Object.keys(d.services).length, 16);
  for (const s of inventory) {
    const v = d.services[s.id]; assert(!v.ports); assert.equal(v.user, '1654:1654'); assert.equal(v.read_only, true); assert.equal(v.restart, 'unless-stopped');
    assert.equal(v.environment.ASPNETCORE_ENVIRONMENT, 'Production'); assert(v.volumes.every(v => v.bind.create_host_path === false));
    assert(!JSON.stringify(v).includes(secret)); assert(v.mem_limit); assert.equal(v.volumes.filter(v => !v.read_only).length, 1);
  }
  assert.deepEqual(d.services.edge.ports, ['0.0.0.0:443:8443']); assert.equal(d.networks.private.external, true);
});
test('TLS proxy verifies backend identity, suppresses URL logs and hides internal/management paths', () => {
  const text = proxyConfig(goodPlan()); assert.match(text, /proxy_ssl_verify on/); assert.match(text, /proxy_ssl_server_name on/); assert.match(text, /ssl_reject_handshake on/); assert.match(text, /access_log off/); assert.match(text, /X-Forwarded-Proto ""/); assert.match(text, /internal/); assert(!text.includes('authorization.nexa.test')); assert(!text.includes('identity.nexa.test'));
});

test('guest capability service routes are hidden on business edges and BFF guest routes remain reachable', () => {
  const text = proxyConfig(goodPlan());
  for (const id of ['pos', 'order', 'catalog', 'payment', 'media']) {
    const server = text.split(`server_name ${id}.nexa.test;`)[1].split('server {')[0];
    assert(server.includes('location ~* /(?:internal|guest)(?:/|$) { return 404; }'));
    assert(new RegExp('/(?:internal|guest)(?:/|$)', 'i').test('/api/order/v1/guest/qr/menu'));
    assert(server.indexOf('(?:internal|guest)') < server.indexOf(`location /api/${id}/`));
  }
  const customer = text.split('server_name customer-portal.nexa.test;')[1].split('server {')[0];
  assert(customer.includes('location / { proxy_pass')); assert(!customer.includes('(?:internal|guest)'));
});
test('initialization refuses overwrite and leaves all actual server gates open', t => {
  const d = path.join(temp(t), 'config'); initialize(d); assert.throws(() => initialize(d), /already exists/);
  const p = JSON.parse(fs.readFileSync(path.join(d, 'plan.json'))); assert.equal(p.server.confirmed, false); assert(!p.acceptance.identityReady);
});
test('render verifies configuration and bundle integrity without copying secrets', t => {
  const d = temp(t), p = goodPlan(), secure = path.join(d, 'secure'), bundle = path.join(d, 'bundle'), out = path.join(d, 'render');
  secureDir(secure, p); bundleDir(bundle); validateInputs(p, secure); render(p, secure, bundle, out);
  assert(!fs.readFileSync(path.join(out, 'compose.json'), 'utf8').includes(secret)); assert(!fs.existsSync(path.join(out, 'bundle')));
  assert.equal(JSON.parse(fs.readFileSync(path.join(out, 'receipt.json'))).deploymentExecuted, false); assert.throws(() => render(p, secure, bundle, out), /already exists/);
  fs.appendFileSync(path.join(bundle, 'pos/NexaConnect.Services.POS.dll'), 'tampered'); assert.throws(() => verifyBundle(bundle), /checksum/);
});
test('bundle rejects added or removed files even if original files still hash correctly', t => {
  const d = path.join(temp(t), 'bundle'); bundleDir(d); fs.writeFileSync(path.join(d, 'injected.env'), 'synthetic'); assert.throws(() => verifyBundle(d), /checksum/);
});
test('render blocks mismatched architecture and missing certificate inputs', t => {
  const d = temp(t), p = goodPlan(), secure = path.join(d, 'secure'), bundle = path.join(d, 'bundle'); secureDir(secure, p); bundleDir(bundle);
  p.runtime = 'linux-arm64'; assert.throws(() => render(p, secure, bundle, path.join(d, 'out')), /runtime/);
  fs.unlinkSync(path.join(secure, 'pos/tls.pfx')); assert.throws(() => validateInputs(p, secure));
});
test('duplicate/aliased database options cannot override the checked runtime identity', () => {
  const p = goodPlan(), s = inventory.find(v => v.name === 'Order');
  for (const extra of [';Username=postgres', ';User ID=postgres', ';SSL Mode=Disable', ';Trust Server Certificate=true']) {
    const c = goodSettings(s, p); c.ConnectionStrings.Order += extra; assert.throws(() => validateSettings(s, c, p));
  }
});
test('short database password and duplicate Redis TLS option are rejected', () => {
  const p = goodPlan(), s = inventory.find(v => v.name === 'Order'), c = goodSettings(s, p); c.ConnectionStrings.Order = c.ConnectionStrings.Order.replace(/Password=[^;]+/, 'Password=short'); assert.throws(() => validateSettings(s, c, p));
  const b = inventory.find(v => v.spa), bc = goodSettings(b, p); bc.ConnectionStrings.BffSessionCache += ',ssl=false'; assert.throws(() => validateSettings(b, bc, p));
});
test('credentials cannot be reused across independent service ownership', t => {
  const p = goodPlan(), d = path.join(temp(t), 'secure'); secureDir(d, p);
  const f = path.join(d, 'restaurant/appsettings.Production.json'), c = JSON.parse(fs.readFileSync(f));
  const first = JSON.parse(fs.readFileSync(path.join(d, 'platform-directory/appsettings.Production.json'))).KeycloakAdmin.ClientSecret;
  c.ConnectionStrings.Restaurant = c.ConnectionStrings.Restaurant.replace(/Password=[^;]+/, 'Password=' + first); fs.writeFileSync(f, JSON.stringify(c));
  assert.throws(() => validateInputs(p, d), /independent/);
});
test('nonstandard external HTTPS port is preserved in probes and OIDC Host', async t => {
  const p = goodPlan(); p.httpsPort = 9443; assert.match(proxyConfig(p), /Host customer-portal.nexa.test:9443/);
  await probe(p, path.join(temp(t), 'report.json'), async url => { if (!url.includes('identity.nexa.test')) assert.equal(new URL(url).port, '9443'); return new Response('', { status: 503 }); });
});
test('anonymous HTTPS checks bind exact issuer and never follow login redirects', async t => {
  const p = goodPlan(), out = path.join(temp(t), 'report.json'); let calls = 0;
  const report = await probe(p, out, async (url, options) => {
    calls++; assert.equal(options.redirect, 'manual'); assert(options.signal); assert(!options.headers.Authorization);
    if (url.endsWith('openid-configuration')) return Response.json({ issuer: p.issuer, authorization_endpoint: p.issuer + '/protocol/openid-connect/auth', token_endpoint: p.issuer + '/protocol/openid-connect/token' });
    return new Response('', { status: url.endsWith('/me') ? 401 : url.endsWith('/metrics') ? 404 : 200 });
  });
  assert.equal(report.passed, true); assert.equal(calls, 13); assert(!fs.readFileSync(out, 'utf8').includes(p.issuer));
});
test('probe distinguishes redirect, dependency failure and wrong issuer from a pass', async t => {
  const p = goodPlan(), out = path.join(temp(t), 'report.json');
  const report = await probe(p, out, async url => {
    if (url.endsWith('openid-configuration')) return Response.json({ issuer: 'https://evil.nexa.test' });
    if (url.includes('/kitchen')) throw new Error(secret);
    return new Response('', { status: 302 });
  });
  assert.equal(report.passed, false); assert(report.results.every(v => !v.passed)); assert(!fs.readFileSync(out, 'utf8').includes(secret));
});
test('CLI failure never prints raw JSON, credentials or exception payloads', t => {
  const d = temp(t), f = path.join(d, 'bad.json'); fs.writeFileSync(f, '{invalid:' + secret);
  const r = spawnSync(process.execPath, [path.join(import.meta.dirname, 'hosting.mjs'), 'validate', f, d], { encoding: 'utf8' });
  assert.equal(r.status, 1); assert(!r.stderr.includes(secret)); assert(!r.stderr.includes('SyntaxError'));
});
test('production realm bootstrap receives every confidential-client secret and owner callback input', () => {
  const repo = path.resolve(import.meta.dirname, '../..');
  const realm = fs.readFileSync(path.join(repo, 'docker/keycloak/realm/nexa-dev-realm.json'), 'utf8');
  const compose = fs.readFileSync(path.join(repo, 'docker-compose.production.yml'), 'utf8');
  const example = fs.readFileSync(path.join(repo, '.env.production.example'), 'utf8');
  const required = [...new Set([...realm.matchAll(/\$\{([A-Z0-9_]+CLIENT_SECRET)\}/g)].map(m => m[1]))];
  for (const key of [...required, 'PLATFORM_ADMIN_BFF_REDIRECT_URI', 'PLATFORM_ADMIN_BFF_ORIGIN']) {
    assert(compose.includes(`${key}: "\${${key}:?`), `Missing required production injection: ${key}`);
    assert(example.includes(key + '='), `Missing production template setting: ${key}`);
  }
});
