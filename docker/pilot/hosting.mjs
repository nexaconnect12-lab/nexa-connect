// Dedicated-server artifact tooling. Never starts containers, changes DNS or reads .env.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { source, sameSource } from '../../scripts/release/source.mjs';

export const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const names = ['PlatformDirectory', 'Authorization', 'Restaurant', 'Catalog', 'Inventory', 'Order', 'Kitchen', 'Customer', 'Payment', 'POS', 'Media', 'Reporting', 'Notification'];
export const inventory = names.map(name => ({ name, id: name.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase(), project: `src/Services/NexaConnect.Services.${name}`, assembly: `NexaConnect.Services.${name}` }));
inventory.push(...[['CustomerBff', 'customer-portal', 'customer-portal'], ['PlatformAdminBff', 'owner-portal', 'product-owner-portal']].map(([name, id, spa]) => ({ name, id, spa, project: `src/Gateway/NexaConnect.${name}`, assembly: `NexaConnect.${name}` })));
const publicIds = ['customer-portal', 'owner-portal', 'pos', 'order', 'catalog', 'payment', 'media'];
const read = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const write = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx', mode: 0o600 });
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
export class Invalid extends Error {}
function check(ok, message) { if (!ok) throw new Invalid(message); }
function object(value) { return value && typeof value === 'object' && !Array.isArray(value); }
function configurationKeys(c) {
  const seen = new Set();
  for (const [key, value] of Object.entries(c)) {
    check(!key.includes(':') && !key.includes('__') && !seen.has(key.toLowerCase()), 'Ambiguous configuration keys are forbidden.'); seen.add(key.toLowerCase());
    if (object(value)) configurationKeys(value);
  }
}
function fresh(dir) { check(!fs.existsSync(dir), 'Output already exists; use a new directory.'); fs.mkdirSync(dir, { recursive: true, mode: 0o700 }); }
function get(c, key) { return key.split(':').reduce((v, k) => v?.[k], c); }
function dbName(s) { return s.name === 'PlatformDirectory' ? 'PlatformDirectory' : `NexaConnect_${s.name}`; }
function dbUser(s) { return s.name === 'PlatformDirectory' ? 'platform_directory_app' : `nexaconnect_${s.name.toLowerCase()}_app`; }
function secret(value, key) { check(typeof value === 'string' && value.length >= 32 && !/replace|example|change.me|development|local-dev|<|>/i.test(value), `Missing or placeholder secret: ${key}`); }
function https(value, key) {
  let u; try { u = new URL(value); } catch { throw new Invalid(`Invalid HTTPS URL: ${key}`); }
  check(u.protocol === 'https:' && !u.username && !u.password && !u.search && !u.hash && !['localhost', '127.0.0.1', '[::1]'].includes(u.hostname) && !/example|\.invalid$/i.test(u.hostname), `Invalid HTTPS URL: ${key}`);
  return u;
}
function host(value) { check(typeof value === 'string' && value.length <= 253 && value.includes('.') && value.split('.').every(v => /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/.test(v)), 'Invalid DNS host.'); https(`https://${value}`, 'host'); }
function image(value) { check(typeof value === 'string' && /^[a-zA-Z0-9][a-zA-Z0-9._/:-]+@sha256:[0-9a-f]{64}$/.test(value), 'Container images must be pinned by digest.'); }
function absoluteLinux(value, key) { check(typeof value === 'string' && /^\/[a-zA-Z0-9/_-]+$/.test(value) && !value.includes('/../'), `Invalid Linux container-host path: ${key}`); }
function connectionParts(value, separator, allowed, label) {
  check(typeof value === 'string', `Missing connection: ${label}`); const parts = {};
  for (const item of value.split(separator).filter(Boolean)) {
    const at = item.indexOf('='); check(at > 0, `Invalid connection syntax: ${label}`);
    const key = item.slice(0, at).trim().toLowerCase(), val = item.slice(at + 1).trim();
    check(allowed.includes(key) && !(key in parts), `Unknown or duplicate connection option: ${label}`); parts[key] = val;
  }
  return parts;
}

