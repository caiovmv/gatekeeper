const validityMs = 5 * 60 * 1000;

type Entry = {
  body: unknown;
  tags: string[];
  expires: number;
};

const entries = new Map<string, Entry>();

export const cacheEvent = "gatekeeper:cache";

export function peek<T>(scope: string, path: string, now = Date.now()): T | undefined {
  const item = entries.get(key(scope, path));
  if (!item) {
    return undefined;
  }

  if (item.expires <= now) {
    entries.delete(key(scope, path));
    return undefined;
  }

  return item.body as T;
}

export function remember(scope: string, path: string, body: unknown, header: string, now = Date.now()): void {
  const tags = split(header);
  if (tags.length === 0) {
    return;
  }

  entries.set(key(scope, path), { body, tags, expires: now + validityMs });
}

export function evict(header: string | null): void {
  const tags = split(header ?? "");
  if (tags.length === 0) {
    return;
  }

  const drop = new Set(tags);
  for (const [id, item] of entries) {
    if (item.tags.some((tag) => drop.has(tag))) {
      entries.delete(id);
    }
  }

  window.dispatchEvent(new CustomEvent(cacheEvent, { detail: tags }));
}

export function clearCache(): void {
  entries.clear();
  window.dispatchEvent(new CustomEvent(cacheEvent, { detail: ["*"] }));
}

function key(scope: string, path: string): string {
  return scope + "\n" + path;
}

function split(header: string): string[] {
  return header.split(",").map((item) => item.trim()).filter((item) => item.length > 0);
}
