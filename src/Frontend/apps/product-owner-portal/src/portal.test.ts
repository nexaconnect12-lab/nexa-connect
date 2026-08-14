import { describe, expect, it } from "vitest";
import { capabilitiesFor, parseAdminLinks } from "./portal";
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
});
