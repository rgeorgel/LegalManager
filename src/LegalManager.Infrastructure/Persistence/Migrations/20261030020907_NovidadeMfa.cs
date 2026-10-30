using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NovidadeMfa : Migration
    {
        private static readonly Guid Id = new("0b7f3c1e-2a4d-4e5f-9a10-000000000009");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Novidade da verificação em duas etapas. Como as da migration Novidades: inserida só aqui
            // (não via HasData), para o super admin poder editá-la ou excluí-la. Em destaque e para
            // todos os planos. PublicadaEm = hora do deploy (não uma data fixa), para não ficar
            // "antes" de quem abriu o painel ✨ entre o commit e o deploy. O link leva à seção do MFA
            // em Configurações (configuracoes.html trata #mfa).
            const string descricao =
                "Agora você pode exigir, além da senha, um **código de 6 dígitos gerado no seu celular** para entrar no Causify. " +
                "Mesmo que alguém descubra sua senha, não consegue acessar a conta — e os dados dos seus clientes.\n\n" +
                "- Funciona com Google Authenticator, Microsoft Authenticator, Authy, 1Password e similares\n" +
                "- Para ativar: **Configurações → Verificação em duas etapas**, escaneie o QR code e confirme o código\n" +
                "- Você recebe **10 códigos de recuperação** para entrar se perder o celular — guarde-os num lugar seguro\n\n" +
                "_É opcional e vale por usuário: cada pessoa do escritório ativa na própria conta. " +
                "Quem entra com o Google continua usando a proteção da conta Google._";

            migrationBuilder.Sql($"""
                INSERT INTO "Novidades" ("Id", "Titulo", "Descricao", "LinkUrl", "LinkTexto", "PlanoMinimo", "Destaque", "TourId", "Publicada", "PublicadaEm", "CriadoEm")
                VALUES ('{Id}', 'Verificação em duas etapas 🔒', '{descricao.Replace("'", "''")}',
                        '/pages/configuracoes.html#mfa', 'Ativar agora', NULL, TRUE, NULL, TRUE,
                        now(), now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "Novidades", keyColumn: "Id", keyValue: Id);
        }
    }
}
