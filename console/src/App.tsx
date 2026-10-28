import { useEffect, useState, type FormEvent } from "react";
import { useLocation, useNavigate } from "react-router";
import { canEnter, canWrite, clearToken, roleFrom, storeToken } from "./auth";
import { load, peek, post, remove, save } from "./api";
import { contains, invalid, problems, required } from "./forms";
import { useI18n } from "./i18n";
import { Access } from "./Access";
import { ConfirmDialog } from "./confirm";
import { Overview } from "./dashboard";
import { Audit, Health, Logs, MachineKeys, Members, Policies, Security } from "./Office";
import { Shell } from "./shell";
import { Field, LoadingOho, Page, Table, ToastProvider, control, holdSubmit, useToast } from "./ui";
import type { AccountProfile, Confirmation, Organization, ScreenProps } from "./types";

const screens = ["overview", "organizations", "members", "health", "machine-keys", "policies", "audit", "logs", "security"];

function screenFrom(path: string): string {
  const name = path.replace(/^\/+/, "").split("/")[0];
  return screens.includes(name) ? name : "overview";
}

export function App() {
  return (
    <ToastProvider>
      <Gate />
    </ToastProvider>
  );
}

function Gate() {
  const tell = useToast();
  const { copy, feedback } = useI18n();
  const location = useLocation();
  const navigate = useNavigate();
  const [token, setToken] = useState("");
  const [ready, setReady] = useState(false);
  const [screen, setScreen] = useState(screenFrom(location.pathname));
  const [focus, setFocus] = useState("");
  const [profile, setProfile] = useState<AccountProfile | null>(null);

  useEffect(() => {
    setScreen(screenFrom(location.pathname));
  }, [location.pathname]);

  useEffect(() => {
    if (location.pathname === "/") {
      navigate("/overview", { replace: true });
    }
  }, [location.pathname, navigate]);

  useEffect(() => {
    load<AccountProfile>("/api/account/me", "session")
      .then((next) => {
        if (!canEnter(roleFrom(next.roles))) {
          clearToken();
          setReady(true);
          return;
        }

        storeToken("session");
        setProfile(next);
        setToken("session");
        setReady(true);
      })
      .catch(() => {
        clearToken();
        setReady(true);
      });
  }, []);

  const role = profile ? roleFrom(profile.roles) : "";

  useEffect(() => {
    if (role === "auditor" && screen !== "audit" && screen !== "logs") {
      setScreen("audit");
      navigate("/audit");
    }
  }, [role, screen, navigate]);

  if (!ready) {
    return <div className="papel grid min-h-screen place-items-center p-8"><LoadingOho label={copy.loading} /></div>;
  }

  if (!token || !profile) {
    return (
      <Access
        onSignedIn={() => {
          storeToken("session");
          setToken("session");
          load<AccountProfile>("/api/account/me", "session").then(setProfile).catch((reason: unknown) => tell(feedback(reason), "err"));
        }}
      />
    );
  }

  const shared = { token, role, focus };

  return (
    <Shell
      token={token}
      role={role}
      name={profile.displayName}
      email={profile.email}
      screen={screen}
      focus={focus}
      onScreen={(next) => {
        setScreen(next);
        const path = "/" + next;
        if (location.pathname !== path) {
          navigate(path);
        }
      }}
      onFocus={setFocus}
      onSignOut={() => {
        post("/api/account/logout", "session", {}).catch(() => undefined).finally(() => {
          clearToken();
          setToken("");
          setProfile(null);
          navigate("/overview");
        });
      }}
    >
      {screen === "overview" ? <Overview token={token} role={role} focus={focus} /> : null}
      {screen === "organizations" ? <Organizations {...shared} /> : null}
      {screen === "members" ? <Members {...shared} /> : null}
      {screen === "machine-keys" ? <MachineKeys {...shared} /> : null}
      {screen === "audit" ? <Audit token={token} focus={focus} /> : null}
      {screen === "logs" ? <Logs token={token} focus={focus} /> : null}
      {screen === "policies" ? <Policies {...shared} /> : null}
      {screen === "security" ? <Security token={token} /> : null}
      {screen === "health" ? <Health token={token} /> : null}
    </Shell>
  );
}


