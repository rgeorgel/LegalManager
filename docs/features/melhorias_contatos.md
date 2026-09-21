# Melhorias: Módulo de Contatos

Backlog de melhorias para o módulo de Contatos (CRM jurídico), levantado a partir do dia a dia do
advogado. Itens marcados com ✅ foram implementados; os demais ficam como ideias futuras.

## Contexto

O módulo de Contatos (`Contato`) já centraliza clientes, partes contrárias, testemunhas e peritos,
com tags, atendimentos, vínculo a processos (`ProcessoParte`), contratos de honorário, lançamentos
financeiros, tarefas e acesso ao Portal do Cliente. As melhorias abaixo focam em **expor** essa
informação de forma mais útil (visão 360°, timeline) e em fechar lacunas de produtividade e
qualidade de dados.

---

## Ideias mapeadas

1. ✅ **Timeline unificada no perfil do contato** — feed cronológico juntando atendimentos, tarefas,
   lançamentos financeiros e vínculos com processos.
2. ✅ **Contato 360°** — card de resumo no topo do perfil: processos ativos, saldo financeiro,
   próximo prazo, último atendimento.
3. ✅ **Vínculos entre contatos** — relação família/sócio/indicação/representante legal entre dois
   contatos do mesmo escritório.
4. ✅ **Detecção de duplicados** — contatos com mesmo CPF/CNPJ, e-mail, telefone ou nome.
5. ✅ **Lembretes e follow-up** — ação rápida "Agendar retorno" que cria uma Tarefa vinculada ao
   contato (reaproveita o módulo de Tarefas já existente).
6. ✅ **Aniversariantes do mês** — lista rápida de contatos que fazem aniversário no mês corrente.
7. Importação em massa (CSV/planilha).
8. Exportação de contatos (backup / envio a terceiros, com filtro por tag/tipo).
9. ✅ **Busca avançada / filtros salvos** — salvar combinações de filtros (busca, tipo, categoria,
   tag) com um nome, para reaplicar depois.
10. Histórico de comunicações (WhatsApp/e-mail/SMS) — log de mensagens além dos atendimentos manuais.
11. Envio de mensagem em massa por tag/filtro.
12. Templates de e-mail/mensagem vinculados ao contato.
13. Validação de CPF/CNPJ e consulta de OAB ao cadastrar.
14. Consentimento LGPD — opt-in/opt-out de comunicações.
15. Anexos/documentos do contato — já existe (seção de Documentos no modal do contato).

---

## Detalhamento do que foi implementado

### 1 + 2. Perfil do contato (resumo 360° + timeline)

**Endpoint:** `GET /api/contatos/{id}/perfil`

Retorna:
- **Resumo:** nº de processos onde o contato é parte, saldo financeiro (receitas pendentes −
  despesas pendentes vinculadas ao contato), próxima tarefa/prazo em aberto, data do último
  atendimento.
- **Timeline:** lista ordenada (mais recente primeiro) combinando atendimentos, tarefas
  (criação/conclusão), lançamentos financeiros e vínculos com processos — cada item com tipo,
  data, título e descrição, para renderização com ícone por tipo no frontend.

**Arquivos:** `ContatoService.cs` (`GetPerfilAsync`), `ContatosController.cs`,
`DTOs/Contatos/ContatoDto.cs` (`ContatoPerfilDto`, `ContatoResumoDto`, `TimelineItemDto`).

### 3. Vínculos entre contatos

Nova entidade `ContatoVinculo` (`ContatoId`, `ContatoRelacionadoId`, `Tipo`, `Observacao?`). O
vínculo é salvo uma vez e aparece no perfil dos dois contatos (consulta por `ContatoId OR
ContatoRelacionadoId`). Tipos: Sócio, Cônjuge, Familiar, Representante Legal, Indicação, Outro.

**Endpoints:**
- `GET /api/contatos/{id}/vinculos`
- `POST /api/contatos/{id}/vinculos`
- `DELETE /api/contatos/{id}/vinculos/{vinculoId}`

### 4. Detecção de duplicados

**Endpoint:** `GET /api/contatos/duplicados`

Agrupa contatos ativos do tenant por CPF/CNPJ, e-mail, telefone (normalizados) e nome
(case-insensitive, trim) e retorna os grupos com mais de um contato. Sem merge automático — o
advogado decide manualmente (editar/desativar um dos registros).

### 5. Lembretes / follow-up ("Agendar retorno")

Sem novo endpoint — reaproveita `POST /api/tarefas` já existente. O botão "Agendar retorno" no
menu do contato abre um modal simples (data + nota) e cria uma `Tarefa` do tipo `Tarefa` vinculada
ao `ContatoId`.

### 6. Aniversariantes do mês

**Endpoint:** `GET /api/contatos/aniversariantes?mes={1-12}` (default: mês corrente)

Lista contatos ativos com `DataNascimento` no mês informado, ordenados por dia. Exibido via botão
"🎂 Aniversariantes" na tela de Contatos.

### 9. Filtros salvos

Nova entidade `ContatoFiltroSalvo` (`TenantId`, `UsuarioId`, `Nome`, `Busca?`, `TipoContato?`,
`Tipo?`, `Tag?`). Salvos por usuário (não por tenant inteiro).

**Endpoints:**
- `GET /api/contatos/filtros-salvos`
- `POST /api/contatos/filtros-salvos`
- `DELETE /api/contatos/filtros-salvos/{filtroId}`

---

## Arquivos alterados

```
Domain/Entities/ContatoVinculo.cs          (novo)
Domain/Entities/ContatoFiltroSalvo.cs      (novo)
Domain/Enums/Enums.cs                      (+TipoVinculoContato)
Infrastructure/Persistence/Configurations/ContatoVinculoConfiguration.cs      (novo)
Infrastructure/Persistence/Configurations/ContatoFiltroSalvoConfiguration.cs  (novo)
Infrastructure/Persistence/AppDbContext.cs (+DbSets)
Infrastructure/Persistence/Migrations/...  (nova migration)
Infrastructure/Services/ContatoService.cs  (+GetPerfilAsync, vínculos, duplicados, aniversariantes, filtros salvos)
Application/Interfaces/IContatoService.cs  (+assinaturas)
Application/DTOs/Contatos/ContatoDto.cs    (+DTOs novos)
API/Controllers/ContatosController.cs      (+endpoints)
wwwroot/js/contatos.js                     (+chamadas de API)
wwwroot/pages/contatos.html                (+modal de Perfil, modal de Aniversariantes, UI de filtros salvos)
tests/LegalManager.UnitTests/ContatoServiceTests.cs (+casos novos)
```
