# Setup Instructions

## Variáveis de ambiente

Copie o arquivo `.env.example` para `.env` e preencha os valores conforme abaixo.

---

## DataJud (CNJ) — Monitoramento automático de processos

A integração com o DataJud usa a API pública do Conselho Nacional de Justiça para buscar andamentos processuais automaticamente.

**Como obter a chave:**

1. Acesse **datajud.cnj.jus.br**
2. Clique em **"Acesso à API"** ou **"Solicitar Acesso"**
3. Faça login com conta **gov.br** (CPF + senha)
4. Solicite credenciais para uso da API pública

**Configuração:**

```env
DataJud__ApiKey=sua_chave_aqui
```

> **Nota:** O sistema já inclui a chave pública de demonstração do CNJ como fallback (`cDZHYzlZa0JadVREZDJCendOM3Yw`). Ela funciona para testes, mas tem rate limiting mais restrito. Para produção, use uma chave própria.
