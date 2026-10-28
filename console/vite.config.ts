import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ mode }) => {
  // loadEnv com prefixo vazio traz também as variáveis do ambiente do processo, sem exigir os tipos do Node.
  const api = loadEnv(mode, ".", "").GATEKEEPER_API_URL ?? "http://127.0.0.1:5080";
  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 5173,
      fs: { allow: [".."] },
      proxy: {
        "/api": api,
        "/health/ready": api,
        "/health/complete": api,
      },
    },
    build: {
      rollupOptions: {
        input: ["index.html"],
      },
    },
  };
});
