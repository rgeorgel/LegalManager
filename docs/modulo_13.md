# Módulo 13 — Armazenamento de Arquivos (OCI Object Storage)

## Visão Geral

O Módulo 13 implementa o sistema de armazenamento de documentos jurídicos no **Oracle Cloud Infrastructure (OCI) Object Storage**, accessed via S3 Compatibility API. Todos os uploads de documentos são armazenados de forma segura na nuvem, com controle de cota por tenant e isolamento total entre escritórios.

## Stack Tecnológica

- **Backend:** C# .NET 10
- **Storage:** Oracle Cloud Infrastructure Object Storage (S3 Compatibility API)
- **SDK:** AWS SDK for .NET (`AWSSDK.S3`)
- **Pacote NuGet:** `AWSSDK.S3` v3.7.402.4

## Arquitetura

```
┌─────────────────────────────────────────────────────────┐
│              Frontend (HTML/CSS/JS Vanilla)              │
│         Upload via fetch multipart/form-data              │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│                   API (ASP.NET Core)                    │
│  ┌─────────────────────────────────────────────────┐   │
│  │           DocumentosController                    │   │
│  │   POST /api/documentos  (upload c/ FormData)     │   │
│  │   GET  /api/documentos  (listar todos)           │   │
│  │   GET  /api/documentos/{id}  (buscar por id)     │   │
│  │   GET  /api/documentos/{id}/download  (URL pré-assinada)│
│  │   DELETE /api/documentos/{id}  (excluir)         │   │
│  │   GET  /api/documentos/cota  (usage da cota)    │   │
│  └─────────────────────────────────────────────────┘   │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              Application Layer                           │
│  ┌──────────────────┐  ┌─────────────────────────────┐  │
│  │  IDocumentoService│  │    IStorageService          │  │
│  │  (DocumentoService)│ │    (OciStorageService)      │  │
│  └──────────────────┘  └─────────────────────────────┘  │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────▼────────────────────────────────┐
│              Infrastructure Layer                        │
│  ┌──────────────────────────────────────────────────┐   │
│  │          OCI Object Storage (S3 Compatible)      │   │
│  │  Endpoint: https://<ns>.compat.objectstorage.<region>.oraclecloud.com
│  └──────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

## Configuração

### appsettings.json

```json
{
  "OciStorage": {
    "Namespace": "mynamespace",
    "Region": "sa-saopaulo-1",
    "BucketName": "legal-manager-docs",
    "AccessKey": "...",
    "SecretKey": "..."
  }
}
```

### Configuração OCI (Console)

1. Criar bucket `legal-manager-docs` na região `sa-saopaulo-1`
2. Gerar **Customer Secret Key** em: `Identity → Users → <usuário> → Customer Secret Keys`
3. Copiar o **namespace** do tenancy em: `Object Storage → Namespace`
4. Configurar policies IAM para acesso ao bucket

## Modelos de Dados

### Domain/Entities/Documento.cs

```csharp
public class Documento
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ProcessoId { get; set; }
    public Guid? ClienteId { get; set; }
    public Guid? ContratoId { get; set; }
    public Guid? ModeloId { get; set; }
    public string Nome { get; set; }           // nome original do arquivo
    public string ObjectKey { get; set; }       // chave no OCI
    public string ContentType { get; set; }     // "application/pdf", etc.
    public long TamanhoBytes { get; set; }
    public TipoDocumento Tipo { get; set; }    // Peticao|Decisao|Contrato|Prova|Modelo|Outro
    public DateTime CriadoEm { get; set; }
    public Guid UploadedPorId { get; set; }

    // Navigations
    public Tenant Tenant { get; set; }
    public Processo? Processo { get; set; }
    public Contato? Cliente { get; set; }
    public Usuario? UploadedPor { get; set; }
}
```

### Domain/Enums/Enums.cs (extensão)

```csharp
public enum TipoDocumento { Peticao, Decisao, Contrato, Prova, Modelo, Outro }
```

## API Endpoints

### POST /api/documentos

Upload de arquivo com `multipart/form-data`.

**Form Fields:**
- `file` (IFormFile, required) — arquivo até 100MB
- `tipo` (TipoDocumento, required) — Peticao|Decisao|Contrato|Prova|Modelo|Outro
- `processoId` (Guid?, optional) — vincular a processo
- `clienteId` (Guid?, optional) — vincular a cliente
- `contratoId` (Guid?, optional) — vincular a contrato
- `modeloId` (Guid?, optional) — vincular a modelo de documento

**Response:** `201 Created` com `DocumentoDto`

### GET /api/documentos

Lista todos os documentos do tenant.

**Response:** `200 OK` com `IEnumerable<DocumentoDto>`

### GET /api/documentos/{id}

Busca documento por ID.

**Response:** `200 OK` com `DocumentoDto` ou `404 Not Found`

### GET /api/documentos/{id}/download

Gera URL pré-assinada para download (válida por 30 minutos).

**Response:** `200 OK` com `{ url: "https://..." }`

### DELETE /api/documentos/{id}

Exclui documento (arquivo no OCI + registro no banco).

**Response:** `204 No Content`

### GET /api/documentos/cota

Retorna usage de armazenamento do tenant.

**Response:**
```json
{
  "usadoBytes": 1073741824,
  "cotaBytes": 21474836480,
  "usadoFormatado": "1.0 GB",
  "cotaFormatado": "20.0 GB",
  "porcentagemUsada": 5.0
}
```

## Convenção de Object Keys

```
{tenantId}/processos/{processoId}/{timestamp}_{nomeArquivo}
{tenantId}/contratos/{contratoId}/{timestamp}_{nomeArquivo}
{tenantId}/clientes/{clienteId}/{timestamp}_{nomeArquivo}
{tenantId}/modelos/{modeloId}/{timestamp}_{nomeArquivo}
{tenantId}/documentos/{timestamp}_{nomeArquivo}
```

Exemplo: `550e8400-e29b-41d4-a716-446655440000/processos/123e4567-e89b-12d3-a456-426614174000/20260423_143052_peticao_inicial.pdf`

## Controle de Cota

- **Plano Smart:** 20 GB por tenant
- Verificação antes de cada upload
- Exibição de barra de progresso no frontend
- Bloqueio de upload quando cota excedida

## Segurança

- **Isolamento por tenant:** Object keys incluem `tenantId`
- **URLs pré-assinadas:** Download expira em 30 minutos
- **Server-side encryption:** AES-256 (ServerSideEncryptionMethod.AES256)
- **JWT Authentication:** Todos os endpoints requerem token válido

## Estrutura de Arquivos

```
src/
├── LegalManager.Domain/
│   ├── Entities/
│   │   └── Documento.cs
│   └── Enums/
│       └── Enums.cs (+ TipoDocumento)
├── LegalManager.Application/
│   ├── DTOs/
│   │   └── Documentos/
│   │       └── DocumentoDto.cs
│   └── Interfaces/
│       ├── IStorageService.cs
│       └── IDocumentoService.cs
├── LegalManager.Infrastructure/
│   ├── Storage/
│   │   └── OciStorageService.cs
│   ├── Services/
│   │   └── DocumentoService.cs
│   └── Persistence/
│       └── AppDbContext.cs (+ Documentos DbSet)
└── LegalManager.API/
    ├── Controllers/
    │   └── DocumentosController.cs
    └── Program.cs (+ DI registration)
