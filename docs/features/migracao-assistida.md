# Migração Assistida de Outros Sistemas — Plano de Implementação

**Data:** 2026-11-23
**Autor:** Claude (Claude Code), a pedido de Ricardo
**Status:** Planejado — não iniciado. Documento de referência para quando a feature entrar no roadmap.

---

## TL;DR

Um assistente em **Configurações → Importar de outro sistema** que traz contatos, processos, prazos e
financeiro de **Astrea, ADVBOX, Projuris ou planilhas próprias**. O usuário envia o XLSX/CSV, confere o
mapeamento de colunas, vê uma **simulação** (nada é gravado) com o que será criado, completado e o que
tem erro, e só então importa — em background, com relatório final e **botão Desfazer**.

Por dentro: cada origem é só um **perfil de mapeamento** (aliases de colunas + tabelas de valores) que
traduz as linhas para um **formato canônico** do Causify; um **único importador** grava o formato
canônico. Cada linha gera um `MigracaoItem` ligando linha → registro criado, o que viabiliza o desfazer,
a auditoria e a reconciliação ("linhas lidas = criadas + completadas + ignoradas + erros").

**Por quê:** trocar de sistema é a principal barreira de quem já usa um concorrente. A EasyJur vende
migração assistida a partir de ADVBOX, Astrea e Projuris; a Projuris oferece modelos de planilha. Os
concorrentes não facilitam a saída (ADVBOX não entrega backup completo, só Excel por menu), então quem
resolver bem a entrada ganha um argumento de venda forte — inclusive para a divulgação em grupos de
advogados.

**Fases:** (1) planilha-modelo do Causify — contatos e processos, com todo o circuito (simulação,
relatório, desfazer, testes); (2) perfis Astrea e ADVBOX + prazos/tarefas — **depende de arquivos reais
de exportação**; (3) financeiro/honorários, Projuris e enriquecimento opcional via Escavador.

---

## 1. Contexto: o que cada origem entrega

