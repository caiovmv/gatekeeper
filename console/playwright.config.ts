import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  use: {
    baseURL: process.env.GATEKEEPER_CONSOLE_URL ?? "http://127.0.0.1:5173",
    locale: "pt-BR",
  },
});
