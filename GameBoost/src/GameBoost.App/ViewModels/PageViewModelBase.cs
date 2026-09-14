using CommunityToolkit.Mvvm.ComponentModel;

namespace GameBoost.App.ViewModels;

/// <summary>
/// Base de toda pagina do menu lateral (secao 6 do spec). O shell so conhece
/// este contrato: titulo, icone e se a pagina ja existe de verdade.
/// </summary>
public abstract partial class PageViewModelBase : ObservableObject
{
    /// <summary>Nome curto, como aparece no menu lateral.</summary>
    public abstract string Nome { get; }

    /// <summary>Titulo da pagina, no cabecalho.</summary>
    public abstract string Titulo { get; }

    /// <summary>Uma linha explicando o que a pagina faz.</summary>
    public abstract string Subtitulo { get; }

    /// <summary>Glifo da fonte Segoe Fluent Icons / Segoe MDL2 Assets.</summary>
    public abstract string Icone { get; }

    /// <summary>Falso enquanto o modulo nao existe: a pagina mostra o aviso de fase.</summary>
    public virtual bool Implementada => true;

    /// <summary>Chamado toda vez que a pagina entra em foco no shell.</summary>
    public virtual void AoEntrar()
    {
    }

    /// <summary>
    /// Chamado ao sair da pagina. Existe para quem liga trabalho continuo
    /// poder desligar: o monitoramento do Diagnostico nao deve seguir rodando
    /// enquanto o usuario le a tela de Limpeza.
    /// </summary>
    public virtual void AoSair()
    {
    }
}
