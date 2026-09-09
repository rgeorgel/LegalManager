using LegalManager.Domain.Entities;
using LegalManager.Domain.Enums;
using LegalManager.Infrastructure.Services;

namespace LegalManager.UnitTests;

public class TenantAnonymizerTests
{
    private readonly TenantAnonymizer _anonymizer = new();

    [Fact]
    public void AnonymizeString_EmailProperty_SubstituiDominio()
    {
        var result = _anonymizer.AnonymizeString("Email", "joao.silva@empresa.com.br");
        Assert.Equal("joao.silva@causify-replica.com", result);
    }

    [Fact]
    public void AnonymizeString_EmailComMais_SubstituiDominio()
    {
        var result = _anonymizer.AnonymizeString("Email", "maria+tag@gmail.com");
        Assert.Equal("maria+tag@causify-replica.com", result);
    }

    [Fact]
    public void AnonymizeString_UserName_SubstituiDominio()
    {
        var result = _anonymizer.AnonymizeString("UserName", "usuario.teste@dominio.io");
        Assert.Equal("usuario.teste@causify-replica.com", result);
    }

    [Fact]
    public void AnonymizeString_NormalizedEmail_Maiusculo_SubstituiDominio()
    {
        var result = _anonymizer.AnonymizeString("NormalizedEmail", "JOAO@GMAIL.COM");
        Assert.Equal("joao@causify-replica.com", result);
    }

    [Fact]
    public void AnonymizeString_TelefoneBRComDdd_SubstituiPorPlaceholder()
    {
        var result = _anonymizer.AnonymizeString("Telefone", "(11) 98765-4321");
        Assert.Equal("+5511900000000", result);
    }

    [Fact]
    public void AnonymizeString_TelefoneSemDdd_SubstituiPorPlaceholder()
    {
        var result = _anonymizer.AnonymizeString("Celular", "987654321");
        Assert.Equal("+5511900000000", result);
    }

    [Fact]
    public void AnonymizeString_TelefoneComPrefixo55_SubstituiPorPlaceholder()
    {
        var result = _anonymizer.AnonymizeString("WhatsApp", "+5511987654321");
        Assert.Equal("+5511900000000", result);
    }

    [Fact]
    public void AnonymizeString_TelefoneDeterministico_MesmoOriginal_MesmoResultado()
    {
        var first = _anonymizer.AnonymizeString("Telefone", "+5511912345678");
        var second = _anonymizer.AnonymizeString("Telefone", "+5511912345678");
        Assert.Equal(first, second);
        Assert.Equal("+5511900000000", first);
    }

    [Fact]
    public void AnonymizeString_PropriedadeNaoSensiveis_NaoModifica()
    {
        Assert.Equal("Maria Silva", _anonymizer.AnonymizeString("Nome", "Maria Silva"));
        Assert.Equal("Rua das Flores 123", _anonymizer.AnonymizeString("Endereco", "Rua das Flores 123"));
        Assert.Equal("123.456.789-00", _anonymizer.AnonymizeString("CpfCnpj", "123.456.789-00"));
    }

    [Fact]
    public void AnonymizeString_StringVazia_RetornaVazia()
    {
        Assert.Equal("", _anonymizer.AnonymizeString("Email", ""));
    }

    [Fact]
    public void AnonymizeString_StringNula_RetornaNula()
    {
        Assert.Null(_anonymizer.AnonymizeString("Email", null!));
    }

    [Fact]
    public void AnonymizeInPlace_Contato_AnonimizaEmailETelefone()
    {
        var contato = new Contato
        {
            Nome = "João Silva",
            Email = "joao@original.com",
            Telefone = "(11) 99999-8888"
        };

        var changes = _anonymizer.AnonymizeInPlace(contato);

        Assert.Equal(2, changes);
        Assert.Equal("joao@causify-replica.com", contato.Email);
        Assert.Equal("+5511900000000", contato.Telefone);
        Assert.Equal("João Silva", contato.Nome);
    }

    [Fact]
    public void AnonymizeInPlace_Contato_ApenasEmailPresente_ApenasEmailAnomizado()
    {
        var contato = new Contato { Email = "teste@teste.com", Nome = "X" };
        var changes = _anonymizer.AnonymizeInPlace(contato);
        Assert.Equal(1, changes);
        Assert.Equal("teste@causify-replica.com", contato.Email);
    }

    [Fact]
    public void AnonymizeInPlace_ConfiguracaoHonorario_AnonimizaEmailETelefone()
    {
        var config = new ConfiguracaoHonorario
        {
            NomeEscritorio = "Escritório X",
            Email = "contato@escritorio.com.br",
            Telefone = "1133334444",
            OAB = "SP123456"
        };

        var changes = _anonymizer.AnonymizeInPlace(config);

        Assert.True(changes >= 2);
        Assert.Equal("contato@causify-replica.com", config.Email);
        Assert.Equal("+5511900000000", config.Telefone);
        Assert.Equal("SP123456", config.OAB);
    }

    [Fact]
    public void AnonymizeString_EmailLocalPartComCaracteresEspeciais_Sanitizado()
    {
        var result = _anonymizer.AnonymizeString("Email", "user.name+tag@empresa.com");
        Assert.Contains("user.name+tag@causify-replica.com", result);
    }

    [Fact]
    public void AnonymizeString_EmailLocalPartMuitoLongo_Truncado()
    {
        var localLongo = new string('a', 80);
        var result = _anonymizer.AnonymizeString("Email", $"{localLongo}@empresa.com");
        var localParte = result.Split('@')[0];
        Assert.True(localParte.Length <= 60);
    }

    [Fact]
    public void AnonymizeString_StringSemEmailOuTelefone_NaoModificada()
    {
        var result = _anonymizer.AnonymizeString("Observacoes", "Cliente VIP, tratar com prioridade");
        Assert.Equal("Cliente VIP, tratar com prioridade", result);
    }
}
