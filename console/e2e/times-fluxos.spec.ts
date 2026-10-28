import { expect, test, type Page } from "@playwright/test";

const agentes = [
  { id: "ag-ana", nome: "Ana Comercial", instrucao: "Qualificar leads e agendar reunião.", motor: "voxtral", modalidade: "Cascata Gerenciada", modelo: "sdr", rosto: "sdr" },
  { id: "ag-rui", nome: "Rui Suporte", instrucao: "Resolver o chamado e confirmar o próximo passo.", motor: "elevenlabs", modalidade: "Cascata Gerenciada", modelo: "lead", rosto: "suporte" },
];

async function abrir(page: Page, comAgentes: boolean) {
  await page.addInitScript(({ lista, semear }) => {
    if (semear && !localStorage.getItem("semente")) {
      localStorage.setItem("kohors.agentes.v1", JSON.stringify(lista));
    }
    localStorage.setItem("semente", "1");
    localStorage.setItem("kohors.menu", "0");
  }, { lista: agentes, semear: comAgentes });
  await page.route("**/fonts.googleapis.com/**", (rota) => rota.abort());
  await page.goto("/produto.html");
  await page.waitForTimeout(300);
}

const irPara = (page: Page, item: string) => page.getByRole("button", { name: item, exact: true }).click();

test.use({ viewport: { width: 1440, height: 900 }, reducedMotion: "reduce" });

test("sem agentes, Meus times explica o que fazer primeiro", async ({ page }) => {
  await abrir(page, false);
  await irPara(page, "Meus times");
  await expect(page.getByRole("heading", { name: "Crie um agente primeiro" })).toBeVisible();
  await page.getByRole("button", { name: "Ir para Meus agentes" }).click();
  await expect(page.getByRole("heading", { name: "Meus agentes" })).toBeVisible();
});

test("do processo ao time: processo BPMN, workflow derivado e time que o segue", async ({ page }) => {
  await abrir(page, true);

  // Processo por modelo, desenhado no editor BPMN com raias.
  await irPara(page, "Processos e Workflows");
  await page.getByRole("button", { name: "Novo processo" }).first().click();
  await page.getByRole("radio", { name: /Partir de um modelo/ }).check();
  await page.getByLabel("Nome do processo").fill("Atendimento ao cliente");
  await page.getByRole("button", { name: "Criar", exact: true }).click();
  await expect(page.locator(".react-flow__node").first()).toBeVisible();
  await expect(page.locator(".bpmn-raia")).not.toHaveCount(0);
  await expect(page.locator(".react-flow__edge")).not.toHaveCount(0);

  // Editar: adicionar uma tarefa deixa pendências; desfazer devolve o estado anterior.
  const estado = page.locator(".editor-estado");
  const antes = await estado.innerText();
  await page.getByRole("button", { name: "+ Tarefa humana" }).click();
  await expect(estado).not.toHaveText(antes);
  await page.getByRole("button", { name: "Desfazer" }).click();
  await expect(estado).toHaveText(antes);

  // Workflow derivado do processo: sequência com participantes e fragmento de decisão.
  await page.getByRole("button", { name: "Voltar" }).first().click();
  await page.getByRole("tab", { name: /Workflows/ }).click();
  await page.getByRole("button", { name: "Novo workflow" }).first().click();
  await page.getByLabel("Nome do workflow").fill("Atendimento padrão");
  await page.getByRole("button", { name: "Criar", exact: true }).click();
  await expect(page.locator(".seq-svg")).toBeVisible();
  await expect(page.locator(".seq-cabeca")).not.toHaveCount(0);
  await expect(page.locator(".seq-fragmento")).not.toHaveCount(0);
  // Selecionar um passo abre as propriedades dele.
  await page.locator(".seq-alvo[role=button]").nth(5).click({ force: true });
  await expect(page.getByLabel("Texto", { exact: true })).toBeVisible();
  // Esc devolve a tela inteira ao diagrama.
  await page.keyboard.press("Escape");
  await expect(page.getByLabel("Texto", { exact: true })).toHaveCount(0);

  // Time com um agente que segue o workflow.
  await page.getByRole("button", { name: "Voltar" }).first().click();
  await irPara(page, "Meus times");
  await page.getByRole("button", { name: "Criar time" }).first().click();
  await page.getByRole("button", { name: "Salvar time" }).click();
  await expect(page.getByRole("alert")).toContainText("Revise estes pontos");
  await page.getByLabel("Nome do time").fill("Atendimento premium");
  await page.getByLabel("Objetivo do time").fill("Resolver pedidos dos clientes no primeiro contato.");
  await page.getByRole("checkbox", { name: /Ana Comercial/ }).check();
  await page.getByRole("checkbox", { name: /Atendimento padrão/ }).check();
  await page.getByRole("button", { name: "Salvar time" }).click();
  await expect(page.getByRole("heading", { name: "Atendimento premium" })).toBeVisible();
  await expect(page.getByText("Detalha o processo:")).toContainText("Atendimento ao cliente");

  // O que foi criado sobrevive a recarregar a página.
  await page.reload();
  await irPara(page, "Meus times");
  await expect(page.getByRole("button", { name: /Atendimento premium/ })).toBeVisible();
});

test("a IA monta o processo quando responde e cai num esqueleto quando não responde", async ({ page }) => {
  await abrir(page, true);
  const resposta = {
    raias: [{ nome: "Cliente", tipo: "cliente" }, { nome: "Agente", tipo: "agente" }],
    nos: [
      { id: "a", tipo: "inicio", rotulo: "Início", raia: "Cliente" },
      { id: "b", tipo: "tarefa-agente", rotulo: "Triagem inteligente", raia: "Agente" },
      { id: "c", tipo: "fim", rotulo: "Fim", raia: "Agente" },
    ],
    arestas: [{ de: "a", para: "b" }, { de: "b", para: "c" }],
  };
  await page.route("**/api/turnos", (rota) => rota.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ texto: "```json\n" + JSON.stringify(resposta) + "\n```" }) }));
  await irPara(page, "Processos e Workflows");
  await page.getByRole("button", { name: "Novo processo" }).first().click();
  await page.getByLabel("Nome do processo").fill("Triagem");
  await page.getByLabel("Descreva o processo").fill("O cliente chega e o agente faz a triagem do pedido antes de seguir.");
  await page.getByRole("button", { name: "Gerar diagrama" }).click();
  await expect(page.locator(".bpmn-titulo", { hasText: "Triagem inteligente" })).toBeVisible();
  await expect(page.getByText("Gerado com IA. Revise antes de usar.")).toBeVisible();

  // Sem resposta da IA: esqueleto honesto, avisando a pessoa.
  await page.unroute("**/api/turnos");
  await page.route("**/api/turnos", (rota) => rota.fulfill({ status: 500, body: "{}" }));
  await page.getByRole("button", { name: "Voltar" }).first().click();
  await page.getByRole("button", { name: "Novo processo" }).first().click();
  await page.getByLabel("Nome do processo").fill("Cobrança");
  await page.getByLabel("Descreva o processo").fill("O sistema acha a fatura em atraso. O agente contata o cliente. Registrar o acordo.");
  await page.getByRole("button", { name: "Gerar diagrama" }).click();
  await expect(page.getByText("A IA não respondeu agora")).toBeVisible();
  await expect(page.locator(".react-flow__node").first()).toBeVisible();
});
