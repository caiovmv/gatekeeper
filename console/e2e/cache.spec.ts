import { expect, test } from "@playwright/test";

test.use({ viewport: { width: 1280, height: 800 } });

test("plans stay in memory until a save asks for them again", async ({ page }) => {
  await page.goto("/plans");
  await page.getByLabel("E-mail").fill("staff@agentflow.test");
  await page.getByLabel("Nome de exibição").fill("Staff");
  await page.getByLabel("Senha").fill("Staff-pass-1");
  await page.getByRole("button", { name: "Criar o administrador" }).click();
  await expect(page.getByRole("heading", { name: "Planos" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Nada salvo ainda.")).toBeVisible();

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
  await expect(page.getByText("Nada salvo ainda.")).toBeVisible();
  expect(plans).toEqual([]);

  plans.length = 0;
  const name = "Cache " + Date.now().toString().slice(-6);
  await page.getByLabel("Nome").fill(name);
  await page.getByLabel("Preço").fill("10");
  await page.getByLabel("Limite de chamadas").fill("2");
  await page.getByLabel("Limite de assentos").fill("2");
  await page.getByRole("button", { name: "Salvar plano" }).click();
  await expect(page.getByRole("cell", { name })).toBeVisible();
  expect(plans.length).toBeGreaterThan(0);
});
