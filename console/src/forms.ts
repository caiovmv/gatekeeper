export function required(value: string): string {
  return value.trim() ? "" : "required";
}

export function emailOf(value: string): string {
  const text = value.trim();
  if (!text) {
    return "required";
  }

  return text.includes("@") ? "" : "email";
}

export function passwordOf(value: string, optional: boolean): string {
  if (!value && optional) {
    return "";
  }

  return value.trim().length >= 8 ? "" : "password";
}

export function whole(value: string, min: number): string {
  if (!/^\d+$/.test(value.trim())) {
    return "whole";
  }

  return Number(value) >= min ? "" : "min:" + min;
}

export function amount(value: string): string {
  return parseAmount(value) === null ? "amount" : "";
}

export function parseAmount(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  const normalized = trimmed.includes(",") ? trimmed.replace(/\./g, "").replace(",", ".") : trimmed;
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) {
    return null;
  }

  return Number(normalized);
}

export function problems(checks: Record<string, string>): Record<string, string> {
  return Object.fromEntries(Object.entries(checks).filter(([, value]) => value.length > 0));
}

export function invalid(errors: Record<string, string>): boolean {
  return Object.keys(errors).length > 0;
}

export function contains(value: unknown, focus: string): boolean {
  const needle = focus.trim().toLowerCase();
  if (!needle) {
    return true;
  }

  return JSON.stringify(value).toLowerCase().includes(needle);
}
