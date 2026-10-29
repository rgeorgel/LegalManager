namespace LegalManager.Domain.Entities;

/// <summary>
/// Contas fora do ASP.NET Identity (portal do cliente, portal do parceiro) que bloqueiam
/// o login depois de várias senhas erradas — o mesmo que o Identity Lockout faz para Usuario.
/// </summary>
public interface IBloqueioLogin
{
    int TentativasLoginFalhas { get; set; }
    DateTime? BloqueadoAte { get; set; }
}

public static class BloqueioLogin
{
    // Mesmos valores do Identity Lockout configurado em Program.cs.
    public const int MaxTentativas = 5;
    public static readonly TimeSpan Duracao = TimeSpan.FromMinutes(15);

    public const string Mensagem =
        "Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em 15 minutos.";

    public static bool EstaBloqueado(this IBloqueioLogin conta, DateTime agoraUtc) =>
        conta.BloqueadoAte is { } ate && ate > agoraUtc;

    /// <summary>Conta uma senha errada; devolve true se a conta acabou de ser bloqueada.</summary>
    public static bool RegistrarFalha(this IBloqueioLogin conta, DateTime agoraUtc)
    {
        conta.TentativasLoginFalhas++;
        if (conta.TentativasLoginFalhas < MaxTentativas) return false;

        conta.TentativasLoginFalhas = 0;
        conta.BloqueadoAte = agoraUtc + Duracao;
        return true;
    }

    public static void RegistrarSucesso(this IBloqueioLogin conta)
    {
        conta.TentativasLoginFalhas = 0;
        conta.BloqueadoAte = null;
    }
}
