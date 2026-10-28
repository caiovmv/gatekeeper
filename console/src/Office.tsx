import { useEffect, useState, type FormEvent } from "react";
import { canGovern, canWrite } from "./auth";
import { creationOptions, serialize } from "./Access";
import { load, peek, post, remove, save } from "./api";
import { ConfirmDialog } from "./confirm";
import { contains, emailOf, invalid, passwordOf, problems, required } from "./forms";
import { useI18n } from "./i18n";
import { Field, OrganizationSelect, Page, Table, control, holdSubmit, useToast } from "./ui";
import type { AccessLog, AccountResult, AuditEntry, Confirmation, HealthRow, IssuedKey, MachineKey, Member, MemberSave, Organization, PolicyRule, ScreenProps } from "./types";

type ReadProps = {
  token: string;
  focus: string;
};

function remembered<T>(token: string, path: string): T | undefined {
  return peek<T>(path, token);
}

function scopedStart<T>(token: string, pathFor: (organizationId: string) => string): { organizations: Organization[]; organizationId: string; rows: T[]; loading: boolean } {
  const organizations = remembered<Organization[]>(token, "/api/organizations");
  const list = organizations ?? [];
  const organizationId = list[0]?.id ?? "";
  if (!organizations) {
    return { organizations: list, organizationId, rows: [], loading: true };
  }

  if (!organizationId) {
    return { organizations: list, organizationId, rows: [], loading: false };
  }

  const rows = remembered<T[]>(token, pathFor(organizationId));
  return { organizations: list, organizationId, rows: rows ?? [], loading: rows === undefined };
}

