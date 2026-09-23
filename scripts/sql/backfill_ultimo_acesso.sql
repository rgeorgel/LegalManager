-- Backfill de UltimoAcessoEm para usuários cadastrados antes da correção
-- que passou a preencher esse campo automaticamente no registro (login
-- automático no cadastro não estava setando UltimoAcessoEm).
--
-- Usa CriadoEm como valor de UltimoAcessoEm apenas para quem nunca
-- de fato acessou o sistema (UltimoAcessoEm IS NULL). Usuários que já
-- possuem UltimoAcessoEm (já fizeram login normalmente) não são tocados.

-- 1) Conferir quantos registros serão afetados antes de rodar o UPDATE
SELECT "Id", "Email", "CriadoEm", "UltimoAcessoEm"
FROM "AspNetUsers"
WHERE "UltimoAcessoEm" IS NULL;

-- 2) Aplicar o backfill
UPDATE "AspNetUsers"
SET "UltimoAcessoEm" = "CriadoEm"
WHERE "UltimoAcessoEm" IS NULL;
