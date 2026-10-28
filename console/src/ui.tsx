import { createContext, useContext, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { useI18n } from "./i18n";
import type { Cell, Organization } from "./types";

type Tone = "ok" | "err";

type Toast = {
  id: number;
  tone: Tone;
  text: string;
};

type ToastApi = (text: string, tone?: Tone) => void;

const ToastContext = createContext<ToastApi>(() => {});

export function holdSubmit(event: FormEvent<HTMLFormElement>) {
  const button = event.currentTarget.querySelector("button[type=submit]");
  if (!(button instanceof HTMLButtonElement)) {
    return;
  }

  button.disabled = true;
  const release = () => {
    button.disabled = false;
    window.removeEventListener("gatekeeper:idle", release);
  };
  window.addEventListener("gatekeeper:idle", release);
  window.setTimeout(release, 15000);
}

export function useToast(): ToastApi {
  return useContext(ToastContext);
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const { copy } = useI18n();
  const [toasts, setToasts] = useState<Toast[]>([]);

  function tell(text: string, tone: Tone = "ok") {
    const id = Date.now() + Math.random();
    setToasts((current) => [...current, { id, tone, text }]);
    if (tone === "ok") {
      window.setTimeout(() => {
        setToasts((current) => current.filter((item) => item.id !== id));
      }, 3600);
    }
  }

  return (
    <ToastContext.Provider value={tell}>
      {children}
      <div className="pointer-events-none fixed inset-x-0 bottom-4 z-40 flex flex-col items-center gap-2 px-4 sm:top-4 sm:bottom-auto sm:items-end sm:px-6">
        {toasts.map((toast) => (
          <div key={toast.id} className={toast.tone === "err" ? "pointer-events-auto w-full max-w-sm rounded-xl px-3 py-2 text-sm text-white shadow" : "pointer-events-auto w-full max-w-sm rounded-xl px-3 py-2 text-sm shadow"} style={toast.tone === "err" ? { background: "var(--danger)" } : { background: "var(--action)", color: "var(--action-ink)" }}>
            <div className="flex items-start justify-between gap-3">
              <p>{toast.text}</p>
              <button className="text-xs underline" type="button" onClick={() => setToasts((current) => current.filter((item) => item.id !== toast.id))}>{copy.actions.dismiss}</button>
            </div>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export const control = "campo text-sm";

export function Field({ label, error, children }: { label: string; error?: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-sm tinta-apoio">{label}</span>
      {children}
      {error ? <span className="mt-1 block text-xs" style={{ color: "var(--danger)" }}>{error}</span> : null}
    </label>
  );
}

export function Page({ title, lede, action, compact = false, editorial = false, children }: { title: ReactNode; lede?: ReactNode; action?: ReactNode; compact?: boolean; editorial?: boolean; children: ReactNode }) {
  return (
    <section className={[compact ? "cockpit-denso" : "", editorial ? "pagina-showroom" : ""].filter(Boolean).join(" ") || undefined}>
      <div className={"flex flex-wrap items-start justify-between gap-3 " + (compact ? "" : "gap-4")}>
        <div className="min-w-0">
          <h2 className={editorial ? "titulo-agency" : (compact ? "text-2xl" : "text-3xl") + " font-semibold tracking-tight"}>{title}</h2>
          {lede ? (editorial ? <div className="lede-showroom">{lede}</div> : <p className={(compact ? "mt-1 text-xs" : "mt-2 text-sm") + " max-w-xl tinta-apoio"}>{lede}</p>) : null}
        </div>
        {action}
      </div>
      <div className={compact ? "min-h-0 overflow-hidden flex flex-col" : "mt-8"}>{children}</div>
    </section>
  );
}

export function Rail({
  brand,
  groups,
  items,
  active,
  onChoose,
  footer,
  escopo,
  recolhido = false,
  onAlternar,
  rotuloAlternar,
}: {
  brand: string;
  groups: { id: string; label: string }[];
  items: { id: string; group: string; label: string }[];
  active: string;
  onChoose: (id: string) => void;
  footer?: ReactNode;
  // Seletor de organização, tenant e workspace, logo abaixo da marca.
  escopo?: ReactNode;
  // Só o menu fixo (tablet em diante) é retrátil; a gaveta do celular não passa onAlternar.
  recolhido?: boolean;
  onAlternar?: () => void;
  rotuloAlternar?: string;
}) {
  const alternar = onAlternar ? (
    <button className="trilho-alternar" type="button" aria-label={rotuloAlternar} aria-expanded={!recolhido} onClick={onAlternar}>
      <svg viewBox="0 0 20 20" width="18" height="18" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
        {recolhido ? <path d="M3 5.5h14M3 10h14M3 14.5h14" /> : <path d="M12.5 4.5 7 10l5.5 5.5" />}
      </svg>
    </button>
  ) : null;
  if (recolhido) {
    return <aside className="trilho trilho-recolhido">{alternar}</aside>;
  }
  return (
    <aside className="trilho">
      <div className="mb-4 flex items-center justify-between gap-2 px-3">
        <p className="text-sm font-medium tracking-[0.2em]">{brand}</p>
        {alternar}
      </div>
      {escopo ? <div className="mb-3 px-2">{escopo}</div> : null}
      <nav className="min-h-0 flex-1 overflow-y-auto">
        {groups.map((group) => {
          const entries = items.filter((item) => item.group === group.id);
          if (entries.length === 0) {
            return null;
          }
          return (
            <div key={group.id}>
              {group.label ? <p className="trilho-grupo">{group.label}</p> : null}
              {entries.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  className={active === item.id ? "trilho-item trilho-item-ativo" : "trilho-item"}
                  onClick={() => onChoose(item.id)}
                >
                  {item.label}
                </button>
              ))}
            </div>
          );
        })}
      </nav>
      {footer ? <div className="mt-auto border-t pt-3 linha">{footer}</div> : null}
    </aside>
  );
}

// O "OHO" do meio do nome: os dois O são olhos que vigiam, piscam e olham em volta; o H é a ponte.
export function LoadingOho({ label }: { label: string }) {
  return (
    <span className="oho" role="status" aria-live="polite">
      <svg className="oho-arte" viewBox="0 0 96 32" aria-hidden="true">
        <g className="oho-olho oho-olho-esq">
          <circle className="oho-anel" cx="16" cy="16" r="12" />
          <circle className="oho-pupila" cx="16" cy="16" r="4.5" />
        </g>
        <path className="oho-ponte" d="M42 6v20M54 6v20M42 16h12" />
        <g className="oho-olho oho-olho-dir">
          <circle className="oho-anel" cx="80" cy="16" r="12" />
          <circle className="oho-pupila" cx="80" cy="16" r="4.5" />
        </g>
      </svg>
      <span className="oho-texto">{label}</span>
    </span>
  );
}

export function Skeleton({ rows = 4 }: { rows?: number }) {
  return (
    <div className="space-y-3" aria-hidden="true">
      {Array.from({ length: rows }, (_, index) => (
        <div key={index} className="esqueleto h-12 animate-pulse rounded-xl" />
      ))}
    </div>
  );
}

export function OrganizationSelect({ organizations, value, onChange }: { organizations: Organization[]; value: string; onChange: (value: string) => void }) {
  return (
    <select className={control} value={value} onChange={(event) => onChange(event.target.value)}>
      {organizations.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
    </select>
  );
}

type TableProps = {
  columns: string[];
  rows: Cell[][];
  onEdit?: ((id: string) => void) | null;
  onRemove?: ((id: string) => void) | null;
  removeLabel?: string;
  loading?: boolean;
};

export function Table({ columns, rows, onEdit, onRemove, removeLabel, loading = false }: TableProps) {
  const { copy } = useI18n();
  const removal = removeLabel ?? copy.actions.deactivate;
  if (loading) {
    return <Skeleton />;
  }

  if (rows.length === 0) {
    return <p className="cartao text-sm tinta-apoio">{copy.empty}</p>;
  }

  return (
    <>
      <div className="space-y-3 md:hidden">
        {rows.map((row, index) => (
          <article key={index} className="cartao">
            {row.map((cell, cellIndex) => {
              const last = cellIndex === row.length - 1;
              if ((onEdit || onRemove) && last) {
                return <Actions key={cellIndex} id={String(cell ?? "")} onEdit={onEdit} onRemove={onRemove} removeLabel={removal} editLabel={copy.actions.edit} />;
              }
              if (!columns[cellIndex]) {
                return null;
              }
              return (
                <p key={cellIndex} className="flex items-start justify-between gap-4 py-1 text-sm">
                  <span className="tinta-apoio">{columns[cellIndex]}</span>
                  <span className="text-right">{String(cell ?? "—")}</span>
                </p>
              );
            })}
          </article>
        ))}
      </div>
      <table className="cartao hidden w-full border-collapse text-left text-sm md:table">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column || "action"} className="border-b px-3 py-2 font-medium tinta-apoio linha">{column}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              {row.map((cell, cellIndex) => (
                <td key={cellIndex} className="border-b px-3 py-2 linha">
                  {(onEdit || onRemove) && cellIndex === row.length - 1
                    ? <Actions id={String(cell ?? "")} onEdit={onEdit} onRemove={onRemove} removeLabel={removal} editLabel={copy.actions.edit} />
                    : String(cell ?? "")}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

function Actions({ id, onEdit, onRemove, removeLabel, editLabel }: { id: string; onEdit?: ((id: string) => void) | null; onRemove?: ((id: string) => void) | null; removeLabel: string; editLabel: string }) {
  return (
    <span className="mt-3 flex gap-4 md:mt-0">
      {onEdit ? <button className="text-sm underline" type="button" onClick={() => onEdit(id)}>{editLabel}</button> : null}
      {onRemove ? <button className="text-sm underline" style={{ color: "var(--danger)" }} type="button" onClick={() => onRemove(id)}>{removeLabel}</button> : null}
    </span>
  );
}

export function useDocumentTitle(title: string) {
  useEffect(() => {
    document.title = title + " · gatekeeper";
  }, [title]);
}
