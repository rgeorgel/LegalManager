using LegalManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LegalManager.Infrastructure.Persistence.Configurations;

public class AnuncioProprioConfiguration : IEntityTypeConfiguration<AnuncioProprio>
{
    public void Configure(EntityTypeBuilder<AnuncioProprio> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Icone).HasMaxLength(16);
        builder.Property(a => a.Titulo).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Texto).HasMaxLength(200).IsRequired();
        builder.Property(a => a.TextoBotao).HasMaxLength(30).IsRequired();
        builder.Property(a => a.LinkUrl).HasMaxLength(300).IsRequired();
        builder.Property(a => a.Cor).HasMaxLength(7);
        builder.HasIndex(a => a.Ativo);

        // Anúncios iniciais: os recursos que o plano Free não tem, todos levando à assinatura.
        var criadoEm = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);
        AnuncioProprio Inicial(string id, string cor, string icone, string titulo, string texto, string botao) => new()
        {
            Id = Guid.Parse(id), Cor = cor, Icone = icone, Titulo = titulo, Texto = texto, TextoBotao = botao,
            LinkUrl = "/pages/assinatura.html", Ativo = true, CriadoEm = criadoEm,
        };
        builder.HasData(
            Inicial("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b01", "#1e3a8a", "🚫", "Trabalhe sem anúncios",
                "Nos planos pagos do Causify não há publicidade: só você e os seus processos.", "Ver planos"),
            Inicial("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b02", "#6d28d9", "📰", "Publicações sem abrir o Diário",
                "No plano Pro, o Causify captura as publicações da sua OAB e você não perde nenhum prazo.", "Conhecer o Pro"),
            Inicial("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b03", "#0f766e", "🗂️", "Tarefas em Kanban",
                "Organize as tarefas do escritório em colunas e arraste os cartões conforme o trabalho avança. A partir do Plus.", "Conhecer o Plus"),
            Inicial("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b04", "#fbbf24", "💰", "Financeiro por processo",
                "Lance honorários, custas e recebimentos em cada processo e saiba quanto o escritório tem a receber. A partir do Plus.", "Conhecer o Plus"),
            Inicial("6b1f3a52-8c1e-4d7a-9f0b-2a5c7e9d1b05", "#be185d", "🤝", "Portal do cliente",
                "Dê ao cliente um acesso para acompanhar os processos dele, sem ligações perguntando o andamento. A partir do Plus.", "Conhecer o Plus"));
    }
}
