using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace LegalManager.UnitTests;

/// <summary>
/// Em record, atributo de validação com [property:] no parâmetro do construtor faz o ASP.NET lançar
/// InvalidOperationException ao validar o corpo — o endpoint responde 500 em toda chamada
/// (aconteceu no "Criar prazo" da sugestão da IA). O atributo tem de ficar no parâmetro.
/// </summary>
public class ValidacaoRecordsTests
{
    [Fact]
    public void NenhumRecordTemValidacaoNaPropriedadeDoConstrutor()
    {
        var assemblies = new[]
        {
            typeof(LegalManager.Application.DTOs.Publicacoes.AceitarTriagemDto).Assembly,
            typeof(LegalManager.API.Controllers.PublicacoesController).Assembly
        };

        var erros = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.GetMethod("<Clone>$") != null)
            .SelectMany(t => t.GetConstructors()
                .Where(c => c.GetParameters().Length > 0)
                .SelectMany(c => c.GetParameters())
                .Select(p => t.GetProperty(p.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(prop => prop?.GetCustomAttributes<ValidationAttribute>(inherit: false).Any() == true)
                .Select(prop => $"{t.FullName}.{prop!.Name}"))
            .Distinct()
            .ToList();

        Assert.True(erros.Count == 0, "Validação em [property:] de record: " + string.Join(", ", erros));
    }
}
