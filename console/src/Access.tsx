import { useEffect, useState, type FormEvent } from "react";
import { post } from "./api";
import { emailOf, invalid, passwordOf, problems, required } from "./forms";
import { useI18n } from "./i18n";
import { Field, control, holdSubmit } from "./ui";
import type { AccountResult, AccountStatus, PasskeyOptions } from "./types";

type AccessMode = "signin" | "setup" | "mfa" | "reset" | "reset-confirm" | "passkey";

type AccessProps = {
  onSignedIn: (token: string) => void;
};

export function Access({ onSignedIn }: AccessProps) {
  const { copy, text, feedback } = useI18n();
  const [mode, setMode] = useState<AccessMode>("signin");
  const [provider, setProvider] = useState("local");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [code, setCode] = useState("");
  const [challenge, setChallenge] = useState("");
  const [hint, setHint] = useState("");
  const [error, setError] = useState("");
  const [errors, setErrors] = useState<Record<string, string>>({});

  useEffect(() => {
    fetch("/api/account/status")
      .then((response) => response.json() as Promise<AccountStatus>)
      .then((status) => {
        setProvider(status.provider);
        if (status.setupRequired) {
          setMode("setup");
        }
      })
      .catch((reason: unknown) => setError(feedback(reason)));
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    const next = problems({
      email: mode === "mfa" ? "" : emailOf(email),
      displayName: mode === "setup" ? required(displayName) : "",
      password: mode === "signin" || mode === "setup" || mode === "reset-confirm" ? passwordOf(password, false) : "",
      code: mode === "mfa" || mode === "reset-confirm" ? required(code) : "",
    });
    setErrors(next);
    if (invalid(next)) {
      return;
    }
    holdSubmit(event);
    try {
      if (mode === "setup") {
        const result = await post<AccountResult>("/api/account/setup", "", { email, password, displayName });
        onSignedIn(result.token ?? "");
        return;
      }

      if (mode === "signin") {
        const result = await post<AccountResult>("/api/account/login", "", { email, password });
        if (result.status === "mfa") {
          setChallenge(result.challenge ?? "");
          setMode("mfa");
          return;
        }

        onSignedIn(result.token ?? "");
        return;
      }

      if (mode === "mfa") {
        const result = await post<AccountResult>("/api/account/mfa", "", { challenge, code });
        onSignedIn(result.token ?? "");
        return;
      }

      if (mode === "reset") {
        const result = await post<AccountResult>("/api/account/password-reset", "", { email });
        setHint(feedback(result.hint || result.detail || ""));
        setMode("reset-confirm");
        return;
      }

      if (mode === "reset-confirm") {
        const result = await post<AccountResult>("/api/account/password-reset/confirm", "", { email, code, password });
        onSignedIn(result.token ?? "");
        return;
      }

      const options = await post<AccountResult>("/api/account/passkey/options", "", { email });
      if (!options.options) {
        throw new Error("The passkey challenge is missing");
      }

      const credential = await navigator.credentials.get({ publicKey: requestOptions(options.options) });
      if (!(credential instanceof PublicKeyCredential)) {
        throw new Error("The authenticator did not return a passkey");
      }

      const result = await post<AccountResult>("/api/account/passkey", "", { challenge: options.options.challengeId, credential: serialize(credential) });
      onSignedIn(result.token ?? "");
    } catch (reason: unknown) {
      setError(feedback(reason) || copy.server.signInFailed);
    }
  }

  return (
    <main className="papel grid min-h-screen place-items-center p-6">
      <section className="cartao w-full max-w-md p-8">
        <p className="faixa-rotulo">{copy.brand}</p>
        <h1 className="mt-2 text-2xl font-semibold">{copy.access.title}</h1>
        <p className="mt-2 text-sm tinta-apoio">{provider === "zitadel" ? copy.access.zitadel : copy.access.local}</p>
        <nav className="mt-4 flex gap-3 text-sm tinta-apoio">
          <button className="marca" type="button" onClick={() => setMode(provider === "local" && mode === "setup" ? "setup" : "signin")}>{copy.access.signIn}</button>
          <button type="button" onClick={() => setMode("reset")}>{copy.access.reset}</button>
          <button type="button" onClick={() => setMode("passkey")}>{copy.access.passkey}</button>
        </nav>
        {error ? <p className="mt-4 rounded-xl px-3 py-2 text-sm" style={{ background: "var(--warning)", color: "var(--danger)" }}>{error}</p> : null}
        {hint ? <p className="mt-4 rounded-xl px-3 py-2 text-sm" style={{ background: "var(--warning)", color: "var(--warning-ink)" }}>{hint}</p> : null}
        <form className="mt-4 space-y-3" onSubmit={submit}>
          {mode !== "mfa" ? <Field label={copy.fields.email} error={text(errors.email, copy.fields.email)}><input className={control} value={email} onChange={(event) => setEmail(event.target.value)} /></Field> : null}
          {mode === "setup" ? <Field label={copy.fields.displayName} error={text(errors.displayName, copy.fields.displayName)}><input className={control} value={displayName} onChange={(event) => setDisplayName(event.target.value)} /></Field> : null}
          {mode === "signin" || mode === "setup" || mode === "reset-confirm" ? <Field label={copy.fields.password} error={text(errors.password, copy.fields.password)}><input className={control} placeholder={mode === "reset-confirm" ? copy.fields.newPassword : ""} type="password" value={password} onChange={(event) => setPassword(event.target.value)} /></Field> : null}
          {mode === "mfa" || mode === "reset-confirm" ? <Field label={mode === "mfa" ? copy.fields.authenticator : copy.fields.resetCode} error={text(errors.code, copy.fields.code)}><input className={control} value={code} onChange={(event) => setCode(event.target.value)} /></Field> : null}
          <button className="acao w-full rounded-xl px-3 py-2 text-sm" type="submit">{submitLabel(mode, copy)}</button>
        </form>
      </section>
    </main>
  );
}

import type { Copy } from "./messages";

function submitLabel(mode: AccessMode, copy: Copy): string {
  if (mode === "setup") {
    return copy.access.create;
  }

  if (mode === "mfa") {
    return copy.access.confirmCode;
  }

  if (mode === "reset") {
    return copy.access.sendCode;
  }

  if (mode === "reset-confirm") {
    return copy.access.newPassword;
  }

  if (mode === "passkey") {
    return copy.access.usePasskey;
  }

  return copy.access.signIn;
}

export function requestOptions(options: PasskeyOptions): PublicKeyCredentialRequestOptions {
  return {
    challenge: bytes(options.challenge),
    rpId: options.rpId,
    timeout: 60000,
    userVerification: "preferred",
    allowCredentials: (options.credentialIds ?? []).map((id) => ({ id: bytes(id), type: "public-key" })),
  };
}

export function creationOptions(options: PasskeyOptions): PublicKeyCredentialCreationOptions {
  return {
    challenge: bytes(options.challenge),
    rp: { name: "Backoffice", id: options.rpId },
    user: { id: bytes(options.userId), name: options.userName, displayName: options.userName },
    pubKeyCredParams: [{ type: "public-key", alg: -7 }],
    timeout: 60000,
    authenticatorSelection: { residentKey: "preferred", userVerification: "preferred" },
  };
}

export function serialize(credential: PublicKeyCredential): string {
  const response = credential.response;
  const attestation = response instanceof AuthenticatorAttestationResponse ? response.attestationObject : undefined;
  const assertion = response instanceof AuthenticatorAssertionResponse ? response : null;
  return JSON.stringify({
    id: credential.id,
    type: credential.type,
    response: {
      clientDataJSON: text(response.clientDataJSON),
      attestationObject: attestation ? text(attestation) : undefined,
      authenticatorData: assertion ? text(assertion.authenticatorData) : undefined,
      signature: assertion ? text(assertion.signature) : undefined,
    },
  });
}

function bytes(value: string): Uint8Array<ArrayBuffer> {
  const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/"));
  const output = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    output[index] = binary.charCodeAt(index);
  }

  return output;
}

function text(buffer: ArrayBuffer): string {
  const values = new Uint8Array(buffer);
  let binary = "";
  values.forEach((value) => {
    binary += String.fromCharCode(value);
  });
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}
