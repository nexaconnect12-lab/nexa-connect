import { expect, it } from "vitest";
import { createTranslator } from "./index";

it("falls back and interpolates named values", () => {
  expect(createTranslator({}, { welcome: "Welcome, {name}" })("welcome", { name: "Mya" })).toBe("Welcome, Mya");
});