wwwroot/
├── pages/
│   └── documentos.html
└── js/
    └── layout.js (+ NAV_ITEMS update)
```

## Frontend

- **Página:** `/pages/documentos.html`
- **Funcionalidades:**
  - Lista de documentos com filtros (nome, tipo, origem)
  - Upload via drag-and-drop ou seleção de arquivo
  - Vinculação a processos ou clientes
  - Barra de cota de armazenamento
  - Download via URL pré-assinada
  - Exclusão com confirmação

### Integração na Tela de Processo (`processo-detalhe.html`)

A seção de **Documentos** aparece na coluna esquerda da tela de detalhe do processo, abaixo dos Andamentos:

- **Botão "+ Adicionar"** — abre modal de upload vinculado ao processo
- **Lista de documentos** — mostra nome, tamanho, tipo e data
- **Visualizar** — abre o documento em nova aba via URL pré-assinada
- **Excluir** — confirmação: `"⚠️ Tem certeza?\n\nO documento será excluído permanentemente e não poderá ser recuperado."`

**Endpoints utilizados:**
- `GET /api/documentos/processo/{processoId}` — lista documentos do processo
- `POST /api/documentos` — upload (multipart/form-data com `processoId`)
- `GET /api/documentos/{id}/download` — URL pré-assinada
- `DELETE /api/documentos/{id}` — exclusão

## Dependências

| Pacote | Versão | Uso |
|--------|--------|-----|
| AWSSDK.S3 | 3.7.402.4 | Cliente S3 para OCI |

## Serviços Registrados (DI)

```csharp
// Program.cs
builder.Services.AddSingleton<IStorageService, OciStorageService>();
builder.Services.AddScoped<IDocumentoService, DocumentoService>();
```