function Organizations({ token, role, focus }: ScreenProps) {
  const tell = useToast();
  const { copy, text, feedback } = useI18n();
  const [rows, setRows] = useState<Organization[]>(() => peek<Organization[]>("/api/organizations", token) ?? []);
  const [loading, setLoading] = useState(() => peek<Organization[]>("/api/organizations", token) === undefined);
  const [id, setId] = useState("");
  const [name, setName] = useState("");
  const [host, setHost] = useState("");
  const [idpIssuer, setIdpIssuer] = useState("");
  const [directoryId, setDirectoryId] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [pending, setPending] = useState<Confirmation | null>(null);
  const writable = canWrite(role);
  const visible = rows.filter((row) => contains(row, focus));

  async function refresh() {
    setRows(await load<Organization[]>("/api/organizations", token));
  }

  useEffect(() => {
    refresh().catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = problems({ name: required(name), host: required(host) });
    setErrors(next);
    if (invalid(next)) {
      return;
    }
    holdSubmit(event);
    try {
      await save("/api/organizations/" + (id || crypto.randomUUID()), token, { name, host, idpIssuer, directoryId });
      setId("");
      setName("");
      setHost("");
      setIdpIssuer("");
      setDirectoryId("");
      tell(id ? copy.organizations.updated : copy.organizations.saved);
      await refresh();
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  async function deactivate() {
    if (!pending) {
      return;
    }
    try {
      await remove("/api/organizations/" + pending.id, token);
      setPending(null);
      tell(copy.organizations.deactivated);
      await refresh();
    } catch (reason: unknown) {
      setPending(null);
      tell(feedback(reason), "err");
    }
  }

  return (
    <Page title={copy.nav.organizations} lede={writable ? copy.organizations.ledeWrite : copy.organizations.ledeRead}>
      {writable ? (
        <form className="mb-6 grid grid-cols-1 gap-3 sm:grid-cols-2" onSubmit={submit}>
          <Field label={copy.fields.name} error={text(errors.name, copy.fields.name)}><input className={control} value={name} onChange={(event) => setName(event.target.value)} /></Field>
          <Field label={copy.fields.host} error={text(errors.host, copy.fields.host)}><input className={control} value={host} onChange={(event) => setHost(event.target.value)} /></Field>
          <Field label={copy.fields.issuer}><input className={control} value={idpIssuer} onChange={(event) => setIdpIssuer(event.target.value)} /></Field>
          <Field label={copy.fields.directory}><input className={control} value={directoryId} onChange={(event) => setDirectoryId(event.target.value)} /></Field>
          <div className="sm:col-span-2"><button className="acao rounded-xl px-3 py-2 text-sm" type="submit">{id ? copy.organizations.update : copy.organizations.save}</button></div>
        </form>
      ) : null}
      <Table
        loading={loading}
        columns={writable ? [copy.fields.name, copy.fields.host, copy.fields.issuer, copy.fields.directory, ""] : [copy.fields.name, copy.fields.host, copy.fields.issuer, copy.fields.directory]}
        rows={visible.map((row) => writable ? [row.name, row.host, row.idpIssuer, row.directoryId, row.id] : [row.name, row.host, row.idpIssuer, row.directoryId])}
        onEdit={writable ? (rowId) => {
          const row = rows.find((item) => item.id === rowId);
          if (!row) {
            return;
          }
          setId(row.id);
          setName(row.name);
          setHost(row.host);
          setIdpIssuer(row.idpIssuer);
          setDirectoryId(row.directoryId ?? "");
        } : null}
        onRemove={writable ? (rowId) => setPending({ id: rowId, title: copy.organizations.title, confirm: copy.organizations.confirm, warning: copy.organizations.warning, detail: copy.organizations.detail }) : null}
      />
      <ConfirmDialog pending={pending} onCancel={() => setPending(null)} onConfirm={deactivate} />
    </Page>
  );
}