export function template() {
  return {
    version: 1, project: 'nexa-pilot-CHANGE', runtime: 'linux-x64', network: 'nexa-pilot-private',
    server: { confirmed: false, os: '', cpu: '', ramGiB: 0, freeDiskGiB: 0, location: '', uploadMbps: 0, existingWorkloads: '' },
    operations: { owner: '', backupDestination: '', retentionDays: 0, rpoMinutes: 0, rtoMinutes: 0, alertRecipient: '', maintenanceWindow: '' },
    issuer: 'https://identity.example.invalid/realms/nexa-prod',
    runtimeImage: 'REPLACE_WITH_ASPNET_10_LINUX_IMAGE_AND_SHA256', proxyImage: 'REPLACE_WITH_NGINX_IMAGE_AND_SHA256',
    deploymentRoot: '/srv/nexaconnect/pilot', secretRoot: '/srv/nexaconnect/private', stateRoot: '/srv/nexaconnect/state',
    bindAddress: '0.0.0.0', httpsPort: 443,
    services: Object.fromEntries(inventory.map(s => [s.id, { host: `${s.id}.example.invalid`, memoryMiB: 512, cpus: 0.5 }])),
    acceptance: { linuxContainerHostReady: false, infrastructurePrivate: false, identityReady: false, migrationsReviewed: false, queueBindingsReady: false, certificateTrustReady: false }
  };
}

export function validatePlan(p) {
  check(object(p) && p.version === 1, 'Unsupported plan version.');
  check(/^nexa-pilot-[a-z0-9-]{1,40}$/.test(p.project), 'Use a distinct nexa-pilot-<name> project.');
  check(['linux-x64', 'linux-arm64'].includes(p.runtime), 'Supported container runtime: linux-x64 or linux-arm64.');
  check(p.server?.confirmed === true, 'Server facts remain unconfirmed.');
  for (const k of ['os', 'cpu', 'location', 'existingWorkloads']) check(typeof p.server[k] === 'string' && p.server[k].trim().length > 0, `Missing server fact: ${k}`);
  for (const k of ['ramGiB', 'freeDiskGiB', 'uploadMbps']) check(Number.isFinite(p.server[k]) && p.server[k] > 0, `Missing server measurement: ${k}`);
  for (const k of ['owner', 'backupDestination', 'alertRecipient', 'maintenanceWindow']) check(typeof p.operations?.[k] === 'string' && p.operations[k].trim().length > 0, `Missing operations prerequisite: ${k}`);
  check(!/^(\/|[a-z]:\\|localhost)/i.test(p.operations.backupDestination), 'Backup destination must identify an off-server target.');
  for (const k of ['retentionDays', 'rpoMinutes', 'rtoMinutes']) check(Number.isInteger(p.operations[k]) && p.operations[k] > 0, `Missing recovery objective: ${k}`);
  const issuer = https(p.issuer, 'issuer'); check(/^\/realms\/[a-zA-Z0-9_-]+$/.test(issuer.pathname), 'Issuer must be an exact realm URL without trailing slash.');
  image(p.runtimeImage); image(p.proxyImage);
  for (const k of ['deploymentRoot', 'secretRoot', 'stateRoot']) absoluteLinux(p[k], k);
  const dirs = [p.deploymentRoot, p.secretRoot, p.stateRoot];
  check(dirs.every((a, i) => dirs.every((b, j) => i === j || !(a === b || a.startsWith(b + '/') || b.startsWith(a + '/')))), 'Deployment, secret and state roots must be separate.');
  check(/^nexa-pilot-[a-z0-9-]+$/.test(p.network), 'Use a dedicated pilot infrastructure network.');
  check(['0.0.0.0', '127.0.0.1'].includes(p.bindAddress) && Number.isInteger(p.httpsPort) && p.httpsPort >= 1 && p.httpsPort <= 65535, 'Invalid HTTPS binding.');
  check(object(p.services) && Object.keys(p.services).sort().join() === inventory.map(s => s.id).sort().join(), 'Service inventory differs from the pilot graph.');
  const hosts = new Set([issuer.hostname]); let memory = 0;
  for (const s of inventory) {
    const v = p.services[s.id]; host(v.host); check(!hosts.has(v.host), 'Service and identity hosts must be distinct.'); hosts.add(v.host);
    check(Number.isInteger(v.memoryMiB) && v.memoryMiB >= 256 && v.memoryMiB <= 32768 && Number.isFinite(v.cpus) && v.cpus >= 0.1 && v.cpus <= 64, `Invalid resource budget: ${s.id}`); memory += v.memoryMiB;
  }
  check(memory < p.server.ramGiB * 1024, 'Application memory leaves no measured capacity for infrastructure/OS.');
  const gates = ['linuxContainerHostReady', 'infrastructurePrivate', 'identityReady', 'migrationsReviewed', 'queueBindingsReady', 'certificateTrustReady'];
  for (const gate of gates) check(p.acceptance?.[gate] === true, `Deployment prerequisite unconfirmed: ${gate}`);
  return p;
}

