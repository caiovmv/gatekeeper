import { expect, test } from "@playwright/test";

const email = process.env.AGENT_STAFF_EMAIL ?? "staff@agentflow.test";
const password = process.env.AGENT_STAFF_PASSWORD ?? "Staff-pass-1";

test("a signed-out visitor sees the console sign-in", async ({ page }) => {
  await page.goto("/health");
  await expect(page.getByRole("heading", { name: "Acesso à console" })).toBeVisible();
  await expect(page.getByText("provedor de identidade")).toBeVisible();
});

test("a deep link stays on the sign-in until the session exists", async ({ page }) => {
  await page.goto("/audit", { waitUntil: "domcontentloaded" });
  await expect(page.getByRole("heading", { name: "Acesso à console" })).toBeVisible();
  await page.goto("/plans", { waitUntil: "domcontentloaded" });
  await expect(page.getByRole("heading", { name: "Acesso à console" })).toBeVisible();
});

test("staff opens health and saves a plan", async ({ page }) => {
  await page.goto("/health");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(password);
  await page.locator("form").getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Saúde" })).toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Conta" }).click();
  await page.getByRole("button", { name: "Escuro" }).click();
  await expect(page.locator("html")).toHaveClass(/dark/);
  await expect(page.getByRole("row", { name: /zitadel/ })).toContainText("saudável");
  await expect(page.getByRole("row", { name: /cerbos/ })).toContainText("saudável");

  await page.getByRole("button", { name: "Planos" }).click();
  const name = "Ao vivo " + Date.now().toString().slice(-6);
  await page.getByLabel("Nome").fill(name);
  await page.getByLabel("Preço").fill("10");
  await page.getByLabel("Limite de chamadas").fill("2");
  await page.getByLabel("Limite de assentos").fill("2");
  await page.getByRole("button", { name: "Salvar plano" }).click();
  await expect(page.getByRole("cell", { name })).toBeVisible();
  await page.getByLabel("Busca").fill(name);
  await expect(page.getByRole("button", { name })).toBeVisible();

  const plans: string[] = [];
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (request.method() === "GET" && url.pathname === "/api/plans") {
      plans.push(url.pathname);
    }
  });
  await page.getByRole("button", { name: "Saúde" }).click();
  await expect(page.getByRole("heading", { name: "Saúde" })).toBeVisible();
  await page.getByRole("button", { name: "Planos" }).click();
  await expect(page.getByRole("cell", { name })).toBeVisible();
  expect(plans).toEqual([]);

  plans.length = 0;
  const renamed = name + " novo";
  await page.getByLabel("Nome").fill(renamed);
  await page.getByLabel("Preço").fill("10");
  await page.getByLabel("Limite de chamadas").fill("2");
  await page.getByLabel("Limite de assentos").fill("2");
  await page.getByRole("button", { name: "Salvar plano" }).click();
  await expect(page.getByRole("cell", { name: renamed })).toBeVisible();
  expect(plans.length).toBeGreaterThan(0);

  await expect(page.getByRole("row", { name }).getByRole("cell", { name: /R\$\s*10,00/ })).toBeVisible();
  await page.reload({ waitUntil: "domcontentloaded" });
  await expect(page.getByRole("cell", { name })).toBeVisible({ timeout: 30_000 });
  await page.getByRole("row", { name }).getByRole("button", { name: "Desativar" }).click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await expect(page.getByText("Esta é uma exclusão lógica.")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toBeHidden();
  await page.getByRole("button", { name: "Visão geral" }).click();
  await expect(page.getByText("Fonte: API de uso")).toBeVisible();
});

test("a denied overview is shown as a denial", async ({ page }) => {
  await page.route("**/api/organizations", (route) => route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ detail: "denied" }) }));
  await page.goto("/overview", { waitUntil: "domcontentloaded" });
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(password);
  await page.locator("form").getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByText("Esta organização está fora da sua atribuição.")).toBeVisible({ timeout: 30_000 });
});
