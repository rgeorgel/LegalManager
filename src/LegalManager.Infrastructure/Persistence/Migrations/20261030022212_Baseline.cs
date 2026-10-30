using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LegalManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Squash de todas as migrations até 20261030020907_NovidadeMfa. Bancos que já passaram por elas
            // são marcados como migrados por MigracoesBaseline antes do MigrateAsync; esta trava garante que,
            // se mesmo assim chegar aqui com o esquema presente, nada é alterado.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('"Tenants"') IS NOT NULL THEN
                        RAISE EXCEPTION 'Baseline: o esquema já existe neste banco. Bancos criados pelas migrations antigas são registrados pela API ao iniciar (MigracoesBaseline.RegistrarAsync); rode a API uma vez em vez de "dotnet ef database update".';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "AcoesAssistente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    InteracaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EntidadeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titulo = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    AlteracoesJson = table.Column<string>(type: "text", nullable: false),
                    DadosJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DesfazerJson = table.Column<string>(type: "text", nullable: true),
                    DesfeitaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcoesAssistente", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Acao = table.Column<string>(type: "text", nullable: false),
                    Entidade = table.Column<string>(type: "text", nullable: false),
                    EntidadeId = table.Column<string>(type: "text", nullable: true),
                    DadosAnteriores = table.Column<string>(type: "text", nullable: true),
                    DadosNovos = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImpersonadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodigosPromocionais",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Plano = table.Column<int>(type: "integer", nullable: true),
                    DiasGratuitos = table.Column<int>(type: "integer", nullable: false),
                    DescontoPercentual = table.Column<int>(type: "integer", nullable: true),
                    DescontoMeses = table.Column<int>(type: "integer", nullable: true),
                    MaxUsos = table.Column<int>(type: "integer", nullable: true),
                    ValidoAte = table.Column<DateOnly>(type: "date", nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodigosPromocionais", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsultasExternas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Api = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TipoConsulta = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Origem = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    ParametrosJson = table.Column<string>(type: "text", nullable: true),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    MensagemErro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    QuantidadeResultados = table.Column<int>(type: "integer", nullable: true),
                    ResultadoResumoJson = table.Column<string>(type: "text", nullable: true),
                    DuracaoMs = table.Column<int>(type: "integer", nullable: true),
                    CreditosUtilizados = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultasExternas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GruposPergunta",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposPergunta", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GruposPerguntaMembros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdicionadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposPerguntaMembros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IndicesCorrecaoMonetaria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Ano = table.Column<int>(type: "integer", nullable: false),
                    Mes = table.Column<int>(type: "integer", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(8,6)", nullable: false),
                    Fonte = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndicesCorrecaoMonetaria", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InteracoesAssistente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImpersonadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConversaId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Pergunta = table.Column<string>(type: "text", nullable: false),
                    Resposta = table.Column<string>(type: "text", nullable: true),
                    Sucesso = table.Column<bool>(type: "boolean", nullable: false),
                    MensagemErro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IdiomaCorrigido = table.Column<bool>(type: "boolean", nullable: false),
                    FerramentasJson = table.Column<string>(type: "text", nullable: true),
                    QuantidadeFerramentas = table.Column<int>(type: "integer", nullable: false),
                    Provedor = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Modelo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TokensEntrada = table.Column<int>(type: "integer", nullable: false),
                    TokensSaida = table.Column<int>(type: "integer", nullable: false),
                    DuracaoMs = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InteracoesAssistente", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "Parceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Telefone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Codigo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PercentualComissao = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    MesesComissao = table.Column<int>(type: "integer", nullable: true),
                    ChavePix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: true),
                    TokenSenha = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TokenSenhaExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoAcessoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TentativasLoginFalhas = table.Column<int>(type: "integer", nullable: false),
                    BloqueadoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parceiros", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Perguntas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TipoResposta = table.Column<int>(type: "integer", nullable: false),
                    OpcoesJson = table.Column<string>(type: "text", nullable: true),
                    Publico = table.Column<int>(type: "integer", nullable: false),
                    Segmento = table.Column<int>(type: "integer", nullable: false),
                    PlanoAlvo = table.Column<int>(type: "integer", nullable: true),
                    GrupoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Perguntas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrecosPlanos",
                columns: table => new
                {
                    Plano = table.Column<int>(type: "integer", nullable: false),
                    ValorMensal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecosPlanos", x => x.Plano);
                });

            migrationBuilder.CreateTable(
                name: "ProcessosImportacaoCache",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Fonte = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DadosJson = table.Column<string>(type: "text", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessosImportacaoCache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RespostasPerguntas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PerguntaId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RespondenteTipo = table.Column<int>(type: "integer", nullable: false),
                    RespondenteId = table.Column<Guid>(type: "uuid", nullable: false),
                    RespostaTexto = table.Column<string>(type: "text", nullable: true),
                    OpcaoEscolhida = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OpcoesEscolhidasJson = table.Column<string>(type: "text", nullable: true),
                    RespondidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RespostasPerguntas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Plano = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SolicitacoesPagamentoParceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParceiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ChavePix = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ObservacaoParceiro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SolicitadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvidaPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Resposta = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitacoesPagamentoParceiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SolicitacoesPagamentoParceiros_Parceiros_ParceiroId",
                        column: x => x.ParceiroId,
                        principalTable: "Parceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ComissoesParceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParceiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeEscritorio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FaturamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ValorPagamento = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Percentual = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    PagoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SolicitacaoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComissoesParceiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComissoesParceiros_Parceiros_ParceiroId",
                        column: x => x.ParceiroId,
                        principalTable: "Parceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComissoesParceiros_SolicitacoesPagamentoParceiros_Solicitac~",
                        column: x => x.SolicitacaoId,
                        principalTable: "SolicitacoesPagamentoParceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AcessosCliente",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SenhaHash = table.Column<string>(type: "text", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimoAcessoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenConvite = table.Column<string>(type: "text", nullable: true),
                    TokenConviteExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TokenRedefinicao = table.Column<string>(type: "text", nullable: true),
                    TokenRedefinicaoExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TentativasLoginFalhas = table.Column<int>(type: "integer", nullable: false),
                    BloqueadoAte = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcessosCliente", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Andamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Fonte = table.Column<int>(type: "integer", nullable: false),
                    DescricaoTraduzidaIA = table.Column<string>(type: "text", nullable: true),
                    RegistradoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CodigoCNJ = table.Column<int>(type: "integer", nullable: true),
                    OrgaoJulgador = table.Column<string>(type: "text", nullable: true),
                    DadosExtras = table.Column<string>(type: "text", nullable: true),
                    VisivelCliente = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Andamentos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AreasAtuacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AreasAtuacao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "text", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Perfil = table.Column<int>(type: "integer", nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OnboardingImportacaoCompleto = table.Column<bool>(type: "boolean", nullable: false),
                    OabImportadaNumero = table.Column<string>(type: "text", nullable: true),
                    OabImportadaUf = table.Column<string>(type: "text", nullable: true),
                    UltimoAcessoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ToursConcluidos = table.Column<string>(type: "text", nullable: true),
                    DashboardLayout = table.Column<string>(type: "text", nullable: true),
                    DashboardVersao = table.Column<string>(type: "text", nullable: true),
                    AssistenteMascote = table.Column<string>(type: "text", nullable: true),
                    NovidadesVistasEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DestaqueVistoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OrigemCadastro = table.Column<string>(type: "text", nullable: true),
                    UtmSource = table.Column<string>(type: "text", nullable: true),
                    UtmMedium = table.Column<string>(type: "text", nullable: true),
                    UtmCampaign = table.Column<string>(type: "text", nullable: true),
                    Referrer = table.Column<string>(type: "text", nullable: true),
                    LandingPage = table.Column<string>(type: "text", nullable: true),
                    Fbclid = table.Column<string>(type: "text", nullable: true),
                    Gclid = table.Column<string>(type: "text", nullable: true),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "text", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revogado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImpersonadoPorId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AspNetUsers_ImpersonadoPorId",
                        column: x => x.ImpersonadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Cnpj = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    LogoObjectKey = table.Column<string>(type: "text", nullable: true),
                    Endereco = table.Column<string>(type: "text", nullable: true),
                    Telefone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TimbradoComplemento = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FusoHorario = table.Column<string>(type: "text", nullable: true),
                    Plano = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TrialExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AbacatePayBillingId = table.Column<string>(type: "text", nullable: true),
                    StripeCustomerId = table.Column<string>(type: "text", nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    PeriodoBilling = table.Column<string>(type: "text", nullable: true),
                    ValorMensalAssinatura = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    PlanoExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    BillingCycleStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoucherUtilizado = table.Column<string>(type: "text", nullable: true),
                    CodigoDesconto = table.Column<string>(type: "text", nullable: true),
                    DescontoPromocionalUsadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ParceiroId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParceiroVinculadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PrimaryColor = table.Column<string>(type: "text", nullable: true),
                    SidebarColor = table.Column<string>(type: "text", nullable: true),
                    AccentColor = table.Column<string>(type: "text", nullable: true),
                    LayoutMode = table.Column<string>(type: "text", nullable: true),
                    CustomCss = table.Column<string>(type: "text", nullable: true),
                    TrialConcedidoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrialConcedidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrialConcedidoDias = table.Column<int>(type: "integer", nullable: true),
                    TrialConcedidoMotivo = table.Column<string>(type: "text", nullable: true),
                    TrialGratisBoasVindasVisualizado = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tenants_AspNetUsers_TrialConcedidoPorId",
                        column: x => x.TrialConcedidoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tenants_Parceiros_ParceiroId",
                        column: x => x.ParceiroId,
                        principalTable: "Parceiros",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CalculosPrazo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QuantidadeDias = table.Column<int>(type: "integer", nullable: true),
                    TipoCalculo = table.Column<int>(type: "integer", nullable: true),
                    FeriadosAdicionaisJson = table.Column<string>(type: "text", nullable: true),
                    DataFinal = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PrazoProcessual = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculosPrazo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalculosPrazo_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalculosPrazo_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CategoriasFinanceiras",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriasFinanceiras", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoriasFinanceiras_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracoesCalculadora",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdicionalEspecialidade = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesCalculadora", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracoesCalculadora_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracoesHonorarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NomeEscritorio = table.Column<string>(type: "text", nullable: true),
                    AdvogadoResponsavel = table.Column<string>(type: "text", nullable: true),
                    OAB = table.Column<string>(type: "text", nullable: true),
                    Endereco = table.Column<string>(type: "text", nullable: true),
                    Telefone = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    MetaMensalPadrao = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentualMultaDefault = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentualJurosMensalDefault = table.Column<decimal>(type: "numeric", nullable: false),
                    DiasAvisoVencimento = table.Column<int>(type: "integer", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesHonorarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConfiguracoesHonorarios_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContatoFiltrosSalvos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Busca = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TiposContato = table.Column<int[]>(type: "integer[]", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    ImportadoAutomaticamente = table.Column<bool>(type: "boolean", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContatoFiltrosSalvos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContatoFiltrosSalvos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContatoFiltrosSalvos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contatos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    TipoContato = table.Column<int>(type: "integer", nullable: false),
                    Nome = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CpfCnpj = table.Column<string>(type: "character varying(18)", maxLength: 18, nullable: true),
                    Oab = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Telefone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Endereco = table.Column<string>(type: "text", nullable: true),
                    Cidade = table.Column<string>(type: "text", nullable: true),
                    Estado = table.Column<string>(type: "text", nullable: true),
                    Cep = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DataNascimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    NotificacaoHabilitada = table.Column<bool>(type: "boolean", nullable: false),
                    IAHabilitada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ImportadoAutomaticamente = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contatos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contatos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConvitesUsuario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Perfil = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Usado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvitesUsuario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConvitesUsuario_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CreditosAI",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QuantidadeTotal = table.Column<int>(type: "integer", nullable: false),
                    QuantidadeUsada = table.Column<int>(type: "integer", nullable: false),
                    Origem = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditosAI", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditosAI_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Faturamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Periodo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Moeda = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DataPagamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataCriacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Faturamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Faturamentos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "feriados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Data = table.Column<DateOnly>(type: "date", nullable: false),
                    Nome = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Municipio = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feriados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_feriados_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HonorariosCalculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Cliente = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Mode = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Area = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tipo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ItemOAB = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ValorCausa = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    HonorarioBase = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    HonorarioAjustado = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalCorrigido = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Multiplicador = table.Column<decimal>(type: "numeric(8,4)", nullable: false),
                    TaxaCorrecao = table.Column<decimal>(type: "numeric(6,4)", nullable: false),
                    FormaPagamento = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    Complexidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Risco = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Urgencia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Capacidade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Exito = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StateJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HonorariosCalculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HonorariosCalculos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HonorariosCalculos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportacoesProcessos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Modo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NumeroOab = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Processados = table.Column<int>(type: "integer", nullable: false),
                    Importados = table.Column<int>(type: "integer", nullable: false),
                    JaCadastrados = table.Column<int>(type: "integer", nullable: false),
                    Erros = table.Column<int>(type: "integer", nullable: false),
                    MensagemErro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CursorEscavador = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    BuscaEscavadorConcluida = table.Column<bool>(type: "boolean", nullable: false),
                    BuscaTribunaisConcluida = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConcluidoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacoesProcessos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportacoesProcessos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModelosDocumento",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "text", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: true),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    Variaveis = table.Column<string>(type: "text", nullable: false),
                    UsarTimbrado = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelosDocumento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelosDocumento_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ModelosDocumento_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notificacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Lida = table.Column<bool>(type: "boolean", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChaveDedup = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notificacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notificacoes_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notificacoes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Pastas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ParentPastaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntidadeTipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pastas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pastas_Pastas_ParentPastaId",
                        column: x => x.ParentPastaId,
                        principalTable: "Pastas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Pastas_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreferenciasNotificacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TarefasInApp = table.Column<bool>(type: "boolean", nullable: false),
                    TarefasEmail = table.Column<bool>(type: "boolean", nullable: false),
                    TarefasAtrasadasInApp = table.Column<bool>(type: "boolean", nullable: false),
                    TarefasAtrasadasEmail = table.Column<bool>(type: "boolean", nullable: false),
                    EventosInApp = table.Column<bool>(type: "boolean", nullable: false),
                    EventosEmail = table.Column<bool>(type: "boolean", nullable: false),
                    PrazosInApp = table.Column<bool>(type: "boolean", nullable: false),
                    PrazosEmail = table.Column<bool>(type: "boolean", nullable: false),
                    PublicacoesInApp = table.Column<bool>(type: "boolean", nullable: false),
                    PublicacoesEmail = table.Column<bool>(type: "boolean", nullable: false),
                    TrialInApp = table.Column<bool>(type: "boolean", nullable: false),
                    GeralInApp = table.Column<bool>(type: "boolean", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferenciasNotificacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PreferenciasNotificacoes_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PreferenciasNotificacoes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Processos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tribunal = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Vara = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Comarca = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AreaDireito = table.Column<int>(type: "integer", nullable: false),
                    TipoAcao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Fase = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValorCausa = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    AdvogadoResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Monitorado = table.Column<bool>(type: "boolean", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    Decisao = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Resultado = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EncerradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoMonitoramento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimoAndamentoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Classe = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Assuntos = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DataAjuizamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataDistribuicao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Grau = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SiglaTribunal = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Segmento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Sistema = table.Column<string>(type: "text", nullable: true),
                    Formato = table.Column<string>(type: "text", nullable: true),
                    NivelSigilo = table.Column<int>(type: "integer", nullable: true),
                    UltimaAtualizacaoDataJud = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ementa = table.Column<string>(type: "text", nullable: true),
                    DecisaoDataJud = table.Column<string>(type: "text", nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    Relator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TipoDecisao = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResultadoJulgamento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CodigoClasse = table.Column<int>(type: "integer", nullable: true),
                    Instancia = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DataJulgamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataPublicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscavadorMonitoramentoId = table.Column<string>(type: "text", nullable: true),
                    MonitoramentoSemanal = table.Column<bool>(type: "boolean", nullable: false),
                    AndamentosPendentes = table.Column<bool>(type: "boolean", nullable: false),
                    UltimaConsultaEscavadorEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Processos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Processos_AspNetUsers_AdvogadoResponsavelId",
                        column: x => x.AdvogadoResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Processos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantOabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Uf = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Numero = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ativo = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UltimaVerificacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EscavadorMonitoramentoId = table.Column<long>(type: "bigint", nullable: true),
                    UltimoSyncEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SyncError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantOabs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantOabs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TenantOabs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Atendimentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    Data = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Atendimentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Atendimentos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Atendimentos_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContatoTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContatoTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContatoTags_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContatoVinculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoRelacionadoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Observacao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContatoVinculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContatoVinculos_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContatoVinculos_Contatos_ContatoRelacionadoId",
                        column: x => x.ContatoRelacionadoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContatoVinculos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TraducoesAndamentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AndamentoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SolicitadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    TextoOriginal = table.Column<string>(type: "text", nullable: false),
                    TextoTraduzido = table.Column<string>(type: "text", nullable: false),
                    EnviadoAoCliente = table.Column<bool>(type: "boolean", nullable: false),
                    RevisadoPreviamente = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraducoesAndamentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TraducoesAndamentos_Andamentos_AndamentoId",
                        column: x => x.AndamentoId,
                        principalTable: "Andamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TraducoesAndamentos_AspNetUsers_SolicitadoPorId",
                        column: x => x.SolicitadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TraducoesAndamentos_Contatos_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Contatos",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ImportacaoProcessoItens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tribunal = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DadosJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacaoProcessoItens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportacaoProcessoItens_ImportacoesProcessos_ImportacaoId",
                        column: x => x.ImportacaoId,
                        principalTable: "ImportacoesProcessos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ContratosHonorarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    NumeroContrato = table.Column<string>(type: "text", nullable: false),
                    Objeto = table.Column<string>(type: "text", nullable: true),
                    ValorTotal = table.Column<decimal>(type: "numeric", nullable: false),
                    FormaPagamento = table.Column<int>(type: "integer", nullable: false),
                    Periodicidade = table.Column<int>(type: "integer", nullable: true),
                    NumeroParcelas = table.Column<int>(type: "integer", nullable: true),
                    DataPrimeiraParcela = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorEntrada = table.Column<decimal>(type: "numeric", nullable: true),
                    VencimentoEntrada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PercentualMulta = table.Column<decimal>(type: "numeric", nullable: false),
                    PercentualJurosMensal = table.Column<decimal>(type: "numeric", nullable: false),
                    TipoCobranca = table.Column<string>(type: "text", nullable: false),
                    Observacoes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DistratoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DistratoMotivo = table.Column<string>(type: "text", nullable: true),
                    DataInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataFim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContratosHonorarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ContratosHonorarios_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Documentos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClienteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModeloId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nome = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "bigint", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    PastaId = table.Column<Guid>(type: "uuid", nullable: true),
                    ModeloDocumentoId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documentos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documentos_AspNetUsers_UploadedPorId",
                        column: x => x.UploadedPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documentos_Contatos_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documentos_ModelosDocumento_ModeloDocumentoId",
                        column: x => x.ModeloDocumentoId,
                        principalTable: "ModelosDocumento",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Documentos_Pastas_PastaId",
                        column: x => x.PastaId,
                        principalTable: "Pastas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documentos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Documentos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Eventos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DataHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataHoraFim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Local = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Observacoes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Eventos_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Eventos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Eventos_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PecasGeradas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    GeradoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tipo = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DescricaoSolicitacao = table.Column<string>(type: "text", nullable: false),
                    ConteudoGerado = table.Column<string>(type: "text", nullable: false),
                    JurisprudenciaCitada = table.Column<string>(type: "text", nullable: true),
                    TesesSugeridas = table.Column<string>(type: "text", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PecasGeradas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PecasGeradas_AspNetUsers_GeradoPorId",
                        column: x => x.GeradoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PecasGeradas_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProcessoPartes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoParte = table.Column<int>(type: "integer", nullable: false),
                    PortalAcessoAtivo = table.Column<bool>(type: "boolean", nullable: false),
                    PortalAcessoPendente = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessoPartes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessoPartes_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessoPartes_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessosFavoritos",
                columns: table => new
                {
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessosFavoritos", x => new { x.UsuarioId, x.ProcessoId });
                    table.ForeignKey(
                        name: "FK_ProcessosFavoritos_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProcessosFavoritos_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Publicacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    NumeroCNJ = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Diario = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DataPublicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Urgente = table.Column<bool>(type: "boolean", nullable: false),
                    ClassificacaoIA = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdExterno = table.Column<string>(type: "text", nullable: true),
                    HashDje = table.Column<string>(type: "text", nullable: true),
                    Secao = table.Column<string>(type: "text", nullable: true),
                    Pagina = table.Column<string>(type: "text", nullable: true),
                    CapturaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkEscavador = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LinkPdf = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DiarioId = table.Column<long>(type: "bigint", nullable: true),
                    DiarioSigla = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Snippet = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OrigemId = table.Column<long>(type: "bigint", nullable: true),
                    FonteCaptura = table.Column<int>(type: "integer", nullable: false),
                    UuidExterno = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tribunal = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    OrigemEstado = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Publicacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Publicacoes_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Publicacoes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecorrenciasTarefa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Frequencia = table.Column<int>(type: "integer", nullable: false),
                    Intervalo = table.Column<int>(type: "integer", nullable: false),
                    DiasSemana = table.Column<int>(type: "integer", nullable: false),
                    DiaDoMes = table.Column<int>(type: "integer", nullable: true),
                    Ancora = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TerminaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UltimaGerada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tags = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecorrenciasTarefa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecorrenciasTarefa_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResumosProcesso",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeradoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Conteudo = table.Column<string>(type: "text", nullable: false),
                    GeradoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VisivelCliente = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResumosProcesso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResumosProcesso_AspNetUsers_GeradoPorId",
                        column: x => x.GeradoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ResumosProcesso_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricosContratoHonorario",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoEvento = table.Column<int>(type: "integer", nullable: false),
                    Descricao = table.Column<string>(type: "text", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DadosAnterioresJson = table.Column<string>(type: "text", nullable: true),
                    DadosNovosJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricosContratoHonorario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricosContratoHonorario_ContratosHonorarios_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "ContratosHonorarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ParcelasHonorarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContratoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    IsEntrada = table.Column<bool>(type: "boolean", nullable: false),
                    Vencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValorOriginal = table.Column<decimal>(type: "numeric", nullable: false),
                    DataPagamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValorPago = table.Column<decimal>(type: "numeric", nullable: true),
                    Observacao = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LancamentoFinanceiroId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParcelasHonorarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParcelasHonorarios_ContratosHonorarios_ContratoId",
                        column: x => x.ContratoId,
                        principalTable: "ContratosHonorarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TriagensPublicacao",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicacaoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "integer", nullable: false),
                    Erro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessadaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Resumo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Ato = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PrazoDias = table.Column<int>(type: "integer", nullable: true),
                    TipoCalculo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrazoTrecho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AudienciaDataHora = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AudienciaTrecho = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataPublicacao = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Vencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MemoriaCalculo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TarefaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvidaPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolvidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriagensPublicacao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TriagensPublicacao_Publicacoes_PublicacaoId",
                        column: x => x.PublicacaoId,
                        principalTable: "Publicacoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tarefas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ResponsavelId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prazo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DataInicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QuantidadeDias = table.Column<int>(type: "integer", nullable: true),
                    TipoCalculo = table.Column<int>(type: "integer", nullable: true),
                    Prioridade = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    AndamentoId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecorrenciaId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcluidaEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tarefas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tarefas_AspNetUsers_CriadoPorId",
                        column: x => x.CriadoPorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tarefas_AspNetUsers_ResponsavelId",
                        column: x => x.ResponsavelId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_RecorrenciasTarefa_RecorrenciaId",
                        column: x => x.RecorrenciaId,
                        principalTable: "RecorrenciasTarefa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Tarefas_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LancamentosFinanceiros",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContatoId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContratoHonorarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParcelaHonorarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tipo = table.Column<int>(type: "integer", nullable: false),
                    Categoria = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataVencimento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DataPagamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ParcelaHonorarioId1 = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosFinanceiros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_Contatos_ContatoId",
                        column: x => x.ContatoId,
                        principalTable: "Contatos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_ContratosHonorarios_ContratoHonorari~",
                        column: x => x.ContratoHonorarioId,
                        principalTable: "ContratosHonorarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_ParcelasHonorarios_ParcelaHonorarioI~",
                        column: x => x.ParcelaHonorarioId1,
                        principalTable: "ParcelasHonorarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LancamentosFinanceiros_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RegistrosTempo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcessoId = table.Column<Guid>(type: "uuid", nullable: true),
                    TarefaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Inicio = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Fim = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DuracaoMinutos = table.Column<int>(type: "integer", nullable: true),
                    Descricao = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EmAndamento = table.Column<bool>(type: "boolean", nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrosTempo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegistrosTempo_AspNetUsers_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RegistrosTempo_Processos_ProcessoId",
                        column: x => x.ProcessoId,
                        principalTable: "Processos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RegistrosTempo_Tarefas_TarefaId",
                        column: x => x.TarefaId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RegistrosTempo_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TarefaTags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TarefaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TarefaTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TarefaTags_Tarefas_TarefaId",
                        column: x => x.TarefaId,
                        principalTable: "Tarefas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "CodigosPromocionais",
                columns: new[] { "Id", "Ativo", "AtualizadoEm", "Codigo", "CriadoEm", "CriadoPorId", "DescontoMeses", "DescontoPercentual", "Descricao", "DiasGratuitos", "MaxUsos", "Plano", "ValidoAte" },
                values: new object[] { new Guid("5f0c8a52-6d1e-4c1a-9b7e-2a4d3c1e0f01"), true, null, "primeiros20", new DateTime(2026, 9, 26, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, "30 dias do plano Plus gratuito — aproveite!", 30, 20, 3, null });

            migrationBuilder.InsertData(
                table: "PrecosPlanos",
                columns: new[] { "Plano", "AtualizadoEm", "AtualizadoPorId", "ValorMensal" },
                values: new object[,]
                {
                    { 1, null, null, 50m },
                    { 3, null, null, 20m },
                    { 4, null, null, 125m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcessosCliente_ContatoId",
                table: "AcessosCliente",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_AcessosCliente_Email",
                table: "AcessosCliente",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_AcessosCliente_TenantId_ContatoId",
                table: "AcessosCliente",
                columns: new[] { "TenantId", "ContatoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcessosCliente_TenantId_Email",
                table: "AcessosCliente",
                columns: new[] { "TenantId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcoesAssistente_InteracaoId",
                table: "AcoesAssistente",
                column: "InteracaoId");

            migrationBuilder.CreateIndex(
                name: "IX_AcoesAssistente_TenantId_CriadoEm",
                table: "AcoesAssistente",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_ProcessoId_CriadoEm",
                table: "Andamentos",
                columns: new[] { "ProcessoId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_ProcessoId_Data",
                table: "Andamentos",
                columns: new[] { "ProcessoId", "Data" });

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_RegistradoPorId",
                table: "Andamentos",
                column: "RegistradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Andamentos_TenantId",
                table: "Andamentos",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AreasAtuacao_TenantId_Ativo",
                table: "AreasAtuacao",
                columns: new[] { "TenantId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_AreasAtuacao_TenantId_Nome",
                table: "AreasAtuacao",
                columns: new[] { "TenantId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_ContatoId",
                table: "Atendimentos",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_Atendimentos_UsuarioId",
                table: "Atendimentos",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_CalculosPrazo_TenantId_CriadoEm",
                table: "CalculosPrazo",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_CalculosPrazo_UsuarioId",
                table: "CalculosPrazo",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_TenantId_Ativo",
                table: "CategoriasFinanceiras",
                columns: new[] { "TenantId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_TenantId_Nome_Tipo",
                table: "CategoriasFinanceiras",
                columns: new[] { "TenantId", "Nome", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodigosPromocionais_Codigo",
                table: "CodigosPromocionais",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesParceiros_FaturamentoId",
                table: "ComissoesParceiros",
                column: "FaturamentoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesParceiros_ParceiroId_TenantId",
                table: "ComissoesParceiros",
                columns: new[] { "ParceiroId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComissoesParceiros_SolicitacaoId",
                table: "ComissoesParceiros",
                column: "SolicitacaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesCalculadora_TenantId",
                table: "ConfiguracoesCalculadora",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfiguracoesHonorarios_TenantId",
                table: "ConfiguracoesHonorarios",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasExternas_Api",
                table: "ConsultasExternas",
                column: "Api");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasExternas_TenantId_CriadoEm",
                table: "ConsultasExternas",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsultasExternas_UsuarioId",
                table: "ConsultasExternas",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoFiltrosSalvos_TenantId_UsuarioId",
                table: "ContatoFiltrosSalvos",
                columns: new[] { "TenantId", "UsuarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContatoFiltrosSalvos_UsuarioId",
                table: "ContatoFiltrosSalvos",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Contatos_TenantId_CpfCnpj",
                table: "Contatos",
                columns: new[] { "TenantId", "CpfCnpj" });

            migrationBuilder.CreateIndex(
                name: "IX_Contatos_TenantId_Nome",
                table: "Contatos",
                columns: new[] { "TenantId", "Nome" });

            migrationBuilder.CreateIndex(
                name: "IX_ContatoTags_ContatoId",
                table: "ContatoTags",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_ContatoId",
                table: "ContatoVinculos",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_ContatoRelacionadoId",
                table: "ContatoVinculos",
                column: "ContatoRelacionadoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_TenantId_ContatoId",
                table: "ContatoVinculos",
                columns: new[] { "TenantId", "ContatoId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContatoVinculos_TenantId_ContatoRelacionadoId",
                table: "ContatoVinculos",
                columns: new[] { "TenantId", "ContatoRelacionadoId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_ContatoId",
                table: "ContratosHonorarios",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_CriadoPorId",
                table: "ContratosHonorarios",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_ProcessoId",
                table: "ContratosHonorarios",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_ContratosHonorarios_TenantId",
                table: "ContratosHonorarios",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvitesUsuario_TenantId",
                table: "ConvitesUsuario",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditosAI_TenantId_Tipo",
                table: "CreditosAI",
                columns: new[] { "TenantId", "Tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ClienteId",
                table: "Documentos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ModeloDocumentoId",
                table: "Documentos",
                column: "ModeloDocumentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_PastaId",
                table: "Documentos",
                column: "PastaId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_ProcessoId",
                table: "Documentos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ClienteId",
                table: "Documentos",
                columns: new[] { "TenantId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ContratoId",
                table: "Documentos",
                columns: new[] { "TenantId", "ContratoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ModeloId",
                table: "Documentos",
                columns: new[] { "TenantId", "ModeloId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_TenantId_ProcessoId",
                table: "Documentos",
                columns: new[] { "TenantId", "ProcessoId" });

            migrationBuilder.CreateIndex(
                name: "IX_Documentos_UploadedPorId",
                table: "Documentos",
                column: "UploadedPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_ProcessoId",
                table: "Eventos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_ResponsavelId",
                table: "Eventos",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_TenantId_DataHora",
                table: "Eventos",
                columns: new[] { "TenantId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_TenantId_Tipo",
                table: "Eventos",
                columns: new[] { "TenantId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_BillingId",
                table: "Faturamentos",
                column: "BillingId");

            migrationBuilder.CreateIndex(
                name: "IX_Faturamentos_TenantId_Status",
                table: "Faturamentos",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "idx_feriados_ativo",
                table: "feriados",
                column: "Ativo");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_data",
                table: "feriados",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_global_unique",
                table: "feriados",
                columns: new[] { "Data", "Tipo", "Uf", "Municipio" },
                unique: true,
                filter: "\"TenantId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_tenant_unique",
                table: "feriados",
                columns: new[] { "Data", "TenantId", "Uf", "Municipio" },
                unique: true,
                filter: "\"TenantId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_feriados_tipo",
                table: "feriados",
                column: "Tipo");

            migrationBuilder.CreateIndex(
                name: "IX_feriados_TenantId",
                table: "feriados",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_GruposPerguntaMembros_GrupoId_UsuarioId",
                table: "GruposPerguntaMembros",
                columns: new[] { "GrupoId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosContratoHonorario_ContratoId",
                table: "HistoricosContratoHonorario",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_HonorariosCalculos_TenantId_UsuarioId_CriadoEm",
                table: "HonorariosCalculos",
                columns: new[] { "TenantId", "UsuarioId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_HonorariosCalculos_UsuarioId",
                table: "HonorariosCalculos",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportacaoProcessoItens_ImportacaoId_Ordem",
                table: "ImportacaoProcessoItens",
                columns: new[] { "ImportacaoId", "Ordem" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportacoesProcessos_TenantId_UsuarioId_Status",
                table: "ImportacoesProcessos",
                columns: new[] { "TenantId", "UsuarioId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_IndicesCorrecaoMonetaria_Tipo_Ano_Mes",
                table: "IndicesCorrecaoMonetaria",
                columns: new[] { "Tipo", "Ano", "Mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_ConversaId",
                table: "InteracoesAssistente",
                column: "ConversaId");

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_CriadoEm",
                table: "InteracoesAssistente",
                column: "CriadoEm");

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_TenantId_CriadoEm",
                table: "InteracoesAssistente",
                columns: new[] { "TenantId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_InteracoesAssistente_UsuarioId",
                table: "InteracoesAssistente",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_ContatoId",
                table: "LancamentosFinanceiros",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_ContratoHonorarioId",
                table: "LancamentosFinanceiros",
                column: "ContratoHonorarioId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_ParcelaHonorarioId1",
                table: "LancamentosFinanceiros",
                column: "ParcelaHonorarioId1");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_ProcessoId",
                table: "LancamentosFinanceiros",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_TenantId_DataVencimento",
                table: "LancamentosFinanceiros",
                columns: new[] { "TenantId", "DataVencimento" });

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosFinanceiros_TenantId_Status",
                table: "LancamentosFinanceiros",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelosDocumento_CriadoPorId",
                table: "ModelosDocumento",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelosDocumento_TenantId",
                table: "ModelosDocumento",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notificacoes_ChaveDedup",
                table: "Notificacoes",
                column: "ChaveDedup",
                unique: true,
                filter: "\"ChaveDedup\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notificacoes_TenantId",
                table: "Notificacoes",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Notificacoes_UsuarioId_Lida_CriadaEm",
                table: "Notificacoes",
                columns: new[] { "UsuarioId", "Lida", "CriadaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Novidades_Publicada_PublicadaEm",
                table: "Novidades",
                columns: new[] { "Publicada", "PublicadaEm" });

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_Codigo",
                table: "Parceiros",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_Email",
                table: "Parceiros",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parceiros_TokenSenha",
                table: "Parceiros",
                column: "TokenSenha");

            migrationBuilder.CreateIndex(
                name: "IX_ParcelasHonorarios_ContratoId",
                table: "ParcelasHonorarios",
                column: "ContratoId");

            migrationBuilder.CreateIndex(
                name: "IX_Pastas_ParentPastaId",
                table: "Pastas",
                column: "ParentPastaId");

            migrationBuilder.CreateIndex(
                name: "IX_Pastas_TenantId_EntidadeTipo_ParentPastaId",
                table: "Pastas",
                columns: new[] { "TenantId", "EntidadeTipo", "ParentPastaId" });

            migrationBuilder.CreateIndex(
                name: "IX_Pastas_TenantId_EntidadeTipo_ParentPastaId_Nome",
                table: "Pastas",
                columns: new[] { "TenantId", "EntidadeTipo", "ParentPastaId", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_GeradoPorId",
                table: "PecasGeradas",
                column: "GeradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_ProcessoId",
                table: "PecasGeradas",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_TenantId",
                table: "PecasGeradas",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_TenantId_ProcessoId",
                table: "PecasGeradas",
                columns: new[] { "TenantId", "ProcessoId" });

            migrationBuilder.CreateIndex(
                name: "IX_PecasGeradas_TenantId_Tipo",
                table: "PecasGeradas",
                columns: new[] { "TenantId", "Tipo" });

            migrationBuilder.CreateIndex(
                name: "IX_Perguntas_Ativa_Publico",
                table: "Perguntas",
                columns: new[] { "Ativa", "Publico" });

            migrationBuilder.CreateIndex(
                name: "IX_PreferenciasNotificacoes_TenantId_UsuarioId",
                table: "PreferenciasNotificacoes",
                columns: new[] { "TenantId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PreferenciasNotificacoes_UsuarioId",
                table: "PreferenciasNotificacoes",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessoPartes_ContatoId",
                table: "ProcessoPartes",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessoPartes_ProcessoId_ContatoId_TipoParte",
                table: "ProcessoPartes",
                columns: new[] { "ProcessoId", "ContatoId", "TipoParte" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processos_AdvogadoResponsavelId",
                table: "Processos",
                column: "AdvogadoResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Processos_EscavadorMonitoramentoId",
                table: "Processos",
                column: "EscavadorMonitoramentoId",
                filter: "\"EscavadorMonitoramentoId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Processos_TenantId_NumeroCNJ",
                table: "Processos",
                columns: new[] { "TenantId", "NumeroCNJ" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Processos_TenantId_Status",
                table: "Processos",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosFavoritos_ProcessoId",
                table: "ProcessosFavoritos",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosImportacaoCache_ExpiraEm",
                table: "ProcessosImportacaoCache",
                column: "ExpiraEm");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessosImportacaoCache_TenantId_NumeroCNJ_Fonte",
                table: "ProcessosImportacaoCache",
                columns: new[] { "TenantId", "NumeroCNJ", "Fonte" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_ProcessoId",
                table: "Publicacoes",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_DataPublicacao",
                table: "Publicacoes",
                columns: new[] { "TenantId", "DataPublicacao" });

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_FonteCaptura",
                table: "Publicacoes",
                columns: new[] { "TenantId", "FonteCaptura" });

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_Status",
                table: "Publicacoes",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Publicacoes_TenantId_UuidExterno",
                table: "Publicacoes",
                columns: new[] { "TenantId", "UuidExterno" },
                unique: true,
                filter: "\"UuidExterno\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_Ativa_UltimaGerada",
                table: "RecorrenciasTarefa",
                columns: new[] { "Ativa", "UltimaGerada" });

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_ContatoId",
                table: "RecorrenciasTarefa",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_CriadoPorId",
                table: "RecorrenciasTarefa",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_ProcessoId",
                table: "RecorrenciasTarefa",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_ResponsavelId",
                table: "RecorrenciasTarefa",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_RecorrenciasTarefa_TenantId",
                table: "RecorrenciasTarefa",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ImpersonadoPorId",
                table: "RefreshTokens",
                column: "ImpersonadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UsuarioId",
                table: "RefreshTokens",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosTempo_ProcessoId",
                table: "RegistrosTempo",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosTempo_TarefaId",
                table: "RegistrosTempo",
                column: "TarefaId");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosTempo_TenantId_EmAndamento",
                table: "RegistrosTempo",
                columns: new[] { "TenantId", "EmAndamento" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosTempo_TenantId_UsuarioId",
                table: "RegistrosTempo",
                columns: new[] { "TenantId", "UsuarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrosTempo_UsuarioId",
                table: "RegistrosTempo",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_RespostasPerguntas_PerguntaId_RespondenteTipo_RespondenteId",
                table: "RespostasPerguntas",
                columns: new[] { "PerguntaId", "RespondenteTipo", "RespondenteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_GeradoPorId",
                table: "ResumosProcesso",
                column: "GeradoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_ProcessoId",
                table: "ResumosProcesso",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_TenantId",
                table: "ResumosProcesso",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ResumosProcesso_TenantId_ProcessoId",
                table: "ResumosProcesso",
                columns: new[] { "TenantId", "ProcessoId" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitacoesPagamentoParceiros_ParceiroId_Status",
                table: "SolicitacoesPagamentoParceiros",
                columns: new[] { "ParceiroId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ContatoId",
                table: "Tarefas",
                column: "ContatoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_CriadoPorId",
                table: "Tarefas",
                column: "CriadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ProcessoId",
                table: "Tarefas",
                column: "ProcessoId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_RecorrenciaId",
                table: "Tarefas",
                column: "RecorrenciaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_ResponsavelId",
                table: "Tarefas",
                column: "ResponsavelId");

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_TenantId_Prazo",
                table: "Tarefas",
                columns: new[] { "TenantId", "Prazo" });

            migrationBuilder.CreateIndex(
                name: "IX_Tarefas_TenantId_Status",
                table: "Tarefas",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TarefaTags_TarefaId_Tag",
                table: "TarefaTags",
                columns: new[] { "TarefaId", "Tag" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_TenantId_Ativo",
                table: "TenantOabs",
                columns: new[] { "TenantId", "Ativo" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_TenantId_EscavadorMonitoramentoId",
                table: "TenantOabs",
                columns: new[] { "TenantId", "EscavadorMonitoramentoId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_TenantId_Uf_Numero",
                table: "TenantOabs",
                columns: new[] { "TenantId", "Uf", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantOabs_UserId",
                table: "TenantOabs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Cnpj",
                table: "Tenants",
                column: "Cnpj",
                unique: true,
                filter: "\"Cnpj\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_ParceiroId",
                table: "Tenants",
                column: "ParceiroId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_TrialConcedidoPorId",
                table: "Tenants",
                column: "TrialConcedidoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_AndamentoId",
                table: "TraducoesAndamentos",
                column: "AndamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_ClienteId",
                table: "TraducoesAndamentos",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_SolicitadoPorId",
                table: "TraducoesAndamentos",
                column: "SolicitadoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_TenantId_AndamentoId",
                table: "TraducoesAndamentos",
                columns: new[] { "TenantId", "AndamentoId" });

            migrationBuilder.CreateIndex(
                name: "IX_TraducoesAndamentos_TenantId_ClienteId",
                table: "TraducoesAndamentos",
                columns: new[] { "TenantId", "ClienteId" });

            migrationBuilder.CreateIndex(
                name: "IX_TriagensPublicacao_PublicacaoId",
                table: "TriagensPublicacao",
                column: "PublicacaoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TriagensPublicacao_Status_CriadoEm",
                table: "TriagensPublicacao",
                columns: new[] { "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_TriagensPublicacao_TenantId_Status",
                table: "TriagensPublicacao",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_AcessosCliente_Contatos_ContatoId",
                table: "AcessosCliente",
                column: "ContatoId",
                principalTable: "Contatos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AcessosCliente_Tenants_TenantId",
                table: "AcessosCliente",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Andamentos_AspNetUsers_RegistradoPorId",
                table: "Andamentos",
                column: "RegistradoPorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Andamentos_Processos_ProcessoId",
                table: "Andamentos",
                column: "ProcessoId",
                principalTable: "Processos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AreasAtuacao_Tenants_TenantId",
                table: "AreasAtuacao",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                table: "AspNetUserClaims",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                table: "AspNetUserLogins",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                table: "AspNetUserRoles",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Dados de referência que as migrations antigas semeavam (feriados e novidades).
            migrationBuilder.Sql("""
                INSERT INTO "Novidades" ("Id", "Titulo", "Descricao", "ImagemUrl", "LinkUrl", "LinkTexto", "PlanoMinimo", "Destaque", "TourId", "Publicada", "PublicadaEm", "CriadoEm", "CriadoPorId", "AtualizadoEm") VALUES
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000001', 'Modelos de documento com papel timbrado', 'A aba **Modelos** em Documentos foi refeita para você gerar peças em poucos cliques:
                - Editor com pré-visualização e as variáveis detectadas enquanto você digita
                - Preencha a partir de um processo e baixe em Word, imprima ou salve direto no processo
                - **Papel timbrado** do escritório (logo, nome, CNPJ, endereço e contatos) em todas as páginas
                - Novos exemplos prontos: Substabelecimento, Notificação Extrajudicial e Recibo de Honorários', NULL, '/pages/documentos.html', 'Abrir Documentos', 3, false, 'novidade-modelos', true, '2026-09-28 15:00:00+00', '2026-09-28 15:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000002', 'Filtre contatos por várias categorias e tags', 'Agora dá para marcar **várias categorias e tags** ao mesmo tempo no filtro de Contatos: aparecem os contatos com qualquer uma delas.
                A combinação também pode ser guardada nos **filtros salvos**, para usar de novo com um clique.', NULL, '/pages/contatos.html', 'Abrir Contatos', NULL, false, 'novidade-contatos', true, '2026-09-28 16:00:00+00', '2026-09-28 16:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000003', 'Feriados de 2027 na calculadora de prazos', 'Os feriados nacionais e estaduais de 2027 já estão cadastrados, então os prazos que atravessam a virada do ano são contados corretamente.', NULL, '/pages/calculadora-prazos.html', 'Calcular um prazo', 3, false, NULL, true, '2026-09-28 17:00:00+00', '2026-09-28 17:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000004', 'Importação por OAB em segundo plano', 'A importação pela sua OAB agora traz **todos** os seus processos e roda em segundo plano: você continua usando o sistema enquanto uma barra no rodapé mostra o progresso, e recebe um aviso quando terminar.
                Os andamentos de cada processo são buscados na primeira vez que você o abre. Até lá, a listagem mostra ⏳ no lugar da contagem.', NULL, '/pages/processos.html', 'Ir para Processos', NULL, false, NULL, true, '2026-09-29 15:00:00+00', '2026-09-29 15:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000005', 'Monte o dashboard do seu jeito', 'Os **indicadores** agora ficam no próprio dashboard: Resumo de tarefas e Timesheet do mês já aparecem, e você pode ligar Processos por fase e Financeiro do mês em **⚙️ Personalizar**.
                No modo Personalizar também dá para escolher a **altura de cada bloco** (½, 1 ou 2) e encaixar dois blocos menores no espaço de um.
                _Indicadores disponíveis a partir do plano Plus._', NULL, '/pages/dashboard.html', 'Ir para o dashboard', NULL, false, 'novidade-dashboard', true, '2026-10-07 16:00:00+00', '2026-10-07 16:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000006', 'Ache o contato pelo nome ao adicionar partes', 'Ao cadastrar um processo ou adicionar uma parte, digite o **nome, CPF/CNPJ ou e-mail** e escolha o contato na lista, sem rolar uma lista enorme. Se a pessoa ainda não estiver cadastrada, o atalho para criar o contato aparece ali mesmo.', NULL, '/pages/processos.html', 'Ir para Processos', NULL, false, NULL, true, '2026-10-07 17:00:00+00', '2026-10-07 17:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000007', 'Marque seus processos favoritos ⭐', 'Clique na **estrela ☆** de um processo para marcá-lo como favorito. Os favoritos aparecem primeiro na listagem, têm um filtro próprio (**★ Favoritos**) e ganham um bloco no dashboard.
                Cada pessoa do escritório tem os seus.', NULL, '/pages/processos.html', 'Ver meus processos', NULL, true, 'novidade-favoritos', true, '2026-10-07 18:00:00+00', '2026-10-07 18:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000008', 'Conheça o Assistente de IA ✨', 'Pergunte em linguagem natural e o assistente consulta os dados do seu escritório para responder:
                - **Prazos e tarefas**: o que vence esta semana, o que está atrasado, o que cada pessoa tem para fazer
                - **Agenda**: audiências, reuniões e perícias dos próximos dias
                - **Processos e contatos**: resumo de um processo, partes, últimos andamentos, clientes por tag
                - **Financeiro e honorários**: resumo do mês, clientes em atraso, parcelas de um contrato
                As respostas trazem links para abrir o registro no sistema. Use o botão **✨ Assistente** no canto inferior direito; dá para maximizar a janela para ver tabelas maiores e retomar conversas anteriores em 🕘.
                _O assistente só consulta dados — não cria nem altera nada. Confira as informações importantes nos registros._', NULL, '/pages/dashboard.html?assistente=1', 'Abrir o assistente', 3, true, 'novidade-assistente', true, '2026-10-09 15:00:00+00', '2026-10-09 15:00:00+00', NULL, NULL),
                	('0b7f3c1e-2a4d-4e5f-9a10-000000000009', 'Verificação em duas etapas 🔒', 'Agora você pode exigir, além da senha, um **código de 6 dígitos gerado no seu celular** para entrar no Causify. Mesmo que alguém descubra sua senha, não consegue acessar a conta — e os dados dos seus clientes.
                - Funciona com Google Authenticator, Microsoft Authenticator, Authy, 1Password e similares
                - Para ativar: **Configurações → Verificação em duas etapas**, escaneie o QR code e confirme o código
                - Você recebe **10 códigos de recuperação** para entrar se perder o celular — guarde-os num lugar seguro
                _É opcional e vale por usuário: cada pessoa do escritório ativa na própria conta. Quem entra com o Google continua usando a proteção da conta Google._', NULL, '/pages/configuracoes.html#mfa', 'Ativar agora', NULL, true, NULL, true, NOW(), NOW(), NULL, NULL);
                INSERT INTO feriados ("Id", "Data", "Nome", "Tipo", "Uf", "Municipio", "Ativo", "CriadoEm", "AtualizadoEm", "TenantId") VALUES
                	(1, '2025-01-01', 'Confraternização Universal', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(2, '2025-04-18', 'Sexta-feira Santa', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(3, '2025-04-21', 'Tiradentes', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(4, '2025-05-01', 'Dia do Trabalho', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(5, '2025-06-19', 'Corpus Christi', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(6, '2025-09-07', 'Independência do Brasil', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(7, '2025-10-12', 'Nossa Senhora Aparecida', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(8, '2025-11-02', 'Finados', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(9, '2025-11-15', 'Proclamação da República', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(10, '2025-11-20', 'Consciência Negra', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(11, '2025-12-25', 'Natal', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(12, '2026-01-01', 'Confraternização Universal', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(13, '2026-04-03', 'Sexta-feira Santa', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(14, '2026-04-21', 'Tiradentes', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(15, '2026-05-01', 'Dia do Trabalho', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(16, '2026-06-04', 'Corpus Christi', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(17, '2026-09-07', 'Independência do Brasil', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(18, '2026-10-12', 'Nossa Senhora Aparecida', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(19, '2026-11-02', 'Finados', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(20, '2026-11-15', 'Proclamação da República', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(21, '2026-11-20', 'Consciência Negra', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(22, '2026-12-25', 'Natal', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(23, '2025-01-04', 'Criação do Estado de Rondônia', 'estadual', 'RO', NULL, true, NOW(), NOW(), NULL),
                	(24, '2025-03-06', 'Revolução Pernambucana', 'estadual', 'PE', NULL, true, NOW(), NOW(), NULL),
                	(25, '2025-03-25', 'Data Magna do Ceará', 'estadual', 'CE', NULL, true, NOW(), NOW(), NULL),
                	(26, '2025-06-15', 'Aniversário do Estado do Acre', 'estadual', 'AC', NULL, true, NOW(), NOW(), NULL),
                	(27, '2025-07-02', 'Independência da Bahia', 'estadual', 'BA', NULL, true, NOW(), NOW(), NULL),
                	(28, '2025-07-09', 'Revolução Constitucionalista de 1932', 'estadual', 'SP', NULL, true, NOW(), NOW(), NULL),
                	(29, '2025-07-26', 'Fundação da Cidade de Goiás', 'estadual', 'GO', NULL, true, NOW(), NOW(), NULL),
                	(30, '2025-07-28', 'Adesão do Maranhão à Independência do Brasil', 'estadual', 'MA', NULL, true, NOW(), NOW(), NULL),
                	(31, '2025-08-15', 'Adesão do Grão-Pará à Independência do Brasil', 'estadual', 'PA', NULL, true, NOW(), NOW(), NULL),
                	(32, '2025-09-05', 'Elevação à Categoria de Estado do Amazonas', 'estadual', 'AM', NULL, true, NOW(), NOW(), NULL),
                	(33, '2025-09-16', 'Emancipação Política de Alagoas', 'estadual', 'AL', NULL, true, NOW(), NOW(), NULL),
                	(34, '2025-09-20', 'Proclamação da República Rio-Grandense', 'estadual', 'RS', NULL, true, NOW(), NOW(), NULL),
                	(35, '2025-10-03', 'Mártires de Cunhaú e Uruaçu', 'estadual', 'RN', NULL, true, NOW(), NOW(), NULL),
                	(36, '2025-10-05', 'Criação do Estado do Amapá', 'estadual', 'AP', NULL, true, NOW(), NOW(), NULL),
                	(37, '2025-10-05', 'Criação do Estado de Roraima', 'estadual', 'RR', NULL, true, NOW(), NOW(), NULL),
                	(38, '2025-10-05', 'Criação do Estado do Tocantins', 'estadual', 'TO', NULL, true, NOW(), NOW(), NULL),
                	(39, '2025-10-11', 'Criação do Estado de Mato Grosso do Sul', 'estadual', 'MS', NULL, true, NOW(), NOW(), NULL),
                	(40, '2025-10-19', 'Dia do Piauí', 'estadual', 'PI', NULL, true, NOW(), NOW(), NULL),
                	(41, '2025-12-19', 'Emancipação Política do Paraná', 'estadual', 'PR', NULL, true, NOW(), NOW(), NULL),
                	(42, '2026-01-04', 'Criação do Estado de Rondônia', 'estadual', 'RO', NULL, true, NOW(), NOW(), NULL),
                	(43, '2026-03-06', 'Revolução Pernambucana', 'estadual', 'PE', NULL, true, NOW(), NOW(), NULL),
                	(44, '2026-03-25', 'Data Magna do Ceará', 'estadual', 'CE', NULL, true, NOW(), NOW(), NULL),
                	(45, '2026-06-15', 'Aniversário do Estado do Acre', 'estadual', 'AC', NULL, true, NOW(), NOW(), NULL),
                	(46, '2026-07-02', 'Independência da Bahia', 'estadual', 'BA', NULL, true, NOW(), NOW(), NULL),
                	(47, '2026-07-09', 'Revolução Constitucionalista de 1932', 'estadual', 'SP', NULL, true, NOW(), NOW(), NULL),
                	(48, '2026-07-26', 'Fundação da Cidade de Goiás', 'estadual', 'GO', NULL, true, NOW(), NOW(), NULL),
                	(49, '2026-07-28', 'Adesão do Maranhão à Independência do Brasil', 'estadual', 'MA', NULL, true, NOW(), NOW(), NULL),
                	(50, '2026-08-15', 'Adesão do Grão-Pará à Independência do Brasil', 'estadual', 'PA', NULL, true, NOW(), NOW(), NULL),
                	(51, '2026-09-05', 'Elevação à Categoria de Estado do Amazonas', 'estadual', 'AM', NULL, true, NOW(), NOW(), NULL),
                	(52, '2026-09-16', 'Emancipação Política de Alagoas', 'estadual', 'AL', NULL, true, NOW(), NOW(), NULL),
                	(53, '2026-09-20', 'Proclamação da República Rio-Grandense', 'estadual', 'RS', NULL, true, NOW(), NOW(), NULL),
                	(54, '2026-10-03', 'Mártires de Cunhaú e Uruaçu', 'estadual', 'RN', NULL, true, NOW(), NOW(), NULL),
                	(55, '2026-10-05', 'Criação do Estado do Amapá', 'estadual', 'AP', NULL, true, NOW(), NOW(), NULL),
                	(56, '2026-10-05', 'Criação do Estado de Roraima', 'estadual', 'RR', NULL, true, NOW(), NOW(), NULL),
                	(57, '2026-10-05', 'Criação do Estado do Tocantins', 'estadual', 'TO', NULL, true, NOW(), NOW(), NULL),
                	(58, '2026-10-11', 'Criação do Estado de Mato Grosso do Sul', 'estadual', 'MS', NULL, true, NOW(), NOW(), NULL),
                	(59, '2026-10-19', 'Dia do Piauí', 'estadual', 'PI', NULL, true, NOW(), NOW(), NULL),
                	(60, '2026-12-19', 'Emancipação Política do Paraná', 'estadual', 'PR', NULL, true, NOW(), NOW(), NULL),
                	(61, '2025-01-20', 'São Sebastião', 'estadual', 'RJ', NULL, true, NOW(), NOW(), NULL),
                	(62, '2025-04-23', 'São Jorge', 'estadual', 'RJ', NULL, true, NOW(), NOW(), NULL),
                	(63, '2025-07-08', 'Emancipação Política de Sergipe', 'estadual', 'SE', NULL, true, NOW(), NOW(), NULL),
                	(64, '2025-08-11', 'Criação da Província de Santa Catarina', 'estadual', 'SC', NULL, true, NOW(), NOW(), NULL),
                	(65, '2025-08-24', 'Dia da Inconfidência Mineira', 'estadual', 'MG', NULL, true, NOW(), NOW(), NULL),
                	(66, '2026-01-20', 'São Sebastião', 'estadual', 'RJ', NULL, true, NOW(), NOW(), NULL),
                	(67, '2026-04-23', 'São Jorge', 'estadual', 'RJ', NULL, true, NOW(), NOW(), NULL),
                	(68, '2026-07-08', 'Emancipação Política de Sergipe', 'estadual', 'SE', NULL, true, NOW(), NOW(), NULL),
                	(69, '2026-08-11', 'Criação da Província de Santa Catarina', 'estadual', 'SC', NULL, true, NOW(), NOW(), NULL),
                	(70, '2026-08-24', 'Dia da Inconfidência Mineira', 'estadual', 'MG', NULL, true, NOW(), NOW(), NULL),
                	(71, '2025-04-28', 'Nossa Senhora da Penha', 'estadual', 'ES', NULL, true, NOW(), NOW(), NULL),
                	(72, '2025-07-26', 'João Pessoa', 'estadual', 'PB', NULL, true, NOW(), NOW(), NULL),
                	(73, '2025-08-05', 'Fundação da Paraíba', 'estadual', 'PB', NULL, true, NOW(), NOW(), NULL),
                	(75, '2026-07-26', 'João Pessoa', 'estadual', 'PB', NULL, true, NOW(), NOW(), NULL),
                	(76, '2026-08-05', 'Fundação da Paraíba', 'estadual', 'PB', NULL, true, NOW(), NOW(), NULL),
                	(74, '2026-04-13', 'Nossa Senhora da Penha', 'estadual', 'ES', NULL, true, NOW(), NOW(), NULL),
                	(77, '2027-01-01', 'Confraternização Universal', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(78, '2027-03-26', 'Sexta-feira Santa', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(79, '2027-04-21', 'Tiradentes', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(80, '2027-05-01', 'Dia do Trabalho', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(81, '2027-05-27', 'Corpus Christi', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(82, '2027-09-07', 'Independência do Brasil', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(83, '2027-10-12', 'Nossa Senhora Aparecida', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(84, '2027-11-02', 'Finados', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(85, '2027-11-15', 'Proclamação da República', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(86, '2027-11-20', 'Consciência Negra', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(87, '2027-12-25', 'Natal', 'nacional', NULL, NULL, true, NOW(), NOW(), NULL),
                	(88, '2027-01-04', 'Criação do Estado de Rondônia', 'estadual', 'RO', NULL, true, NOW(), NOW(), NULL),
                	(89, '2027-01-20', 'São Sebastião', 'estadual', 'RJ', NULL, true, NOW(), NOW(), NULL),
                	(90, '2027-03-06', 'Revolução Pernambucana', 'estadual', 'PE', NULL, true, NOW(), NOW(), NULL),
                	(91, '2027-03-25', 'Data Magna do Ceará', 'estadual', 'CE', NULL, true, NOW(), NOW(), NULL),
                	(92, '2027-04-05', 'Nossa Senhora da Penha', 'estadual', 'ES', NULL, true, NOW(), NOW(), NULL),
                	(93, '2027-04-23', 'São Jorge', 'estadual', 'RJ', NULL, true, NOW(), NOW(), NULL),
                	(94, '2027-06-15', 'Aniversário do Estado do Acre', 'estadual', 'AC', NULL, true, NOW(), NOW(), NULL),
                	(95, '2027-07-02', 'Independência da Bahia', 'estadual', 'BA', NULL, true, NOW(), NOW(), NULL),
                	(96, '2027-07-08', 'Emancipação Política de Sergipe', 'estadual', 'SE', NULL, true, NOW(), NOW(), NULL),
                	(97, '2027-07-09', 'Revolução Constitucionalista de 1932', 'estadual', 'SP', NULL, true, NOW(), NOW(), NULL),
                	(98, '2027-07-26', 'Fundação da Cidade de Goiás', 'estadual', 'GO', NULL, true, NOW(), NOW(), NULL),
                	(99, '2027-07-26', 'João Pessoa', 'estadual', 'PB', NULL, true, NOW(), NOW(), NULL),
                	(100, '2027-07-28', 'Adesão do Maranhão à Independência do Brasil', 'estadual', 'MA', NULL, true, NOW(), NOW(), NULL),
                	(101, '2027-08-05', 'Fundação da Paraíba', 'estadual', 'PB', NULL, true, NOW(), NOW(), NULL),
                	(102, '2027-08-11', 'Criação da Província de Santa Catarina', 'estadual', 'SC', NULL, true, NOW(), NOW(), NULL),
                	(103, '2027-08-15', 'Adesão do Grão-Pará à Independência do Brasil', 'estadual', 'PA', NULL, true, NOW(), NOW(), NULL),
                	(104, '2027-08-24', 'Dia da Inconfidência Mineira', 'estadual', 'MG', NULL, true, NOW(), NOW(), NULL),
                	(105, '2027-09-05', 'Elevação à Categoria de Estado do Amazonas', 'estadual', 'AM', NULL, true, NOW(), NOW(), NULL),
                	(106, '2027-09-16', 'Emancipação Política de Alagoas', 'estadual', 'AL', NULL, true, NOW(), NOW(), NULL),
                	(107, '2027-09-20', 'Proclamação da República Rio-Grandense', 'estadual', 'RS', NULL, true, NOW(), NOW(), NULL),
                	(108, '2027-10-03', 'Mártires de Cunhaú e Uruaçu', 'estadual', 'RN', NULL, true, NOW(), NOW(), NULL),
                	(109, '2027-10-05', 'Criação do Estado do Amapá', 'estadual', 'AP', NULL, true, NOW(), NOW(), NULL),
                	(110, '2027-10-05', 'Criação do Estado de Roraima', 'estadual', 'RR', NULL, true, NOW(), NOW(), NULL),
                	(111, '2027-10-05', 'Criação do Estado do Tocantins', 'estadual', 'TO', NULL, true, NOW(), NOW(), NULL),
                	(112, '2027-10-11', 'Criação do Estado de Mato Grosso do Sul', 'estadual', 'MS', NULL, true, NOW(), NOW(), NULL),
                	(113, '2027-10-19', 'Dia do Piauí', 'estadual', 'PI', NULL, true, NOW(), NOW(), NULL),
                	(114, '2027-12-19', 'Emancipação Política do Paraná', 'estadual', 'PR', NULL, true, NOW(), NOW(), NULL);

                SELECT setval(pg_get_serial_sequence('feriados', 'Id'), (SELECT MAX("Id") FROM feriados));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "AcessosCliente");

            migrationBuilder.DropTable(
                name: "AcoesAssistente");

            migrationBuilder.DropTable(
                name: "AreasAtuacao");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Atendimentos");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "CalculosPrazo");

            migrationBuilder.DropTable(
                name: "CategoriasFinanceiras");

            migrationBuilder.DropTable(
                name: "CodigosPromocionais");

            migrationBuilder.DropTable(
                name: "ComissoesParceiros");

            migrationBuilder.DropTable(
                name: "ConfiguracoesCalculadora");

            migrationBuilder.DropTable(
                name: "ConfiguracoesHonorarios");

            migrationBuilder.DropTable(
                name: "ConsultasExternas");

            migrationBuilder.DropTable(
                name: "ContatoFiltrosSalvos");

            migrationBuilder.DropTable(
                name: "ContatoTags");

            migrationBuilder.DropTable(
                name: "ContatoVinculos");

            migrationBuilder.DropTable(
                name: "ConvitesUsuario");

            migrationBuilder.DropTable(
                name: "CreditosAI");

            migrationBuilder.DropTable(
                name: "Documentos");

            migrationBuilder.DropTable(
                name: "Eventos");

            migrationBuilder.DropTable(
                name: "Faturamentos");

            migrationBuilder.DropTable(
                name: "feriados");

            migrationBuilder.DropTable(
                name: "GruposPergunta");

            migrationBuilder.DropTable(
                name: "GruposPerguntaMembros");

            migrationBuilder.DropTable(
                name: "HistoricosContratoHonorario");

            migrationBuilder.DropTable(
                name: "HonorariosCalculos");

            migrationBuilder.DropTable(
                name: "ImportacaoProcessoItens");

            migrationBuilder.DropTable(
                name: "IndicesCorrecaoMonetaria");

            migrationBuilder.DropTable(
                name: "InteracoesAssistente");

            migrationBuilder.DropTable(
                name: "LancamentosFinanceiros");

            migrationBuilder.DropTable(
                name: "Notificacoes");

            migrationBuilder.DropTable(
                name: "Novidades");

            migrationBuilder.DropTable(
                name: "PecasGeradas");

            migrationBuilder.DropTable(
                name: "Perguntas");

            migrationBuilder.DropTable(
                name: "PrecosPlanos");

            migrationBuilder.DropTable(
                name: "PreferenciasNotificacoes");

            migrationBuilder.DropTable(
                name: "ProcessoPartes");

            migrationBuilder.DropTable(
                name: "ProcessosFavoritos");

            migrationBuilder.DropTable(
                name: "ProcessosImportacaoCache");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RegistrosTempo");

            migrationBuilder.DropTable(
                name: "RespostasPerguntas");

            migrationBuilder.DropTable(
                name: "ResumosProcesso");

            migrationBuilder.DropTable(
                name: "TarefaTags");

            migrationBuilder.DropTable(
                name: "TenantOabs");

            migrationBuilder.DropTable(
                name: "TraducoesAndamentos");

            migrationBuilder.DropTable(
                name: "TriagensPublicacao");

            migrationBuilder.DropTable(
                name: "WaitlistEntries");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "SolicitacoesPagamentoParceiros");

            migrationBuilder.DropTable(
                name: "ModelosDocumento");

            migrationBuilder.DropTable(
                name: "Pastas");

            migrationBuilder.DropTable(
                name: "ImportacoesProcessos");

            migrationBuilder.DropTable(
                name: "ParcelasHonorarios");

            migrationBuilder.DropTable(
                name: "Tarefas");

            migrationBuilder.DropTable(
                name: "Andamentos");

            migrationBuilder.DropTable(
                name: "Publicacoes");

            migrationBuilder.DropTable(
                name: "ContratosHonorarios");

            migrationBuilder.DropTable(
                name: "RecorrenciasTarefa");

            migrationBuilder.DropTable(
                name: "Contatos");

            migrationBuilder.DropTable(
                name: "Processos");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Parceiros");
        }
    }
}
