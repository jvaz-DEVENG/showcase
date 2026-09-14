using System.Text;
using System.Text.Json;
using GameBoost.Core;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.GameMode;
using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.Cleaner;
using GameBoost.Core.Modules.HealthReport;
using GameBoost.Core.State;
using Microsoft.Extensions.DependencyInjection;

namespace GameBoost.Cli;

/// <summary>
/// Executa os comandos de linha. Vive numa biblioteca para que o mesmo
/// GameBoost.exe atenda a UI e a CLI, como na v1 (--scan sem instalar nada a parte).
/// </summary>
public sealed class CommandRunner
{
    private readonly IServiceProvider _services;
    private readonly TextWriter _saida;

    public CommandRunner(IServiceProvider services, TextWriter saida)
    {
        _services = services;
        _saida = saida;
    }

    public async Task<int> ExecutarAsync(CommandLineOptions opcoes, CancellationToken ct = default)
    {
        if (opcoes.Erro is not null)
        {
            _saida.WriteLine($"Erro: {opcoes.Erro}");
            _saida.WriteLine();
            _saida.WriteLine(CommandLineOptions.TextoDeAjuda);
            return 2;
        }

        return opcoes.Comando switch
        {
            CliCommand.Help => Ajuda(),
            CliCommand.Scan => await ScanAsync(opcoes, ct),
            CliCommand.GameModeOn => await GameModeAsync(opcoes, ligar: true, ct),
            CliCommand.GameModeOff => await GameModeAsync(opcoes, ligar: false, ct),
            CliCommand.RevertAll => ReverterTudo(opcoes),
            CliCommand.Report => await ReportAsync(opcoes, ct),
            CliCommand.Clean => await CleanAsync(opcoes, ct),
            _ => Ajuda()
        };
    }

    private int Ajuda()
    {
        _saida.WriteLine(CommandLineOptions.TextoDeAjuda);
        return 0;
    }

    private int NaoImplementado(string comando, string fase)
    {
        _saida.WriteLine($"{comando} ainda nao esta disponivel. Chega na {fase}.");
        return 3;
    }

    /// <summary>
    /// Presets da secao 8. "seguro" leva so o que o catalogo ja marca por
    /// padrao; "completo" leva tudo que nao esta bloqueado, e por isso avisa
    /// antes do que esta levando junto.
    /// </summary>
    private async Task<int> CleanAsync(CommandLineOptions opcoes, CancellationToken ct)
    {
        if (!opcoes.DryRun && !CoreServices.RodandoComoAdministrador())
            _saida.WriteLine("Aviso: sem privilegios de administrador, partes do sistema ficam de fora.");

        var modulo = _services.GetRequiredService<CleanerModule>();

        _saida.WriteLine("Medindo o que pode ser liberado...");
        var scan = await modulo.ScanAsync(
            new Progress<ModuleProgress>(p => _saida.WriteLine($"  {p.Percentual,3}%  {p.Etapa}")), ct);

        _saida.WriteLine();
        _saida.WriteLine(scan.Resumo);
        _saida.WriteLine();

        var selecionados = Selecionar(scan, opcoes).ToList();

        foreach (var item in scan.Itens)
        {
            var marca = item.Bloqueado ? "[x]" : selecionados.Contains(item.Id) ? "[*]" : "[ ]";
            _saida.WriteLine($"  {marca} {item.Titulo,-42} {item.GanhoEstimado,10}   risco {item.Risco}");

            if (item.MotivoBloqueio is not null)
                _saida.WriteLine($"      {item.MotivoBloqueio}");
        }

        if (selecionados.Count == 0)
        {
            _saida.WriteLine();
            _saida.WriteLine("Nada selecionado. Use --preset seguro, --preset completo ou --categorias.");
            return 0;
        }

        _saida.WriteLine();
        var resultado = await modulo.ApplyAsync(selecionados, opcoes.DryRun, ct);

        foreach (var acao in resultado.Acoes)
            _saida.WriteLine($"  {(acao.Sucesso ? "ok  " : "FALHA")} {acao.Detalhe}");

        _saida.WriteLine();
        _saida.WriteLine(resultado.Resumo);
        _saida.WriteLine(resultado.GanhoMedido);

        return resultado.Falhas > 0 ? 1 : 0;
    }

