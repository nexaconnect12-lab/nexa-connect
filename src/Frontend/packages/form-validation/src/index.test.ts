import { describe, expect, it } from "vitest";
import { requiredText, validateForm, z } from "./index";

it("maps schema failures to fields", () => {
  const result = validateForm(z.object({ name: requiredText("Name") }), { name: " " });
  expect(result).toEqual({ success: false, errors: { name: ["Name is required"] } });
});

it("preserves .NET GUID contracts without accepting malformed identities", () => {
  expect(z.guid().safeParse("11111111-1111-1111-1111-111111111111").success).toBe(true);
  expect(z.guid().safeParse("11111111-1111-4111-8111-111111111111").success).toBe(true);
  for (const value of ["", "../orders", "11111111111111111111111111111111", "x11111111-1111-1111-1111-111111111111", 12, null])
    expect(z.guid().safeParse(value).success).toBe(false);
});

it("rejects oversized constrained collections while retaining valid values and field errors", () => {
  const schema=z.object({items:z.array(z.number().int().positive()).max(50)});
  expect(validateForm(schema,{items:Array(50).fill(1)}).success).toBe(true);
  const result=validateForm(schema,{items:Array(10000).fill(1)});
  expect(result.success).toBe(false);
  if(!result.success)expect(Object.keys(result.errors)).toEqual(["items"]);
  expect(validateForm(schema,{items:[-1]}).success).toBe(false);
});
