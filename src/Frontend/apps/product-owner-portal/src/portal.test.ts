import { describe, expect, it } from "vitest";
import { capabilitiesFor, parseAdminLinks } from "./portal";
describe("Product Owner Portal boundary", () => {
  it("keeps support away from platform mutations and product administration", () => { const c=capabilitiesFor(["platform-support"]); expect(c.has("support.request")).toBe(true); expect(c.has("organizations.manage")).toBe(false); expect(c.has("admin-links.open")).toBe(false); });
  it("allows only controlled same-origin or HTTPS links", () => { const links=parseAdminLinks("nexa|Nexa Admin|https://admin.example.test,evil|Bad|javascript:alert(1),local|Local|/admin","https://owner.example.test"); expect(links.map(x=>x.applicationCode)).toEqual(["nexa","local"]); });
});
