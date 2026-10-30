using System.Reflection;
using LegalManager.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LegalManager.Infrastructure.Persistence;

/// <summary>
/// As migrations até <see cref="UltimaMigracaoAntiga"/> foram consolidadas em <see cref="Baseline"/>.
/// Chamado antes do <c>MigrateAsync</c>: num banco que já passou por elas, registra a baseline como
/// aplicada (sem executá-la), e as migrations seguintes rodam normalmente. Banco novo → não faz nada
/// e a baseline cria tudo. As linhas antigas do histórico ficam, para que um rollback da API para
/// uma versão anterior ao squash continue enxergando o banco como migrado.
/// </summary>
public static class MigracoesBaseline
{
    public const string UltimaMigracaoAntiga = "20261030020907_NovidadeMfa";

    public static readonly string BaselineId = typeof(Baseline).GetCustomAttribute<MigrationAttribute>()!.Id;

    public static Task RegistrarAsync(DatabaseFacade database, CancellationToken ct = default) =>
        database.ExecuteSqlRawAsync(Sql, ct);

    // Só constantes do código são interpoladas (ids das migrations e versão do EF).
    // Em PL/pgSQL cada comando só é planejado ao executar: o histórico só é lido se a tabela existir.
    private static readonly string Sql = $"""
        DO $$
        BEGIN
            IF to_regclass('"__EFMigrationsHistory"') IS NULL THEN
                RETURN;
            END IF;
            IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{BaselineId}') THEN
                RETURN;
            END IF;

            IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '{UltimaMigracaoAntiga}') THEN
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('{BaselineId}', '{ProductInfo.GetVersion()}')
                ON CONFLICT DO NOTHING;
            ELSIF EXISTS (SELECT 1 FROM "__EFMigrationsHistory") THEN
                RAISE EXCEPTION 'Banco com migrations antigas pendentes (falta {UltimaMigracaoAntiga}). Atualize-o primeiro com uma versão da API anterior ao squash das migrations.';
            END IF;
        END $$;
        """;
}