export function settings(s, p) {
  const c = read(path.join(root, s.project, 'appsettings.json'));
  c.AllowedHosts = p.services[s.id].host;
  c.Tls = { CertificatePath: '/run/private/tls.pfx', CertificatePassword: 'REPLACE_FROM_SECRET_MANAGER' };
  c.DataProtection = { KeyDirectory: '/var/lib/nexaconnect/keys', CertificatePath: '/run/private/data-protection.pfx', CertificatePassword: 'REPLACE_FROM_SECRET_MANAGER', ApplicationName: `nexaconnect-${s.id}` };
  c.Observability = { OtlpEnabled: true, OtlpEndpoint: 'https://collector.example.invalid:4317' };
  c.Authentication = { ...c.Authentication, Authority: p.issuer, Audience: 'nexaconnect-api', RequireHttpsMetadata: true, ClockSkewSeconds: 30 };
  if (s.spa === 'customer-portal') c.Services.Notification = 'https://notification.example.invalid/';
  if (s.name === 'Notification') c.Services = { PlatformDirectory: '', Authorization: '' };
  for (const key of Object.keys(c.Services ?? {})) { const dep = inventory.find(v => v.name === key); if (dep) c.Services[key] = `https://${p.services[dep.id].host}:8443/`; }
  if (s.spa) {
    c.Bff = { ...c.Bff, Authority: p.issuer, RequireHttpsMetadata: true, ClientSecret: 'REPLACE_FROM_SECRET_MANAGER' };
    c.ConnectionStrings = { BffSessionCache: 'redis.private:6380,ssl=true,password=REPLACE_FROM_SECRET_MANAGER,abortConnect=false' };
    c.BffSessionCache = { InstanceName: `nexa:${s.id}:` };
  } else {
    c.Persistence = { Provider: 'PostgreSQL' };
    c.ConnectionStrings = { [s.name]: `Host=postgres.private;Database=${dbName(s)};Username=${dbUser(s)};Password=REPLACE_FROM_SECRET_MANAGER;SSL Mode=VerifyFull` };
    if (!['Authorization', 'Reporting'].includes(s.name)) c.Outbox = { ...c.Outbox, Enabled: true, ConnectionString: 'amqps://REPLACE_FROM_SECRET_MANAGER', Exchange: 'nexaconnect.events' };
  }
  if (c.WorkloadIdentity) c.WorkloadIdentity = { ...c.WorkloadIdentity, Authority: p.issuer, ClientSecret: 'REPLACE_FROM_SECRET_MANAGER' };
  if (s.name === 'PlatformDirectory') c.KeycloakAdmin = { ...c.KeycloakAdmin, BaseUrl: 'https://identity-admin.example.invalid/', Realm: new URL(p.issuer).pathname.split('/').pop(), ClientSecret: 'REPLACE_FROM_SECRET_MANAGER' };
  if (s.name === 'Order') {
    c.Authentication = { ...c.Authentication, TokenEndpoint: `${p.issuer}/protocol/openid-connect/token`, ClientId: 'nexaconnect-order-service', ClientSecret: 'REPLACE_FROM_SECRET_MANAGER' };
    c.Workflow = { ...c.Workflow, UseHttpAdapters: true }; c.WorkflowRecovery.Enabled = true;
    c.CardCheckout = { EnableOmiseTestCheckout: false };
  }
  if (s.name === 'POS') { c.SettlementRecovery.Enabled = true; c.CashClosePublication.Enabled = true; }
  if (s.name === 'Payment') c.PaymentProvider = { Adapter: 'Disabled' };
  if (s.name === 'Media') { c.MediaSafety.MalwareScanEnabled = true; c.MediaSafety.ClamAvHost = 'clamav.private'; c.MediaStorage.AccessKey = 'REPLACE_FROM_SECRET_MANAGER'; c.MediaStorage.SecretKey = 'REPLACE_FROM_SECRET_MANAGER'; }
  for (const key of [...Object.keys(c).filter(k => k.endsWith('Consumer')), ...(s.name === 'Reporting' ? ['SettlementConsumer'] : []), ...(s.name === 'Notification' ? ['NotificationConsumer'] : [])]) c[key] = { ...c[key], Enabled: true, ConnectionString: 'amqps://REPLACE_FROM_SECRET_MANAGER' };
  return c;
}

