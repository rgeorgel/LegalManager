# Backup diário e restauração do banco de produção

O banco de produção é o container `postgres` (Postgres 16) do `docker-compose.yml` no servidor da
Oracle, com os dados em `./data/postgres`. Este documento descreve como fazer um backup diário dele e
como restaurá-lo.

## Resumo

| | |
|---|---|
| **O quê** | `pg_dump` em formato custom (comprimido) do banco `legalmanager`, sem o schema `hangfire` |
| **Quando** | Todo dia às 01:30 (Brasília) |
| **Onde** | No disco do servidor (7 dias) + bucket privado no OCI Object Storage (30 dias de diários, 12 meses de mensais) |
| **Tamanho / tempo** | ~16 MB e poucos segundos (medido em 01/11/2026 com 86 MB de dados) |
| **Perda máxima (RPO)** | Até 24 h — o que entrou depois do último backup |
| **Tempo de volta (RTO)** | ~15–30 min (baixar o arquivo + restaurar + subir a API) |

Por que **sem o `hangfire`**: é só a fila de jobs. A API recria o schema ao subir e registra os jobs
recorrentes de novo; restaurar uma fila antiga só faria rodar jobs velhos.

> **Copiar a pasta `./data/postgres` com o banco no ar não é backup** — os arquivos podem estar
> inconsistentes. Use sempre o `pg_dump`.

---

## Parte 1 — Configurar o backup (uma vez)

### 1.1 Bucket no OCI Object Storage

1. **Object Storage → Buckets → Create Bucket**, no compartimento do projeto, região `sa-saopaulo-1`:
   - Nome: `causify-backups` (separado do bucket de documentos `legal-manager-docs`)
   - Visibilidade: **privado** (padrão). A criptografia em repouso já vem ligada (chave gerenciada pela Oracle).
2. **Lifecycle Policy Rules** do bucket — apagam os backups antigos sozinhas:
   - `diario/` → *Delete* após **30 dias**
   - `mensal/` → *Delete* após **365 dias**

   O Object Storage precisa de permissão para aplicar as regras. Em **Identity → Policies**, no
   compartimento:
   ```
   Allow service objectstorage-sa-saopaulo-1 to manage object-family in compartment <COMPARTIMENTO>
   ```
3. *(Recomendado)* **Retention Rules** do bucket: uma regra de, por exemplo, 7 dias impede que
   qualquer pessoa — inclusive alguém que invada o servidor — apague ou sobrescreva backups recentes.

Espaço: 30 diários + 12 mensais de ~16 MB ≈ 0,7 GB, dentro dos 20 GB do Always Free.

### 1.2 Credenciais só para o backup

Não reaproveite a chave `OCI_ACCESS_KEY` da aplicação. Crie um usuário (ou grupo) só para o backup e
gere uma **Customer Secret Key** para ele (**Identity → Users → \<usuário> → Customer Secret Keys**).
Dê a ele permissão só de gravar no bucket de backups:

```
Allow group causify-backup to manage objects in compartment <COMPARTIMENTO> where all {target.bucket.name='causify-backups', any {request.permission='OBJECT_CREATE', request.permission='OBJECT_INSPECT'}}
Allow group causify-backup to read buckets in compartment <COMPARTIMENTO> where target.bucket.name='causify-backups'
```

Assim, uma credencial vazada do servidor não consegue apagar nem ler os backups. Para **baixar** um
backup na hora de restaurar, use uma credencial sua (com leitura) ou o console da OCI.

No servidor, guarde as credenciais fora do repositório:

```bash
sudo mkdir -p /etc/causify
sudo tee /etc/causify/backup.env > /dev/null <<'EOF'
OCI_NAMESPACE=seu_namespace_oci
OCI_REGION=sa-saopaulo-1
OCI_BACKUP_BUCKET=causify-backups
OCI_BACKUP_ACCESS_KEY=xxxxxxxxxxxxxxxxxxxx
OCI_BACKUP_SECRET_KEY=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
# Opcional: URL de um monitor do tipo "dead man's switch" (ex.: healthchecks.io).
# Ele avisa por e-mail se o ping diário não chegar ou se chegar um /fail.
HEALTHCHECK_URL=
EOF
sudo chmod 600 /etc/causify/backup.env
```

