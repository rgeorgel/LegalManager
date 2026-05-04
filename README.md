# LegalManager

Sistema de gestão de escritórios de advocacia construído com .NET 8 e Clean Architecture.

## Arquitetura

```
src/
├── LegalManager.API          # Controllers e configuração da API
├── LegalManager.Application   # Interfaces e DTOs
├── LegalManager.Domain       # Entidades e regras de domínio
├── LegalManager.Infrastructure # Implementações, banco de dados, serviços externos
└── LegalManager.Web          # Frontend (se existir)
```

## Tecnologias

- **.NET 8** com ASP.NET Core
- **PostgreSQL** via Entity Framework Core
- **Docker Compose** (PostgreSQL, Seq, API)
- **Seq** para centralização de logs
- **Hangfire** para jobs agendados
- **JWT** para autenticação
- **Resend** para envio de emails
- **AbacatePay** para pagamentos
- **OCI Storage** para armazenamento de documentos
- **DataJud CNJ** para consulta processual

## Entidades Principais

| Entidade | Descrição |
|----------|-----------|
| `Processo` | Processos jurídicos |
| `Documento` | Documentos e modelos |
| `LancamentoFinanceiro` | Movimentações financeiras |
| `Tarefa` | Tarefas e timesheet |
| `Prazo` | Prazos processuais |
| `Notificacao` | Sistema de notificações |
| `Usuario` | Usuários do sistema |

## Configuração

### Variáveis de Ambiente

Copie `.env.example` para `.env` e configure:

```env
# Banco de dados
POSTGRES_PASSWORD=
POSTGRES_DB=legalmanager

# JWT
JWT_KEY=
JWT_ISSUER=LegalManager
JWT_AUDIENCE=LegalManagerUsers

# Email (Resend)
RESEND_API_TOKEN=
RESEND_FROM_EMAIL=

# Pagamentos (AbacatePay)
ABACATEPAY_API_KEY=

# IA
IA_PROVIDER=Anthropic
IA_API_KEY=
IA_MODEL=claude-3-5-sonnet-latest

# DataJud
DATAJUD_API_KEY=

# Armazenamento (OCI)
OCI_NAMESPACE=
OCI_REGION=sa-saopaulo-1
OCI_BUCKET_NAME=legal-manager-docs
OCI_ACCESS_KEY=
OCI_SECRET_KEY=
```

### DataJud (CNJ)

A API pública do CNJ permite buscar andamentos processuais. Solicite credenciais em [datajud.cnj.jus.br](https://datajud.cnj.jus.br).

## Execução

### Docker Compose

```bash
docker-compose up -d
```

Serviços iniciados:
- **API**: http://localhost:6600
- **Seq UI**: http://localhost:6681
- **PostgreSQL**: localhost:6632

### Desenvolvimento

```bash
cd src/LegalManager.API
dotnet run
```

## Estrutura de Diretórios

```
docs/                    # Documentação de módulos
coverage/               # Relatórios de coverage
tests/                   # Projetos de teste
docker-compose.yml      # Configuração Docker
.env.example            # Template de variáveis ambiente
```

## Módulos Implementados

- Modulo 01-14: various features documented in `docs/`
