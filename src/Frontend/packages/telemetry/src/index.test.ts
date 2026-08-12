import { expect, it, vi } from "vitest";
import { createTelemetry, sanitizeAttributes } from "./index";

it("drops attributes that could contain sensitive values", () => {
  expect(sanitizeAttributes({ route: "/orders/:id", accessToken: "secret", requestBody: "private" })).toEqual({ route: "/orders/:id" });
});

it("adds the portal service name", () => {
  const emit = vi.fn();
  const telemetry = createTelemetry("nexaconnect-customer-portal", { emit }, () => new Date("2026-01-01T00:00:00Z"));
  telemetry.pageView("/orders/:id");
  telemetry.event("ui.test", { service: "spoofed-portal" });
  expect(emit).toHaveBeenCalledWith(expect.objectContaining({ attributes: { service: "nexaconnect-customer-portal", route: "/orders/:id" } }));
  expect(emit).toHaveBeenCalledWith(expect.objectContaining({ attributes: { service: "nexaconnect-customer-portal" } }));
});