### 1.3 Script de backup

Salve como `/usr/local/bin/causify-backup.sh` e ajuste `PROJETO_DIR` para a pasta do
`docker-compose.yml` no servidor:

```bash
#!/usr/bin/env bash
# Backup diário do Postgres de produção → disco local + OCI Object Storage.
set -euo pipefail

PROJETO_DIR=/CAMINHO/DO/PROJETO     # pasta do docker-compose.yml
DESTINO=/var/backups/causify
RETENCAO_LOCAL_DIAS=7

source /etc/causify/backup.env

ping_monitor() { [ -n "${HEALTHCHECK_URL:-}" ] && curl -fsS -m 10 --retry 3 "${HEALTHCHECK_URL}$1" > /dev/null || true; }
trap 'ping_monitor /fail' ERR

DATA=$(date -u +%Y-%m-%d)
ARQUIVO="legalmanager-${DATA}.dump"
mkdir -p "$DESTINO"
cd "$PROJETO_DIR"

# 1. Dump: formato custom comprimido, sem dono/permissões e sem a fila do Hangfire.
#    Não bloqueia a aplicação: o pg_dump lê uma foto consistente do banco.
docker compose exec -T postgres sh -c \
  'pg_dump -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" -Fc -Z 9 --no-owner --no-acl --exclude-schema=hangfire' \
  > "$DESTINO/$ARQUIVO.tmp"

# 2. Só vira backup se o arquivo for legível e tiver os dados (pega dump vazio ou truncado).
docker compose exec -T postgres pg_restore --list < "$DESTINO/$ARQUIVO.tmp" | grep -q 'TABLE DATA public Tenants '
mv "$DESTINO/$ARQUIVO.tmp" "$DESTINO/$ARQUIVO"

# 3. Cópia fora do servidor (API S3-compatível da OCI). Versão fixada: a partir da 2.23 o aws-cli
#    envia checksums novos por padrão, que provedores S3-compatíveis podem recusar.
enviar() {
  docker run --rm -v "$DESTINO:/backup:ro" \
    -e AWS_ACCESS_KEY_ID="$OCI_BACKUP_ACCESS_KEY" -e AWS_SECRET_ACCESS_KEY="$OCI_BACKUP_SECRET_KEY" \
    amazon/aws-cli:2.22.35 s3 cp "/backup/$ARQUIVO" "s3://$OCI_BACKUP_BUCKET/$1/$ARQUIVO" \
    --region "$OCI_REGION" --endpoint-url "https://$OCI_NAMESPACE.compat.objectstorage.$OCI_REGION.oraclecloud.com" \
    --only-show-errors
}
enviar diario
[ "$(date -u +%d)" = "01" ] && enviar mensal

# 4. Limpa os backups locais antigos (os do bucket somem pelas Lifecycle Rules).
find "$DESTINO" -name 'legalmanager-*.dump' -mtime +"$RETENCAO_LOCAL_DIAS" -delete

echo "$(date -u +%FT%TZ) backup ok: $ARQUIVO ($(du -h "$DESTINO/$ARQUIVO" | cut -f1))"
ping_monitor ""
```

```bash
sudo chmod 700 /usr/local/bin/causify-backup.sh
sudo /usr/local/bin/causify-backup.sh      # primeira execução manual
```

Confira se o arquivo apareceu em `/var/backups/causify/` e em `causify-backups/diario/` no console da
OCI antes de agendar.

### 1.4 Agendar

`sudo crontab -e` e adicione (04:30 UTC = 01:30 em Brasília — confira o fuso do servidor com
`timedatectl`; se ele estiver em `America/Sao_Paulo`, use `30 1 * * *`):

