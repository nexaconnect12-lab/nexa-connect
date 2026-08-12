import { describe, expect, it } from "vitest";
import { createCapabilityEvaluator } from "./index";

describe("createCapabilityEvaluator", () => {
  it("exposes only capabilities supplied by the owning portal", () => {
    const can = createCapabilityEvaluator(new Set(["catalog.read"]));
    expect(can("catalog.read")).toBe(true);
    expect(can("platform.users.write")).toBe(false);
  });
});
