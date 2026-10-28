import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "@playwright/test";

const raiz = join(dirname(fileURLToPath(import.meta.url)), "../../produto");

test("showroom, rack e widget mostram os controles do contrato", async ({ page }) => {
  await page.setContent(readFileSync(join(raiz, "index.html"), "utf8"));
  await expect(page.getByRole("button", { name: "Captura de lead" })).toBeVisible();
  await page.getByRole("button", { name: "SDR" }).click();
  await expect(page.locator("#escolha")).toHaveText("sdr");
  await expect(page.locator("#instrucao")).toHaveValue("Qualificar o lead e agendar.");
  await expect(page.locator("#modalidade")).toHaveText("Cascata Gerenciada");
  await expect(page.locator("#passo-experimentar")).toBeVisible();
  await page.locator("#proximo").click();
  await expect(page.locator("#passo-atual")).toHaveText("voz");
  await page.locator("#prompt").fill("atender clientes");
  await page.locator("#atalho").click();
  await expect(page.locator("#esqueleto")).toContainText("objetivo: atender clientes");
  await page.locator("#abrir-avancado").click();
  await expect(page.locator("#avancado")).toBeVisible();
  await page.locator("#medir-replay").click();
  await expect(page.locator("#quadro-replay")).toHaveText("ganho 50");
  await page.locator("#canal-texto").click();
  await expect(page.locator("#motor")).toHaveText("nenhum");
  await page.locator("#salvar-avancado").click();
  await expect(page.locator("#pares")).toBeVisible();
  await expect(page.locator("#hop")).toHaveText("base");
  await page.locator("#confirmar-hop").click();
  await expect(page.locator("#hop")).toHaveText("candidato");
  await expect(page.locator("#agente")).toBeHidden();
  await page.locator("#comprar").click();
  await expect(page.locator("#agente")).toBeVisible();
  await expect(page.locator("#organizacao")).not.toBeEmpty();
  await page.locator("#argumento").fill("novo");
  await page.locator("#ferramenta").selectOption("salvar_agente");
  await page.locator("#perguntar").click();
  await expect(page.locator("#saida")).toHaveText("novo");

  await page.setContent(readFileSync(join(raiz, "dsp.html"), "utf8"));
  await expect(page.locator("#preset")).toHaveText("Voz");
  await expect(page.locator("#ruido-estado")).toHaveText("automatico");
  await page.locator("#bypass").click();
  await expect(page.locator("#ruido-estado")).toHaveText("bypass");
  await expect(page.locator("#hpf")).toHaveValue("100");
  await expect(page.locator("#lpf")).toHaveValue("3400");
  await page.locator("#experimentar").click();
  await expect(page.locator("#erro")).toHaveText("WER 0");

  await page.setContent(readFileSync(join(raiz, "widget.html"), "utf8"));
  await page.locator("#caminho-g711").click();
  await expect(page.locator("#caminho")).toHaveText("g711");
  await page.locator("#caminho-g729").click();
  await expect(page.locator("#caminho")).toHaveText("g729");
  await expect(page.locator("#quadro")).toContainText("WER");
  await page.locator("#injetar-conjunto").click();
  for (const provedor of ["elevenlabs", "voice-live", "openai", "mistral", "mixtral"]) {
    await expect(page.locator(`[data-provedor="${provedor}"]`)).toContainText("WER");
  }
  await expect(page.locator("#quadro")).toHaveText("WER por provedor");
});