```
30 4 * * * /usr/local/bin/causify-backup.sh >> /var/log/causify-backup.log 2>&1
```

O horário não conflita com nada: o dump não trava escritas, e os jobs do Hangfire seguem rodando.

### 1.5 Acompanhar

- `tail /var/log/causify-backup.log` — uma linha `backup ok` por dia.
- Se configurou o `HEALTHCHECK_URL`, o monitor avisa por e-mail quando o backup falha ou não roda.
- **Uma vez por mês, teste uma restauração** (Cenário A abaixo). Backup que nunca foi restaurado não é
  garantia de nada.

---

## Parte 2 — Restaurar

### 2.0 Pegar o arquivo

Os backups dos últimos 7 dias estão no próprio servidor:

```bash
ls -lh /var/backups/causify/
```

Mais antigos (ou se o servidor se perdeu): baixe pelo console da OCI
(**Buckets → causify-backups → diario/ ou mensal/ → Download**) ou pelo aws-cli, com uma credencial
que tenha leitura no bucket:

```bash
docker run --rm -v "$PWD:/backup" \
  -e AWS_ACCESS_KEY_ID=SUA_CHAVE -e AWS_SECRET_ACCESS_KEY=SEU_SEGREDO \
  amazon/aws-cli:2.22.35 s3 cp "s3://causify-backups/diario/legalmanager-2026-11-01.dump" /backup/ \
  --region sa-saopaulo-1 --endpoint-url "https://SEU_NAMESPACE.compat.objectstorage.sa-saopaulo-1.oraclecloud.com"
```

Nos comandos abaixo, `BACKUP=/var/backups/causify/legalmanager-AAAA-MM-DD.dump` e todos rodam na
pasta do `docker-compose.yml`.

### Cenário A — Recuperar dados específicos (o mais comum)

Ex.: alguém apagou um processo ou um contato por engano. **Não mexa no banco de produção** — restaure
o backup num banco temporário ao lado e copie só o que precisa.

```bash
# 1. Banco temporário dentro do mesmo container
docker compose exec -T postgres sh -c 'createdb -U "${POSTGRES_USER:-postgres}" legalmanager_restore'

# 2. Restaura o backup nele
docker compose exec -T postgres sh -c \
  'pg_restore -U "${POSTGRES_USER:-postgres}" -d legalmanager_restore --no-owner --no-acl --exit-on-error' < "$BACKUP"

# 3. Consulte e compare (psql, ou o DataGrip apontando para o banco legalmanager_restore)
docker compose exec postgres sh -c 'psql -U "${POSTGRES_USER:-postgres}" -d legalmanager_restore'
```

Para trazer as linhas de volta, exporte do temporário e importe na produção — por exemplo, os
andamentos de um processo:

```bash
# Exporta do temporário (a consulta vai no heredoc, sem brigar com aspas)
docker compose exec -T postgres sh -c 'psql -U "${POSTGRES_USER:-postgres}" -d legalmanager_restore' > andamentos.tsv <<'SQL'
\copy (select * from "Andamentos" where "ProcessoId" = 'UUID-DO-PROCESSO') to pstdout
SQL

# Importa na produção
docker compose exec -T postgres sh -c \
  'psql -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" -c "\copy \"Andamentos\" from pstdin"' \
  < andamentos.tsv
```

Se a linha ainda existir na produção (foi alterada, não apagada), o import falha pela chave primária —
apague-a antes ou faça um `UPDATE` comparando os dois bancos.

Respeite as chaves estrangeiras: traga primeiro as linhas "pai" (o processo) e depois as "filhas"
(andamentos, partes, tarefas). Quando terminar:

```bash
docker compose exec -T postgres sh -c 'dropdb -U "${POSTGRES_USER:-postgres}" legalmanager_restore'
rm -f andamentos.tsv
```

### Cenário B — Voltar o banco inteiro (mesmo servidor)

