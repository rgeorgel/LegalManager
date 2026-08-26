using LegalManager.Application.DTOs.Contatos;
using LegalManager.Application.DTOs.Processos;
using LegalManager.Application.Interfaces;
using LegalManager.Domain.Enums;

namespace LegalManager.Infrastructure.Services;

/// <summary>
/// Implementação extraída de OnboardingController.ResolverPartesDataJudAsync (ver
/// docs/features/busca-processo-cadastro-manual.md, Fase 1, item 2). Mantém o mesmo
/// comportamento: busca Contato por nome antes de criar, para evitar duplicidade.
/// </summary>
public class ContatoResolverService(IContatoService contatoService) : IContatoResolverService
{
    public async Task<List<ProcessoParteDto>> ResolverPartesDataJudAsync(
        IReadOnlyList<TribunalParte> partes, CancellationToken ct = default)
    {
        var resultado = new List<ProcessoParteDto>();

        foreach (var parte in partes)
        {
            if (string.IsNullOrWhiteSpace(parte.Nome)) continue;

            var contato = await contatoService.GetByNomeAsync(parte.Nome, ct);
            if (contato == null)
            {
                var tipoPessoa = !string.IsNullOrEmpty(parte.Cpf)
                    ? TipoPessoa.PF
                    : !string.IsNullOrEmpty(parte.Cnpj)
                        ? TipoPessoa.PJ
                        : TipoPessoa.PF;

                contato = await contatoService.CreateAsync(new CreateContatoDto(
                    Tipo: tipoPessoa,
                    TipoContato: TipoContato.Cliente,
                    Nome: parte.Nome,
                    CpfCnpj: parte.Cpf ?? parte.Cnpj,
                    Oab: parte.OAB,
                    Email: null,
                    Telefone: null,
                    Endereco: null,
                    Cidade: null,
                    Estado: null,
                    Cep: null,
                    DataNascimento: null,
                    Observacoes: null,
                    NotificacaoHabilitada: false,
                    Tags: null
                ), ct);
            }

            var tipoParte = MapearPolo(parte.Polo);
            resultado.Add(new ProcessoParteDto(contato.Id, tipoParte));
        }

        return resultado;
    }

    private static TipoParteProcesso MapearPolo(string? polo)
    {
        if (string.IsNullOrWhiteSpace(polo)) return TipoParteProcesso.Terceiro;
        var upper = polo.ToUpperInvariant();
        if (upper.Contains("AUTOR") || upper.Contains("RECLAMANTE") || upper.Contains("IMPETRANTE") || upper.Contains("REQUERENTE") || upper.Contains("EXEQUENTE"))
            return TipoParteProcesso.Autor;
        if (upper.Contains("RÉU") || upper.Contains("REU") || upper.Contains("RECLAMADO") || upper.Contains("INDICIADO") || upper.Contains("REQUERIDO") || upper.Contains("EXECUTADO"))
            return TipoParteProcesso.Reu;
        if (upper.Contains("INTERESSADO"))
            return TipoParteProcesso.Interessado;
        return TipoParteProcesso.Terceiro;
    }
}