function flatten(c, prefix = '') { return Object.entries(c).flatMap(([k, v]) => object(v) ? flatten(v, prefix + k + ':') : [[prefix + k, v]]); }
export function validateSettings(s, c, p) {
  check(object(c), `Invalid settings: ${s.id}`);
  configurationKeys(c);
  const expected = settings(s, p);
  for (const [key, v] of flatten(expected)) {
    const actual = get(c, key);
    if (typeof v === 'boolean') check(actual === v, `Unsafe switch: ${s.id}:${key}`);
    else if (key.endsWith('ClientSecret') || key.endsWith('CertificatePassword') || key === 'MediaStorage:AccessKey' || key === 'MediaStorage:SecretKey') secret(actual, `${s.id}:${key}`);
    else if (key === 'ConnectionStrings:BffSessionCache') {
      const [endpoint, ...options] = typeof actual === 'string' ? actual.split(',') : [];
      check(/^[a-z0-9.-]+:[0-9]+$/i.test(endpoint ?? '') && !/localhost|127\.0\.0\.1/i.test(endpoint), `Redis endpoint required: ${s.id}`);
      const parts = connectionParts(options.join(','), ',', ['ssl', 'password', 'abortconnect', 'sslhost', 'connecttimeout', 'synctimeout', 'user'], s.id);
      check(parts.ssl === 'true', `TLS Redis session configuration required: ${s.id}`); secret(parts.password, s.id);
    }
    else if (key === `ConnectionStrings:${s.name}`) {
      const parts = connectionParts(actual, ';', ['host', 'port', 'database', 'username', 'password', 'ssl mode', 'root certificate', 'timeout', 'command timeout', 'maximum pool size', 'pooling'], s.id);
      check(parts['ssl mode'] === 'VerifyFull' && !!parts.host && !/replace|example|localhost|127\.0\.0\.1/i.test(parts.host), `Verified TLS runtime database required: ${s.id}`);
      check(parts.database === dbName(s) && parts.username === dbUser(s), `Database ownership mismatch: ${s.id}`); secret(parts.password, s.id);
    }
    else if (key.endsWith(':ConnectionString')) { let u; try { u = new URL(actual); } catch {} check(u?.protocol === 'amqps:' && !!u.username && !['guest', 'admin'].includes(u.username) && !u.search && !u.hash && !/replace|example/i.test(actual), `TLS broker credential required: ${s.id}:${key}`); secret(decodeURIComponent(u.password), s.id); }
    else if (typeof v === 'string' && v.startsWith('https://')) {
      https(actual, `${s.id}:${key}`);
      if (key.startsWith('Services:') || key.endsWith(':Authority') || key === 'Authentication:TokenEndpoint') check(actual === v, `Endpoint mismatch: ${s.id}:${key}`);
    }
    else if (key === 'Persistence:Provider' || key === 'AllowedHosts' || key.startsWith('DataProtection:') || key.startsWith('BffSessionCache:') || key.startsWith('Tls:') || key.endsWith(':ClientId') || key === 'Authentication:Audience' || key.endsWith(':Exchange') || key.endsWith(':Queue')) check(actual === v, `Scope/configuration mismatch: ${s.id}:${key}`);
    else check(actual !== undefined, `Missing configuration: ${s.id}:${key}`);
  }
  for (const [key, value] of flatten(c)) {
    check(!/^(Kestrel|urls|ASPNETCORE_|DOTNET_)/i.test(key), `Unsupported listener override: ${s.id}`);
    check(key !== 'Authentication:OutboundToken', `Static workload tokens are not supported: ${s.id}`);
    if (typeof value === 'string') check(!/REPLACE_|example\.invalid|local-dev-only|http:\/\/localhost|http:\/\/127\.0\.0\.1/i.test(value), `Unresolved deployment setting: ${s.id}:${key}`);
  }
  check(get(c, 'PaymentProvider:Adapter') === undefined || get(c, 'PaymentProvider:Adapter') === 'Disabled', 'Real/test card provider is outside the pilot scope.');
  check(get(c, 'OmiseWebhooks:Enabled') !== true, 'Provider webhooks are outside the pilot scope.');
  return c;
}

