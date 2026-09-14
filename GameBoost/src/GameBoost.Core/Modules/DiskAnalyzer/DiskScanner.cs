using System.Collections.Concurrent;
using System.Diagnostics;
using GameBoost.Core.Logging;
using GameBoost.Core.Native;

namespace GameBoost.Core.Modules.DiskAnalyzer;

public sealed record ScanProgresso(string PastaAtual, int Arquivos, long Bytes);

public sealed record DiskScanResult(
    DiskNode Raiz,
    TimeSpan Duracao,
    int TotalDeArquivos,
    int PastasIgnoradas,
    long EspacoTotal,
    long EspacoLivre);

/// <summary>
/// Varre um volume inteiro montando a árvore de tamanhos.
///
/// Usa FindFirstFileEx com busca grande, paralelizado por subárvore. Não lê a
/// MFT crua: ver docs/DECISOES.md para o motivo e o que isso custa em
/// velocidade.
/// </summary>
public sealed class DiskScanner
{
    private readonly IGameBoostLogger _log;

    public DiskScanner(IGameBoostLogger log)
    {
        _log = log;
    }

    /// <summary>Pastas que a varredura nunca entra: são reparse points ou lixo de kernel.</summary>
    private static readonly IReadOnlySet<string> Pular = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information",
        "$Recycle.Bin",
        "$WinREAgent",
        "Config.Msi"
    };

    public DiskScanResult Varrer(
        string raizDoVolume,
        IProgress<ScanProgresso>? progresso,
        CancellationToken ct)
    {
        var relogio = Stopwatch.StartNew();

        var raiz = new DiskNode(raizDoVolume, raizDoVolume, ehPasta: true);
        var arquivos = 0;
        var ignoradas = 0;
        long bytes = 0;
        var ultimoAviso = 0L;

        // Uma fila de trabalho em vez de recursão: a profundidade de um disco
        // real é imprevisível e o paralelismo fica explícito.
        var fila = new ConcurrentQueue<DiskNode>();
        fila.Enqueue(raiz);

        var paralelismo = Math.Min(Environment.ProcessorCount, 8);

        while (!fila.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();

            var lote = new List<DiskNode>();
            while (fila.TryDequeue(out var no) && lote.Count < 512)
                lote.Add(no);

            var descobertas = new ConcurrentBag<DiskNode>();

            Parallel.ForEach(
                lote,
                new ParallelOptions { MaxDegreeOfParallelism = paralelismo, CancellationToken = ct },
                pasta =>
                {
                    var entradas = FastFind.Listar(pasta.Caminho, out var acessoNegado);

                    // So conta como ignorada quando o Windows recusou de fato.
                    // Pasta vazia nao e pasta inacessivel.
                    if (acessoNegado)
                        Interlocked.Increment(ref ignoradas);

                    foreach (var entrada in entradas)
                    {
                        if (entrada.EhPasta && Pular.Contains(entrada.Nome))
                            continue;

                        var caminho = Path.Combine(pasta.Caminho, entrada.Nome);
                        var filho = new DiskNode(entrada.Nome, caminho, entrada.EhPasta, pasta)
                        {
                            TamanhoProprio = entrada.Tamanho,
                            Modificado = entrada.Modificado,
                            Categoria = entrada.EhPasta
                                ? DiskCategory.Outros
                                : DiskCategories.Classificar(caminho, entrada.Tamanho)
                        };

                        lock (pasta.Filhos)
                            pasta.Filhos.Add(filho);

                        if (entrada.EhPasta)
                        {
                            descobertas.Add(filho);
                        }
                        else
                        {
                            Interlocked.Increment(ref arquivos);
                            Interlocked.Add(ref bytes, entrada.Tamanho);
                        }
                    }
                });

            foreach (var nova in descobertas)
                fila.Enqueue(nova);

            // Progresso a cada 25 mil arquivos: relatar a cada arquivo custaria
            // mais que a própria leitura.
            var lidos = Volatile.Read(ref arquivos);
            if (lidos - ultimoAviso > 25_000)
            {
                ultimoAviso = lidos;
                progresso?.Report(new ScanProgresso(
                    lote.Count > 0 ? lote[0].Caminho : raizDoVolume, lidos, Volatile.Read(ref bytes)));
            }
        }

        raiz.PastasIgnoradas = ignoradas;
        raiz.Consolidar();

        relogio.Stop();

        var (total, livre) = EspacoDoVolume(raizDoVolume);

        _log.Info("DiskAnalyzer", "Varrer", raizDoVolume,
            $"{arquivos} arquivos, {GameMode.GameModeModule.Formatar(raiz.Tamanho)} em {relogio.Elapsed.TotalSeconds:0.0}s");

        return new DiskScanResult(raiz, relogio.Elapsed, arquivos, ignoradas, total, livre);
    }

    private static (long Total, long Livre) EspacoDoVolume(string raiz)
    {
        try
        {
            var unidade = new DriveInfo(raiz);
            return unidade.IsReady ? (unidade.TotalSize, unidade.TotalFreeSpace) : (0, 0);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    public static IReadOnlyList<DriveInfo> VolumesDisponiveis()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable)
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<DriveInfo>();
        }
    }
}
