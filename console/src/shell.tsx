import { useEffect, useState, type ReactNode } from "react";
import { useSearchParams } from "react-router";
import { canWrite } from "./auth";
import { load, peek } from "./api";
import { cacheEvent } from "./cache";
import { useI18n } from "./i18n";
import { Rail, useDocumentTitle } from "./ui";
import { lerTema, pintarTema, type Tema } from "./theme";
import type { Organization } from "./types";

export type NavItem = {
  id: string;
  group: "support" | "admin" | "audit";
};

export const navigation: NavItem[] = [
  { id: "overview", group: "support" },
  { id: "organizations", group: "support" },
  { id: "members", group: "support" },
  { id: "health", group: "support" },
  { id: "machine-keys", group: "admin" },
  { id: "policies", group: "admin" },
  { id: "audit", group: "audit" },
  { id: "logs", group: "audit" },
];

const groups = ["support", "admin", "audit"] as const;

const navKey: Record<string, keyof ReturnType<typeof useI18n>["copy"]["nav"]> = {
  overview: "overview",
  organizations: "organizations",
  members: "members",
  health: "health",
  "machine-keys": "machineKeys",
  policies: "policies",
  audit: "audit",
  logs: "logs",
  security: "security",
};

type Hit = {
  screen: string;
  title: string;
  detail: string;
};

type ShellProps = {
  token: string;
  role: string;
  name: string;
  email: string;
  screen: string;
  focus: string;
  onScreen: (id: string) => void;
  onFocus: (value: string) => void;
  onSignOut: () => void;
  children: ReactNode;
};

