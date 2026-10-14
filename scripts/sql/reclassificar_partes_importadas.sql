-- Reclassifica os contatos criados pela importação de processos por OAB.
--
-- A importação cadastrou TODAS as partes de cada processo como "Cliente"
-- (TipoContato = 0), inclusive a parte contrária e os advogados (contatos com OAB,
-- vindos do Escavador). Este script usa o JSON bruto do Escavador guardado em
-- "ProcessosImportacaoCache" para descobrir, processo a processo, em que polo está a
-- advogada (pela OAB dela) e então:
--   * advogado (contato com OAB, ou listado no JSON com
--     polo "ADVOGADO"/tipo "Advogado")                  -> Outro (4)
--   * parte no mesmo polo da advogada em algum processo -> continua Cliente (0)
--   * parte só no polo oposto                           -> Parte Contrária (1)
--   * sem como saber (sem cache, OAB não achada no JSON,
--     interessado/terceiro)                             -> não mexe; sai na lista da etapa 3
--
-- Só toca contatos ativos, importados automaticamente e ainda como Cliente, e pula
-- quem tem atendimento registrado ou acesso ao portal (sinal de cliente de verdade).
-- Enums: TipoContato Cliente=0, ParteContraria=1, Outro=4;
--        TipoParte (ProcessoPartes) Autor=0, Reu=1, Interessado=2, Terceiro=3.
--
-- Rode tudo na MESMA sessão (usa tabelas temporárias). A etapa 4 termina em ROLLBACK:
-- confira a saída e só então troque por COMMIT.

-- 0) Achar o escritório e a OAB usada na importação (troque o e-mail)
SELECT u."TenantId", t."Nome", i."NumeroOab", i."Uf", i."Importados", i."CriadoEm"
FROM "AspNetUsers" u
JOIN "Tenants" t ON t."Id" = u."TenantId"
LEFT JOIN "ImportacoesProcessos" i ON i."TenantId" = u."TenantId"
WHERE lower(u."Email") = lower('email-da-cliente@exemplo.com')
ORDER BY i."CriadoEm" DESC;

-- 1) Parâmetros (preencha com o resultado da etapa 0)
DROP TABLE IF EXISTS params;
CREATE TEMP TABLE params AS
SELECT '00000000-0000-0000-0000-000000000000'::uuid AS tenant_id,
       '123456'::text                               AS oab_numero,
       'SP'::text                                   AS oab_uf;

-- Lado da advogada em cada processo (0 = polo ativo/autor, 1 = polo passivo/réu).
-- Cobre os dois formatos de "envolvidos": advogado como envolvido próprio (com "oabs"
-- e "polo") ou aninhado em "advogados" dentro da parte que representa.
DROP TABLE IF EXISTS lado_advogada;
CREATE TEMP TABLE lado_advogada AS
WITH envolvidos AS (
  SELECT p."Id" AS processo_id, env
  FROM params
  JOIN "Processos" p ON p."TenantId" = params.tenant_id
  JOIN "ProcessosImportacaoCache" c
    ON c."TenantId" = p."TenantId"
   AND c."Fonte" = 'escavador'
   AND regexp_replace(c."NumeroCNJ", '\D', '', 'g') = regexp_replace(p."NumeroCNJ", '\D', '', 'g')
  CROSS JOIN LATERAL jsonb_array_elements(
    CASE WHEN jsonb_typeof(c."DadosJson"::jsonb -> 'fontes') = 'array'
         THEN c."DadosJson"::jsonb -> 'fontes' ELSE '[]'::jsonb END) fonte
  CROSS JOIN LATERAL jsonb_array_elements(
    CASE WHEN jsonb_typeof(fonte -> 'envolvidos') = 'array'
         THEN fonte -> 'envolvidos' ELSE '[]'::jsonb END) env
),
oabs AS (
  SELECT e.processo_id, upper(coalesce(e.env ->> 'polo', '')) AS polo, o
  FROM envolvidos e
  CROSS JOIN LATERAL jsonb_array_elements(
    CASE WHEN jsonb_typeof(e.env -> 'oabs') = 'array' THEN e.env -> 'oabs' ELSE '[]'::jsonb END) o
  UNION ALL
  SELECT e.processo_id, upper(coalesce(e.env ->> 'polo', '')), o
  FROM envolvidos e
  CROSS JOIN LATERAL jsonb_array_elements(
    CASE WHEN jsonb_typeof(e.env -> 'advogados') = 'array' THEN e.env -> 'advogados' ELSE '[]'::jsonb END) a
  CROSS JOIN LATERAL jsonb_array_elements(
    CASE WHEN jsonb_typeof(a -> 'oabs') = 'array' THEN a -> 'oabs' ELSE '[]'::jsonb END) o
),
lados AS (
  -- Mesmo mapeamento de ImportadorProcessosOab.MapearPoloEscavador
  SELECT DISTINCT x.processo_id,
         CASE
           WHEN x.polo = 'ATIVO' OR x.polo ~ '(AUTOR|RECLAMANTE|IMPETRANTE|REQUERENTE|EXEQUENTE)' THEN 0
           WHEN x.polo = 'PASSIVO' OR x.polo ~ '(RÉU|REU|RECLAMADO|REQUERIDO|EXECUTADO)' THEN 1
         END AS lado
  FROM oabs x
  CROSS JOIN params
  WHERE ltrim(regexp_replace(coalesce(x.o ->> 'numero', ''), '\D', '', 'g'), '0')
        = ltrim(regexp_replace(params.oab_numero, '\D', '', 'g'), '0')
    AND upper(coalesce(x.o ->> 'uf', '')) = upper(params.oab_uf)
)
-- Processo com a advogada nos dois polos (ou polo desconhecido) fica sem lado.
SELECT processo_id, min(lado) AS lado
FROM lados
WHERE lado IS NOT NULL
GROUP BY processo_id
HAVING count(DISTINCT lado) = 1;