    private static IEnumerable<string> Selecionar(ScanResult scan, CommandLineOptions opcoes)
    {
        var disponiveis = scan.Itens.Where(i => !i.Bloqueado);

        if (opcoes.Categorias.Count > 0)
        {
            // Casa pelo sufixo do id: "temp" pega temp-usuario e temp-sistema.
            return disponiveis
                .Where(i => opcoes.Categorias.Any(c =>
                    i.Id.Contains(c, StringComparison.OrdinalIgnoreCase)
                    || i.Categoria.Equals(c, StringComparison.OrdinalIgnoreCase)))
                .Select(i => i.Id);
        }

        return opcoes.Preset switch
        {
            "completo" => disponiveis.Select(i => i.Id),
            "seguro" => disponiveis.Where(i => i.PreMarcado).Select(i => i.Id),
            _ => Enumerable.Empty<string>()
        };
    }

    private async Task<int> ReportAsync(CommandLineOptions opcoes, CancellationToken ct)
    {
        var modulo = _services.GetRequiredService<HealthReportModule>();

        _saida.WriteLine("Medindo a maquina por alguns segundos...");

        var progresso = new Progress<ModuleProgress>(p => _saida.WriteLine($"  {p.Percentual,3}%  {p.Etapa}"));
        var relatorio = await modulo.GerarAsync(progresso, ct);

        var destino = opcoes.ArquivoDeSaida;

        if (destino is null)
        {
            _saida.WriteLine();
            _saida.WriteLine(opcoes.Json ? HtmlReportWriter.GerarJson(relatorio) : FormatarRelatorio(relatorio));
            return 0;
        }

        // A extensao decide o formato, e --json continua valendo por cima dela.
        var json = opcoes.Json || destino.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        var conteudo = json ? HtmlReportWriter.GerarJson(relatorio) : HtmlReportWriter.GerarHtml(relatorio);

        File.WriteAllText(destino, conteudo, new UTF8Encoding(false));

        _saida.WriteLine();
        _saida.WriteLine($"Nota geral: {relatorio.Pontuacao.NotaGeral} ({relatorio.Pontuacao.Conceito})");
        _saida.WriteLine($"Relatorio gravado em {Path.GetFullPath(destino)}");
        return 0;
    }

