using System.Diagnostics;
using System.Management;
using System.Text;
using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Native;

namespace GameBoost.Core.Modules.Tools;

/// <summary>
/// Executa as ferramentas rápidas (seção 5.13).
///
/// Não é um IModule: aqui não há lista de ActionItem para marcar, aplicar e
/// reverter. Forçar o contrato de módulo daria um ApplyAsync que não faz nada e
/// um RevertAsync que mente.
/// </summary>
public sealed class QuickToolsService
{
    public const string ModuloId = "tools";

    private readonly IProcessService _processos;
    private readonly IMemoryService _memoria;
    private readonly IGameBoostLogger _log;

    public QuickToolsService(IProcessService processos, IMemoryService memoria, IGameBoostLogger log)
    {
        _processos = processos;
        _memoria = memoria;
        _log = log;
    }

    public async Task<ToolResult> ExecutarAsync(
        string id,
        IProgress<string>? saida,
        CancellationToken ct)
    {
        _log.Info(ModuloId, "Executar", id, "inicio");

        var resultado = id switch
        {
            QuickToolsCatalog.ReiniciarExplorer => ReiniciarExplorer(),
            QuickToolsCatalog.ReiniciarVideo => ReiniciarVideo(),
            QuickToolsCatalog.LimparDns => LimparDns(),
            QuickToolsCatalog.EsvaziarLixeira => EsvaziarLixeira(),
            QuickToolsCatalog.LimpezaDeDisco => AbrirLimpezaDeDisco(),
            QuickToolsCatalog.PontoDeRestauracao => await Task.Run(CriarPontoDeRestauracao, ct),
            QuickToolsCatalog.VerificarIntegridade => await RodarAsync("sfc.exe", "/scannow", saida, ct),
            QuickToolsCatalog.RepararImagem => await RodarAsync("DISM.exe", "/Online /Cleanup-Image /RestoreHealth", saida, ct),
            QuickToolsCatalog.TesteDeDisco => await Task.Run(() => TestarDisco(saida, ct), ct),
            QuickToolsCatalog.InfoDoSistema => await Task.Run(ColetarInfo, ct),
            _ => ToolResult.Falha($"Ferramenta desconhecida: {id}")
        };

        _log.Log(resultado.Sucesso ? LogLevel.Info : LogLevel.Warn,
            ModuloId, "Executar", id, resultado.Mensagem);

        return resultado;
    }

    // ---------------- Interface ----------------

    private ToolResult ReiniciarExplorer()
    {
        var explorers = _processos.GetProcesses()
            .Where(p => p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (explorers.Count == 0)
            return ToolResult.Falha("O Explorer não está em execução.");

        foreach (var p in explorers)
            _processos.Kill(p.Pid);

        // O Windows costuma reabrir sozinho. Se não reabrir em 3 s, força:
        // deixar o usuário sem barra de tarefas seria pior que o problema original.
        Thread.Sleep(3000);

        var voltou = _processos.GetProcesses()
            .Any(p => p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase));

        if (!voltou)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            _processos.Start(Path.Combine(windows, "explorer.exe"), null, windows);
            Thread.Sleep(1500);

            voltou = _processos.GetProcesses()
                .Any(p => p.Name.Equals("explorer", StringComparison.OrdinalIgnoreCase));
        }