export function initialize(dir) {
  fresh(dir); const p = template(); write(path.join(dir, 'plan.json'), p);
  for (const s of inventory) { const d = path.join(dir, s.id); fs.mkdirSync(d, { mode: 0o700 }); write(path.join(d, 'appsettings.Production.json'), settings(s, p)); }
}

export function files(dir, prefix = '') {
  return fs.readdirSync(dir, { withFileTypes: true }).sort((a,b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0).flatMap(e => {
    check(!e.isSymbolicLink(), 'Symlinks are not allowed in deployment input.'); const rel = prefix + e.name;
    return e.isDirectory() ? files(path.join(dir, e.name), rel + '/') : [{ path: rel, sha256: hash(fs.readFileSync(path.join(dir, e.name))) }];
  });
}
export function verifyBundle(dir) {
  const m = read(path.join(dir, 'manifest.json'));
  check(m.version === 1 && m.runtime && m.files && m.schemas && /^[0-9a-f]{40}$/.test(m.sourceRevision), 'Invalid bundle manifest.');
  const expected = [...m.files].sort((a,b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
  const actual = files(dir).filter(f => f.path !== 'manifest.json').sort((a,b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
  check(JSON.stringify(actual) === JSON.stringify(expected), 'Bundle file set or checksum changed.');
  for (const s of inventory) check(fs.existsSync(path.join(dir, s.id, s.assembly + '.dll')) && (!s.spa || fs.existsSync(path.join(dir, s.id, 'wwwroot/index.html'))), `Incomplete bundle: ${s.id}`);
  return m;
}
function run(command, args, cwd) {
  // No arguments carry secrets; do not echo tool output that might include local environment.
  const env = Object.fromEntries(Object.entries(process.env).filter(([k]) => !/^(VITE_|ASPNETCORE_|DOTNET_ENVIRONMENT|ConnectionStrings__|NEXACONNECT_)/i.test(k)));
  const r = spawnSync(command, args, { cwd, env, stdio: ['ignore', 'pipe', 'pipe'], shell: false, timeout: 600000, maxBuffer: 16 * 1024 * 1024 });
  check(r.status === 0, `Build command failed: ${command}; inspect locally with the documented build command.`);
}
export function build(dir, runtime = 'linux-x64') {
  check(['linux-x64', 'linux-arm64'].includes(runtime), 'Invalid runtime.');
  for (const d of ['src/Frontend', 'src/Frontend/apps/customer-portal', 'src/Frontend/apps/product-owner-portal']) check(!fs.readdirSync(path.join(root, d)).some(f => /^\.env(?:\.|$)/.test(f)), 'Remove frontend .env inputs before building a portable bundle.');
  fresh(dir);
  const releaseSource = source();
  const sourceInput = () => {
    const r = spawnSync('git', ['ls-files', '-co', '--exclude-standard', '-z'], { cwd: root, encoding: 'utf8' });
    check(r.status === 0, 'Cannot inventory source inputs.');
    return hash(JSON.stringify([...new Set(r.stdout.split('\0').filter(f => f && !f.endsWith('.md')))].sort().map(f => ({ path: f, sha256: hash(fs.readFileSync(path.join(root, f))) }))));
  };
  const sourceInputSha256 = sourceInput();
  const npm = process.platform === 'win32' ? 'npm.cmd' : 'npm';
  // npm.cmd cannot be invoked without a shell on Windows: use the installed npm CLI via node.
  if (process.platform === 'win32') {
    const npmCli = path.join(path.dirname(process.execPath), 'node_modules/npm/bin/npm-cli.js');
    run(process.execPath, [npmCli, 'run', 'build'], path.join(root, 'src/Frontend'));
  } else run(npm, ['run', 'build'], path.join(root, 'src/Frontend'));
  for (const s of [...inventory, { id: 'migrations', project: 'src/Tools/NexaConnect.DataMigration' }]) {
    const out = path.join(dir, s.id);
    run('dotnet', ['publish', s.project, '-c', 'Release', '-r', runtime, '--self-contained', 'false', '-o', out], root);
    for (const f of fs.readdirSync(out)) if (/^appsettings\.(Development|Testing|Test|Local)\.json$/.test(f)) fs.unlinkSync(path.join(out, f));
    if (s.spa) fs.cpSync(path.join(root, 'src/Frontend/apps', s.spa, 'dist'), path.join(out, 'wwwroot'), { recursive: true });
  }
  const schemas = {};
  for (const n of names) {
    const catalog = path.join(dir, 'migrations/Scripts', n);
    schemas[n] = Math.max(...fs.readdirSync(catalog).map(d => read(path.join(catalog, d, 'migration.json')).version));
  }
  const revision = spawnSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' });
  check(revision.status === 0, 'Cannot bind source revision.');
  const status = spawnSync('git', ['status', '--porcelain'], { cwd: root, encoding: 'utf8' });
  check(status.status === 0 && sourceInput() === sourceInputSha256 && sameSource(releaseSource, source()), 'Source changed during build; use a new bundle directory.');
  write(path.join(dir, 'manifest.json'), { version: 1, createdAtUtc: new Date().toISOString(), sourceRevision: revision.stdout.trim(), sourceInputSha256, sourceDirty: status.stdout.length > 0, releaseSource, runtime, schemas, files: files(dir) });
  return verifyBundle(dir);
}

export function validateInputs(p, secure) {
  validatePlan(p);
  const credentials = new Set();
  for (const s of inventory) {
    const d = path.join(secure, s.id), c = read(path.join(d, 'appsettings.Production.json')); validateSettings(s, c, p);
    // The Order service intentionally uses its same client in two outbound adapters.
    const owned = [...new Set([get(c, 'Bff:ClientSecret'), get(c, 'WorkloadIdentity:ClientSecret'), get(c, 'KeycloakAdmin:ClientSecret'), get(c, 'Authentication:ClientSecret')].filter(Boolean))];
    if (!s.spa) owned.push(connectionParts(c.ConnectionStrings[s.name], ';', ['host', 'port', 'database', 'username', 'password', 'ssl mode', 'root certificate', 'timeout', 'command timeout', 'maximum pool size', 'pooling'], s.id).password);
    for (const value of owned) { check(!credentials.has(value), 'Database and confidential-client credentials must be independent across service scopes.'); credentials.add(value); }
    for (const f of ['tls.pfx', 'data-protection.pfx']) check(fs.statSync(path.join(d, f)).isFile(), `Missing certificate: ${s.id}:${f}`);
  }
  for (const f of ['proxy.crt', 'proxy.key']) check(fs.statSync(path.join(secure, f)).isFile(), 'Missing proxy TLS file.');
}

export function proxyConfig(p) {
  const servers = publicIds.map(id => {
    const h = p.services[id].host;
    const locations = id.endsWith('portal')
      ? 'location / { proxy_pass https://backend; }'
      : `location ~* /internal(?:/|$) { return 404; }\n    location /api/${id}/ { proxy_pass https://backend; }\n    location / { return 404; }`;
    return `server {\n    listen 8443 ssl; server_name ${h};\n    ssl_certificate /run/private/proxy.crt; ssl_certificate_key /run/private/proxy.key;\n    ssl_protocols TLSv1.2 TLSv1.3;\n    proxy_ssl_verify on; proxy_ssl_trusted_certificate /etc/ssl/certs/ca-certificates.crt;\n    proxy_ssl_server_name on; proxy_ssl_name ${h};\n    proxy_set_header Host ${h}${p.httpsPort === 443 ? '' : ':' + p.httpsPort}; proxy_set_header X-Forwarded-For ""; proxy_set_header X-Forwarded-Host ""; proxy_set_header X-Forwarded-Proto "";\n    proxy_set_header Forwarded ""; proxy_set_header Connection ""; proxy_http_version 1.1;\n    proxy_connect_timeout 5s; proxy_read_timeout 60s; proxy_send_timeout 60s;\n    location ~ ^/(health|metrics|openapi|swagger)(/|$) { return 404; }\n    ${locations.replaceAll('https://backend', `https://${h}:8443`)}\n  }`;
  });
  return `pid /tmp/nginx.pid;\nevents { worker_connections 1024; }\nhttp {\n  access_log off; error_log /dev/stderr crit;\n  client_max_body_size 24m; client_body_temp_path /tmp/client; proxy_temp_path /tmp/proxy;\n  server { listen 8443 ssl default_server; ssl_reject_handshake on; }\n  ${servers.join('\n  ')}\n}\n`;
}

export function compose(p) {
  const services = {};
  for (const s of inventory) {
    const v = p.services[s.id];
    services[s.id] = {
      image: `${p.project}/${s.id}:candidate`, build: { context: `${p.deploymentRoot}/bundle/${s.id}`, dockerfile: '../Containerfile', args: { RUNTIME_IMAGE: p.runtimeImage } },
      command: ['dotnet', `/app/${s.assembly}.dll`], working_dir: '/app', user: '1654:1654', read_only: true,
      environment: { ASPNETCORE_ENVIRONMENT: 'Production', DOTNET_ENVIRONMENT: 'Production', ASPNETCORE_URLS: 'https://+:8443' },
      volumes: [
        { type: 'bind', source: `${p.secretRoot}/${s.id}/appsettings.Production.json`, target: '/app/appsettings.Production.json', read_only: true, bind: { create_host_path: false } },
        { type: 'bind', source: `${p.secretRoot}/${s.id}`, target: '/run/private', read_only: true, bind: { create_host_path: false } },
        { type: 'bind', source: `${p.stateRoot}/${s.id}/keys`, target: '/var/lib/nexaconnect/keys', bind: { create_host_path: false } }
      ],
      tmpfs: ['/tmp:rw,nosuid,noexec,size=64m'], cap_drop: ['ALL'], security_opt: ['no-new-privileges:true'],
      restart: 'unless-stopped', stop_grace_period: '60s', init: true, pids_limit: 256, mem_limit: `${v.memoryMiB}m`, cpus: v.cpus,
      logging: { driver: 'local', options: { 'max-size': '10m', 'max-file': '3' } }, networks: { private: { aliases: [v.host] } }
    };
    if (s.spa) services[s.id].depends_on = Object.fromEntries(names.map(n => [inventory.find(v => v.name === n).id, { condition: 'service_started' }]));
  }
  services.edge = { image: p.proxyImage, command: ['nginx', '-g', 'daemon off;', '-c', '/etc/nginx/pilot.conf'], user: '1654:1654', read_only: true, tmpfs: ['/tmp:rw,nosuid,noexec,size=64m'], cap_drop: ['ALL'], security_opt: ['no-new-privileges:true'], restart: 'unless-stopped', pids_limit: 64, mem_limit: '256m', cpus: 0.5,
    ports: [`${p.bindAddress}:${p.httpsPort}:8443`],
    volumes: ['proxy.crt', 'proxy.key'].map(f => ({ type: 'bind', source: `${p.secretRoot}/${f}`, target: `/run/private/${f}`, read_only: true, bind: { create_host_path: false } })).concat([{ type: 'bind', source: `${p.deploymentRoot}/nginx.conf`, target: '/etc/nginx/pilot.conf', read_only: true, bind: { create_host_path: false } }]),
    networks: ['private'], depends_on: Object.fromEntries(publicIds.map(id => [id, { condition: 'service_started' }])), logging: { driver: 'local', options: { 'max-size': '10m', 'max-file': '3' } }
  };
  return { name: p.project, services, networks: { private: { external: true, name: p.network } } };
}

export function render(p, secure, bundle, out) {
  validateInputs(p, secure); const m = verifyBundle(bundle); check(m.runtime === p.runtime, 'Bundle runtime does not match plan.'); fresh(out);
  write(path.join(out, 'compose.json'), compose(p)); fs.writeFileSync(path.join(out, 'nginx.conf'), proxyConfig(p), { flag: 'wx' });
  fs.writeFileSync(path.join(out, 'Containerfile'), 'ARG RUNTIME_IMAGE\nFROM ${RUNTIME_IMAGE}\nWORKDIR /app\nCOPY . /app/\nUSER 1654:1654\n', { flag: 'wx' });
  write(path.join(out, 'receipt.json'), { version: 1, generatedAtUtc: new Date().toISOString(), project: p.project, planSha256: hash(JSON.stringify(p)), bundleManifestSha256: hash(fs.readFileSync(path.join(bundle, 'manifest.json'))), deploymentExecuted: false });
}

export async function probe(p, out, request = fetch) {
  validatePlan(p); check(!fs.existsSync(out), 'Probe report already exists.'); const results = [];
  const origin = id => `https://${p.services[id].host}${p.httpsPort === 443 ? '' : ':' + p.httpsPort}`;
  const cases = [
    ['customer-shell', `${origin('customer-portal')}/`, [200]],
    ['kitchen-shell', `${origin('customer-portal')}/kitchen`, [200]],
    ['owner-shell', `${origin('owner-portal')}/`, [200]],
    ['customer-denied', `${origin('customer-portal')}/bff/customer/me`, [401]],
    ['owner-denied', `${origin('owner-portal')}/bff/platform-admin/me`, [401]],
    ...publicIds.map(id => [`${id}-management-hidden`, `${origin(id)}/metrics`, [404]]),
    ['identity-discovery', `${p.issuer}/.well-known/openid-configuration`, [200]]
  ];
  for (const [id, url, expected] of cases) {
    let passed = false, status = null; const correlationId = randomUUID();
    try {
      const r = await request(url, { redirect: 'manual', signal: AbortSignal.timeout(10000), headers: { 'X-Correlation-ID': correlationId } }); status = r.status;
      passed = expected.includes(status);
      if (id === 'identity-discovery' && passed) {
        check(Number(r.headers.get('content-length') ?? 0) <= 262144, 'Discovery too large.');
        const reader = r.body.getReader(); let length = 0; const chunks = [];
        try { for (;;) { const { done, value } = await reader.read(); if (done) break; length += value.length; check(length <= 262144, 'Discovery too large.'); chunks.push(value); } }
        finally { await reader.cancel(); }
        const discovery = JSON.parse(Buffer.concat(chunks).toString());
        passed = discovery.issuer === p.issuer && discovery.authorization_endpoint === `${p.issuer}/protocol/openid-connect/auth` && discovery.token_endpoint === `${p.issuer}/protocol/openid-connect/token`;
      } else await r.body?.cancel();
    } catch { passed = false; }
    results.push({ id, correlationId, status, passed });
  }
  const report = { version: 1, atUtc: new Date().toISOString(), project: p.project, results, passed: results.every(r => r.passed), scope: 'Anonymous HTTPS edge checks only; no database, authenticated business, reboot, restore or user acceptance proof.' };
  write(out, report); return report;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [cmd, ...args] = process.argv.slice(2);
    if (cmd === 'init' && args.length === 1) initialize(path.resolve(args[0]));
    else if (cmd === 'build' && [1, 2].includes(args.length)) build(path.resolve(args[0]), args[1]);
    else if (cmd === 'validate' && args.length === 2) validateInputs(read(args[0]), args[1]);
    else if (cmd === 'render' && args.length === 4) render(read(args[0]), args[1], args[2], args[3]);
    else if (cmd === 'probe' && args.length === 2) { const r = await probe(read(args[0]), args[1]); if (!r.passed) process.exitCode = 1; }
    else throw new Invalid('Usage: hosting.mjs init <new-config-dir> | build <new-bundle-dir> [linux-x64|linux-arm64] | validate <plan.json> <secure-dir> | render <plan.json> <secure-dir> <bundle-dir> <new-output-dir> | probe <plan.json> <new-report.json>');
    if (!process.exitCode) process.stdout.write('Pilot operation completed. No deployment was performed.\n');
  } catch (e) { process.stderr.write((e instanceof Invalid ? e.message : 'Pilot operation failed; inspect private inputs locally. No raw exception or configuration emitted.') + '\n'); process.exitCode = 1; }
}
