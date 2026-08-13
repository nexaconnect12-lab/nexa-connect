export const capabilitiesByRole: Readonly<Record<string, readonly string[]>> = {
  "platform-owner": ["summary.read", "organizations.manage", "memberships.manage", "products.manage", "subscriptions.manage", "users.manage", "roles.read", "audit.read", "support.workflow", "support.request", "support.effective", "support.inspect", "support.approve", "admin-links.open"],
  "platform-admin": ["summary.read", "organizations.manage", "memberships.manage", "products.manage", "subscriptions.manage", "users.manage", "roles.read", "audit.read", "support.workflow", "support.request", "support.effective", "support.inspect", "support.approve", "admin-links.open"],
  "platform-support": ["summary.read", "roles.read", "support.workflow", "support.request", "support.effective"],
  "platform-auditor": ["summary.read", "roles.read", "audit.read", "support.workflow", "support.inspect"]
};
export function capabilitiesFor(roles: readonly string[]): ReadonlySet<string> { return new Set(roles.flatMap(role => capabilitiesByRole[role] ?? [])); }
export interface ProductAdminLink { applicationCode: string; label: string; url: string }
export function parseAdminLinks(value: string | undefined, origin: string): ProductAdminLink[] {
  if (!value) return [];
  return value.split(",").flatMap(entry => {
    const [applicationCode, label, rawUrl] = entry.split("|").map(part => part?.trim());
    if (!applicationCode || !label || !rawUrl) return [];
    try { const url = new URL(rawUrl, origin); if (url.protocol !== "https:" && url.origin !== origin) return []; return [{ applicationCode, label, url: url.toString() }]; } catch { return []; }
  });
}
