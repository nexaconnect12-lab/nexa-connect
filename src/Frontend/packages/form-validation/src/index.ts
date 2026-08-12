import { z, type ZodError, type ZodType } from "zod";

export type FieldErrors<T> = Partial<Record<Extract<keyof T, string>, string[]>>;

export function toFieldErrors<T>(error: ZodError<T>): FieldErrors<T> {
  const fields: Record<string, string[]> = {};
  for (const issue of error.issues) {
    const field = String(issue.path[0] ?? "form");
    (fields[field] ??= []).push(issue.message);
  }
  return fields as FieldErrors<T>;
}

export function validateForm<T>(schema: ZodType<T>, value: unknown) {
  const result = schema.safeParse(value);
  return result.success ? { success: true as const, data: result.data } : { success: false as const, errors: toFieldErrors(result.error) };
}

export const requiredText = (label: string, max = 200) => z.string().trim().min(1, `${label} is required`).max(max, `${label} is too long`);
export { z };