| Origem | Como o cliente tira os dados | Implicação |
|---|---|---|
| **Astrea (Aurum)** | Backup completo em **XLSX** enviado em até 24h; pode ser pedido em até 60 dias após o cancelamento ([FAQ Aurum](https://www.reclameaqui.com.br/empresa/aurum/faq/astrea-como-solicitar-seu-backup-completo-e-quais-dados-esto-includos_sS6iIHciU7AAANAn/)) | Um arquivo com várias abas; precisamos de um exemplo real para montar o perfil |
| **ADVBOX** | Sem backup completo; **exportação Excel em cada menu** (processos, pessoas, financeiro) ([resposta da empresa](https://www.reclameaqui.com.br/adv-box/cliente-advbox-solicita-backup-de-dados-e-acusa-empresa-de-reter-informacoes-o-que-pode-configurar-violacao-da-lgpd_VG4buJDLKGPaAguL/), [política de dados](https://advbox.com.br/policy.pdf)) | Vários arquivos, um por entidade; o assistente aceita mais de um arquivo por migração |
| **Projuris ADV** | Planilhas por módulo ([Projuris](https://www.projuris.com.br/blog/como-mudar-seu-software-juridico-com-seguranca/)) | Perfil na fase 3 |
| **Planilha própria** | O que o escritório já controla em Excel/Google Planilhas | Modelo do Causify para baixar (fase 1) + mapeamento livre |

Conclusão: a entrada é quase sempre **XLSX com colunas que não controlamos**. O mapeamento de colunas é
parte central do produto, não um detalhe.

---

## 2. Experiência do usuário

Página nova `pages/migracao.html` (link em Configurações e no onboarding "Já usa outro sistema?").
Assistente em 5 passos, com o estado salvo no servidor (dá para sair e voltar):

1. **Origem** — Astrea · ADVBOX · Projuris · Planilha do Causify (botão "Baixar modelo") · Outra planilha.
   Cada origem mostra um passo a passo curto de como exportar do sistema antigo.
2. **Envio** — um ou mais arquivos `.xlsx`/`.csv` (limite sugerido: 20 MB por arquivo). O sistema lista as
   abas encontradas e sugere a entidade de cada uma (Contatos, Processos, Prazos, Financeiro) pelos
   cabeçalhos; o usuário confirma ou marca "ignorar esta aba".
3. **Mapeamento** — por aba, cada coluna ligada a um campo do Causify (ou "não importar"). O perfil da
   origem já preenche quase tudo; colunas não reconhecidas ficam destacadas. Também mapeia **valores**
   (ex.: "Em andamento" → `StatusProcesso.Ativo`, "Trabalhista" → `AreaDireito.Trabalhista`). Prévia de 10
   linhas já normalizadas. Opções globais:
   - Duplicados: **completar campos vazios** (padrão) · ignorar a linha.
   - Prazos vencidos: importar como concluídos (padrão) · importar como pendentes · não importar.
4. **Simulação** (dry-run, nada gravado) — resumo por entidade:
   > 312 contatos: 280 novos · 32 já existem (serão completados) · 0 com erro
   > 198 processos: 190 novos · 8 já cadastrados · 14 com erro
   - Botão **"Baixar linhas com erro"**: a planilha original só com as linhas problemáticas + coluna
     "Motivo" (CNJ inválido, data ilegível, CPF com dígito errado, cliente não encontrado...). O usuário
     corrige e reenvia só esse arquivo.
   - Avisos de plano (ex.: armazenamento, usuários) antes de importar.
5. **Importação** — roda em background (Hangfire), com a barra de progresso inferior já existente
   (`js/importacao-bar.js`, hoje usada pela importação por OAB). Ao terminar: notificação + relatório final
   com a reconciliação (seção 7.8), a amostra lado a lado e o botão **Desfazer importação**.

Histórico em **Configurações → Importações**: cada migração com data, origem, totais, quem fez, relatório
e o desfazer (enquanto disponível).

---

## 3. Arquitetura

```
 arquivo(s) XLSX/CSV ──► Leitor ──► linhas brutas (aba, nº linha, {coluna: valor})
                                         │
                       perfil da origem  ▼   (aliases de colunas + tabelas de valores)
                                     Adaptador ──► registros canônicos + erros por linha
                                                         │
                                                         ▼
                                              Importador (único)
                                   simular: só calcula   │   importar: grava + MigracaoItem
                                                         ▼
                                       Processos, Contatos, Tarefas, Lançamentos…
```

### 3.1 Leitura dos arquivos

- O projeto **não tem** biblioteca de XLSX hoje. Adicionar `ExcelDataReader` (MIT, só leitura, leve) ou
  `ClosedXML` (MIT, também escreve — útil para gerar o modelo e a planilha de erros). **Recomendado:
  ClosedXML**, por cobrir leitura e escrita.
- CSV: detectar codificação (UTF-8 com/sem BOM, Windows-1252/Latin-1 — comum em exportações brasileiras)
  e separador (`;` é o padrão do Excel em pt-BR, `,` também ocorre).
- Leitura e validação **no servidor** (não confiar em parse do navegador). O arquivo original vai para o
  storage (`IStorageService`, OCI) em um prefixo próprio do tenant, para reprocessar e auditar.

### 3.2 Formato canônico

Records na Application (`DTOs/Migracao`), independentes de origem:

```csharp
record ContatoMigrado(string Nome, TipoPessoa? Tipo, TipoContato? TipoContato, string? CpfCnpj,
    string? Email, string? Telefone, string? Endereco, string? Cidade, string? Estado, string? Cep,
    DateTime? DataNascimento, string? Observacoes, string? ChaveOrigem);

record ProcessoMigrado(string NumeroCnj, string? Tribunal, string? Vara, string? Comarca,
    AreaDireito? Area, string? TipoAcao, FaseProcessual? Fase, StatusProcesso? Status,
    decimal? ValorCausa, string? ResponsavelEmail, IReadOnlyList<ParteMigrada> Partes,
    string? Observacoes, string? ChaveOrigem);

record ParteMigrada(string Nome, string? CpfCnpj, TipoParteProcesso? Polo, bool Cliente);

record TarefaMigrada(string Titulo, TipoTarefa Tipo, DateTime? Prazo, StatusTarefa? Status,
    string? ProcessoCnj, string? ResponsavelEmail, string? Descricao, string? ChaveOrigem);

record LancamentoMigrado(TipoLancamento Tipo, decimal Valor, DateTime Vencimento, DateTime? Pagamento,
    string? Categoria, string? Descricao, string? ProcessoCnj, string? ContatoDocumentoOuNome,
    string? ChaveOrigem);
```

`ChaveOrigem` = id do registro no sistema antigo, quando a exportação traz um (permite ligar abas entre
si, ex.: financeiro → cliente pelo id do Astrea, e reimportar sem duplicar).

### 3.3 Perfis de origem (configuração, não código)

Um perfil por origem em JSON embarcado (`Infrastructure/Migracao/Perfis/astrea.json` etc.):

```json
{
  "origem": "astrea",
  "versao": 1,
  "abas": [
    {
      "entidade": "processo",
      "reconhecerPor": ["Número do processo", "Pasta"],
      "colunas": {
        "NumeroCnj":  ["Número do processo", "Processo", "Nº CNJ"],
        "Vara":       ["Vara", "Órgão julgador"],
        "Status":     ["Situação", "Status"],
        "ValorCausa": ["Valor da causa"]
      },
      "valores": {
        "Status": { "Ativo": "Ativo", "Em andamento": "Ativo", "Arquivado": "Arquivado", "Encerrado": "Encerrado" }
      }
    }
  ]
}
```

- Cabeçalhos comparados sem acento, sem caixa e sem espaços extras.
- O mapeamento que o usuário ajusta na tela é salvo na `Migracao` (e pode virar sugestão para o perfil).
- **Origem nova = perfil novo + fixture de teste.** Código só muda se a origem tiver um formato estrutural
  diferente (ex.: partes em colunas "Autor 1, Autor 2…" em vez de uma aba própria) — aí entra um
  pré-processador específico, pequeno, no adaptador.
- `versao` no perfil: exportações mudam de layout; guardamos qual versão do perfil cada migração usou.

### 3.4 Normalização (onde mora a maior parte dos bugs)

Funções puras em `Infrastructure/Migracao/Normalizacao.cs`, todas com testes de tabela:

| Campo | Regras |
|---|---|
| Datas | `dd/mm/aaaa`, `dd/mm/aa`, `dd/mm/aaaa hh:mm`, ISO, **número de série do Excel** (dias desde 1899-12-30), células de data nativas. Prazos e eventos ficam na **hora de parede**, sem conversão de fuso (regra do CLAUDE.md); data sem hora vira 00:00 (ou 23:59 para prazo — decidir, ver seção 10). |
| Dinheiro | `R$ 1.234,56`, `1234,56`, `1,234.56` (exportação em inglês), negativos com `-` ou parênteses. |
| CNJ | Só dígitos (20), **validar dígito verificador (módulo 97)**, gravar formatado `NNNNNNN-DD.AAAA.J.TR.OOOO`. Número antigo (não CNJ) → erro na linha com sugestão de cadastro manual. |
| CPF/CNPJ | Só dígitos, validar DV; inválido → importar sem o documento e registrar aviso (não descartar o contato). |
| Telefone, CEP, UF | Só dígitos / sigla de 2 letras maiúscula. |
| Enums | Via tabela de valores do perfil + sinônimos gerais (sem acento/caixa). Valor desconhecido → campo vazio + aviso, nunca erro fatal. |
| Texto | `Trim`, colapsar espaços, cortar no tamanho máximo da coluna (com aviso). |

### 3.5 Importador

`MigracaoService` (Infrastructure), com dois modos sobre o mesmo código:

- `SimularAsync(migracaoId)` — roda tudo, resolve duplicados contra o banco, **não grava entidades**;
  grava só o resultado da simulação (contagens + erros) na `Migracao`.
- `ImportarAsync(migracaoId)` — executado por `MigracaoJob` (Hangfire), em lotes (ex.: 200 linhas por
  `SaveChanges`), atualizando `Processados` para a barra de progresso e podendo retomar de onde parou
  (mesmo padrão de `ImportacaoProcessosJob`).

Ordem de gravação (dependências): **Contatos → Processos (+ partes) → Tarefas/Prazos → Lançamentos**.

Reaproveitar o que existe em vez de inserir direto no `DbContext`, para manter as regras de negócio:
`IProcessoService.CreateAsync` (já lança `ProcessoDuplicadoException`), `IContatoService`, serviços de
tarefa e financeiro. Onde o serviço for pesado demais para volume (ex.: dispara notificações), criar uma
variante "modo importação" que pula efeitos colaterais (e-mails, notificações, monitoramento).

Efeitos colaterais **desligados** durante a migração:
- Nenhum e-mail/notificação por tarefa ou prazo criado.
- Processos entram com `Monitorado = false` (respeita `PlanoRestricoes.MaxProcessosMonitorados`; cada
  monitoramento consome Escavador). Depois da importação, uma tela sugere "monitorar processos ativos"
  dentro do limite do plano.
- Auditoria: **um** registro na timeline por migração (via `IAuditService`), não um por linha.

### 3.6 Duplicados

| Entidade | Chave (em ordem) | Ação padrão |
|---|---|---|
| Contato | 1) CPF/CNPJ (dígitos) · 2) `ChaveOrigem` já importada · 3) nome normalizado + (e-mail ou telefone) | Completar campos vazios do existente |
| Processo | 1) CNJ (dígitos) · 2) `ChaveOrigem` | Completar campos vazios; partes novas são adicionadas |
| Tarefa | `ChaveOrigem` · ou (título + prazo + processo) | Ignorar |
| Lançamento | `ChaveOrigem` · ou (valor + vencimento + descrição + processo/contato) | Ignorar |

Regra de ouro: **a migração nunca sobrescreve um dado que já existe no Causify**. Nome só igual (sem
documento, e-mail ou telefone) **não** funde contatos — vira contato novo com aviso "possível duplicado",
e o usuário usa a tela de duplicados já existente (`ContatoService.GetDuplicadosAsync`).

---

## 4. Modelo de dados

```csharp
public class Migracao
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UsuarioId { get; set; }
    public OrigemMigracao Origem { get; set; }         // Causify, Astrea, Advbox, Projuris, Outra
    public int VersaoPerfil { get; set; }
    public StatusMigracao Status { get; set; }         // Rascunho, Simulada, Importando, Concluida, Erro, Desfeita
    public string MapeamentoJson { get; set; } = "{}"; // abas → entidade, colunas → campos, valores, opções
    public string? ResultadoSimulacaoJson { get; set; }
    public int Total { get; set; }
    public int Processados { get; set; }
    public int Criados { get; set; }
    public int Completados { get; set; }
    public int Ignorados { get; set; }
    public int Erros { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public DateTime? ConcluidoEm { get; set; }
    public DateTime? DesfeitoEm { get; set; }
    public List<MigracaoArquivo> Arquivos { get; set; } = [];
    public List<MigracaoItem> Itens { get; set; } = [];
}

public class MigracaoArquivo   // um por arquivo enviado
{
    public Guid Id { get; set; }
    public Guid MigracaoId { get; set; }
    public string NomeOriginal { get; set; } = "";
    public string ObjectKey { get; set; } = "";   // storage
    public long TamanhoBytes { get; set; }
    public string Sha256 { get; set; } = "";       // detectar reenvio do mesmo arquivo
}

public class MigracaoItem      // um por linha lida
{
    public Guid Id { get; set; }
    public Guid MigracaoId { get; set; }
    public Guid ArquivoId { get; set; }
    public string Aba { get; set; } = "";
    public int Linha { get; set; }
    public EntidadeMigrada Entidade { get; set; }  // Contato, Processo, Tarefa, Lancamento
    public AcaoMigracao Acao { get; set; }          // Criado, Completado, Ignorado, Erro
    public Guid? EntidadeId { get; set; }
    public string? CamposCompletadosJson { get; set; } // quais campos foram preenchidos no existente (para desfazer)
    public string? Mensagem { get; set; }
    public string? ChaveOrigem { get; set; }
}
```

Índices: `(TenantId, CriadoEm)` em `Migracao`; `(MigracaoId, Acao)` e `(MigracaoId, Entidade, EntidadeId)`
em `MigracaoItem`. Lembrar de incluir as tabelas novas no `TenantExportService`/`TenantImportService`
e na limpeza do tenant.

---

## 5. API

Controller `MigracoesController` (`api/migracoes`), `[Authorize]`, só perfil Admin do escritório,
`[ExigeRecurso(RecursosEmTeste.Migracao)]` enquanto em teste.

| Método | Rota | O que faz |
|---|---|---|
| `GET` | `/modelo?entidade=` | Baixa a planilha-modelo do Causify (XLSX gerado com ClosedXML, com aba de instruções) |
| `POST` | `/` | Cria a migração (origem) — status Rascunho |
| `POST` | `/{id}/arquivos` | Upload (multipart); devolve abas, cabeçalhos e a sugestão de entidade/mapeamento do perfil |
| `PUT` | `/{id}/mapeamento` | Salva mapeamento e opções; devolve a prévia de 10 linhas normalizadas por aba |
| `POST` | `/{id}/simular` | Roda a simulação (síncrona até ~5 mil linhas; acima disso, job) |
| `GET` | `/{id}/erros.xlsx` | Planilha com as linhas com erro + coluna "Motivo" |
| `POST` | `/{id}/importar` | Enfileira o `MigracaoJob` (exige simulação feita com o mapeamento atual) |
| `GET` | `/{id}` | Status, progresso, totais, relatório |
| `GET` | `/{id}/amostra` | 10 itens aleatórios: linha original × registro gravado |
| `POST` | `/{id}/desfazer` | Desfaz (ver seção 6) |
| `GET` | `/` | Histórico de migrações do escritório |

Super admin: em `superadmin/tenant-detalhe.html`, atalho para abrir o assistente impersonando o escritório
(migração "assistida" feita pela nossa equipe nos primeiros clientes).

---

## 6. Desfazer

- Disponível por **30 dias** após a conclusão (configurável), e só para quem fez a migração ou Admin.
- Remove os registros com `Acao = Criado` daquela migração, na ordem inversa (Lançamentos → Tarefas →
  Processos → Contatos), e **reverte só os campos** listados em `CamposCompletadosJson` dos registros
  completados.
- **Proteção contra perda de trabalho novo:** antes de apagar, verificar se o registro foi editado ou
  ganhou dados depois da migração (`AtualizadoEm > ConcluidoEm`, tarefas novas no processo, documentos,
  lançamentos pagos). Nesses casos o item **não** é apagado e entra num relatório "mantidos porque foram
  alterados depois". A tela mostra esse número antes de confirmar.
- Desfazer é idempotente (rodar de novo não faz nada) e auditado.

---

## 7. Testes e validação

Em camadas, da mais barata para a mais realista. Meta: as camadas 2–5 rodam no CI (EF InMemory, como hoje).

### 7.1 Fixtures reais anonimizadas (dependência principal)
- Conseguir exportações reais: (a) de escritórios piloto que estão migrando (com consentimento por
  escrito), (b) de contas de teste nos sistemas de origem.
- Anonimizar **antes** de entrar no repositório: nomes, CPF/CNPJ (gerar válidos e fictícios), e-mails,
  telefones, endereços, valores com ruído, CNJs trocados por CNJs fictícios com DV válido. Aproveitar a
  lógica do `TenantAnonymizer` num utilitário de linha de comando `scripts/anonimizar-planilha`.
- Guardar em `tests/fixtures/migracao/{causify,astrea,advbox,projuris}/` com um `LEIAME.md` dizendo a data
  e a versão da exportação. **Dado real de cliente nunca entra no git.**

### 7.2 Testes unitários de normalização
Tabelas de casos (`[Theory]`) para cada função da seção 3.4: datas (inclusive série do Excel e 29/02),
dinheiro, CNJ com DV certo/errado, CPF/CNPJ, enums com acento/caixa, textos longos. São os testes mais
baratos e os que mais pegam bug.

### 7.3 Testes de "resultado esperado" por origem (golden files)
Para cada fixture: `arquivo → adaptador → registros canônicos` serializados em JSON e comparados com
`esperado.json` no repositório. Qualquer mudança no perfil ou na normalização aparece como diff no teste;
atualizar o esperado é uma decisão consciente na revisão.

### 7.4 Testes de integração do importador (EF InMemory)
- Simular e importar dão **as mesmas contagens**.
- **Importar o mesmo arquivo duas vezes não duplica nada** (segunda vez: tudo "Completado"/"Ignorado").
- Dados pré-existentes são completados, **nunca sobrescritos**.
- Contato só com nome igual não é fundido.
- Desfazer remove exatamente o que foi criado; mantém o que existia antes e o que foi editado depois.
- Isolamento: nada é lido nem gravado em outro tenant.
- Efeitos colaterais desligados: nenhuma notificação/e-mail criado, nenhum processo `Monitorado`.

### 7.5 Invariante de reconciliação (propriedade)
Em **todo** teste — inclusive com entradas aleatórias (colunas faltando, células vazias, tipos trocados,
lixo, linhas duplicadas) — vale:

> **linhas lidas = criados + completados + ignorados + erros**, e nenhuma exceção não tratada.

Implementar um gerador simples de planilhas aleatórias (FsCheck ou um gerador próprio com seed fixa) e
rodar algumas centenas de casos no CI. É a garantia de que nenhuma linha some em silêncio.

### 7.6 Postgres real
O InMemory não pega tamanho máximo de coluna, constraints, colação nem tradução de queries. Rodar a suíte
de integração da migração contra um Postgres descartável — localmente com um contêiner (como foi feito
para validar a busca global) e, depois, no CI com Testcontainers.

### 7.7 Volume
Fixture sintética com 5 mil processos, 20 mil contatos e 30 mil lançamentos: medir tempo total, memória do
job e tempo da simulação. Alvo inicial: simulação < 30 s, importação < 5 min.

### 7.8 Validação pelo próprio usuário (na tela final)
- **Totais lado a lado** por entidade: "a planilha tinha 198 processos → 190 importados + 8 já existiam".
- **Financeiro:** soma por mês (receitas e despesas) da planilha × soma importada, com diferença destacada.
- **Amostra:** 10 registros aleatórios com a linha original ao lado do registro no Causify e um botão
  "Está certo?" (sim/não + comentário) — o "não" vira um alerta para nós revisarmos o perfil.

### 7.9 Piloto
- Liberar por `RecursosEmTeste` para 2–3 escritórios, com a migração feita pelo super admin.
- Métricas por origem: % de linhas com erro, % de colunas não reconhecidas, respostas do "Está certo?",
  quantas migrações foram desfeitas. Usar os arquivos (anonimizados, com consentimento) para ampliar as
  fixtures antes de abrir a todos.

---

## 8. Segurança e LGPD

- Só Admin do escritório usa a migração; tudo filtrado por `TenantId`.
- Arquivos originais no storage com prefixo do tenant; **apagados após 90 dias** (job de limpeza) —
  ficam só a `Migracao` e os `MigracaoItem`.
- Planilhas são dado sensível (CPF, endereço, valores): nunca logar conteúdo de células; logs só com ids e
  contagens.
- Limites: tamanho de arquivo, número de linhas por migração (ex.: 100 mil) e uma migração em execução por
  escritório.
- Fórmulas e macros no XLSX são ignoradas (ler só valores); rejeitar `.xlsm`.
- A planilha de erros devolvida pode conter dados pessoais: gerar sob demanda, não guardar.

---

## 9. Fases e checklist

### Fase 1 — Planilha do Causify: contatos e processos
- [ ] Pacote ClosedXML; leitor XLSX/CSV com detecção de codificação e separador
- [ ] Modelo XLSX para baixar (abas Contatos e Processos + aba de instruções)
- [ ] Formato canônico (`ContatoMigrado`, `ProcessoMigrado`, `ParteMigrada`) e normalização (seção 3.4)
- [ ] Perfil `causify.json` + mapeamento livre ("Outra planilha")
- [ ] Entidades `Migracao`, `MigracaoArquivo`, `MigracaoItem` + migration EF + export/import do tenant
- [ ] `MigracaoService` (simular/importar), `MigracaoJob`, regras de duplicados da seção 3.6
- [ ] `MigracoesController` + chave `RecursosEmTeste.Migracao`
- [ ] `pages/migracao.html` (5 passos) + histórico em Configurações + barra de progresso
- [ ] Desfazer com proteção de registros alterados
- [ ] Testes 7.2, 7.3 (fixture do modelo), 7.4, 7.5; rodada 7.6 manual; 7.7 com fixture sintética
- [ ] Relatório final com reconciliação, soma e amostra (7.8)

### Fase 2 — Astrea e ADVBOX; prazos e tarefas
- [ ] **Obter exportações reais** (piloto ou contas de teste) e anonimizar (7.1)
- [ ] Perfis `astrea.json` e `advbox.json` (+ pré-processadores se a estrutura exigir)
- [ ] `TarefaMigrada` (prazos vencidos conforme a opção escolhida) e vínculo com processo/responsável
- [ ] Mapear responsáveis por e-mail/nome para usuários do escritório (sem match → responsável = quem importa)
- [ ] Golden files por origem; piloto com 2–3 escritórios (7.9)

### Fase 3 — Financeiro, Projuris, enriquecimento
- [ ] `LancamentoMigrado` e, se a origem trouxer, contratos de honorários e parcelas
- [ ] Perfil `projuris.json`
- [ ] Pós-importação opcional: buscar capa e andamentos (DataJud gratuito primeiro; Escavador dentro do
      limite e com aviso de consumo) para processos ativos
- [ ] Testcontainers no CI para a suíte da migração

---

## 10. Decisões em aberto

1. **Prazo só com data (sem hora):** gravar 00:00 ou 23:59? (23:59 evita o prazo aparecer "atrasado" no
   próprio dia.)
2. **Partes sem documento:** criar contato por nome sempre, ou tentar casar com contato existente de
   mesmo nome quando o processo é o mesmo?
3. **Responsável não encontrado:** quem importa, o Admin, ou deixar sem responsável?
4. **Janela do desfazer:** 30 dias é suficiente? Liberar desfazer parcial (só uma entidade)?
5. **Plano:** migração disponível em todos os planos (inclusive Free/trial, para facilitar a entrada) ou
   só pagos? Sugestão: todos — é ferramenta de aquisição.
6. **Documentos (arquivos):** fora do escopo inicial. Avaliar depois importação de ZIP com estrutura de
   pastas por CNJ.
7. **Migração assistida como serviço pago** (nossa equipe faz para escritórios grandes)?