export function Members({ token, role, focus }: ScreenProps) {
  const tell = useToast();
  const { copy, text, role: roleName, feedback } = useI18n();
  const start = scopedStart<Member>(token, (id) => "/api/members?organizationId=" + id);
  const [organizations, setOrganizations] = useState(start.organizations);
  const [organizationId, setOrganizationId] = useState(start.organizationId);
  const [rows, setRows] = useState(start.rows);
  const [loading, setLoading] = useState(start.loading);
  const [id, setId] = useState("");
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [memberRole, setMemberRole] = useState("support");
  const [password, setPassword] = useState("");
  const [invite, setInvite] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [pending, setPending] = useState<Confirmation | null>(null);
  const writable = canWrite(role);
  const visible = rows.filter((row) => contains(row, focus));

  async function refresh(nextOrganizationId?: string) {
    const current = nextOrganizationId || organizationId;
    const nextOrganizations = await load<Organization[]>("/api/organizations", token);
    setOrganizations(nextOrganizations);
    const selected = current || nextOrganizations[0]?.id || "";
    setOrganizationId(selected);
    setRows(selected ? await load<Member[]>("/api/members?organizationId=" + selected, token) : []);
  }

  useEffect(() => {
    refresh().catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = problems({
      organizationId: required(organizationId),
      email: emailOf(email),
      displayName: required(displayName),
      password: passwordOf(password, true),
    });
    setErrors(next);
    if (invalid(next)) {
      return;
    }
    holdSubmit(event);
    try {
      const saved = await save<MemberSave>("/api/members/" + (id || crypto.randomUUID()), token, { organizationId, email, displayName, role: memberRole, password });
      setInvite(saved.inviteCode || "");
      setId("");
      setEmail("");
      setDisplayName("");
      setPassword("");
      tell(id ? copy.members.updated : copy.members.saved);
      await refresh(organizationId);
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  return (
    <Page title={copy.nav.members} lede={writable ? copy.members.ledeWrite : copy.members.ledeRead}>
      {invite ? <p className="mb-4 rounded-lg bg-amber-100 px-3 py-2 text-sm text-amber-950">{copy.members.invite(invite)}</p> : null}
      {writable ? (
        <form className="mb-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3" onSubmit={submit}>
          <Field label={copy.fields.organization} error={text(errors.organizationId, copy.fields.organization)}>
            <OrganizationSelect organizations={organizations} value={organizationId} onChange={(value) => refresh(value).catch((reason: unknown) => tell(feedback(reason), "err"))} />
          </Field>
          <Field label={copy.fields.email} error={text(errors.email, copy.fields.email)}><input className={control} value={email} onChange={(event) => setEmail(event.target.value)} /></Field>
          <Field label={copy.fields.displayName} error={text(errors.displayName, copy.fields.displayName)}><input className={control} value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></Field>
          <Field label={copy.fields.role}>
            <select className={control} value={memberRole} onChange={(event) => setMemberRole(event.target.value)}>
              <option value="admin">{copy.roles.admin}</option>
              <option value="support">{copy.roles.support}</option>
              <option value="auditor">{copy.roles.auditor}</option>
              <option value="owner">{copy.roles.owner}</option>
            </select>
          </Field>
          <Field label={copy.fields.password} error={text(errors.password, copy.fields.password)}><input className={control} type="password" value={password} onChange={(event) => setPassword(event.target.value)} /></Field>
          <p className="text-sm text-slate-500 sm:col-span-2 lg:col-span-3">{copy.members.hint}</p>
          <div><button className="acao rounded-xl px-3 py-2 text-sm" type="submit">{id ? copy.members.update : copy.members.save}</button></div>
        </form>
      ) : (
        <div className="mb-6 max-w-sm">
          <Field label={copy.fields.organization}>
            <OrganizationSelect organizations={organizations} value={organizationId} onChange={(value) => refresh(value).catch((reason: unknown) => tell(feedback(reason), "err"))} />
          </Field>
        </div>
      )}
      <Table
        loading={loading}
        columns={writable ? [copy.fields.email, copy.fields.name, copy.fields.role, ""] : [copy.fields.email, copy.fields.name, copy.fields.role]}
        rows={visible.map((row) => writable ? [row.email, row.displayName, roleName(row.role), row.id] : [row.email, row.displayName, roleName(row.role)])}
        onEdit={writable ? (rowId) => {
          const row = rows.find((item) => item.id === rowId);
          if (!row) {
            return;
          }
          setId(row.id);
          setEmail(row.email);
          setDisplayName(row.displayName);
          setMemberRole(row.role);
          setOrganizationId(row.organizationId);
        } : null}
        onRemove={writable ? (rowId) => {
          const row = rows.find((item) => item.id === rowId);
          setPending({ id: rowId, title: copy.members.title(row?.email ?? copy.members.fallback), confirm: copy.members.confirm, warning: copy.members.warning, detail: copy.members.detail });
        } : null}
      />
      <ConfirmDialog pending={pending} onCancel={() => setPending(null)} onConfirm={() => {
        if (!pending) {
          return;
        }
        remove("/api/members/" + pending.id, token).then(() => {
          setPending(null);
          tell(copy.members.deactivated);
          return refresh(organizationId);
        }).catch((reason: unknown) => tell(feedback(reason), "err"));
      }} />
    </Page>
  );
}

export function MachineKeys({ token, role, focus }: ScreenProps) {
  const tell = useToast();
  const { copy, text, when, feedback } = useI18n();
  const start = scopedStart<MachineKey>(token, (id) => "/api/machine-keys?organizationId=" + id);
  const [organizations, setOrganizations] = useState(start.organizations);
  const [organizationId, setOrganizationId] = useState(start.organizationId);
  const [rows, setRows] = useState(start.rows);
  const [loading, setLoading] = useState(start.loading);
  const [id, setId] = useState("");
  const [name, setName] = useState("");
  const [secret, setSecret] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [pending, setPending] = useState<Confirmation | null>(null);
  const writable = canWrite(role);
  const visible = rows.filter((row) => contains(row, focus));

  async function refresh(nextOrganizationId?: string) {
    const current = nextOrganizationId || organizationId;
    const nextOrganizations = await load<Organization[]>("/api/organizations", token);
    setOrganizations(nextOrganizations);
    const selected = current || nextOrganizations[0]?.id || "";
    setOrganizationId(selected);
    setRows(selected ? await load<MachineKey[]>("/api/machine-keys?organizationId=" + selected, token) : []);
  }

  useEffect(() => {
    refresh().catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = problems({ organizationId: required(organizationId), name: required(name) });
    setErrors(next);
    if (invalid(next)) {
      return;
    }
    holdSubmit(event);
    try {
      if (id) {
        await save("/api/machine-keys/" + id, token, { name });
        setId("");
        tell(copy.keys.updated);
      } else {
        const issued = await post<IssuedKey>("/api/machine-keys", token, { organizationId, name });
        setSecret(issued.secret);
        tell(copy.keys.saved);
      }
      setName("");
      await refresh(organizationId);
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  return (
    <Page title={copy.keys.title} lede={copy.keys.lede}>
      {secret ? <p className="mb-4 break-all rounded-lg bg-amber-100 px-3 py-2 text-sm text-amber-950">{copy.keys.secret(secret)}</p> : null}
      {writable ? (
        <form className="mb-6 grid grid-cols-1 gap-3 sm:grid-cols-3" onSubmit={submit}>
          <Field label={copy.fields.organization} error={text(errors.organizationId, copy.fields.organization)}>
            <OrganizationSelect organizations={organizations} value={organizationId} onChange={(value) => refresh(value).catch((reason: unknown) => tell(feedback(reason), "err"))} />
          </Field>
          <Field label={copy.fields.name} error={text(errors.name, copy.fields.name)}><input className={control} value={name} onChange={(event) => setName(event.target.value)} /></Field>
          <div className="sm:self-end"><button className="acao rounded-xl px-3 py-2 text-sm" type="submit">{id ? copy.keys.update : copy.keys.save}</button></div>
        </form>
      ) : null}
      <Table
        loading={loading}
        columns={writable ? [copy.fields.name, copy.keys.columns.prefix, copy.keys.columns.lastUse, copy.keys.columns.revoked, ""] : [copy.fields.name, copy.keys.columns.prefix, copy.keys.columns.lastUse, copy.keys.columns.revoked]}
        rows={visible.map((row) => writable ? [row.name, row.prefix, when(row.lastUsedOn), when(row.revokedOn), row.id] : [row.name, row.prefix, when(row.lastUsedOn), when(row.revokedOn)])}
        onEdit={writable ? (rowId) => {
          const row = rows.find((item) => item.id === rowId);
          if (!row || row.revokedOn) {
            return;
          }
          setId(row.id);
          setName(row.name);
        } : null}
        onRemove={writable ? (rowId) => {
          const row = rows.find((item) => item.id === rowId);
          setPending({ id: rowId, title: copy.keys.titleConfirm(row?.name ?? copy.keys.fallback), confirm: copy.keys.confirm, warning: copy.keys.warning, detail: copy.keys.detail });
        } : null}
        removeLabel={copy.actions.revoke}
      />
      <ConfirmDialog pending={pending} onCancel={() => setPending(null)} onConfirm={() => {
        if (!pending) {
          return;
        }
        remove("/api/machine-keys/" + pending.id, token).then(() => {
          setPending(null);
          tell(copy.keys.revoked);
          return refresh(organizationId);
        }).catch((reason: unknown) => tell(feedback(reason), "err"));
      }} />
    </Page>
  );
}

export function Audit({ token, focus }: ReadProps) {
  const tell = useToast();
  const { copy, when, feedback } = useI18n();
  const known = remembered<AuditEntry[]>(token, "/api/audit");
  const [rows, setRows] = useState<AuditEntry[]>(known ?? []);
  const [loading, setLoading] = useState(known === undefined);
  const visible = rows.filter((row) => contains(row, focus));

  useEffect(() => {
    load<AuditEntry[]>("/api/audit", token).then(setRows).catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  return (
    <Page title={copy.nav.audit} lede={copy.audit.lede}>
      <Table loading={loading} columns={[copy.audit.columns.when, copy.audit.columns.actor, copy.audit.columns.action, copy.audit.columns.resource, copy.audit.columns.outcome]} rows={visible.map((row) => [when(row.occurredAt), row.actor, row.action, row.resource, row.outcome])} />
    </Page>
  );
}

export function Logs({ token, focus }: ReadProps) {
  const tell = useToast();
  const { copy, when, number, feedback } = useI18n();
  const known = remembered<AccessLog[]>(token, "/api/access-logs");
  const [rows, setRows] = useState<AccessLog[]>(known ?? []);
  const [loading, setLoading] = useState(known === undefined);
  const visible = rows.filter((row) => contains(row, focus));

  useEffect(() => {
    load<AccessLog[]>("/api/access-logs", token).then(setRows).catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  return (
    <Page title={copy.nav.logs} lede={copy.logs.lede}>
      <Table loading={loading} columns={[copy.logs.columns.when, copy.logs.columns.subject, copy.logs.columns.method, copy.logs.columns.path, copy.logs.columns.status, copy.logs.columns.latency]} rows={visible.map((row) => [when(row.occurredAt), row.subject, row.method, row.path, number(row.status), number(row.latencyMs)])} />
    </Page>
  );
}

export function Health({ token }: { token: string }) {
  const tell = useToast();
  const { copy, status, feedback } = useI18n();
  const known = remembered<HealthRow[]>(token, "/api/platform/health");
  const [rows, setRows] = useState<HealthRow[]>(known ?? []);
  const [loading, setLoading] = useState(known === undefined);

  useEffect(() => {
    load<HealthRow[]>("/api/platform/health", token).then(setRows).catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  return (
    <Page title={copy.nav.health} lede={copy.health.lede}>
      <Table loading={loading} columns={[copy.health.columns.component, copy.health.columns.status, copy.health.columns.detail]} rows={rows.map((row) => [row.name, status(row.status), row.detail])} />
    </Page>
  );
}

export function Policies({ token, role, focus }: ScreenProps) {
  const tell = useToast();
  const { copy, text, role: roleName, feedback } = useI18n();
  const known = remembered<PolicyRule[]>(token, "/api/policies");
  const [rows, setRows] = useState<PolicyRule[]>(known ?? []);
  const [loading, setLoading] = useState(known === undefined);
  const [id, setId] = useState("");
  const [resource, setResource] = useState("plan");
  const [ruleRole, setRuleRole] = useState("support");
  const [action, setAction] = useState("read");
  const [effect, setEffect] = useState("allow");
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [pending, setPending] = useState<Confirmation | null>(null);
  const govern = canGovern(role);
  const visible = rows.filter((row) => contains(row, focus));

  async function refresh() {
    setRows(await load<PolicyRule[]>("/api/policies", token));
  }

  useEffect(() => {
    refresh().catch((reason: unknown) => tell(feedback(reason), "err")).finally(() => setLoading(false));
  }, [token]);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = problems({ resource: required(resource), action: required(action) });
    setErrors(next);
    if (invalid(next)) {
      return;
    }
    holdSubmit(event);
    try {
      await save("/api/policies/" + (id || crypto.randomUUID()), token, { resource, role: ruleRole, action, effect });
      setId("");
      tell(id ? copy.policies.updated : copy.policies.saved);
      await refresh();
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  return (
    <Page title={copy.nav.policies} lede={copy.policies.lede}>
      {govern ? (
        <form className="mb-6 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5" onSubmit={submit}>
          <Field label={copy.fields.resource} error={text(errors.resource, copy.fields.resource)}><input className={control} value={resource} onChange={(event) => setResource(event.target.value)} /></Field>
          <Field label={copy.fields.role}>
            <select className={control} value={ruleRole} onChange={(event) => setRuleRole(event.target.value)}>
              <option value="admin">{copy.roles.admin}</option>
              <option value="support">{copy.roles.support}</option>
              <option value="auditor">{copy.roles.auditor}</option>
              <option value="owner">{copy.roles.owner}</option>
            </select>
          </Field>
          <Field label={copy.fields.action} error={text(errors.action, copy.fields.action)}><input className={control} value={action} onChange={(event) => setAction(event.target.value)} /></Field>
          <Field label={copy.fields.effect}>
            <select className={control} value={effect} onChange={(event) => setEffect(event.target.value)}>
              <option value="allow">allow</option>
              <option value="deny">deny</option>
            </select>
          </Field>
          <div className="lg:self-end"><button className="w-full acao rounded-xl px-3 py-2 text-sm" type="submit">{id ? copy.policies.update : copy.policies.save}</button></div>
        </form>
      ) : <p className="mb-4 text-sm text-slate-500">{copy.policies.readOnly}</p>}
      <Table
        loading={loading}
        columns={govern ? [copy.policies.columns.resource, copy.policies.columns.role, copy.policies.columns.action, copy.policies.columns.effect, ""] : [copy.policies.columns.resource, copy.policies.columns.role, copy.policies.columns.action, copy.policies.columns.effect]}
        rows={visible.map((row) => govern ? [row.resource, roleName(row.role), row.action, row.effect, row.id] : [row.resource, roleName(row.role), row.action, row.effect])}
        onEdit={govern ? (rowId) => {
          const row = rows.find((item) => item.id === rowId);
          if (!row) {
            return;
          }
          setId(row.id);
          setResource(row.resource);
          setRuleRole(row.role);
          setAction(row.action);
          setEffect(row.effect);
        } : null}
        onRemove={govern ? (rowId) => setPending({ id: rowId, title: copy.policies.title, confirm: copy.policies.confirm, warning: copy.policies.warning, detail: copy.policies.detail }) : null}
        removeLabel={copy.actions.deactivate}
      />
      <ConfirmDialog pending={pending} onCancel={() => setPending(null)} onConfirm={() => {
        if (!pending) {
          return;
        }
        remove("/api/policies/" + pending.id, token).then(() => {
          setPending(null);
          tell(copy.policies.deactivated);
          return refresh();
        }).catch((reason: unknown) => tell(feedback(reason), "err"));
      }} />
    </Page>
  );
}

export function Security({ token }: { token: string }) {
  const tell = useToast();
  const { copy, text, feedback } = useI18n();
  const [hint, setHint] = useState("");
  const [code, setCode] = useState("");
  const [error, setError] = useState("");

  async function enrollMfa() {
    setError("");
    try {
      const result = await post<AccountResult>("/api/account/factors/mfa", token, {});
      setHint(feedback(result.hint || result.detail || ""));
      tell(copy.security.started);
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  async function confirmMfa(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const next = required(code);
    setError(next);
    if (next) {
      return;
    }
    try {
      await post("/api/account/factors/mfa/confirm", token, { code });
      setHint(copy.security.enabledHint);
      setCode("");
      tell(copy.security.enabled);
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  async function enrollPasskey() {
    try {
      const started = await post<AccountResult>("/api/account/factors/passkey/options", token, {});
      if (!started.options) {
        throw new Error("The passkey challenge is missing");
      }
      const credential = await navigator.credentials.create({ publicKey: creationOptions(started.options) });
      if (!(credential instanceof PublicKeyCredential)) {
        throw new Error("The authenticator did not return a passkey");
      }
      await post("/api/account/factors/passkey", token, { challenge: started.options.challengeId, credential: serialize(credential) });
      setHint(copy.security.registeredHint);
      tell(copy.security.registered);
    } catch (reason: unknown) {
      tell(feedback(reason), "err");
    }
  }

  return (
    <Page title={copy.nav.security} lede={copy.security.lede}>
      {hint ? <p className="mb-4 break-all rounded-lg bg-amber-100 px-3 py-2 text-sm text-amber-950">{hint}</p> : null}
      <div className="max-w-xl space-y-3">
        <button className="acao rounded-xl px-3 py-2 text-sm" type="button" onClick={enrollMfa}>{copy.security.start}</button>
        <form className="grid grid-cols-1 gap-3 sm:grid-cols-[1fr_auto]" onSubmit={confirmMfa}>
          <Field label={copy.security.code} error={text(error, copy.security.code)}><input className={control} value={code} onChange={(event) => setCode(event.target.value)} /></Field>
          <div className="sm:self-end"><button className="rounded-lg border border-slate-300 px-3 py-2 text-sm" type="submit">{copy.security.enable}</button></div>
        </form>
        <button className="rounded-lg border border-slate-300 px-3 py-2 text-sm" type="button" onClick={enrollPasskey}>{copy.security.passkey}</button>
      </div>
    </Page>
  );
}
