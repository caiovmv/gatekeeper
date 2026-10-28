import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { message } from "./api";
import { en, ptBR, type Copy, type Locale } from "./messages";

const catalogs: Record<Locale, Copy> = { "pt-BR": ptBR, en };

const LocaleContext = createContext<{
  locale: Locale;
  copy: Copy;
  setLocale: (locale: Locale) => void;
}>({ locale: "en", copy: en, setLocale: () => {} });

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>(initialLocale);

  useEffect(() => {
    document.documentElement.lang = locale;
    localStorage.setItem("gatekeeper.locale", locale);
  }, [locale]);

  function setLocale(next: Locale) {
    setLocaleState(next);
  }

  return (
    <LocaleContext.Provider value={{ locale, copy: catalogs[locale], setLocale }}>
      {children}
    </LocaleContext.Provider>
  );
}

export function useI18n() {
  const { locale, copy, setLocale } = useContext(LocaleContext);
  return {
    locale,
    copy,
    setLocale,
    text(code: string | undefined, label: string) {
      return phrase(copy, code, label);
    },
    money(value: number) {
      return new Intl.NumberFormat(locale, { style: "currency", currency: "BRL" }).format(value);
    },
    number(value: number) {
      return new Intl.NumberFormat(locale).format(value);
    },
    day(value: string | null | undefined) {
      return stamp(locale, value, { dateStyle: "medium" });
    },
    when(value: string | null | undefined) {
      return stamp(locale, value, { dateStyle: "medium", timeStyle: "short" });
    },
    role(value: string) {
      if (value === "admin" || value === "support" || value === "auditor" || value === "owner") {
        return copy.roles[value];
      }
      return value;
    },
    status(value: string) {
      if (value in copy.status) {
        return copy.status[value as keyof Copy["status"]];
      }
      return value;
    },
    feedback(reason: unknown) {
      return translate(copy, message(reason));
    },
  };
}

function initialLocale(): Locale {
  const stored = localStorage.getItem("gatekeeper.locale");
  if (stored === "pt-BR" || stored === "en") {
    return stored;
  }

  return navigator.language.toLowerCase().startsWith("pt") ? "pt-BR" : "en";
}

function phrase(copy: Copy, code: string | undefined, label: string): string {
  if (!code) {
    return "";
  }

  if (code === "required") {
    return copy.errors.required(label);
  }

  if (code === "email") {
    return copy.errors.email;
  }

  if (code === "password") {
    return copy.errors.password;
  }

  if (code === "whole") {
    return copy.errors.whole(label);
  }

  if (code === "amount") {
    return copy.errors.amount;
  }

  if (code === "end") {
    return copy.errors.end;
  }

  if (code.startsWith("min:")) {
    return copy.errors.min(label, Number(code.slice(4)));
  }

  return code;
}

function stamp(locale: Locale, value: string | null | undefined, options: Intl.DateTimeFormatOptions): string {
  if (!value) {
    return "—";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return new Intl.DateTimeFormat(locale, options).format(date);
}

const serverText: Record<string, keyof Copy["server"]> = {
  load: "load",
  save: "save",
  request: "request",
  remove: "remove",
  failed: "failed",
  "This console belongs to the service company. Account owners do not sign in here.": "owners",
  "This console belongs to the service company.": "company",
  "sign in is required": "signIn",
  "credentials were rejected": "credentials",
  "account is invalid": "account",
  "setup is closed": "setup",
  denied: "denied",
  "policy is invalid": "policy",
  "The passkey challenge is missing": "passkey",
  "The authenticator did not return a passkey": "authenticator",
  "Sign-in failed": "signInFailed",
};

function translate(copy: Copy, text: string): string {
  const key = serverText[text];
  if (!key) {
    return text;
  }

  return copy.server[key];
}
