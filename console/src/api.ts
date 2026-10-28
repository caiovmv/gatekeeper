import { evict, peek as readCache, remember } from "./cache";

type Problem = {
  detail?: string;
  mensagem?: string;
};

export function message(reason: unknown): string {
  return reason instanceof Error ? reason.message : "failed";
}

async function failure(response: Response, fallback: string): Promise<string> {
  if (response.status === 403) {
    return "denied";
  }

  try {
    const body = (await response.json()) as Problem;
    if (body.detail || body.mensagem) {
      return body.detail || body.mensagem || fallback;
    }
  } catch {
    return fallback;
  }

  return fallback;
}

export function peek<T>(path: string, token: string): T | undefined {
  return readCache<T>(token, path);
}

export async function load<T>(path: string, token: string): Promise<T> {
  const hit = peek<T>(path, token);
  if (hit !== undefined) {
    return hit;
  }

  const response = await fetch(path, { headers: headers(token), credentials: "include" });
  if (!response.ok) {
    throw new Error(await failure(response, "load"));
  }

  const body = (await response.json()) as T;
  const tags = response.headers.get("X-Cache-Tags");
  if (tags) {
    remember(token, path, body, tags);
  }

  return body;
}

export async function save<T>(path: string, token: string, body: unknown): Promise<T> {
  try {
    const response = await fetch(path, {
      method: "PUT",
      credentials: "include",
      headers: { ...headers(token), "Content-Type": "application/json", "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(await failure(response, "save"));
    }

    evict(response.headers.get("X-Cache-Evict"));
    return await response.json() as T;
  } finally {
    settled();
  }
}

export async function post<T>(path: string, token: string, body: unknown): Promise<T> {
  try {
    const response = await fetch(path, {
      method: "POST",
      credentials: "include",
      headers: { ...headers(token), "Content-Type": "application/json", "Idempotency-Key": crypto.randomUUID() },
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      throw new Error(await failure(response, "request"));
    }

    evict(response.headers.get("X-Cache-Evict"));
    return await response.json() as T;
  } finally {
    settled();
  }
}

export async function remove(path: string, token: string): Promise<unknown> {
  try {
    const response = await fetch(path, { method: "DELETE", headers: headers(token), credentials: "include" });
    if (!response.ok) {
      throw new Error(await failure(response, "remove"));
    }

    evict(response.headers.get("X-Cache-Evict"));
    return await response.json() as unknown;
  } finally {
    settled();
  }
}

function settled() {
  window.dispatchEvent(new Event("gatekeeper:idle"));
}

function headers(token: string): HeadersInit {
  const result: Record<string, string> = {};
  if (token && token !== "session") {
    result.Authorization = "Bearer " + token;
  }

  const csrf = readCookie("gatekeeper_csrf");
  if (csrf) {
    result["X-CSRF"] = csrf;
  }

  return result;
}

function readCookie(name: string): string {
  const parts = document.cookie.split("; ");
  for (const part of parts) {
    if (part.startsWith(name + "=")) {
      return decodeURIComponent(part.slice(name.length + 1));
    }
  }

  return "";
}
