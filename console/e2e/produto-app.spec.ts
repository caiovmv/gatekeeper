import { expect, test } from "@playwright/test";

test("o app do cliente reusa o idioma e fala com a API sem a navegação de staff", async ({ page }) => {
  await page.route("**/api/organizations/**", (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    headers: { "X-Cache-Evict": "organizations,audit" },
    body: JSON.stringify({ id: "org" }),
  }));
  await page.route("**/api/produto/agente", (route) => route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ saida: "novo" }),
  }));

  await page.goto("/produto.html");
  await expect(page.getByRole("heading", { name: /Seu próximo grande time/i })).toBeVisible();
  await expect(page.getByRole("heading", { name: "System Level 1" })).toHaveCount(0);
  await page.getByRole("button", { name: "sdr" }).click();
  await expect(page.locator("#instrucao")).toContainText("Qualificar o lead");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.locator("#agente")).toHaveCount(0);
  // Do portfólio ao assistente: validação por etapa, avanço, revisão e publicação.
  await page.getByRole("button", { name: "sdr" }).click();
  await page.getByRole("button", { name: "Usar modelo" }).click();
  await expect(page.getByRole("heading", { name: "Identidade" })).toBeVisible();
  await page.getByLabel("Nome do agente").fill("");
  await page.getByRole("button", { name: "Próximo" }).click();
  await expect(page.getByRole("alert")).toContainText("Dê um nome ao agente.");
  await page.getByLabel("Nome do agente").fill("SDR Teste");
  for (const etapa of ["Voz e canal", "Comportamento", "Ferramentas e limites", "Testar", "Revisar e publicar"]) {
    await page.getByRole("button", { name: "Próximo" }).click();
    await expect(page.getByRole("heading", { name: etapa })).toBeVisible();
  }
  await page.getByRole("button", { name: "Publicar agente" }).click();
  await expect(page.getByRole("alert")).toContainText("Confirme que você revisou");
  await page.getByLabel("Revisei as instruções e os limites deste agente.").check();
  await page.getByRole("button", { name: "Publicar agente" }).click();
  await page.getByRole("button", { name: "Editar cadeia" }).click();
  await page.locator("#canal-texto").click();
  await expect(page.locator("#motor")).toHaveText("nenhum");
  await page.locator("#salvar-avancado").click();
  await expect(page.locator("#hop")).toHaveText("base");
  await page.locator("#confirmar-hop").click();
  await expect(page.locator("#hop")).toHaveText("candidato");
  await page.getByRole("button", { name: "Editar EQ" }).click();
  await expect(page.locator("#preset")).toHaveText("Voz");
  await page.locator("#bypass").click();
  await expect(page.locator("#ruido-estado")).toHaveText("Bypass");
  await page.getByRole("button", { name: "Editar caminho" }).click();
  await page.locator("#caminho-g711").click();
  await expect(page.locator("#caminho")).toHaveText("g711");
  await page.getByRole("button", { name: "Agency Portfolio" }).click();
  await page.locator("#comprar").evaluate((node: HTMLElement) => node.click());
  await expect(page.locator("#agente")).toBeAttached();
  await expect(page.locator("#organizacao")).not.toBeEmpty();
  await page.locator("#perguntar").evaluate((node: HTMLElement) => node.click());
  await expect(page.locator("#saida")).toHaveText("novo");
});