-- Nomes listados como advogado no JSON (o Escavador repete o advogado como envolvido
-- próprio, com polo "ADVOGADO" e às vezes sem "oabs" — aí o contato ficou sem OAB).
DROP TABLE IF EXISTS nomes_advogados;
CREATE TEMP TABLE nomes_advogados AS
SELECT DISTINCT lower(env ->> 'nome') AS nome
FROM params
JOIN "ProcessosImportacaoCache" c ON c."TenantId" = params.tenant_id AND c."Fonte" = 'escavador'
CROSS JOIN LATERAL jsonb_array_elements(
  CASE WHEN jsonb_typeof(c."DadosJson"::jsonb -> 'fontes') = 'array'
       THEN c."DadosJson"::jsonb -> 'fontes' ELSE '[]'::jsonb END) fonte
CROSS JOIN LATERAL jsonb_array_elements(
  CASE WHEN jsonb_typeof(fonte -> 'envolvidos') = 'array'
       THEN fonte -> 'envolvidos' ELSE '[]'::jsonb END) env
WHERE upper(coalesce(env ->> 'polo', '')) = 'ADVOGADO'
   OR lower(coalesce(env ->> 'tipo_normalizado', '')) = 'advogado';

-- 2) Cobertura: em quantos processos deu para identificar o lado da advogada
SELECT (SELECT count(*) FROM "Processos" p, params WHERE p."TenantId" = params.tenant_id) AS processos,
       (SELECT count(*) FROM lado_advogada)                                                AS com_lado_identificado,
       (SELECT count(*) FROM lado_advogada WHERE lado = 0)                                 AS polo_ativo,
       (SELECT count(*) FROM lado_advogada WHERE lado = 1)                                 AS polo_passivo;

DROP TABLE IF EXISTS reclassificacao;
CREATE TEMP TABLE reclassificacao AS
SELECT ct."Id", ct."Nome",
       CASE
         WHEN ct."Oab" IS NOT NULL
           OR EXISTS (SELECT 1 FROM nomes_advogados na WHERE na.nome = lower(ct."Nome")) THEN 4
         WHEN bool_or(pp."TipoParte" = la.lado) THEN 0
         WHEN bool_or(pp."TipoParte" IN (0, 1) AND pp."TipoParte" <> la.lado) THEN 1
       END AS novo_tipo
FROM "Contatos" ct
CROSS JOIN params
LEFT JOIN "ProcessoPartes" pp ON pp."ContatoId" = ct."Id"
LEFT JOIN lado_advogada la ON la.processo_id = pp."ProcessoId"
WHERE ct."TenantId" = params.tenant_id
  AND ct."Ativo"
  AND ct."ImportadoAutomaticamente"
  AND ct."TipoContato" = 0
  AND NOT EXISTS (SELECT 1 FROM "Atendimentos" a WHERE a."ContatoId" = ct."Id")
  AND NOT EXISTS (SELECT 1 FROM "ProcessoPartes" px
                  WHERE px."ContatoId" = ct."Id" AND (px."PortalAcessoAtivo" OR px."PortalAcessoPendente"))
GROUP BY ct."Id", ct."Nome", ct."Oab";

-- 3) Prévia: resumo e contatos que ficam como estão (revisar à mão com a cliente)
SELECT CASE novo_tipo WHEN 0 THEN 'Continua Cliente' WHEN 1 THEN 'Parte Contrária'
                      WHEN 4 THEN 'Outro (advogado)' ELSE 'Indefinido — não mexe' END AS resultado,
       count(*)
FROM reclassificacao
GROUP BY 1
ORDER BY 1;

SELECT "Id", "Nome" FROM reclassificacao WHERE novo_tipo IS NULL ORDER BY "Nome";

-- 4) Aplicar (com backup dos valores antigos para desfazer, se preciso)
BEGIN;

CREATE TABLE IF NOT EXISTS "_BkpReclassificacaoContatos" (
  "ContatoId" uuid, "TenantId" uuid, "TipoContatoAnterior" integer, "FeitoEm" timestamp
);
INSERT INTO "_BkpReclassificacaoContatos"
SELECT ct."Id", ct."TenantId", ct."TipoContato", now() AT TIME ZONE 'utc'
FROM "Contatos" ct
JOIN reclassificacao r ON r."Id" = ct."Id"
WHERE r.novo_tipo IN (1, 4);

UPDATE "Contatos" ct
SET "TipoContato" = r.novo_tipo,
    "AtualizadoEm" = now() AT TIME ZONE 'utc'
FROM reclassificacao r
WHERE r."Id" = ct."Id"
  AND r.novo_tipo IN (1, 4);

-- Conferir antes de confirmar
SELECT ct."TipoContato", count(*)
FROM "Contatos" ct, params
WHERE ct."TenantId" = params.tenant_id AND ct."Ativo"
GROUP BY 1 ORDER BY 1;

ROLLBACK; -- troque por COMMIT depois de conferir

-- Desfazer (se precisar):
-- UPDATE "Contatos" ct SET "TipoContato" = b."TipoContatoAnterior"
-- FROM "_BkpReclassificacaoContatos" b
-- WHERE b."ContatoId" = ct."Id" AND b."TenantId" = '<tenant>';
