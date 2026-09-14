namespace GameBoost.Core.Modules.DiskAnalyzer;

/// <summary>Categoria do treemap: o que é aquele espaço, em linguagem humana.</summary>
public enum DiskCategory
{
    Outros,
    Jogos,
    Videos,
    Imagens,
    Instaladores,
    Caches,
    Sistema,
    Documentos,
    Codigo,
    Musica
}

/// <summary>
/// Um nó da árvore do disco. Guarda o tamanho agregado da subárvore, calculado
/// uma vez ao fim da varredura: recalcular a cada acesso tornaria o treemap
/// inutilizável em disco com milhões de arquivos.
/// </summary>
public sealed class DiskNode
{
    public DiskNode(string nome, string caminho, bool ehPasta, DiskNode? pai = null)
    {
        Nome = nome;
        Caminho = caminho;
        EhPasta = ehPasta;
        Pai = pai;
    }

    public string Nome { get; }
    public string Caminho { get; }
    public bool EhPasta { get; }
    public DiskNode? Pai { get; }

    public List<DiskNode> Filhos { get; } = new();

    /// <summary>Tamanho próprio, quando arquivo. Pasta soma pelos filhos.</summary>
    public long TamanhoProprio { get; set; }

    /// <summary>Soma da subárvore inteira. Preenchido por <see cref="Consolidar"/>.</summary>
    public long Tamanho { get; private set; }

    public int TotalDeArquivos { get; private set; }

    public DateTime Modificado { get; set; }

    public DiskCategory Categoria { get; set; } = DiskCategory.Outros;

    /// <summary>Pastas que não puderam ser lidas, por permissão ou por sumirem no meio.</summary>
    public int PastasIgnoradas { get; set; }

    /// <summary>
    /// Soma a subárvore de baixo para cima e propaga a categoria dominante.
    /// Iterativo, e não recursivo: uma árvore de disco cheio chega a dezenas de
    /// milhares de níveis somados e estouraria a pilha.
    /// </summary>
    public void Consolidar()
    {
        var pilha = new Stack<(DiskNode No, bool Visitado)>();
        pilha.Push((this, false));

        while (pilha.Count > 0)
        {
            var (no, visitado) = pilha.Pop();

            if (!visitado)
            {
                pilha.Push((no, true));

                foreach (var filho in no.Filhos)
                    pilha.Push((filho, false));

                continue;
            }

            if (no.EhPasta)
            {
                long soma = 0;
                var arquivos = 0;

                foreach (var filho in no.Filhos)
                {
                    soma += filho.Tamanho;
                    arquivos += filho.TotalDeArquivos;
                }

                no.Tamanho = soma;
                no.TotalDeArquivos = arquivos;

                if (no.Categoria == DiskCategory.Outros)
                    no.Categoria = CategoriaDominante(no);
            }
            else
            {
                no.Tamanho = no.TamanhoProprio;
                no.TotalDeArquivos = 1;
            }
        }
    }

    /// <summary>A categoria que ocupa mais espaço dentro da pasta é a que a define.</summary>
    private static DiskCategory CategoriaDominante(DiskNode pasta)
    {
        if (pasta.Filhos.Count == 0)
            return DiskCategory.Outros;

        var porCategoria = new Dictionary<DiskCategory, long>();

        foreach (var filho in pasta.Filhos)
        {
            porCategoria.TryGetValue(filho.Categoria, out var atual);
            porCategoria[filho.Categoria] = atual + filho.Tamanho;
        }

        return porCategoria.OrderByDescending(p => p.Value).First().Key;
    }

    /// <summary>Percorre a árvore inteira sem recursão.</summary>
    public IEnumerable<DiskNode> Percorrer()
    {
        var pilha = new Stack<DiskNode>();
        pilha.Push(this);

        while (pilha.Count > 0)
        {
            var no = pilha.Pop();
            yield return no;

            foreach (var filho in no.Filhos)
                pilha.Push(filho);
        }
    }

    public IEnumerable<DiskNode> MaioresArquivos(int quantidade)
        => Percorrer()
            .Where(n => !n.EhPasta)
            .OrderByDescending(n => n.Tamanho)
            .Take(quantidade);

    public IEnumerable<DiskNode> MaioresPastas(int quantidade)
        => Percorrer()
            .Where(n => n.EhPasta && n != this)
            .OrderByDescending(n => n.Tamanho)
            .Take(quantidade);

    public override string ToString() => $"{Caminho} ({Tamanho} bytes)";
}
