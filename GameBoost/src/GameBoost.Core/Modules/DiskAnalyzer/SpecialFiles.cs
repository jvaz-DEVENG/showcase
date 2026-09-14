using GameBoost.Core.Modules.GameMode;

namespace GameBoost.Core.Modules.DiskAnalyzer;

/// <summary>
/// Arquivo grande do Windows que o usuário vê no analisador e não entende.
/// Explicar vale mais que listar: sem isso ele tenta apagar hiberfil.sys na
/// mão e não consegue.
/// </summary>
public sealed record SpecialFile(
    string Nome,
    string Caminho,
    long Bytes,
    string OQueE,
    string ComoRemover,
    RiskLevel Risco);

public static class SpecialFiles
{
    /// <summary>Encontra os arquivos grandes de sistema na raiz do volume.</summary>
    public static IReadOnlyList<SpecialFile> Encontrar(string raizDoVolume)
    {
        var achados = new List<SpecialFile>();

        Adicionar(achados, raizDoVolume, "hiberfil.sys",
            "Guarda o conteúdo da memória quando o PC hiberna. Ocupa perto do tamanho da sua RAM.",
            "Desativar a hibernação libera este espaço, com o comando powercfg /h off. "
          + "Você perde a hibernação e a Inicialização Rápida, que deixa o boot um pouco mais lento.",
            RiskLevel.Medio);

        Adicionar(achados, raizDoVolume, "pagefile.sys",
            "Memória virtual: o Windows usa quando a RAM acaba.",
            "Não mexa sem necessidade. Reduzir ou desativar causa travamento quando a RAM enche, "
          + "e o ganho de espaço não compensa.",
            RiskLevel.Alto);

        Adicionar(achados, raizDoVolume, "swapfile.sys",
            "Arquivo de troca dos aplicativos da Store. Costuma ser pequeno.",
            "Some junto com o pagefile. Não vale mexer separadamente.",
            RiskLevel.Alto);

        // Windows.old aparece depois de uma atualização grande e costuma passar
        // de 20 GB. É o maior ganho fácil de espaço que existe.
        var windowsOld = Path.Combine(raizDoVolume, "Windows.old");
        if (Directory.Exists(windowsOld))
        {
            achados.Add(new SpecialFile(
                "Windows.old",
                windowsOld,
                TamanhoDaPasta(windowsOld),
                "Cópia do Windows anterior, guardada depois de uma atualização grande. "
              + "Serve para voltar à versão antiga nos primeiros dias.",
                "Use a Limpeza de Disco do Windows e marque Instalações anteriores do Windows. "
              + "Depois disso não dá mais para voltar à versão antiga.",
                RiskLevel.Medio));
        }

        return achados;
    }

    private static void Adicionar(
        List<SpecialFile> lista, string raiz, string nome,
        string oQueE, string comoRemover, RiskLevel risco)
    {
        var caminho = Path.Combine(raiz, nome);

        try
        {
            var info = new FileInfo(caminho);
            if (info.Exists && info.Length > 0)
                lista.Add(new SpecialFile(nome, caminho, info.Length, oQueE, comoRemover, risco));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Estes arquivos ficam abertos pelo kernel; nem sempre dá para ler
            // o tamanho, e nesse caso é melhor não listar que listar errado.
        }
    }

    private static long TamanhoDaPasta(string pasta)
    {
        long total = 0;
        var pilha = new Stack<string>();
        pilha.Push(pasta);

        while (pilha.Count > 0)
        {
            foreach (var entrada in Native.FastFind.Listar(pilha.Pop()))
            {
                if (entrada.EhPasta)
                    pilha.Push(Path.Combine(pasta, entrada.Nome));
                else
                    total += entrada.Tamanho;
            }
        }

        return total;
    }

    public static string Descrever(SpecialFile arquivo)
        => $"{arquivo.Nome} — {GameModeModule.Formatar(arquivo.Bytes)}\n\n"
         + $"O que é: {arquivo.OQueE}\n\n"
         + $"Como remover: {arquivo.ComoRemover}";
}
