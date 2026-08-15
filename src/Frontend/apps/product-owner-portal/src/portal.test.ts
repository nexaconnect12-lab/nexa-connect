import { describe, expect, it } from "vitest";
import { capabilitiesFor, identityOptions, membershipPath, organizationOptions, parseAdminLinks } from "./portal";
describe("Product Owner Portal boundary", () => {
  it("keeps support away from platform mutations and product administration", () => { const c=capabilitiesFor(["platform-support"]); expect(c.has("support.request")).toBe(true); expect(c.has("organizations.manage")).toBe(false); expect(c.has("admin-links.open")).toBe(false); });
  it("separates support decisions from audit-only inspection", () => {
    const admin = capabilitiesFor(["platform-admin"]);
    const auditor = capabilitiesFor(["platform-auditor"]);
    expect(admin.has("support.approve")).toBe(true);
    expect(auditor.has("support.inspect")).toBe(true);
    expect(auditor.has("support.approve")).toBe(false);
    expect(auditor.has("support.request")).toBe(false);
  });
  it("allows only controlled same-origin or HTTPS links", () => { const links=parseAdminLinks("nexa|Nexa Admin|https://admin.example.test,evil|Bad|javascript:alert(1),local|Local|/admin","https://owner.example.test"); expect(links.map(x=>x.applicationCode)).toEqual(["nexa","local"]); });
  it("allows platform administrators to provision hierarchy and product roles", () => { const c=capabilitiesFor(["platform-admin"]); expect(c.has("hierarchy.manage")).toBe(true); expect(c.has("product-roles.manage")).toBe(true); });
  it("presents organizations by name while retaining their immutable IDs", () => {
    expect(organizationOptions([
      { organizationId: "org-z", code: "zeta", name: "Zeta Foods", status: "active" },
      { organizationId: "org-a", code: "acme", name: "Acme Dining", status: "suspended" }
    ])).toEqual([
      { value: "org-a", label: "Acme Dining (acme) · suspended" },
      { value: "org-z", label: "Zeta Foods (zeta) · active" }
    ]);
  });
  it("uses the selected organization ID in the unchanged membership route", () => {
    expect(membershipPath("org-a", "identity/subject")).toBe(
      "/bff/platform-admin/organizations/org-a/members/identity%2Fsubject"
    );
  });
  it("presents identity names and emails while retaining Keycloak subject IDs", () => {
    expect(identityOptions([
      { subjectId: "subject-z", username: "zoe", email: "zoe@example.test", enabled: true },
      { subjectId: "subject-a", username: "alex", enabled: false }
    ])).toEqual([
      { value: "subject-a", label: "alex · No email · Disabled" },
      { value: "subject-z", label: "zoe · zoe@example.test · Enabled" }
    ]);
  });
});
