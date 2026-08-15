export const capabilitiesByRole: Readonly<Record<string, readonly string[]>> = {
  "platform-owner": ["summary.read", "organizations.manage", "memberships.manage", "products.manage", "subscriptions.manage", "hierarchy.manage", "product-roles.manage", "users.manage", "roles.read", "audit.read", "support.workflow", "support.request", "support.effective", "support.inspect", "support.approve", "admin-links.open"],
  "platform-admin": ["summary.read", "organizations.manage", "memberships.manage", "products.manage", "subscriptions.manage", "hierarchy.manage", "product-roles.manage", "users.manage", "roles.read", "audit.read", "support.workflow", "support.request", "support.effective", "support.inspect", "support.approve", "admin-links.open"],
  "platform-support": ["summary.read", "roles.read", "support.workflow", "support.request", "support.effective"],
  "platform-auditor": ["summary.read", "roles.read", "audit.read", "support.workflow", "support.inspect"]
};
export function capabilitiesFor(roles: readonly string[]): ReadonlySet<string> { return new Set(roles.flatMap(role => capabilitiesByRole[role] ?? [])); }
export interface ProductAdminLink { applicationCode: string; label: string; url: string }
export interface OrganizationOptionSource { organizationId: string; code: string; name: string; status: string }
export interface OrganizationOption { value: string; label: string }
export interface IdentityOptionSource { subjectId: string; username: string; email?: string; enabled: boolean }
export interface IdentityOption { value: string; label: string }
export function organizationOptions(organizations: readonly OrganizationOptionSource[]): OrganizationOption[] {
  return organizations
    .map(organization => ({
      value: organization.organizationId,
      label: `${organization.name} (${organization.code}) · ${organization.status}`
    }))
    .sort((left, right) => left.label.localeCompare(right.label));
}
export function identityOptions(users: readonly IdentityOptionSource[]): IdentityOption[] {
  return users
    .map(user => ({
      value: user.subjectId,
      label: `${user.username} · ${user.email?.trim() || "No email"} · ${user.enabled ? "Enabled" : "Disabled"}`
    }))
    .sort((left, right) => left.label.localeCompare(right.label));
}
export function membershipPath(organizationId: string, subjectId: string): string {
  return `/bff/platform-admin/organizations/${organizationId}/members/${encodeURIComponent(subjectId)}`;
}
export function parseAdminLinks(value: string | undefined, origin: string): ProductAdminLink[] {
  if (!value) return [];
  return value.split(",").flatMap(entry => {
    const [applicationCode, label, rawUrl] = entry.split("|").map(part => part?.trim());
    if (!applicationCode || !label || !rawUrl) return [];
    try { const url = new URL(rawUrl, origin); if (url.protocol !== "https:" && url.origin !== origin) return []; return [{ applicationCode, label, url: url.toString() }]; } catch { return []; }
  });
}
