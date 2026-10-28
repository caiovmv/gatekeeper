import { useEffect, useState } from "react";
import { load, message, peek } from "./api";
import { useI18n } from "./i18n";
import { Page, Skeleton, useToast } from "./ui";
import type { AuditEntry, Organization } from "./types";

type OverviewProps = {
  token: string;
  role: string;
  focus: string;
};

type ReadyReport = {
  componentes?: Record<string, { status: string; detalhe?: string }>;
};

export function Overview({ token, role, focus }: OverviewProps) {
  const { copy, number, feedback } = useI18n();
  const tell = useToast();
  const knownOrganizations = peek<Organization[]>("/api/organizations", token);
  const knownAudit = peek<AuditEntry[]>("/api/audit", token);
  const warm = knownOrganizations !== undefined && knownAudit !== undefined;
  const [loading, setLoading] = useState(!warm);
  const [organizations, setOrganizations] = useState<Organization[]>(knownOrganizations ?? []);
  const [audit, setAudit] = useState<AuditEntry[]>(knownAudit ?? []);
  const [lastConnection, setLastConnection] = useState("");
  const [ready, setReady] = useState<ReadyReport>({});
  const [denied, setDenied] = useState(false);

  useEffect(() => {
    let live = true;
    if (!warm) {
      setLoading(true);
    }
    setDenied(false);
    read(token, focus).then((value) => {
      if (!live) {
        return;
      }
      setOrganizations(value.organizations);
      setAudit(value.audit);
      setLastConnection(value.lastConnection);
      setReady(value.ready);
    }).catch((reason: unknown) => {
      if (live && message(reason) === "denied") {
        setDenied(true);
        return;
      }
      if (live) {
        tell(feedback(reason), "err");
      }
    }).finally(() => {
      if (live) {
        setLoading(false);
      }
    });
    return () => {
      live = false;
    };
  }, [token, role, focus, warm]);

  if (loading) {
    return (
      <Page title={copy.overview.title} lede={copy.overview.lede}>
        <Skeleton rows={5} />
      </Page>
    );
  }

  if (denied) {
    return (
      <Page title={copy.overview.title} lede={copy.overview.lede}>
        <p className="cartao text-sm" style={{ background: "var(--warning)", color: "var(--warning-ink)" }}>{copy.overview.denied}</p>
      </Page>
    );
  }

  const dependencies = Object.entries(ready.componentes ?? {});

  return (
    <Page title={copy.overview.title} lede={copy.overview.lede}>
      <div className="faixa">
        <Stat label={copy.overview.organizations} value={number(organizations.length)} hint={copy.overview.customers} />
        <Stat label={copy.nav.audit} value={number(audit.length)} hint={copy.overview.timeline} />
        <Stat label={copy.overview.lastConnection} value={lastConnection || copy.overview.noConnection} hint={copy.overview.dependencies} />
      </div>
      <div className="mt-6 grid gap-4 lg:grid-cols-2">
        <section className="cartao">
          <h3 className="text-base font-medium">{copy.overview.timeline}</h3>
          {audit.length === 0 ? <p className="mt-4 text-sm tinta-apoio">{copy.overview.timelineEmpty}</p> : (
            <ol className="mt-4 space-y-4">
              {audit.slice(0, 5).map((item) => (
                <li key={item.occurredAt + item.action + item.resource} className="flex items-start gap-3">
                  <span className="ponto mt-2" />
                  <span>
                    <p className="font-medium">{item.action}</p>
                    <p className="text-sm tinta-apoio">{item.actor} · {item.resource}</p>
                  </span>
                </li>
              ))}
            </ol>
          )}
        </section>
        <section className="cartao">
          <h3 className="text-base font-medium">{copy.overview.dependencies}</h3>
          <ul className="mt-4 space-y-3 text-sm">
            {dependencies.map(([name, item]) => (
              <li key={name} className="flex items-center justify-between gap-3">
                <span>{name}</span>
                <span className="tinta-apoio">{item.detalhe || item.status}</span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </Page>
  );
}

function Stat({ label, value, hint }: { label: string; value: string; hint: string }) {
  return (
    <div>
      <p className="faixa-rotulo">{label}</p>
      <p className="mt-2 text-lg font-semibold tracking-tight">{value}</p>
      <p className="mt-1 text-xs tinta-apoio">{hint}</p>
    </div>
  );
}

async function read(token: string, focus: string) {
  const [organizations, audit, keys, ready] = await Promise.all([
    load<Organization[]>("/api/organizations", token),
    load<AuditEntry[]>("/api/audit", token),
    load<Array<{ lastUsedOn: string | null }>>("/api/machine-keys", token).catch(() => []),
    load<ReadyReport>("/health/ready", token).catch(() => ({ componentes: {} })),
  ]);
  const stamps = keys.map((item) => item.lastUsedOn).filter((item): item is string => Boolean(item)).sort();
  const listed = focus ? organizations.filter((item) => item.id === focus) : organizations;
  return { organizations: listed, audit, lastConnection: stamps[stamps.length - 1] ?? "", ready };
}
