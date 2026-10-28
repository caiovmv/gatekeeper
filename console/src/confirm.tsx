import { useEffect } from "react";
import type { Confirmation } from "./types";
import { useI18n } from "./i18n";

type ConfirmDialogProps = {
  pending: Confirmation | null;
  onCancel: () => void;
  onConfirm: () => void;
};

export function ConfirmDialog({ pending, onCancel, onConfirm }: ConfirmDialogProps) {
  const { copy } = useI18n();
  useEffect(() => {
    if (!pending) {
      return;
    }

    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    function onKey(event: KeyboardEvent) {
      if (event.key === "Escape") {
        onCancel();
      }
    }

    window.addEventListener("keydown", onKey);
    return () => {
      window.removeEventListener("keydown", onKey);
      previous?.focus();
    };
  }, [pending, onCancel]);

  if (!pending) {
    return null;
  }

  return (
    <div className="fixed inset-0 z-20 grid place-items-end p-4 sm:place-items-center" style={{ background: "rgba(0,0,0,0.55)" }} role="dialog" aria-modal="true" aria-labelledby="confirm-title">
      <div className="cartao w-full max-w-md p-6">
        <h3 id="confirm-title" className="text-lg font-semibold tracking-tight">{pending.title}</h3>
        <p className="mt-3 rounded-xl px-3 py-2 text-sm" style={{ background: "var(--warning)", color: "var(--warning-ink)" }}>{pending.warning}</p>
        <p className="mt-3 text-sm tinta-apoio">{pending.detail}</p>
        <div className="mt-5 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <button className="acao-linha rounded-xl px-3 py-2 text-sm" type="button" onClick={onCancel}>{copy.actions.cancel}</button>
          <button className="rounded-xl px-3 py-2 text-sm text-white" style={{ background: "var(--danger)" }} type="button" onClick={onConfirm}>{pending.confirm}</button>
        </div>
      </div>
    </div>
  );
}
