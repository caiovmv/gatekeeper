import { expect, test } from "@playwright/test";

test.use({ viewport: { width: 1280, height: 800 } });

test("organizations stay in memory until a save asks for them again", async ({ page }) => {
  await page.goto("/organizations");
  await page.getByLabel("E-mail").fill("staff@gatekeeper.test");
  await page.getByLabel("Nome de exibição").fill("Staff");
  await page.getByLabel("Senha").fill("Staff-pass-1");
  await page.getByRole("button", { name: "Criar o administrador" }).click();
  await expect(page.getByRole("heading", { name: "Organizações" })).toBeVisible({ timeout: 30_000 });
  await expect(page.getByText("Nada salvo ainda.")).toBeVisible();

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
  await expect(page.getByText("Nada salvo ainda.")).toBeVisible();
  expect(reads).toEqual([]);

  reads.length = 0;
  const name = "Cache " + Date.now().toString().slice(-6);
  await page.getByLabel("Nome").fill(name);
  await page.getByLabel("Host").fill(name.toLowerCase().replaceAll(" ", "") + ".test");
  await page.getByRole("button", { name: "Salvar organização" }).click();
  await expect(page.getByRole("cell", { name })).toBeVisible();
  expect(reads.length).toBeGreaterThan(0);
});
