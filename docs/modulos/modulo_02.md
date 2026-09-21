# Módulo 02 — Gestão de Contatos e Clientes

## Status: ✅ Completo

---

## Visão Geral

CRM jurídico para centralizar informações de clientes, partes, testemunhas e demais contatos. Inclui histórico de atendimentos, tags customizáveis e filtros avançados.

---

## Funcionalidades Implementadas

### Contatos
- Cadastro de contato com tipo de pessoa: `PF` (Pessoa Física) ou `PJ` (Pessoa Jurídica)
- Campos: nome, CPF/CNPJ, OAB, e-mail, telefone, endereço, cidade, estado, CEP, data de nascimento, observações
- Classificação por categoria: `Cliente`, `ParteContraria`, `Testemunha`, `Perito`, `Outro`
- Etiquetas (tags) customizáveis — múltiplas por contato, armazenadas na tabela `ContatoTag`
- Ativação/desativação (soft delete — campo `Ativo`)
- Flag de notificação habilitada por contato (`NotificacaoHabilitada`)
- Isolamento por tenant — nenhum contato visível entre escritórios

### Busca e Filtros
- Busca por nome, CPF/CNPJ ou e-mail (case-insensitive, Contains)
- Filtro por tipo de pessoa (PF/PJ)
- Filtro por categoria de contato
- Filtro por tag
- Filtro por status (ativo/inativo)
- Paginação configurável (padrão: 20 por página)

### Atendimentos
- Registro de atendimentos vinculados ao contato (data + descrição)
- Histórico completo de atendimentos por contato
- Isolamento por tenant via `TenantId`

### Portal do Cliente
- Criação de acesso ao Portal do Cliente diretamente da ficha do contato
- Visualização do status do acesso (ativo/inativo, último login)
- Redefinição de senha do portal
- Revogação de acesso

### Perfil do Contato (resumo 360° + timeline)
- Card de resumo: processos ativos, saldo financeiro pendente, próximo prazo, último atendimento
- Timeline unificada: atendimentos, tarefas, lançamentos financeiros e vínculo com processos

### Vínculos entre Contatos
- Relação entre dois contatos do mesmo escritório (sócio, cônjuge, familiar, representante legal, indicação, outro)
- Visível na ficha dos dois contatos envolvidos

### Detecção de Duplicados
- Agrupamento de contatos ativos por CPF/CNPJ, e-mail, telefone ou nome repetidos

### Lembretes / Follow-up
- Ação "Agendar retorno" cria uma Tarefa vinculada ao contato (reaproveita o módulo de Tarefas)

### Aniversariantes do Mês
- Lista de contatos ativos com aniversário no mês selecionado

### Filtros Salvos
- Combinações de busca/tipo/categoria/tag salvas por usuário, reaplicáveis com um clique

> Ver detalhamento completo em [`melhorias_contatos.md`](../features/melhorias_contatos.md).

---

## Arquitetura

```
ContatosController
  └── ContatoService (IContatoService)
        └── AppDbContext
              ├── Contatos
              ├── ContatoTags
              ├── Atendimentos
              ├── ContatoVinculos
              └── ContatoFiltrosSalvos
```

---

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/contatos` | Listar contatos (com filtros e paginação) |
| GET | `/api/contatos/{id}` | Buscar contato por ID |
| POST | `/api/contatos` | Criar contato |
| PUT | `/api/contatos/{id}` | Atualizar contato |
| DELETE | `/api/contatos/{id}` | Desativar contato (soft delete) |
| GET | `/api/contatos/{id}/atendimentos` | Listar atendimentos do contato |
| POST | `/api/contatos/{id}/atendimentos` | Registrar atendimento |
| POST | `/api/contatos/{id}/portal-acesso` | Criar/redefinir acesso ao portal |
| GET | `/api/contatos/{id}/portal-acesso` | Consultar acesso ao portal |
| DELETE | `/api/contatos/{id}/portal-acesso` | Revogar acesso ao portal |
| GET | `/api/contatos/{id}/perfil` | Resumo 360° + timeline unificada |
| GET | `/api/contatos/{id}/vinculos` | Listar vínculos do contato |
| POST | `/api/contatos/{id}/vinculos` | Criar vínculo com outro contato |
| DELETE | `/api/contatos/{id}/vinculos/{vinculoId}` | Remover vínculo |
| GET | `/api/contatos/duplicados` | Agrupar possíveis contatos duplicados |
| GET | `/api/contatos/aniversariantes?mes=` | Listar aniversariantes do mês |
| GET | `/api/contatos/filtros-salvos` | Listar filtros salvos do usuário |
| POST | `/api/contatos/filtros-salvos` | Salvar filtro atual |
| DELETE | `/api/contatos/filtros-salvos/{filtroId}` | Remover filtro salvo |

---

## Modelos de Dados (EF Core)

```csharp
Contato {
  Id, TenantId, Tipo (PF|PJ), TipoContato, Nome, CpfCnpj, Oab,
  Email, Telefone, Endereco, Cidade, Estado, Cep, DataNascimento,
  Observacoes, Ativo, NotificacaoHabilitada, CriadoEm
}

ContatoTag { Id, ContatoId, Tag }

Atendimento { Id, TenantId, ContatoId, UsuarioId, Descricao, Data, CriadoEm }

ContatoVinculo { Id, TenantId, ContatoId, ContatoRelacionadoId, Tipo, Observacao, CriadoEm }

ContatoFiltroSalvo { Id, TenantId, UsuarioId, Nome, Busca, TipoContato, Tipo, Tag, CriadoEm }
```

**Configurações EF:**
- Índice (não único) em `(TenantId, CpfCnpj)` — CPF/CNPJ pode se repetir (ver detecção de duplicados)
- Cascade delete de `ContatoTag` ao remover `Contato`
- `ContatoTag` sem `Id` próprio — chave composta `(ContatoId, Tag)`
- `ContatoVinculo` referencia `Contato` duas vezes (`ContatoId` e `ContatoRelacionadoId`), cascade delete nos dois lados

---

## Migration

`InitialCreate` — cria `Contatos`, `ContatoTags`, `Atendimentos`
`AddContatosMelhorias` — cria `ContatoVinculos`, `ContatoFiltrosSalvos`

---

## Testes

`ContatoServiceTests.cs` — 5 casos:
- `CreateAsync_DeveRetornarContato_QuandoDadosValidos`
- `GetAllAsync_DeveRetornarApenasDadosDoTenant`
- `UpdateAsync_DeveLancarExcecao_QuandoContatoNaoPertenceAoTenant`
- `DeleteAsync_DeveDesativarContato_QuandoEncontrado`
- `GetAllAsync_DeveFiltrarPorBusca`

---

## Frontend

| Arquivo | Descrição |
|---------|-----------|
| `/pages/contatos.html` | Lista, filtros, modais de criação/edição/atendimento/portal |
| `/js/contatos.js` | CRUD de contatos, atendimentos e portal access via `apiFetch` |

**Modal de Portal do Cliente:**
- Abre ao clicar em "Portal" na linha do contato
- Estado **sem acesso**: formulário de e-mail + senha → cria acesso e envia e-mail ao cliente
- Estado **com acesso**: exibe e-mail, status, datas; campo de nova senha para redefinição; botão de revogação
