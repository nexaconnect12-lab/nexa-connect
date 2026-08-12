import { expect, it } from "vitest";
import { ApiError } from "@nexaconnect/api-client";
import { normalizeError } from "./index";

it("normalizes forbidden responses without leaking server detail", () => {
  expect(normalizeError(new ApiError(403, { detail: "internal policy name" }))).toEqual({ kind: "forbidden", message: "You do not have access to this action.", retryable: false });
});