export function Shell({ token, role, name, email, screen, focus, onScreen, onFocus, onSignOut, children }: ShellProps) {
  const { copy, role: roleName, setLocale, locale } = useI18n();
  const [params, setParams] = useSearchParams();
  const [open, setOpen] = useState(false);
  const [menu, setMenu] = useState(false);
  const [query, setQuery] = useState(params.get("q") ?? "");
  const [hits, setHits] = useState<Hit[]>([]);
  const [organizations, setOrganizations] = useState<Organization[]>(() => peek<Organization[]>("/api/organizations", token) ?? []);
  const [theme, setTheme] = useState<Tema>(() => lerTema());
  const visible = navigation.filter((item) => {
    if (role === "auditor") {
      return item.group === "audit";
    }

    return item.group !== "admin" || canWrite(role);
  });
  const current = navKey[screen];
  useDocumentTitle(current ? copy.nav[current] : copy.product);

  useEffect(() => {
    setTheme(lerTema());
  }, []);

  useEffect(() => {
    pintarTema(theme);
  }, [theme]);

  useEffect(() => {
    let live = true;
    function apply(next: Organization[]) {
      if (live) {
        setOrganizations(next);
      }
    }

    function pull() {
      load<Organization[]>("/api/organizations", token).then(apply).catch(() => apply([]));
    }

    function onCache(event: Event) {
      if (!(event instanceof CustomEvent) || !Array.isArray(event.detail)) {
        return;
      }

      if (event.detail.includes("organizations")) {
        pull();
      }
    }

    pull();
    window.addEventListener(cacheEvent, onCache);
    return () => {
      live = false;
      window.removeEventListener(cacheEvent, onCache);
    };
  }, [token]);

  useEffect(() => {
    const needle = query.trim();
    if (needle.length < 2) {
      setHits([]);
      return;
    }

    let live = true;
    const timer = window.setTimeout(() => {
      find(token, role, needle, copy).then((next) => {
        if (live) {
          setHits(next);
        }
      }).catch(() => {
        if (live) {
          setHits([]);
        }
      });
    }, 250);
    return () => {
      live = false;
      window.clearTimeout(timer);
    };
  }, [query, token, role, copy]);

  function choose(id: string) {
    onScreen(id);
    setOpen(false);
    setMenu(false);
  }

  const railItems = visible.map((item) => ({ id: item.id, group: item.group, label: copy.nav[navKey[item.id]] }));
  const railGroups = groups.map((group) => ({ id: group, label: copy.groups[group] }));
  const account = (
    <div className="relative">
      <button className="flex w-full items-center gap-3 px-2 py-1 text-left" type="button" aria-label={copy.actions.account} onClick={() => setMenu((current) => !current)}>
        <span className="avatar">{(name || email || "A").slice(0, 1).toUpperCase()}</span>
        <span className="min-w-0">
          <span className="block truncate text-sm">{name || email || copy.actions.account}</span>
          <span className="block truncate text-xs tinta-apoio">{email || roleName(role)}</span>
        </span>
      </button>
      {menu ? (
        <div className="cartao absolute bottom-14 left-0 z-30 w-56 p-2">
          <p className="px-2 pt-1 text-sm font-medium">{name || email}</p>
          <p className="px-2 text-xs tinta-apoio">{email}</p>
          <p className="px-2 py-2 text-xs tinta-apoio">{roleName(role)}</p>
          <p className="px-2 pb-1 text-xs tinta-apoio">{copy.language}</p>
          <div className="mb-1 flex gap-2 px-2">
            <button className={locale === "pt-BR" ? "text-sm marca" : "text-sm tinta-apoio"} type="button" onClick={() => setLocale("pt-BR")}>Português</button>
            <button className={locale === "en" ? "text-sm marca" : "text-sm tinta-apoio"} type="button" onClick={() => setLocale("en")}>English</button>
          </div>
          <p className="px-2 pb-1 text-xs tinta-apoio">{copy.actions.theme}</p>
          <div className="mb-1 flex gap-2 px-2">
            <button className={theme === "light" ? "text-sm marca" : "text-sm tinta-apoio"} type="button" onClick={() => setTheme("light")}>{copy.actions.themeLight}</button>
            <button className={theme === "dark" ? "text-sm marca" : "text-sm tinta-apoio"} type="button" onClick={() => setTheme("dark")}>{copy.actions.themeDark}</button>
          </div>
          <button className="block w-full px-2 py-2 text-left text-sm" type="button" onClick={() => choose("security")}>{copy.nav.security}</button>
          <button className="block w-full px-2 py-2 text-left text-sm" type="button" onClick={onSignOut}>{copy.actions.signOut}</button>
        </div>
      ) : null}
    </div>
  );

  return (
    <div className="papel flex min-h-screen">
      <div className="hidden md:block">
        <Rail brand={copy.product} groups={railGroups} items={railItems} active={screen} onChoose={choose} footer={account} />
      </div>
      <div className="min-w-0 flex-1">
        <header className="flex items-center justify-between gap-4 px-6 py-5 md:hidden">
          <button className="text-sm" type="button" onClick={() => setOpen(true)}>{copy.actions.menu}</button>
          <p className="text-sm">{copy.product}</p>
        </header>
        <main className="px-6 pb-16 pt-6 lg:px-10">
          <div className="mb-8 flex flex-col gap-3 lg:flex-row lg:items-center">
            <label className="sr-only" htmlFor="org-filter">{copy.actions.allOrganizations}</label>
            <select
              id="org-filter"
              className="campo max-w-xs text-sm"
              value={organizations.some((item) => item.id === focus) ? focus : ""}
              onChange={(event) => onFocus(event.target.value)}
            >
              <option value="">{copy.actions.allOrganizations}</option>
              {organizations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select>
            <div className="relative min-w-0 flex-1">
              <label className="sr-only" htmlFor="console-search">{copy.actions.search}</label>
              <input
                id="console-search"
                className="campo text-sm"
                placeholder={copy.search.placeholder}
                value={query}
                onChange={(event) => {
                  const next = event.target.value;
                  setQuery(next);
                  const search = new URLSearchParams(params);
                  if (next.trim().length === 0) {
                    search.delete("q");
                  } else {
                    search.set("q", next);
                  }
                  setParams(search, { replace: true });
                }}
              />
              {hits.length > 0 ? (
                <ul className="cartao absolute z-30 mt-1 w-full">
                  {hits.map((hit) => (
                    <li key={hit.screen + hit.title}>
                      <button
                        className="flex w-full items-center justify-between gap-3 px-3 py-2 text-left text-sm"
                        type="button"
                        onClick={() => {
                          onFocus(hit.title);
                          choose(hit.screen);
                          setQuery("");
                          setHits([]);
                        }}
                      >
                        <span>{hit.title}</span>
                        <span className="text-xs tinta-apoio">{hit.detail}</span>
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          </div>
          {focus ? (
            <p className="mb-6 flex items-center justify-between text-sm tinta-apoio">
              <span>{copy.search.showing(organizations.find((item) => item.id === focus)?.name || focus)}</span>
              <button className="marca" type="button" onClick={() => onFocus("")}>{copy.actions.clear}</button>
            </p>
          ) : null}
          {children}
        </main>
      </div>
      {open ? (
        <div className="fixed inset-0 z-30 md:hidden">
          <button className="absolute inset-0" style={{ background: "rgba(0,0,0,0.55)" }} type="button" aria-label={copy.actions.closeMenu} onClick={() => setOpen(false)} />
          <div className="relative h-full w-fit">
            <Rail brand={copy.brand} groups={railGroups} items={railItems} active={screen} onChoose={choose} footer={account} />
          </div>
        </div>
      ) : null}
    </div>
  );
}

async function find(token: string, role: string, query: string, copy: { search: { organization: string } }): Promise<Hit[]> {
  if (role === "auditor") {
    return [];
  }

  const needle = query.toLowerCase();
  const organizations = await load<Organization[]>("/api/organizations", token);
  const hits: Hit[] = organizations
    .filter((item) => (item.name + " " + item.host).toLowerCase().includes(needle))
    .slice(0, 4)
    .map((item) => ({ screen: "organizations", title: item.name, detail: copy.search.organization }));
  return hits.slice(0, 8);
}
