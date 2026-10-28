import { clearCache } from "./cache";

let current = "";

export function readToken(): string {
  return current;
}

export function storeToken(token: string): void {
  if (token !== current) {
    clearCache();
  }

  current = token;
  sessionStorage.removeItem("token");
}

export function clearToken(): void {
  current = "";
  sessionStorage.removeItem("token");
  clearCache();
}

export function roleOf(token: string): string {
  return roleFrom(claims(token));
}

export function roleFrom(roles: string[]): string {
  if (roles.includes("Staff.Admin") || roles.includes("admin")) {
    return "admin";
  }

  if (roles.includes("Staff.Support") || roles.includes("support")) {
    return "support";
  }

  if (roles.includes("Staff.Auditor") || roles.includes("auditor")) {
    return "auditor";
  }

  return roles[0] ?? "";
}

export function canEnter(role: string): boolean {
  return role === "admin" || role === "support" || role === "auditor";
}

export function canWrite(role: string): boolean {
  return role === "admin";
}

export function canGovern(role: string): boolean {
  return role === "admin";
}

export function initials(name: string, email: string): string {
  const source = name.trim() || email.trim() || "Backoffice";
  const parts = source.split(/[\s@._-]+/).filter((part) => part.length > 0);
  if (parts.length >= 2) {
    return (parts[0][0] + parts[1][0]).toUpperCase();
  }

  return source.slice(0, 2).toUpperCase();
}

type SessionPayload = {
  roles?: string[];
};

function claims(token: string): string[] {
  if (!token) {
    return [];
  }

  try {
    const payload = JSON.parse(decode(token.split(".")[1] ?? "")) as SessionPayload;
    return Array.isArray(payload.roles) ? payload.roles : [];
  } catch {
    return [];
  }
}

function decode(value: string): string {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  const rest = padded.length % 4;
  return atob(rest === 0 ? padded : padded + "=".repeat(4 - rest));
}
