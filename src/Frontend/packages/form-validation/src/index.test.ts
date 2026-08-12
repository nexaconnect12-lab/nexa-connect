import { describe, expect, it } from "vitest";
import { requiredText, validateForm, z } from "./index";

it("maps schema failures to fields", () => {
  const result = validateForm(z.object({ name: requiredText("Name") }), { name: " " });
  expect(result).toEqual({ success: false, errors: { name: ["Name is required"] } });
});
