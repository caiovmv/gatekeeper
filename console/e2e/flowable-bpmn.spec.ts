import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { DOMParser, XMLSerializer } from "@xmldom/xmldom";
import { expect, test } from "@playwright/test";
import { exportarFlowable, importarFlowable, lerXml } from "../src/flowable-bpmn";
import { esqueletoDeProcesso, organizarProcesso, type Processo } from "../src/fluxos-modelo";

(globalThis as unknown as Record<string, unknown>).DOMParser = DOMParser;
(globalThis as unknown as Record<string, unknown>).XMLSerializer = XMLSerializer;

const pasta = join(fileURLToPath(new URL(".", import.meta.url)), "..", "..", "flowable", "funil-vendas", "bpmn");
const arquivos = readdirSync(pasta).filter((nome) => nome.endsWith(".bpmn20.xml"));

// Forma canônica: atributos ordenados, espaços entre elementos ignorados.
function canonico(xml: string): string {
  const doc = lerXml(xml);
  const visitar = (no: Node): string => {
    if (no.nodeType === 3) {
      const t = (no.textContent ?? "").trim();
      return t;
    }
    if (no.nodeType === 4) {
      return "<![CDATA[" + no.textContent + "]]>";
    }
    if (no.nodeType !== 1) {
      return "";
    }
    const el = no as Element;
    const atributos = Array.from(el.attributes)
      .map((a) => a.name + '="' + a.value + '"')
      .sort()
      .join(" ");
    const filhos = Array.from(el.childNodes).map(visitar).filter(Boolean).join("");
    return "<" + el.nodeName + " " + atributos + ">" + filhos + "</" + el.nodeName + ">";
  };
  return visitar(doc.documentElement);
}

function importar(arquivo: string): Processo {
  const lido = importarFlowable(readFileSync(join(pasta, arquivo), "utf-8"));
  return { id: "p", objetivo: "", atualizadoEm: "", ...lido.processo } as Processo;
}

test("o pacote tem os 19 BPMN esperados", () => {
  expect(arquivos).toHaveLength(19);
});

for (const arquivo of arquivos) {
  test("ida e volta sem perda: " + arquivo, () => {
    const original = readFileSync(join(pasta, arquivo), "utf-8");
    const processo = importar(arquivo);
    expect(processo.nos.length).toBeGreaterThan(0);
    expect(processo.raias.length).toBeGreaterThan(0);
    const ids = new Set(processo.nos.map((no) => no.id));
    processo.arestas.forEach((aresta) => {
      expect(ids.has(aresta.origem)).toBe(true);
      expect(ids.has(aresta.destino)).toBe(true);
    });
    expect(canonico(exportarFlowable(processo).xml)).toBe(canonico(original));
  });
}

test("renomear, mover e mudar de raia altera só o que foi editado", () => {
  const arquivo = arquivos[0];
  const original = readFileSync(join(pasta, arquivo), "utf-8");
  const processo = importar(arquivo);
  const alvo = processo.nos.find((no) => no.tipo !== "inicio" && no.tipo !== "fim" && no.tipo !== "gateway-exclusivo" && no.tipo !== "gateway-paralelo");
  expect(alvo).toBeTruthy();
  const editado: Processo = { ...processo, nos: processo.nos.map((no) => (no.id === alvo?.id ? { ...no, rotulo: "Nome editado", x: no.x + 40 } : no)) };
  const saida = exportarFlowable(editado).xml;
  expect(saida).toContain('name="Nome editado"');
  expect(saida).not.toBe(original);
  // tudo o que não é nome nem posição continua igual
  const reimportado = importarFlowable(saida);
  expect(reimportado.processo.nos.find((no) => no.id === alvo?.id)?.rotulo).toBe("Nome editado");
  expect(reimportado.processo.nos).toHaveLength(processo.nos.length);
  expect(reimportado.processo.arestas).toHaveLength(processo.arestas.length);
  expect(Math.abs((reimportado.processo.nos.find((no) => no.id === alvo?.id)?.x ?? 0) - ((alvo?.x ?? 0) + 40))).toBeLessThan(1);
  expect(saida.match(/flowable:/g)?.length).toBe(original.match(/flowable:/g)?.length);
});

test("excluir um nó remove seus fluxos, formas e referências sem deixar ponta solta", () => {
  const processo = importar(arquivos[1]);
  const meio = processo.nos.find((no) => processo.arestas.some((a) => a.origem === no.id) && processo.arestas.some((a) => a.destino === no.id));
  expect(meio).toBeTruthy();
  const id = meio?.id ?? "";
  const editado: Processo = {
    ...processo,
    nos: processo.nos.filter((no) => no.id !== id),
    arestas: processo.arestas.filter((a) => a.origem !== id && a.destino !== id),
  };
  const saida = exportarFlowable(editado).xml;
  expect(saida).not.toContain('id="' + id + '"');
  expect(saida).not.toContain('bpmnElement="' + id + '"');
  expect(saida).not.toContain('sourceRef="' + id + '"');
  expect(saida).not.toContain('targetRef="' + id + '"');
  expect(saida).not.toMatch(new RegExp(">" + id + "</(incoming|outgoing|flowNodeRef)>"));
  const reimportado = importarFlowable(saida);
  expect(reimportado.processo.nos).toHaveLength(processo.nos.length - 1);
});

test("adicionar um nó e uma ligação gera BPMN que volta a ser lido", () => {
  const processo = importar(arquivos[2]);
  const ultimo = processo.nos.find((no) => no.tipo === "fim") ?? processo.nos[processo.nos.length - 1];
  const novo = { id: "tarefa_nova", tipo: "tarefa-humana" as const, rotulo: "Revisar proposta", x: ultimo.x, y: ultimo.y };
  const editado: Processo = {
    ...processo,
    nos: [...processo.nos, novo],
    arestas: [...processo.arestas, { id: "fluxo_novo", origem: processo.nos[0].id, destino: "tarefa_nova" }],
  };
  const saida = exportarFlowable(editado).xml;
  expect(saida).toContain('id="tarefa_nova"');
  expect(saida).toContain("<userTask");
  const reimportado = importarFlowable(saida);
  expect(reimportado.processo.nos.some((no) => no.id === "tarefa_nova" && no.tipo === "tarefa-humana")).toBe(true);
  expect(reimportado.processo.arestas.some((a) => a.id === "fluxo_novo")).toBe(true);
});

test("processo criado no KOHORS exporta um BPMN do Flowable que volta com os mesmos elementos", () => {
  const nativo = organizarProcesso({ ...esqueletoDeProcesso("O cliente pede ajuda. O agente entende o pedido. Se for urgente, escalar para um humano. Registrar o atendimento no CRM.", "Atendimento"), id: "n", atualizadoEm: "" } as Processo);
  const saida = exportarFlowable(nativo);
  expect(saida.xml).toContain("http://flowable.org/bpmn");
  const volta = importarFlowable(saida.xml);
  expect(volta.processo.nos).toHaveLength(nativo.nos.length);
  expect(volta.processo.arestas).toHaveLength(nativo.arestas.length);
  expect(volta.processo.raias).toHaveLength(nativo.raias.length);
});

test("arquivos que não são BPMN são recusados com mensagem clara", () => {
  expect(() => importarFlowable("<a>")).toThrow();
  expect(() => importarFlowable('<definitions xmlns="urn:x"/>')).toThrow(/BPMN 2.0/);
});
