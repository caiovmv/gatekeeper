import { expect, test } from "@playwright/test";

const email = process.env.GATEKEEPER_STAFF_EMAIL ?? "staff@gatekeeper.test";
const password = process.env.GATEKEEPER_STAFF_PASSWORD ?? "Staff-pass-1";

test("a signed-out visitor sees the console sign-in", async ({ page }) => {
  await page.goto("/health");
  await expect(page.getByRole("heading", { name: "Acesso ao backoffice" })).toBeVisible();
  await expect(page.getByText("A primeira conta pertence à empresa do serviço.")).toBeVisible();
});

test("a deep link stays on the sign-in until the session exists", async ({ page }) => {
  await page.goto("/audit", { waitUntil: "domcontentloaded" });
  await expect(page.getByRole("heading", { name: "Acesso ao backoffice" })).toBeVisible();
  await page.goto("/organizations", { waitUntil: "domcontentloaded" });
  await expect(page.getByRole("heading", { name: "Acesso ao backoffice" })).toBeVisible();
});

test("staff opens health and saves an organization", async ({ page }) => {
  await page.goto("/health");
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(password);
  await page.locator("form").getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Saúde" })).toBeVisible({ timeout: 30_000 });
  await page.getByRole("button", { name: "Conta" }).click();
  await page.getByRole("button", { name: "Escuro" }).click();
  await expect(page.locator("html")).toHaveClass(/dark/);

  await page.getByRole("button", { name: "Organizações" }).click();
  const name = "Ao vivo " + Date.now().toString().slice(-6);
  const host = name.toLowerCase().replaceAll(" ", "") + ".test";
  await page.getByLabel("Nome").fill(name);
  await page.getByLabel("Host").fill(host);
  await page.getByRole("button", { name: "Salvar organização" }).click();
  await expect(page.getByRole("cell", { name })).toBeVisible();

  const reads: string[] = [];
  page.on("request", (request) => {
    const url = new URL(request.url());
    if (request.method() === "GET" && url.pathname === "/api/organizations") {
      reads.push(url.pathname);
    }
  });
  await page.getByRole("button", { name: "Saúde" }).click();
  await expect(page.getByRole("heading", { name: "Saúde" })).toBeVisible();
  await page.getByRole("button", { name: "Organizações" }).click();
  await expect(page.getByRole("cell", { name })).toBeVisible();
  expect(reads).toEqual([]);

  reads.length = 0;
  const renamed = name + " novo";
  await page.getByLabel("Nome").fill(renamed);
  await page.getByLabel("Host").fill(host);
  await page.getByRole("button", { name: "Salvar organização" }).click();
  await expect(page.getByRole("cell", { name: renamed })).toBeVisible();
  expect(reads.length).toBeGreaterThan(0);

  await page.getByRole("button", { name: "Logs" }).click();
  await expect(page.getByRole("heading", { name: "Logs" })).toBeVisible();
  await page.getByRole("button", { name: "Visão geral" }).click();
  await expect(page.getByRole("heading", { name: "Visão geral" })).toBeVisible();
});

test("a denied overview is shown as a denial", async ({ page }) => {
  await page.route("**/api/organizations", (route) => route.fulfill({ status: 403, contentType: "application/json", body: JSON.stringify({ detail: "denied" }) }));
  await page.goto("/overview", { waitUntil: "domcontentloaded" });
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(password);
  await page.locator("form").getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByText("Esta organização está fora da sua atribuição.")).toBeVisible({ timeout: 30_000 });
});
