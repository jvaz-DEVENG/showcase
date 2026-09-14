using System.Text;
using System.Text.Json;
using GameBoost.Core;
using GameBoost.Core.Modules;
using GameBoost.Core.Modules.GameMode;
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
            CliCommand.Report => NaoImplementado("--report", "Fase 1 (Relatorio de saude, secao 5.12)"),
            CliCommand.Clean => NaoImplementado("--clean", "Fase 2 (Limpeza, secao 5.2)"),
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
