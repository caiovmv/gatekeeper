import { expect, test } from "@playwright/test";
import {
  esqueletoDeProcesso,
  extrairJson,
  organizarProcesso,
  processoDaIa,
  validarProcesso,
  validarWorkflow,
  workflowDoProcesso,
  type Processo,
} from "../src/fluxos-modelo";

const descricao = "O cliente pede ajuda. O agente entende o pedido. Se for urgente, escalar para um humano. Registrar o atendimento no CRM.";

test("o esqueleto de processo tem início, fim, desvio e passa na validação", () => {
  const processo = esqueletoDeProcesso(descricao, "Atendimento");
  expect(processo.nos.filter((no) => no.tipo === "inicio")).toHaveLength(1);
  expect(processo.nos.filter((no) => no.tipo === "fim")).toHaveLength(1);
  expect(processo.nos.some((no) => no.tipo === "gateway-exclusivo")).toBe(true);
  const erros = validarProcesso(processo, []).filter((achado) => achado.nivel === "erro");
  expect(erros).toEqual([]);
  // O desvio tem saída "Sim" e "Não".
  const gateway = processo.nos.find((no) => no.tipo === "gateway-exclusivo");
  const saidas = processo.arestas.filter((aresta) => aresta.origem === gateway?.id).map((aresta) => aresta.rotulo);
  expect(saidas).toEqual(expect.arrayContaining(["Sim", "Não"]));
});

test("a organização coloca os nós em colunas crescentes, dentro das raias", () => {
  const processo = organizarProcesso(esqueletoDeProcesso(descricao, "Atendimento"));
  const inicio = processo.nos.find((no) => no.tipo === "inicio");
  const fim = processo.nos.find((no) => no.tipo === "fim");
  expect(inicio && fim && fim.x > inicio.x).toBe(true);
  expect(processo.nos.every((no) => no.y >= 0)).toBe(true);
});

test("o workflow derivado usa os participantes das raias e tem ramos para o desvio", () => {
  const processo = esqueletoDeProcesso(descricao, "Atendimento");
  const workflow = workflowDoProcesso(processo, [{ id: "ag-1", nome: "Ana" }]);
  expect(workflow.participantes).toHaveLength(processo.raias.length);
  expect(workflow.participantes.some((participante) => participante.agenteId === "ag-1")).toBe(true);
  expect(workflow.passos[0].tipo).toBe("inicio");
  expect(workflow.passos[workflow.passos.length - 1].tipo).toBe("fim");
  expect(workflow.passos.some((passo) => passo.tipo === "decisao")).toBe(true);
  const erros = validarWorkflow(workflow, [processo]).filter((achado) => achado.nivel === "erro");
  expect(erros).toEqual([]);
});

test("a validação do processo acusa fim ausente, nó solto e tarefa de workflow sem workflow", () => {
  const processo: Processo = {
    id: "p",
    nome: "x",
    objetivo: "",
    raias: [{ id: "r", nome: "Agente", tipo: "agente" }],
    nos: [
      { id: "i", tipo: "inicio", rotulo: "Início", x: 0, y: 0 },
      { id: "t", tipo: "tarefa-workflow", rotulo: "Rodar", x: 0, y: 0 },
      { id: "s", tipo: "tarefa-humana", rotulo: "Solta", x: 0, y: 0 },
    ],
    arestas: [{ id: "a", origem: "i", destino: "t" }],
    origem: "manual",
    atualizadoEm: "",
  };
  const mensagens = validarProcesso(processo, []).map((achado) => achado.mensagem).join(" | ");
  expect(mensagens).toContain("Falta um evento de fim");
  expect(mensagens).toContain("“Solta” não recebe nenhuma ligação");
  expect(mensagens).toContain("Escolha o workflow");
});

test("a leitura do JSON da IA aceita bloco de código e rejeita formato inválido", () => {
  const resposta = "Aqui está:\n```json\n" + JSON.stringify({
    raias: [{ nome: "Cliente", tipo: "cliente" }, { nome: "Agente", tipo: "agente" }],
    nos: [
      { id: "a", tipo: "inicio", rotulo: "Início", raia: "Cliente" },
      { id: "b", tipo: "tarefa-agente", rotulo: "Atender", raia: "Agente" },
      { id: "c", tipo: "fim", rotulo: "Fim", raia: "Agente" },
    ],
    arestas: [{ de: "a", para: "b" }, { de: "b", para: "c" }, { de: "x", para: "c" }],
  }) + "\n```";
  const processo = processoDaIa(extrairJson(resposta), "Via IA", "objetivo");
  expect(processo.origem).toBe("ia");
  expect(processo.nos).toHaveLength(3);
  expect(processo.arestas).toHaveLength(2); // a aresta para um nó inexistente foi descartada
  expect(validarProcesso(processo, []).filter((achado) => achado.nivel === "erro")).toEqual([]);
  expect(() => processoDaIa({ nos: [] }, "x", "")).toThrow();
  expect(() => extrairJson("sem json aqui")).toThrow();
});
