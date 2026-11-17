using System.Globalization;
using System.Text;
using LegalManager.Application.DTOs.AssinaturaDigital;
using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.AssinaturaDigital;

/// <summary>Dados do Causify disponíveis para preencher as variáveis de um template do Contratos.</summary>
/// <remarks>Cliente e contrato podem faltar: envio a partir de um processo sem cliente ou de um contato sem contrato.</remarks>
public sealed record ContextoAssinatura(
    Contato? Cliente, ContratoHonorario? Contrato, Processo? Processo, Tenant Escritorio, Usuario Advogado, DateTime Hoje);

/// <summary>
/// Catálogo das fontes ("cliente.nome", "contrato.valorTotal"…), os valores no formato que a API do
/// Contratos aceita (texto; dinheiro "1500.50"; data AAAA-MM-DD) e a sugestão automática de fonte
/// para cada variável do template, pela chave e pelo rótulo.
/// </summary>
public static class FontesAssinatura
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly IReadOnlyList<FonteAssinaturaDto> Catalogo =
    [
        new("cliente.nome", "Nome", "Cliente"),
        new("cliente.cpfCnpj", "CPF/CNPJ", "Cliente"),
        new("cliente.endereco", "Endereço completo", "Cliente"),
        new("cliente.cidade", "Cidade", "Cliente"),
        new("cliente.email", "E-mail", "Cliente"),
        new("cliente.telefone", "Telefone", "Cliente"),
        new("escritorio.nome", "Nome", "Escritório"),
        new("escritorio.cnpj", "CNPJ", "Escritório"),
        new("escritorio.endereco", "Endereço", "Escritório"),
        new("escritorio.email", "E-mail", "Escritório"),
        new("escritorio.telefone", "Telefone", "Escritório"),
        new("advogado.nome", "Nome", "Advogado (você)"),
        new("advogado.email", "E-mail", "Advogado (você)"),
        new("contrato.numero", "Número", "Contrato de honorários"),
        new("contrato.objeto", "Serviço (objeto)", "Contrato de honorários"),
        new("contrato.valorTotal", "Valor total", "Contrato de honorários"),
        new("contrato.valorEntrada", "Valor da entrada", "Contrato de honorários"),
        new("contrato.numeroParcelas", "Número de parcelas", "Contrato de honorários"),
        new("contrato.formaPagamento", "Forma de pagamento", "Contrato de honorários"),
        new("contrato.dataInicio", "Data de início", "Contrato de honorários"),
        new("contrato.dataPrimeiraParcela", "1º vencimento", "Contrato de honorários"),
        new("processo.numero", "Número (CNJ)", "Processo"),
        new("processo.vara", "Vara", "Processo"),
        new("processo.comarca", "Comarca", "Processo"),
        new("hoje", "Data de hoje", "Outros"),
    ];

    private static readonly HashSet<string> Chaves = Catalogo.Select(f => f.Chave).ToHashSet();

    public static bool Existe(string? fonte) => fonte != null && Chaves.Contains(fonte);

    public static string? Valor(string fonte, ContextoAssinatura c) => fonte switch
    {
        "cliente.nome" => c.Cliente?.Nome,
        "cliente.cpfCnpj" => c.Cliente?.CpfCnpj,
        "cliente.endereco" => c.Cliente is { } cli ? EnderecoCompleto(cli) : null,
        "cliente.cidade" => c.Cliente?.Cidade,
        "cliente.email" => c.Cliente?.Email,
        "cliente.telefone" => c.Cliente?.Telefone,
        "escritorio.nome" => c.Escritorio.Nome,
        "escritorio.cnpj" => c.Escritorio.Cnpj,
        "escritorio.endereco" => c.Escritorio.Endereco,
        "escritorio.email" => c.Escritorio.Email,
        "escritorio.telefone" => c.Escritorio.Telefone,
        "advogado.nome" => c.Advogado.Nome,
        "advogado.email" => c.Advogado.Email,
        "contrato.numero" => c.Contrato?.NumeroContrato,
        "contrato.objeto" => c.Contrato?.Objeto,
        "contrato.valorTotal" => c.Contrato is { } ct ? Dinheiro(ct.ValorTotal) : null,
        "contrato.valorEntrada" => c.Contrato?.ValorEntrada is { } e ? Dinheiro(e) : null,
        "contrato.numeroParcelas" => c.Contrato?.NumeroParcelas?.ToString(Inv),
        "contrato.formaPagamento" => c.Contrato?.FormaPagamento switch
        {
            FormaPagamentoContrato.AVista => "À vista",
            FormaPagamentoContrato.Parcelado => "Parcelado",
            FormaPagamentoContrato.EntradaParcelado => "Entrada + parcelas",
            _ => null,
        },
        "contrato.dataInicio" => c.Contrato is { } ci ? Data(ci.DataInicio) : null,
        "contrato.dataPrimeiraParcela" => c.Contrato?.DataPrimeiraParcela is { } d ? Data(d) : null,
        "processo.numero" => c.Processo?.NumeroCNJ,
        "processo.vara" => c.Processo?.Vara,
        "processo.comarca" => c.Processo?.Comarca,
        "hoje" => Data(c.Hoje),
        _ => null,
    };

    /// <summary>A fonte faz sentido neste envio? (Sem contrato de honorários, as fontes "contrato.*" somem; idem cliente e processo.)</summary>
    public static bool Disponivel(string fonte, ContextoAssinatura c) => fonte.Split('.')[0] switch
    {
        "cliente" => c.Cliente != null,
        "contrato" => c.Contrato != null,
        "processo" => c.Processo != null,
        _ => Existe(fonte),
    };

    public static string Dinheiro(decimal v) => v.ToString("0.00", Inv);
    public static string Data(DateTime d) => d.ToString("yyyy-MM-dd", Inv);

    public static string? EnderecoCompleto(Contato c)
    {
        var cidadeUf = string.Join("/", new[] { c.Cidade, c.Estado }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
        var partes = new[] { c.Endereco?.Trim(), cidadeUf, string.IsNullOrWhiteSpace(c.Cep) ? null : $"CEP {c.Cep.Trim()}" }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var texto = string.Join(", ", partes);
        return texto.Length == 0 ? null : texto;
    }

    /// <summary>Telefone no formato que o Contratos exige para WhatsApp/SMS: "+55 11 98765-4321".</summary>
    public static string? TelefoneInternacional(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone)) return null;
        var digitos = new string(telefone.Where(char.IsDigit).ToArray());
        var internacional = telefone.TrimStart().StartsWith('+');
        if (!internacional && digitos.Length is 10 or 11) digitos = "55" + digitos;
        else if (!internacional && digitos.StartsWith('0') && digitos.Length is 11 or 12) digitos = "55" + digitos[1..];
        if (digitos.StartsWith("55") && digitos.Length is 12 or 13)
        {
            var ddd = digitos[2..4];
            var numero = digitos[4..];
            return $"+55 {ddd} {numero[..^4]}-{numero[^4..]}";
        }
        return digitos.Length >= 8 && internacional ? "+" + digitos : null;
    }

    /// <summary>CPF (11 dígitos) do contato pessoa física — o Contratos confere o CPF de quem assina.</summary>
    public static string? CpfDoSignatario(Contato c)
    {
        if (c.Tipo != TipoPessoa.PF || string.IsNullOrWhiteSpace(c.CpfCnpj)) return null;
        var d = new string(c.CpfCnpj.Where(char.IsDigit).ToArray());
        return d.Length == 11 ? d : null;
    }

    // ── Sugestão automática ─────────────────────────────────────────────────────────────

    private static readonly string[] PalavrasEscritorio = ["escritorio", "contratad", "advogad", "outorgad", "prestador", "sociedade"];
    private static readonly string[] PalavrasCliente = ["contratante", "cliente", "outorgante", "tomador"];

    /// <summary>
    /// Sugere a fonte de cada variável. Palavras como "contratante"/"contratada" decidem de quem é o
    /// dado; sem elas, o primeiro grupo com dados de pessoa (prefixo antes do ponto: "parte1") vira o
    /// cliente e o segundo, o escritório.
    /// </summary>
    public static Dictionary<string, string?> Sugerir(IReadOnlyList<ContratosVariavel> variaveis)
    {
        var gruposPessoa = variaveis
            .Where(v => EhDadoDePessoa(Normalizar(v.Key + " " + v.Label), v.Type))
            .Select(v => Grupo(v.Key))
            .Where(g => g != null)
            .Distinct()
            .ToList();

        var r = new Dictionary<string, string?>();
        foreach (var v in variaveis)
        {
            var texto = Normalizar(v.Key + " " + v.Label);
            string? parte = PalavrasEscritorio.Any(texto.Contains) ? "escritorio"
                : PalavrasCliente.Any(texto.Contains) ? "cliente"
                : Grupo(v.Key) is { } g && gruposPessoa.IndexOf(g) is var i && i >= 0 ? (i == 0 ? "cliente" : "escritorio")
                : null;
            r[v.Key] = SugerirUma(texto, v.Type, parte);
        }
        return r;
    }

    private static string? SugerirUma(string t, string tipo, string? parte)
    {
        if (t.Contains("processo") || t.Contains("cnj")) return "processo.numero";
        if (t.Contains("vara")) return "processo.vara";
        if (t.Contains("comarca")) return "processo.comarca";

        switch (tipo)
        {
            case "money":
                return t.Contains("entrada") ? "contrato.valorEntrada" : "contrato.valorTotal";
            case "date":
                if (t.Contains("inicio")) return "contrato.dataInicio";
                if (t.Contains("vencimento") || t.Contains("parcela")) return "contrato.dataPrimeiraParcela";
                return "hoje";
            case "cpf_cnpj":
                return parte == "escritorio" ? "escritorio.cnpj" : "cliente.cpfCnpj";
        }

        if (t.Contains("parcelas")) return "contrato.numeroParcelas";
        if (t.Contains("forma") && t.Contains("pagamento")) return "contrato.formaPagamento";
        if (t.Contains("objeto") || t.Contains("servico") || t.Contains("finalidade")) return "contrato.objeto";
        if (t.Contains("numero") && t.Contains("contrato")) return "contrato.numero";

        var quem = parte ?? "cliente";
        if (t.Contains("cpf") || t.Contains("cnpj") || t.Contains("documento"))
            return quem == "escritorio" ? "escritorio.cnpj" : "cliente.cpfCnpj";
        if (t.Contains("endereco")) return $"{quem}.endereco";
        if (t.Contains("email") || t.Contains("e-mail")) return $"{quem}.email";
        if (t.Contains("telefone") || t.Contains("celular") || t.Contains("whatsapp")) return $"{quem}.telefone";
        if (t.Contains("advogad") && t.Contains("nome")) return "advogado.nome";
        if (t.Contains("nome") || t.Contains("razao")) return $"{quem}.nome";
        if (t.Contains("cidade") || t.Contains("foro")) return parte == "escritorio" ? null : "cliente.cidade";
        return null;
    }

    private static bool EhDadoDePessoa(string t, string tipo) =>
        tipo == "cpf_cnpj" || t.Contains("nome") || t.Contains("documento") || t.Contains("endereco") || t.Contains("cpf") || t.Contains("cnpj");

    private static string? Grupo(string chave)
    {
        var i = chave.IndexOf('.');
        return i > 0 ? chave[..i] : null;
    }

    internal static string Normalizar(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }
}