Ex.: um bug ou uma migration estragou dados em massa. **Tudo o que entrou depois do backup se perde**
— avise os escritórios.

```bash
# 1. Para a API (ninguém grava durante a troca)
docker compose stop api

# 2. Guarda o estado atual, por garantia (dá para voltar atrás ou recuperar algo pelo Cenário A)
docker compose exec -T postgres sh -c \
  'pg_dump -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" -Fc --no-owner --no-acl' \
  > "/var/backups/causify/antes-da-restauracao-$(date -u +%Y%m%dT%H%M).dump"

# 3. Restaura por cima, numa transação só: se der qualquer erro, nada muda
docker compose exec -T postgres sh -c \
  'pg_restore -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" --clean --if-exists --no-owner --no-acl --single-transaction --exit-on-error' \
  < "$BACKUP"

# 4. Descarta a fila do Hangfire (jobs enfileirados depois do backup); a API recria vazia
docker compose exec -T postgres sh -c \
  'psql -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" -c "DROP SCHEMA IF EXISTS hangfire CASCADE"'

# 5. Sobe a API
docker compose up -d api
```

### Cenário C — Servidor perdido (subir em outro)

```bash
# 1. No servidor novo: Docker instalado, repositório clonado e o .env de produção recriado
#    (guarde uma cópia do .env num cofre de senhas — ele NÃO está no backup do banco)

# 2. Sobe só o Postgres, com o banco vazio
docker compose up -d postgres
docker compose exec postgres sh -c 'pg_isready -U "${POSTGRES_USER:-postgres}"'   # repita até "accepting connections"

# 3. Baixa o backup mais recente do bucket (2.0) e restaura
docker compose exec -T postgres sh -c \
  'pg_restore -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" --clean --if-exists --no-owner --no-acl --single-transaction --exit-on-error' \
  < legalmanager-AAAA-MM-DD.dump

# 4. Sobe a API (e refaça a Parte 1 neste servidor)
docker compose up -d api
```

Depois, aponte o DNS para o servidor novo e reconfigure o que depende do endereço (webhooks do Stripe
e do Escavador, origens do Google OAuth).

### Depois de qualquer restauração

- `docker compose logs api | grep -iE "migrations|listening|fail"`:
  - "No migrations were applied" — o backup estava em dia com o código.
  - Se o backup for anterior a uma migration, a API aplica as pendentes ao subir — esperado.
- Entre no sistema com um usuário real e abra processos, contatos e a agenda.
- Confira o número de escritórios:
  `docker compose exec -T postgres sh -c 'psql -U "${POSTGRES_USER:-postgres}" -d "${POSTGRES_DB:-legalmanager}" -Atc "select count(*) from \"Tenants\""'`

---

## Limites desta estratégia

- **Perde até 24 h de dados.** Para chegar a minutos, o próximo passo seria arquivar o WAL
  continuamente (pgBackRest ou WAL-G para o mesmo bucket), o que permite restaurar para qualquer
  momento.
- **Os arquivos fora do banco não estão aqui.** Documentos e logos ficam no bucket
  `legal-manager-docs` do OCI. Ligue o *Object Versioning* nele para poder recuperar arquivos
  apagados ou sobrescritos.
- **O `.env` de produção não está no backup.** Sem ele (chaves JWT, Stripe, Escavador, IA…) o
  Cenário C não fecha. Guarde uma cópia num cofre de senhas.

## Validação

Os comandos de dump, dos três cenários de restauração e da cópia de linhas do Cenário A foram
testados em 01/11/2026 com uma cópia da produção em containers `postgres:16-alpine` (via
`docker exec`, equivalente ao `docker compose exec`): backup de 16 MB, e as 70 tabelas
(130.776 linhas) restauradas idênticas nos três cenários. O envio ao OCI (aws-cli) não foi testado, porque depende das
credenciais do bucket — valide na primeira execução manual (1.3).
