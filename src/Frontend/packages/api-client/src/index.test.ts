import { describe, expect, it, vi } from "vitest";
import { ApiError, createApiClient } from "./index";

describe("createApiClient", () => {
  it("uses BFF cookies and propagates a safe correlation identifier", async () => {
    const fetcher = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(JSON.stringify({ ok: true }), { headers: { "content-type": "application/json" } }));
    const client = createApiClient({ fetch: fetcher as typeof fetch, correlationId: () => "trace-1" });
    await client.request("/api/test", { headers: { Authorization: "Bearer must-not-leave-browser", Cookie: "must-not-be-forwarded" } });
    expect(fetcher).toHaveBeenCalledWith("/api/test", expect.objectContaining({ credentials: "same-origin" }));
    expect(fetcher.mock.calls[0]?.[1]?.headers).toSatisfy((headers: Headers) => headers.get("X-Correlation-ID") === "trace-1");
    expect(fetcher.mock.calls[0]?.[1]?.headers).toSatisfy((headers: Headers) => !headers.has("Authorization") && !headers.has("Cookie"));
  });

  it("returns RFC 7807 failures as ApiError", async () => {
    const fetcher = async () => new Response(JSON.stringify({ title: "Denied" }), { status: 403, headers: { "content-type": "application/problem+json" } });
    await expect(createApiClient({ fetch: fetcher as typeof fetch }).request("/api/test")).rejects.toBeInstanceOf(ApiError);
    await expect(createApiClient({ fetch: fetcher as typeof fetch }).request("/api/test")).rejects.toMatchObject({ status: 403 });
  });
});
