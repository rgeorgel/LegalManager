using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Novidades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DestaqueVistoEm",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NovidadesVistasEm",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Novidades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ImagemUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LinkUrl = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LinkTexto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PlanoMinimo = table.Column<int>(type: "integer", nullable: true),
                    Destaque = table.Column<bool>(type: "boolean", nullable: false),
                    TourId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Publicada = table.Column<bool>(type: "boolean", nullable: false),
                    PublicadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Novidades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Novidades_Publicada_PublicadaEm",
                table: "Novidades",
                columns: new[] { "Publicada", "PublicadaEm" });

            // Primeira leva de novidades (funcionalidades de set–out/2026). Inseridas só aqui, e não
            // via HasData, para o super admin poder editá-las ou excluí-las sem uma migration futura
            // tentar desfazer. Datas no passado: quem já usava o sistema vê todas como não lidas.
            var colunas = new[] { "Id", "Titulo", "Descricao", "LinkUrl", "LinkTexto", "PlanoMinimo", "Destaque", "TourId", "Publicada", "PublicadaEm", "CriadoEm" };
            const int plus = 3; // PlanoTipo.Plus
            object[] Novidade(string id, string titulo, string descricao, string? linkUrl, string? linkTexto,
                int? planoMinimo, bool destaque, string? tourId, DateTime publicadaEm) =>
                [new Guid(id), titulo, descricao, linkUrl!, linkTexto!, planoMinimo!, destaque, tourId!, true, publicadaEm, publicadaEm];

            foreach (var valores in new[]
            {
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000001",
                    "Modelos de documento com papel timbrado",
                    "A aba **Modelos** em Documentos foi refeita para você gerar peças em poucos cliques:\n\n" +
                    "- Editor com pré-visualização e as variáveis detectadas enquanto você digita\n" +
                    "- Preencha a partir de um processo e baixe em Word, imprima ou salve direto no processo\n" +
                    "- **Papel timbrado** do escritório (logo, nome, CNPJ, endereço e contatos) em todas as páginas\n" +
                    "- Novos exemplos prontos: Substabelecimento, Notificação Extrajudicial e Recibo de Honorários",
                    "/pages/documentos.html", "Abrir Documentos", plus, false, "novidade-modelos",
                    new DateTime(2026, 9, 28, 15, 0, 0, DateTimeKind.Utc)),
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000002",
                    "Filtre contatos por várias categorias e tags",
                    "Agora dá para marcar **várias categorias e tags** ao mesmo tempo no filtro de Contatos: " +
                    "aparecem os contatos com qualquer uma delas.\n\n" +
                    "A combinação também pode ser guardada nos **filtros salvos**, para usar de novo com um clique.",
                    "/pages/contatos.html", "Abrir Contatos", null, false, "novidade-contatos",
                    new DateTime(2026, 9, 28, 16, 0, 0, DateTimeKind.Utc)),
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000003",
                    "Feriados de 2027 na calculadora de prazos",
                    "Os feriados nacionais e estaduais de 2027 já estão cadastrados, então os prazos que " +
                    "atravessam a virada do ano são contados corretamente.",
                    "/pages/calculadora-prazos.html", "Calcular um prazo", plus, false, null,
                    new DateTime(2026, 9, 28, 17, 0, 0, DateTimeKind.Utc)),
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000004",
                    "Importação por OAB em segundo plano",
                    "A importação pela sua OAB agora traz **todos** os seus processos e roda em segundo plano: " +
                    "você continua usando o sistema enquanto uma barra no rodapé mostra o progresso, e recebe " +
                    "um aviso quando terminar.\n\n" +
                    "Os andamentos de cada processo são buscados na primeira vez que você o abre. Até lá, " +
                    "a listagem mostra ⏳ no lugar da contagem.",
                    "/pages/processos.html", "Ir para Processos", null, false, null,
                    new DateTime(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc)),
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000005",
                    "Monte o dashboard do seu jeito",
                    "Os **indicadores** agora ficam no próprio dashboard: Resumo de tarefas e Timesheet do mês já " +
                    "aparecem, e você pode ligar Processos por fase e Financeiro do mês em **⚙️ Personalizar**.\n\n" +
                    "No modo Personalizar também dá para escolher a **altura de cada bloco** (½, 1 ou 2) e " +
                    "encaixar dois blocos menores no espaço de um.\n\n" +
                    "_Indicadores disponíveis a partir do plano Plus._",
                    "/pages/dashboard.html", "Ir para o dashboard", null, false, "novidade-dashboard",
                    new DateTime(2026, 10, 7, 16, 0, 0, DateTimeKind.Utc)),
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000006",
                    "Ache o contato pelo nome ao adicionar partes",
                    "Ao cadastrar um processo ou adicionar uma parte, digite o **nome, CPF/CNPJ ou e-mail** e " +
                    "escolha o contato na lista, sem rolar uma lista enorme. Se a pessoa ainda não estiver " +
                    "cadastrada, o atalho para criar o contato aparece ali mesmo.",
                    "/pages/processos.html", "Ir para Processos", null, false, null,
                    new DateTime(2026, 10, 7, 17, 0, 0, DateTimeKind.Utc)),
                Novidade("0b7f3c1e-2a4d-4e5f-9a10-000000000007",
                    "Marque seus processos favoritos ⭐",
                    "Clique na **estrela ☆** de um processo para marcá-lo como favorito. Os favoritos aparecem " +
                    "primeiro na listagem, têm um filtro próprio (**★ Favoritos**) e ganham um bloco no dashboard.\n\n" +
                    "Cada pessoa do escritório tem os seus.",
                    "/pages/processos.html", "Ver meus processos", null, true, "novidade-favoritos",
                    new DateTime(2026, 10, 7, 18, 0, 0, DateTimeKind.Utc)),
            })
            {
                migrationBuilder.InsertData(table: "Novidades", columns: colunas, values: valores);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Novidades");

            migrationBuilder.DropColumn(
                name: "DestaqueVistoEm",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NovidadesVistasEm",
                table: "AspNetUsers");
        }
    }
}
