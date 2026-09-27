-- Backfill de "Eventos"."ResponsavelId" para eventos criados pela Agenda antes da
-- correção que passou a exigir/preencher o responsável. O formulário da Agenda não
-- tinha o campo, então todo evento criado (ou editado) por ele ficou sem responsável
-- e nunca entrou nos e-mails do AlertasJob (resumo diário e "Eventos de amanhã").
--
-- O evento não guarda quem o criou, então só dá para inferir o dono com segurança
-- quando o escritório tem um único usuário interno ativo (Perfil Admin=0,
-- Advogado=1, Colaborador=2 — Cliente=3 e SuperAdmin=5 ficam de fora).
-- A etapa 3 (opcional) cobre escritórios com vários usuários atribuindo ao admin
-- ativo mais antigo; rode só se fizer sentido para o escritório.

-- 1) Conferir: eventos sem responsável por escritório e quantos usuários internos ativos ele tem
SELECT t."Id" AS "TenantId", t."Nome",
       COUNT(DISTINCT e."Id") AS "EventosSemResponsavel",
       (SELECT COUNT(*) FROM "AspNetUsers" u
         WHERE u."TenantId" = t."Id" AND u."Ativo" AND u."Perfil" IN (0, 1, 2)) AS "UsuariosAtivos"
FROM "Eventos" e
JOIN "Tenants" t ON t."Id" = e."TenantId"
WHERE e."ResponsavelId" IS NULL
GROUP BY t."Id", t."Nome"
ORDER BY t."Nome";

BEGIN;

-- 2) Escritórios com um único usuário interno ativo: o evento só pode ser dele
UPDATE "Eventos" e
SET "ResponsavelId" = u."Id",
    "AtualizadoEm" = now()
FROM "AspNetUsers" u
WHERE e."ResponsavelId" IS NULL
  AND u."TenantId" = e."TenantId"
  AND u."Ativo" AND u."Perfil" IN (0, 1, 2)
  AND (SELECT COUNT(*) FROM "AspNetUsers" u2
        WHERE u2."TenantId" = e."TenantId" AND u2."Ativo" AND u2."Perfil" IN (0, 1, 2)) = 1;

-- 3) (Opcional) Escritórios com vários usuários: atribui ao admin ativo mais antigo.
--    Descomente para aplicar.
-- UPDATE "Eventos" e
-- SET "ResponsavelId" = (
--         SELECT u."Id" FROM "AspNetUsers" u
--         WHERE u."TenantId" = e."TenantId" AND u."Ativo" AND u."Perfil" = 0
--         ORDER BY u."CriadoEm"
--         LIMIT 1),
--     "AtualizadoEm" = now()
-- WHERE e."ResponsavelId" IS NULL
--   AND EXISTS (SELECT 1 FROM "AspNetUsers" u
--               WHERE u."TenantId" = e."TenantId" AND u."Ativo" AND u."Perfil" = 0);

-- 4) Conferir o que sobrou sem responsável antes de confirmar
SELECT e."TenantId", e."Id", e."Titulo", e."Tipo", e."DataHora"
FROM "Eventos" e
WHERE e."ResponsavelId" IS NULL
ORDER BY e."TenantId", e."DataHora";

-- 5) Se estiver certo, rode COMMIT; senão, ROLLBACK;
