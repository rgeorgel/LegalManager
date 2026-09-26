// Modelos de documento: variáveis {{nome_da_variavel}}, preenchimento a partir de um processo
// e exportação (impressão/PDF e Word). Usado em documentos.html (aba Modelos) e processo-detalhe.html.
import { esc } from './utils.js';

// Aceita espaços dentro das chaves ({{ nome }}) — o nome é o que fica entre elas, aparado.
const RE_VARIAVEL = /\{\{\s*([^{}]+?)\s*\}\}/g;

/** Variáveis sugeridas no editor, na ordem em que aparecem. `valoresDoProcesso` sabe preencher todas. */
export const VARIAVEIS_SUGERIDAS = [
  { grupo: 'Partes', nomes: ['nome_autor', 'nome_reu', 'nome_interessado', 'nome_terceiro'] },
  { grupo: 'Processo', nomes: ['numero_processo', 'tribunal', 'vara', 'comarca', 'area_direito', 'valor_causa'] },
  { grupo: 'Escritório', nomes: ['nome_advogado', 'data_atual'] },
];

const ROTULOS = {
  nome_autor: 'Nome do autor',
  nome_reu: 'Nome do réu',
  nome_interessado: 'Nome do interessado',
  nome_terceiro: 'Nome do terceiro',
  nome_cliente: 'Nome do cliente',
  nome_advogado: 'Nome do advogado',
  numero_processo: 'Número do processo',
  tribunal: 'Tribunal',
  vara: 'Vara',
  comarca: 'Comarca',
  area_direito: 'Área do direito',
  valor_causa: 'Valor da causa',
  data_atual: 'Data de hoje',
  cpf: 'CPF',
  cnpj: 'CNPJ',
  oab: 'OAB',
  cep: 'CEP',
};

// Palavras comuns em nomes de variáveis, com acento/sigla, para o rótulo automático.
const PALAVRAS = {
  cpf: 'CPF', cnpj: 'CNPJ', oab: 'OAB', cep: 'CEP', rg: 'RG', uf: 'UF', cnj: 'CNJ',
  endereco: 'endereço', numero: 'número', reu: 'réu', razao: 'razão', descricao: 'descrição',
  area: 'área', orgao: 'órgão', inicio: 'início', termino: 'término', honorarios: 'honorários',
  profissao: 'profissão', juizo: 'juízo', clausula: 'cláusula', indice: 'índice',
};

/** Nome legível de uma variável: "nome_autor" → "Nome do autor", "endereco_reu" → "Endereço réu". */
export function rotuloVariavel(nome) {
  const chave = String(nome).toLowerCase();
  if (ROTULOS[chave]) return ROTULOS[chave];
  const texto = chave.split(/[_\-.\s]+/).filter(Boolean).map(p => PALAVRAS[p] ?? p).join(' ');
  return texto.charAt(0).toUpperCase() + texto.slice(1);
}

