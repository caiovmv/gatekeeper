import { BrowserRouter } from "react-router";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { I18nProvider } from "./i18n";
import { lerTema, pintarTema } from "./theme";
import "./index.css";

pintarTema(lerTema());

const root = document.getElementById("root");
if (!root) {
  throw new Error("The console root is missing");
}

createRoot(root).render(
  <StrictMode>
    <BrowserRouter>
      <I18nProvider>
        <App />
      </I18nProvider>
    </BrowserRouter>
  </StrictMode>,
);