    private static string FormatarRelatorio(HealthSnapshot relatorio)
    {
        var sb = new StringBuilder();
        var p = relatorio.Pontuacao;

        sb.AppendLine("GameBoost - relatorio de saude");
        sb.AppendLine($"Gerado em {relatorio.Momento:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Nota geral: {p.NotaGeral}/100  ({p.Conceito})");
        sb.AppendLine();

        foreach (var area in p.Areas)
            sb.AppendLine($"  {area.Nome,-26} {area.Nota,3}/100  {area.Conceito}");

        sb.AppendLine();

        if (p.Principais.Count == 0)
        {
            sb.AppendLine("Nada fora do lugar. A maquina esta bem cuidada.");
        }
        else
        {
            sb.AppendLine("Principais achados:");
            sb.AppendLine();

            foreach (var f in p.Principais)
            {
                var marca = f.Severidade switch
                {
                    FindingSeverity.Critico => "[!]",
                    FindingSeverity.Atencao => "[*]",
                    _ => "[ ]"
                };

                sb.AppendLine($"  {marca} {f.Titulo}");
                sb.AppendLine($"      {f.Detalhe}");
                if (f.TextoDaAcao is not null)
                    sb.AppendLine($"      Sugestao: {f.TextoDaAcao}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("Nada foi alterado. Este comando so le o sistema.");
        return sb.ToString();
    }

    private async Task<int> ScanAsync(CommandLineOptions opcoes, CancellationToken ct)
    {
        var modulo = _services.GetRequiredService<GameModeModule>();
        var resultado = await modulo.ScanAsync(null, ct);

        var texto = opcoes.Json
            ? SerializarScan(resultado)
            : FormatarScan(resultado);

        if (opcoes.ArquivoDeSaida is not null)
        {
            File.WriteAllText(opcoes.ArquivoDeSaida, texto, new UTF8Encoding(false));
            _saida.WriteLine($"Relatorio gravado em {Path.GetFullPath(opcoes.ArquivoDeSaida)}");
        }
        else
        {
            _saida.WriteLine(texto);
        }

        return 0;
    }

    private static string FormatarScan(ScanResult resultado)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GameBoost - relatorio de varredura");
        sb.AppendLine($"Gerado em {resultado.Momento:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Administrador: {(CoreServices.RodandoComoAdministrador() ? "sim" : "nao (somente leitura)")}");
        sb.AppendLine();
        sb.AppendLine(resultado.Resumo);
        sb.AppendLine();

        foreach (var aviso in resultado.Avisos)
            sb.AppendLine($"  aviso: {aviso}");

        if (resultado.Avisos.Count > 0)
            sb.AppendLine();

        foreach (var grupo in resultado.Itens.GroupBy(i => i.Categoria))
        {
            sb.AppendLine($"== {grupo.Key} ==");
            foreach (var item in grupo)
            {
                var marca = item.Bloqueado ? "[x]" : item.PreMarcado ? "[*]" : "[ ]";
                sb.AppendLine($"  {marca} {item.Titulo,-28} {item.GanhoEstimado,-22} risco {item.Risco}");
                sb.AppendLine($"      {item.Descricao}");
                if (item.MotivoBloqueio is not null)
                    sb.AppendLine($"      protegido: {item.MotivoBloqueio}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("Legenda: [*] vem pre-marcado  [ ] aparece desmarcado  [x] protegido, nao selecionavel");
        sb.AppendLine("Nenhum processo foi encerrado. Este comando so le o sistema.");
        return sb.ToString();
    }

    private static string SerializarScan(ScanResult resultado) =>
        JsonSerializer.Serialize(new
        {
            gerado = resultado.Momento,
            resumo = resultado.Resumo,
            avisos = resultado.Avisos,
            itens = resultado.Itens.Select(i => new
            {
                i.Id, i.Categoria, i.Titulo, i.Descricao,
                risco = i.Risco.ToString(),
                i.GanhoBytes, i.GanhoEstimado, i.PreMarcado, i.Bloqueado, i.MotivoBloqueio
            })
        }, new JsonSerializerOptions { WriteIndented = true });

    private async Task<int> GameModeAsync(CommandLineOptions opcoes, bool ligar, CancellationToken ct)
    {
        if (!opcoes.DryRun && !CoreServices.RodandoComoAdministrador())
        {
            _saida.WriteLine("Este comando precisa de privilegios de administrador.");
            return 4;
        }

        var modulo = _services.GetRequiredService<GameModeModule>();

        if (ligar)
        {
            var scan = await modulo.ScanAsync(null, ct);
            var selecionados = scan.Itens.Where(i => i.PreMarcado && !i.Bloqueado).Select(i => i.Id).ToList();

            _saida.WriteLine($"Ativando o Modo Game com {selecionados.Count} apps pre-marcados...");
            var resultado = await modulo.ApplyAsync(selecionados, opcoes.DryRun, ct);
            Imprimir(resultado);
            return resultado.Falhas > 0 ? 1 : 0;
        }

        _saida.WriteLine("Desligando o Modo Game...");
        var desligar = await modulo.RevertAsync(Array.Empty<string>(), opcoes.DryRun, ct);
        Imprimir(desligar);
        return desligar.Falhas > 0 ? 1 : 0;
    }

    private int ReverterTudo(CommandLineOptions opcoes)
    {
        var rollback = _services.GetRequiredService<IRollbackEngine>();
        var backup = _services.GetRequiredService<IStateBackup>();

        if (!backup.TemPendencias)
        {
            _saida.WriteLine("Nada a reverter: nenhuma alteracao pendente no state-backup.");
            return 0;
        }

        var resultados = rollback.ReverterTudo(opcoes.DryRun);

        foreach (var r in resultados)
            _saida.WriteLine($"  {(r.Sucesso ? "ok  " : "FALHA")} {r.Detalhe}");

        var falhas = resultados.Count(r => !r.Sucesso);
        _saida.WriteLine();
        _saida.WriteLine(opcoes.DryRun
            ? $"Dry-run: {resultados.Count} alteracoes seriam revertidas."
            : $"{resultados.Count - falhas} de {resultados.Count} alteracoes revertidas.");

        return falhas > 0 ? 1 : 0;
    }

    private void Imprimir(ApplyResult resultado)
    {
        foreach (var acao in resultado.Acoes)
            _saida.WriteLine($"  {(acao.Sucesso ? "ok  " : "FALHA")} {acao.Detalhe}");

        _saida.WriteLine();
        _saida.WriteLine(resultado.Resumo);

        if (!string.IsNullOrWhiteSpace(resultado.GanhoMedido))
            _saida.WriteLine(resultado.GanhoMedido);
    }
}