        return voltou
            ? ToolResult.Ok("Explorer reiniciado. A barra de tarefas já deve estar de volta.")
            : ToolResult.Falha("O Explorer não voltou sozinho. Abra o Gerenciador de Tarefas e "
                             + "use Arquivo, Executar nova tarefa, explorer.exe.");
    }

    private ToolResult ReiniciarVideo()
        => ToolsNative.ReiniciarDriverDeVideo()
            ? ToolResult.Ok("Pedido enviado. A tela pisca e o driver recarrega em instantes.")
            : ToolResult.Falha("Não foi possível enviar o atalho. Tente Win+Ctrl+Shift+B pelo teclado.");

    // ---------------- Rede ----------------

    private ToolResult LimparDns()
        => ToolsNative.LimparCacheDeDns()
            ? ToolResult.Ok("Cache de DNS limpo.")
            : ToolResult.Falha("O Windows recusou limpar o cache de DNS.");

    // ---------------- Espaço ----------------

    private ToolResult EsvaziarLixeira()
    {
        var (bytes, itens) = RecycleBinBridge.Consultar();

        if (itens == 0)
            return ToolResult.Ok("A Lixeira já está vazia.");

        return RecycleBinBridge.Esvaziar()
            ? ToolResult.Ok($"Lixeira esvaziada: {GameModeModule.Formatar(bytes)} em {itens} itens.")
            : ToolResult.Falha("O Windows recusou esvaziar a Lixeira.");
    }

    private ToolResult AbrirLimpezaDeDisco()
    {
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(windows, "cleanmgr.exe"),
                UseShellExecute = true
            });

            return ToolResult.Ok("Limpeza de Disco aberta. O que acontece lá é decisão sua.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            return ToolResult.Falha($"Não foi possível abrir a Limpeza de Disco: {ex.Message}");
        }
    }

    // ---------------- Segurança ----------------

    /// <summary>
    /// Ponto de restauração via WMI, e não via Checkpoint-Computer do
    /// PowerShell: é a mesma API, sem depender de política de execução (regra 9).
    /// </summary>
    private ToolResult CriarPontoDeRestauracao()
    {
        try
        {
            using var classe = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            var parametros = classe.GetMethodParameters("CreateRestorePoint");

            parametros["Description"] = $"GameBoost {DateTime.Now:dd/MM/yyyy HH:mm}";
            parametros["RestorePointType"] = 12;  // MODIFY_SETTINGS
            parametros["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE

            var retorno = classe.InvokeMethod("CreateRestorePoint", parametros, null);
            var codigo = Convert.ToUInt32(retorno?["ReturnValue"] ?? 1u);

            return codigo switch
            {
                0 => ToolResult.Ok("Ponto de restauração criado."),
                1058 => ToolResult.Falha("A Restauração do Sistema está desativada neste computador. "
                                       + "Ative em Proteção do Sistema antes."),
                _ => ToolResult.Falha($"O Windows recusou criar o ponto (código {codigo}). "
                                    + "Normalmente é porque já existe um ponto das últimas 24 horas.")
            };
        }
        catch (ManagementException ex)
        {
            return ToolResult.Falha($"Não foi possível criar o ponto: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            return ToolResult.Falha("Criar ponto de restauração exige executar como administrador.");
        }
    }

    /// <summary>
    /// Roda um utilitário do Windows mostrando a saída ao vivo. sfc e DISM
    /// demoram muito para rodar em silêncio: sem acompanhamento o usuário acha
    /// que travou.
    /// </summary>
    private async Task<ToolResult> RodarAsync(
        string executavel,
        string argumentos,
        IProgress<string>? saida,
        CancellationToken ct)
    {
        var caminho = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), executavel);

        if (!File.Exists(caminho))
            return ToolResult.Falha($"{executavel} não encontrado neste Windows.");

        var info = new ProcessStartInfo
        {
            FileName = caminho,
            Arguments = argumentos,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            // sfc escreve em UTF-16; sem isto a saída vira caracteres soltos.
            StandardOutputEncoding = executavel.StartsWith("sfc", StringComparison.OrdinalIgnoreCase)
                ? Encoding.Unicode
                : Encoding.UTF8
        };

        try
        {
            using var processo = new Process { StartInfo = info, EnableRaisingEvents = true };
            var linhas = new StringBuilder();

            processo.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;

                linhas.AppendLine(e.Data);
                saida?.Report(e.Data);
            };

            processo.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    saida?.Report(e.Data);
            };

            processo.Start();
            processo.BeginOutputReadLine();
            processo.BeginErrorReadLine();

            await processo.WaitForExitAsync(ct);

            var texto = linhas.ToString();

            return processo.ExitCode == 0
                ? ToolResult.Ok($"{executavel} terminou sem erro.", texto)
                : ToolResult.Falha($"{executavel} terminou com código {processo.ExitCode}.", texto);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Falha($"{executavel} foi cancelado. "
                                  + "Ele pode continuar rodando por conta própria até terminar.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return ToolResult.Falha($"Não foi possível rodar {executavel}: {ex.Message}");
        }
    }

    // ---------------- Diagnóstico ----------------

    /// <summary>
    /// Escrita e leitura sequencial de 1 GB. WriteThrough desliga o cache de
    /// escrita do Windows: sem isso a medição mostraria a velocidade da RAM, não
    /// a do disco, e um HDD pareceria um SSD.
    /// </summary>
    private ToolResult TestarDisco(IProgress<string>? saida, CancellationToken ct)
    {
        const int tamanhoDoBloco = 4 * 1024 * 1024;
        const long total = 1024L * 1024 * 1024;

        var arquivo = Path.Combine(Path.GetTempPath(), $"gameboost-disco-{Guid.NewGuid():N}.tmp");
        var bloco = new byte[tamanhoDoBloco];
        Random.Shared.NextBytes(bloco);

        try
        {
            saida?.Report("Gravando 1 GB...");
            var relogio = Stopwatch.StartNew();

            using (var escrita = new FileStream(arquivo, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, tamanhoDoBloco, FileOptions.WriteThrough))
            {
                for (long escrito = 0; escrito < total; escrito += tamanhoDoBloco)
                {
                    ct.ThrowIfCancellationRequested();
                    escrita.Write(bloco, 0, tamanhoDoBloco);
                }

                escrita.Flush(flushToDisk: true);
            }

            relogio.Stop();
            var escritaMbs = total / 1024.0 / 1024.0 / relogio.Elapsed.TotalSeconds;

            saida?.Report($"Escrita: {escritaMbs:0} MB/s");
            saida?.Report("Lendo 1 GB...");

            relogio.Restart();

            using (var leitura = new FileStream(arquivo, FileMode.Open, FileAccess.Read,
                       FileShare.None, tamanhoDoBloco, FileOptions.SequentialScan))
            {
                var destino = new byte[tamanhoDoBloco];
                int lido;
                while ((lido = leitura.Read(destino, 0, tamanhoDoBloco)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    _ = lido;
                }
            }

            relogio.Stop();
            var leituraMbs = total / 1024.0 / 1024.0 / relogio.Elapsed.TotalSeconds;

            saida?.Report($"Leitura: {leituraMbs:0} MB/s");

            var veredito = Interpretar(escritaMbs, leituraMbs);

            return ToolResult.Ok(
                $"Escrita {escritaMbs:0} MB/s, leitura {leituraMbs:0} MB/s.",
                veredito);
        }
        catch (OperationCanceledException)
        {
            return ToolResult.Falha("Teste cancelado.");
        }
        catch (IOException ex)
        {
            return ToolResult.Falha($"Não foi possível testar o disco: {ex.Message}");
        }
        finally
        {
            // O arquivo sai mesmo se o teste falhar ou for cancelado.
            try
            {
                if (File.Exists(arquivo))
                    File.Delete(arquivo);
            }
            catch (IOException)
            {
                _log.Warn(ModuloId, "TesteDeDisco", arquivo, "nao consegui apagar o arquivo de teste");
            }
        }
    }

    /// <summary>Traduz o número em algo que signifique alguma coisa para o usuário.</summary>
    private static string Interpretar(double escritaMbs, double leituraMbs)
    {
        var menor = Math.Min(escritaMbs, leituraMbs);

        return menor switch
        {
            < 150 => "Estes números são de disco mecânico (HDD). Jogos novos assumem SSD e vão "
                   + "engasgar ao carregar textura. Mover o jogo para um SSD resolve engasgo que "
                   + "nenhum ajuste de software resolve.",
            < 600 => "Faixa de SSD SATA. Suficiente para qualquer jogo atual.",
            < 3000 => "Faixa de SSD NVMe. Está ótimo.",
            _ => "Faixa de NVMe rápido. Está ótimo."
        };
    }

    private ToolResult ColetarInfo()
    {
        var sb = new StringBuilder();
        var memoria = _memoria.GetSnapshot();

        sb.AppendLine("=== Sistema ===");
        sb.AppendLine($"Windows           : {Wmi("Win32_OperatingSystem", "Caption")} (build {Environment.OSVersion.Version.Build})");
        sb.AppendLine($"Nome do computador: {Environment.MachineName}");
        sb.AppendLine();

        sb.AppendLine("=== Processador ===");
        sb.AppendLine($"Modelo            : {Wmi("Win32_Processor", "Name")}");
        sb.AppendLine($"Núcleos lógicos   : {Environment.ProcessorCount}");
        sb.AppendLine();

        sb.AppendLine("=== Memória ===");
        sb.AppendLine($"Total             : {GameModeModule.Formatar(memoria.TotalBytes)}");
        sb.AppendLine($"Disponível        : {GameModeModule.Formatar(memoria.AvailableBytes)}");
        sb.AppendLine(DescreverPentes());
        sb.AppendLine();

        sb.AppendLine("=== Vídeo ===");
        foreach (var placa in TodosOsValores("Win32_VideoController", "Name"))
            sb.AppendLine($"Placa             : {placa}");

        sb.AppendLine($"Driver            : {Wmi("Win32_VideoController", "DriverVersion")}");
        sb.AppendLine();

        sb.AppendLine("=== Discos ===");
        foreach (var unidade in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var usado = unidade.TotalSize - unidade.TotalFreeSpace;
            var percentual = unidade.TotalSize == 0 ? 0 : usado * 100.0 / unidade.TotalSize;

            sb.AppendLine($"{unidade.Name,-18}: {GameModeModule.Formatar(unidade.TotalFreeSpace)} livres "
                        + $"de {GameModeModule.Formatar(unidade.TotalSize)} ({percentual:0}% em uso)");
        }

        sb.AppendLine();
        sb.AppendLine("=== BIOS ===");
        sb.AppendLine($"Placa-mãe         : {Wmi("Win32_BaseBoard", "Manufacturer")} {Wmi("Win32_BaseBoard", "Product")}");
        sb.AppendLine($"BIOS              : {Wmi("Win32_BIOS", "SMBIOSBIOSVersion")}");
        sb.AppendLine();
        sb.AppendLine($"Coletado pelo GameBoost em {DateTime.Now:dd/MM/yyyy HH:mm}.");

        return ToolResult.Ok("Informações coletadas.", sb.ToString());
    }

    /// <summary>
    /// Canal único ou duplo muda a banda de memória e rende FPS de verdade em
    /// jogo limitado por CPU. Por isso vale mais que só o total instalado.
    /// </summary>
    private string DescreverPentes()
    {
        try
        {
            using var consulta = new ManagementObjectSearcher(
                "SELECT Capacity, Speed FROM Win32_PhysicalMemory");

            var pentes = consulta.Get().Cast<ManagementObject>().ToList();

            if (pentes.Count == 0)
                return "Pentes            : não foi possível ler";

            var velocidade = pentes[0]["Speed"];
            var canal = pentes.Count == 1 ? " (canal único)" : " (canal duplo ou mais)";

            var texto = $"Pentes            : {pentes.Count}{canal}";

            if (velocidade is not null)
                texto += $", {velocidade} MHz";

            foreach (var p in pentes)
                p.Dispose();

            return texto;
        }
        catch (ManagementException)
        {
            return "Pentes            : não foi possível ler";
        }
    }

    private static string Wmi(string classe, string propriedade)
        => TodosOsValores(classe, propriedade).FirstOrDefault() ?? "desconhecido";

    private static IReadOnlyList<string> TodosOsValores(string classe, string propriedade)
    {
        try
        {
            using var consulta = new ManagementObjectSearcher($"SELECT {propriedade} FROM {classe}");

            return consulta.Get()
                .Cast<ManagementObject>()
                .Select(mo =>
                {
                    var valor = mo[propriedade]?.ToString()?.Trim();
                    mo.Dispose();
                    return valor;
                })
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToList();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