/** Normaliza o que o usuário digitou para um nome de variável: "Endereço do Réu" → "endereco_do_reu". */
export function normalizarNomeVariavel(texto) {
  return String(texto ?? '')
    .normalize('NFD').replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

/**
 * Quantas vezes cada variável aparece no conteúdo, na ordem da primeira ocorrência.
 * Maiúsculas não diferenciam variáveis ({{Nome}} e {{nome}} são a mesma); vale a primeira grafia.
 */
export function contarVariaveis(conteudo) {
  const grafia = new Map();
  const contagem = {};
  for (const [, nome] of String(conteudo ?? '').matchAll(RE_VARIAVEL)) {
    const chave = nome.toLowerCase();
    if (!grafia.has(chave)) grafia.set(chave, nome);
    const nomeBase = grafia.get(chave);
    contagem[nomeBase] = (contagem[nomeBase] || 0) + 1;
  }
  return contagem;
}

/** Variáveis do conteúdo, sem repetição, na ordem da primeira ocorrência. */
export function extrairVariaveis(conteudo) {
  return Object.keys(contarVariaveis(conteudo));
}

// Busca o valor sem diferenciar maiúsculas (o backend também aplica com IgnoreCase).
function valorDe(valores, nome) {
  if (Object.hasOwn(valores, nome)) return valores[nome];
  const chave = Object.keys(valores).find(k => k.toLowerCase() === nome.toLowerCase());
  return chave === undefined ? undefined : valores[chave];
}

/** Substitui as variáveis preenchidas; as vazias continuam como {{variavel}}. */
export function aplicarVariaveis(conteudo, valores = {}) {
  return String(conteudo ?? '').replace(RE_VARIAVEL, (trecho, nome) => {
    const valor = valorDe(valores, nome);
    return valor ? valor : trecho;
  });
}

/**
 * HTML do conteúdo para pré-visualização: texto escapado, variáveis destacadas.
 * Sem `valores`, mostra o rótulo de cada variável (editor); com `valores`, mostra o valor
 * preenchido ou marca a variável como pendente (tela de uso).
 */
export function previewHtml(conteudo, valores = null) {
  const partes = [];
  let ultimo = 0;
  for (const m of String(conteudo ?? '').matchAll(RE_VARIAVEL)) {
    partes.push(esc(conteudo.slice(ultimo, m.index)));
    const nome = m[1];
    const valor = valores ? valorDe(valores, nome) : undefined;
    if (valores && valor) {
      partes.push(`<mark class="mv-token mv-token-ok" data-var="${esc(nome)}">${esc(valor)}</mark>`);
    } else {
      const cls = valores ? 'mv-token mv-token-pendente' : 'mv-token';
      partes.push(`<mark class="${cls}" data-var="${esc(nome)}" title="{{${esc(nome)}}}">${esc(rotuloVariavel(nome))}</mark>`);
    }
    ultimo = m.index + m[0].length;
  }
  partes.push(esc(String(conteudo ?? '').slice(ultimo)));
  return partes.join('');
}

/** Data por extenso para documentos: "26 de setembro de 2026". */
export function dataPorExtenso(data = new Date()) {
  return data.toLocaleDateString('pt-BR', { day: 'numeric', month: 'long', year: 'numeric' });
}

const AREAS = {
  Civil: 'Cível', Trabalhista: 'Trabalhista', Criminal: 'Criminal', Previdenciario: 'Previdenciário',
  Tributario: 'Tributário', Familia: 'Família', Empresarial: 'Empresarial', Consumidor: 'Consumidor',
  Administrativo: 'Administrativo', Ambiental: 'Ambiental', Imobiliario: 'Imobiliário', Outro: 'Outro',
};

/**
 * Valores que um processo (GET /processos/{id}) consegue preencher para as variáveis dadas.
 * Só devolve as variáveis reconhecidas e com dado no processo.
 */
export function valoresDoProcesso(variaveis, processo) {
  const valores = {};
  if (!processo) return valores;
  const parte = tipo => processo.partes?.find(p => p.tipoParte === tipo)?.nomeContato;
  for (const nome of variaveis) {
    const v = nome.toLowerCase();
    let valor;
    if (v === 'nome_advogado') valor = processo.nomeAdvogadoResponsavel;
    else if (v === 'nome_autor') valor = parte('Autor');
    else if (v === 'nome_reu') valor = parte('Reu');
    else if (v === 'nome_interessado' || v === 'nome_cliente') valor = parte('Interessado');
    else if (v === 'nome_terceiro') valor = parte('Terceiro');
    else if (v.includes('processo')) valor = processo.numeroCNJ;
    else if (v.includes('tribunal')) valor = processo.tribunal;
    else if (v.includes('vara')) valor = processo.vara;
    else if (v.includes('comarca')) valor = processo.comarca;
    else if (v.includes('area')) valor = AREAS[processo.areaDireito] || processo.areaDireito;
    else if (v.includes('valor') && processo.valorCausa != null) {
      valor = Number(processo.valorCausa).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
    }
    if (valor) valores[nome] = String(valor);
  }
  return valores;
}

/** Valores que não dependem de processo (ex.: data de hoje). */
export function valoresAutomaticos(variaveis) {
  const valores = {};
  for (const nome of variaveis) {
    const v = nome.toLowerCase();
    if (v === 'data_atual' || v === 'data_hoje' || v === 'data') valores[nome] = dataPorExtenso();
  }
  return valores;
}

function documentoHtml(titulo, texto) {
  return `<!DOCTYPE html><html lang="pt-BR"><head><meta charset="UTF-8"><title>${esc(titulo)}</title><style>
    body { font-family: 'Times New Roman', Georgia, serif; font-size: 12pt; line-height: 1.6; margin: 40px auto; max-width: 800px; color: #000; }
    .conteudo { white-space: pre-wrap; word-wrap: break-word; text-align: justify; }
    @media print { body { margin: 20mm; max-width: none; } }
  </style></head><body><div class="conteudo">${esc(texto)}</div></body></html>`;
}

/** Abre o texto em uma janela formatada e chama a impressão (o navegador oferece "Salvar como PDF"). */
export function imprimirTexto(titulo, texto) {
  const win = window.open('', '_blank', 'width=800,height=900');
  if (!win) return false;
  win.document.write(documentoHtml(titulo, texto).replace('</body>',
    '<script>window.onload = function () { window.print(); }<\/script></body>'));
  win.document.close();
  return true;
}

/** Arquivo .doc (HTML que o Word abre) com o texto do documento. */
export function arquivoWord(nome, texto) {
  const base = String(nome || 'Documento').replace(/[\\/:*?"<>|]+/g, ' ').trim() || 'Documento';
  return new File(['﻿', documentoHtml(base, texto)], `${base}.doc`, { type: 'application/msword' });
}

/** Baixa o texto como .doc. */
export function baixarWord(nome, texto) {
  const arquivo = arquivoWord(nome, texto);
  const url = URL.createObjectURL(arquivo);
  const a = document.createElement('a');
  a.href = url;
  a.download = arquivo.name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Modelos prontos oferecidos quando o escritório ainda não tem nenhum. */
export const MODELOS_EXEMPLO = [
  {
    nome: 'Procuração Ad Judicia',
    descricao: 'Outorga de poderes gerais para o foro, com cláusula ad judicia et extra.',
    conteudo: `PROCURAÇÃO AD JUDICIA ET EXTRA

OUTORGANTE: {{nome_cliente}}, inscrito(a) no CPF sob o nº {{cpf_cliente}}, residente e domiciliado(a) em {{endereco_cliente}}.

OUTORGADO: {{nome_advogado}}, advogado(a) inscrito(a) na OAB/{{uf_oab}} sob o nº {{numero_oab}}, com escritório em {{endereco_escritorio}}.

PODERES: pelo presente instrumento, o(a) outorgante nomeia e constitui o(a) outorgado(a) seu(sua) procurador(a), conferindo-lhe os poderes da cláusula ad judicia et extra, para o foro em geral, podendo propor as ações competentes e defendê-lo(a) nas contrárias, seguindo umas e outras até final decisão, usando os recursos legais e acompanhando-os, conferindo-lhe ainda poderes especiais para confessar, desistir, transigir, firmar compromissos ou acordos, receber e dar quitação, podendo ainda substabelecer esta a outrem, com ou sem reservas de iguais poderes.

{{comarca}}, {{data_atual}}.


_______________________________________
{{nome_cliente}}`,
  },
  {
    nome: 'Declaração de Hipossuficiência',
    descricao: 'Declaração para pedido de gratuidade da justiça (art. 99 do CPC).',
    conteudo: `DECLARAÇÃO DE HIPOSSUFICIÊNCIA ECONÔMICA

Eu, {{nome_cliente}}, inscrito(a) no CPF sob o nº {{cpf_cliente}}, residente e domiciliado(a) em {{endereco_cliente}}, DECLARO, para os fins do art. 99 do Código de Processo Civil e da Lei nº 1.060/50, que não possuo condições de arcar com as custas processuais e os honorários advocatícios sem prejuízo do meu próprio sustento e do de minha família.

Declaro, ainda, estar ciente de que a falsidade desta declaração sujeita o declarante às sanções civis, administrativas e criminais previstas em lei.

{{comarca}}, {{data_atual}}.


_______________________________________
{{nome_cliente}}`,
  },
  {
    nome: 'Contrato de Honorários Advocatícios',
    descricao: 'Contrato simples de prestação de serviços advocatícios com valor fixo.',
    conteudo: `CONTRATO DE PRESTAÇÃO DE SERVIÇOS ADVOCATÍCIOS

CONTRATANTE: {{nome_cliente}}, inscrito(a) no CPF sob o nº {{cpf_cliente}}, residente e domiciliado(a) em {{endereco_cliente}}.

CONTRATADO: {{nome_advogado}}, advogado(a) inscrito(a) na OAB/{{uf_oab}} sob o nº {{numero_oab}}.

CLÁUSULA 1ª — DO OBJETO
O presente contrato tem por objeto a prestação de serviços advocatícios para {{objeto_contrato}}.

CLÁUSULA 2ª — DOS HONORÁRIOS
Pelos serviços prestados, o(a) CONTRATANTE pagará ao CONTRATADO o valor de {{valor_honorarios}}, da seguinte forma: {{forma_pagamento}}.

CLÁUSULA 3ª — DAS DESPESAS
Custas processuais, emolumentos e demais despesas necessárias ao andamento do feito correrão por conta do(a) CONTRATANTE.

CLÁUSULA 4ª — DO FORO
Fica eleito o foro da comarca de {{comarca}} para dirimir quaisquer dúvidas oriundas deste contrato.

E, por estarem justas e contratadas, as partes assinam o presente em duas vias de igual teor.

{{comarca}}, {{data_atual}}.


_______________________________________
{{nome_cliente}}

_______________________________________
{{nome_advogado}}`,
  },
];
