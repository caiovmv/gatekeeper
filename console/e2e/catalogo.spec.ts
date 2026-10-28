import { expect, test } from "@playwright/test";

const perfil = {
  subject: "staff",
  organization: "org",
  roles: ["admin"],
  email: "staff@agentflow.test",
  displayName: "Staff",
};

const vozes = [
  { nome: "ElevenLabs", modalidade: "CascataGerenciada", modelo: "eleven-agents", outra: null, outros: null, entradas: ["Pcm16 16000"], local: false },
  { nome: "Azure Foundry", modalidade: "SpeechToSpeech", modelo: "gpt-realtime", outra: "CascataGerenciada", outros: null, entradas: ["Pcm16 16000"], local: false },
  { nome: "OpenAI Realtime", modalidade: "SpeechToSpeech", modelo: "gpt-realtime", outra: null, outros: null, entradas: ["Pcm16 24000"], local: false },
  { nome: "Voxtral 3", modalidade: "CascataGerenciada", modelo: "voxtral-mini-tts-realtime-3-0", outra: null, outros: ["voxtral-mini-tts-3-0"], entradas: ["Pcm16 16000"], local: false },
];

test("a console mostra as três seções, a pré-escuta e o dry-run", async ({ page }) => {
  await page.route("**/api/account/me", (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    headers: { "X-Cache-Tags": "account" },
    body: JSON.stringify(perfil),
  }));
  await page.route("**/api/organizations", (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: "[]",
  }));
  await page.route("**/api/voice-engines", (route) => {
    if (route.request().method() !== "GET") {
      return route.fallback();
    }

    return route.fulfill({
      status: 200,
      contentType: "application/json",
      headers: { "X-Cache-Tags": "voice-engines" },
      body: JSON.stringify(vozes),
    });
  });
  await page.route("**/api/voice-engines/*/preview", (route) => route.fulfill({
    status: 200,
    contentType: "audio/mpeg",
    body: Buffer.from([0xff, 0xfb, 0x90, 0x00]),
  }));
  await page.route("**/api/guardrails/dry-run", (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    headers: { "X-Cache-Evict": "guardrails,audit" },
    body: JSON.stringify({ permite: true, herdado: true, codigos: ["base"], aplicado: false, resumo: "aplicado=nao" }),
  }));

  await page.goto("/catalogo");
  await expect(page.getByRole("heading", { name: "System Level 1" })).toBeVisible();
  await expect(page.getByText("Jev", { exact: true })).toBeVisible();
  await expect(page.getByRole("heading", { name: "System Level 2" })).toBeVisible();
  await expect(page.getByText("LlamaServer")).toBeVisible();
  await expect(page.getByText("continuous batching")).toBeVisible();
  await expect(page.getByRole("heading", { name: "System Voice" })).toBeVisible();
  await expect(page.getByText("ElevenLabs").locator("xpath=..")).toContainText("Cascata Gerenciada");
  await expect(page.getByText("Azure Foundry").locator("xpath=..")).toContainText("Speech To Speech");
  await expect(page.getByText("Voxtral 3").locator("xpath=..")).toContainText("Cascata Gerenciada");

  await page.route("**/api/brokers/**", (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    headers: { "X-Cache-Tags": "brokers" },
    body: JSON.stringify({
      modelos: [{ id: "gpt-4o", ferramentas: true, visao: true, audio: false, streaming: true, raciocinio: false, saidaEstruturada: true, modalidade: "" }],
      sugestoes: [{ id: "claude-3-5-sonnet", provedor: "Anthropic", ferramentas: true, visao: true, audio: false, streaming: true, raciocinio: false, saidaEstruturada: true, modalidade: "" }],
    }),
  }));

  await page.getByRole("button", { name: "OpenAI", exact: true }).click();
  await expect(page.locator("#modelos-broker")).toContainText("ferramentas");
  await expect(page.locator("#sugestoes")).toContainText("claude-3-5-sonnet");

  await page.getByRole("button", { name: "Pré-escuta" }).first().click();
  await expect(page.locator("#previa")).toBeVisible();

  await page.getByLabel("Texto").fill("ola");
  await page.getByRole("button", { name: "Dry-run" }).click();
  await expect(page.locator("#dry-run")).toHaveText("aplicado=nao");
});
