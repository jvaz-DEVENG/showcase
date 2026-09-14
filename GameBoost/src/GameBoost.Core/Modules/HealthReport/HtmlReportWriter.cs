using System.Net;
using System.Text;
using System.Text.Json;
using GameBoost.Core.Modules.Bottleneck;
using GameBoost.Core.Modules.GameMode;

namespace GameBoost.Core.Modules.HealthReport;

/// <summary>
/// Exporta o relatorio de saude (secao 5.12).
///
/// O HTML e um arquivo unico, sem link externo nenhum: o usuario manda para
/// quem da suporte e abre em qualquer lugar. Todo texto vindo da maquina passa
/// por escape, porque nome de processo e titulo de janela sao conteudo
/// arbitrario e acabariam quebrando a pagina.
/// </summary>
public static class HtmlReportWriter
{
    public static string GerarHtml(HealthSnapshot relatorio)
    {
        var sb = new StringBuilder();
        var p = relatorio.Pontuacao;

        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>GameBoost - Relatorio de saude</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style></head><body>");

        sb.AppendLine("<header>");
        sb.AppendLine("<h1>GameBoost</h1>");
        sb.AppendLine($"<p class=\"sub\">Relatorio de saude gerado em {E(relatorio.Momento.ToString("dd/MM/yyyy HH:mm"))}</p>");
        sb.AppendLine("</header>");

        // Nota geral
        sb.AppendLine("<section class=\"nota\">");
        sb.AppendLine($"<div class=\"circulo {Classe(p.NotaGeral)}\"><span>{p.NotaGeral}</span></div>");
        sb.AppendLine($"<div><h2>{E(p.Conceito)}</h2><p class=\"sub\">Media das quatro areas avaliadas.</p></div>");
        sb.AppendLine("</section>");

        // Areas
        sb.AppendLine("<section><h2>Areas</h2><div class=\"areas\">");
        foreach (var area in p.Areas)
        {
            sb.AppendLine("<div class=\"area\">");
            sb.AppendLine($"<div class=\"area-nome\">{E(area.Nome)}</div>");
            sb.AppendLine($"<div class=\"barra\"><div class=\"preenche {Classe(area.Nota)}\" style=\"width:{area.Nota}%\"></div></div>");
            sb.AppendLine($"<div class=\"area-nota\">{area.Nota} <span class=\"sub\">{E(area.Conceito)}</span></div>");
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div></section>");

        // Findings
        sb.AppendLine("<section><h2>O que encontramos</h2>");
        var todos = p.Areas.SelectMany(a => a.Findings)
            .OrderByDescending(f => f.Severidade)
            .ThenByDescending(f => f.Impacto)
            .ToList();

        if (todos.Count == 0)
        {
            sb.AppendLine("<p class=\"vazio\">Nada fora do lugar. A maquina esta bem cuidada.</p>");
        }
        else
        {
            foreach (var f in todos)
            {
                sb.AppendLine($"<article class=\"finding {Severidade(f.Severidade)}\">");
                sb.AppendLine($"<h3>{E(f.Titulo)}</h3>");
                sb.AppendLine($"<p>{E(f.Detalhe)}</p>");
                if (f.TextoDaAcao is not null)
                    sb.AppendLine($"<p class=\"acao\">Sugestao: {E(f.TextoDaAcao)}</p>");
                sb.AppendLine("</article>");
            }
        }

        sb.AppendLine("</section>");

        // Medicoes
        sb.AppendLine("<section><h2>Medicoes no momento do relatorio</h2><table>");
        var m = relatorio.Metricas;
        Linha(sb, "CPU", $"{m.CpuPercent:0}%");
        Linha(sb, "Memoria", $"{GameModeModule.Formatar(m.RamTotalBytes - m.RamDisponivelBytes)} de {GameModeModule.Formatar(m.RamTotalBytes)} ({m.RamUsadaPercent:0}%)");
        Linha(sb, "Memoria livre", GameModeModule.Formatar(m.RamDisponivelBytes));
        Linha(sb, "Standby List", m.StandbyBytes > 0 ? GameModeModule.Formatar(m.StandbyBytes) : "indisponivel");
        Linha(sb, "GPU", m.GpuPercent.Disponivel ? $"{m.GpuPercent.Valor:0}%" : "indisponivel");
        Linha(sb, "Disco do sistema", $"{m.DiscoSistemaUsadoPercent:0}% ocupado");
        Linha(sb, "Rede", m.RedeBytesPorSegundo.Disponivel ? $"{GameModeModule.Formatar((long)m.RedeBytesPorSegundo.Valor)}/s" : "indisponivel");
        Linha(sb, "Temperatura da CPU", m.TemperaturaCpu.Disponivel ? $"{m.TemperaturaCpu.Valor:0} C" : "indisponivel");
        sb.AppendLine("</table></section>");

        // Sistema
        sb.AppendLine("<section><h2>Sistema</h2><table>");
        var f2 = relatorio.Fatos;
        Linha(sb, "Windows", f2.BuildDoWindows ?? "desconhecido");
        Linha(sb, "Nucleos logicos", m.NucleosLogicos.ToString());
        Linha(sb, "Plano de energia", f2.NomeDoPlanoDeEnergia ?? "desconhecido");
        Linha(sb, "Placa de video", f2.FabricanteDaGpu ?? "desconhecida");
        Linha(sb, "Driver", f2.VersaoDoDriver ?? "desconhecido");
        Linha(sb, "Data do driver", f2.DataDoDriver?.ToString("MM/yyyy") ?? "desconhecida");
        Linha(sb, "Monitor", f2.TaxaDeAtualizacaoAtual > 0 ? $"{f2.TaxaDeAtualizacaoAtual} Hz (maximo {f2.TaxaDeAtualizacaoMaxima} Hz)" : "desconhecido");
        Linha(sb, "Isolamento do nucleo (VBS)", f2.VbsAtivo ? "ativo" : "desligado");
        Linha(sb, "Programas na inicializacao", f2.ItensNaInicializacao.ToString());
        Linha(sb, "Pentes de memoria", f2.PentesDeMemoria > 0 ? f2.PentesDeMemoria.ToString() : "desconhecido");
        Linha(sb, "Jogo em execucao", relatorio.JogoEmExecucao ?? "nenhum detectado");
        sb.AppendLine("</table></section>");

        // Processos
        sb.AppendLine("<section><h2>Processos que mais consomem</h2><table>");
        sb.AppendLine("<tr><th>Processo</th><th>CPU</th><th>Memoria</th></tr>");
        foreach (var proc in m.Processos.Take(10))
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{E(proc.NomeExibido)}</td>");
            sb.AppendLine($"<td>{proc.CpuPercent:0.0}%</td>");
            sb.AppendLine($"<td>{E(GameModeModule.Formatar(proc.WorkingSetBytes))}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</table></section>");

        sb.AppendLine("<footer><p>Gerado pelo GameBoost. Nenhum dado saiu desta maquina: "
                    + "este arquivo foi escrito localmente e so vai para onde voce mandar.</p></footer>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    public static string GerarJson(HealthSnapshot relatorio)
    {
        var m = relatorio.Metricas;

        return JsonSerializer.Serialize(new
        {
            gerado = relatorio.Momento,
            notaGeral = relatorio.Pontuacao.NotaGeral,
            conceito = relatorio.Pontuacao.Conceito,
            areas = relatorio.Pontuacao.Areas.Select(a => new { area = a.Nome, nota = a.Nota, conceito = a.Conceito }),
            findings = relatorio.Pontuacao.Areas.SelectMany(a => a.Findings).Select(f => new
            {
                f.RegraId,
                f.Titulo,
                f.Detalhe,
                severidade = f.Severidade.ToString(),
                area = f.Area.ToString(),
                f.Impacto,
                f.Penalidade,
                acao = f.TextoDaAcao
            }),
            metricas = new
            {
                cpuPercent = Math.Round(m.CpuPercent, 1),
                ramTotalBytes = m.RamTotalBytes,
                ramDisponivelBytes = m.RamDisponivelBytes,
                standbyBytes = m.StandbyBytes,
                gpuPercent = m.GpuPercent.Disponivel ? Math.Round(m.GpuPercent.Valor, 1) : (double?)null,
                discoSistemaUsadoPercent = Math.Round(m.DiscoSistemaUsadoPercent, 1),
                redeBytesPorSegundo = m.RedeBytesPorSegundo.Disponivel ? Math.Round(m.RedeBytesPorSegundo.Valor) : (double?)null,
                temperaturaCpu = m.TemperaturaCpu.Disponivel ? Math.Round(m.TemperaturaCpu.Valor, 1) : (double?)null,
                nucleosLogicos = m.NucleosLogicos
            },
            sistema = new
            {
                relatorio.Fatos.BuildDoWindows,
                relatorio.Fatos.NomeDoPlanoDeEnergia,
                relatorio.Fatos.FabricanteDaGpu,
                relatorio.Fatos.VersaoDoDriver,
                relatorio.Fatos.DataDoDriver,
                relatorio.Fatos.TaxaDeAtualizacaoAtual,
                relatorio.Fatos.TaxaDeAtualizacaoMaxima,
                relatorio.Fatos.VbsAtivo,
                relatorio.Fatos.ItensNaInicializacao,
                relatorio.Fatos.PentesDeMemoria
            },
            jogoEmExecucao = relatorio.JogoEmExecucao,
            processos = m.Processos.Take(10).Select(p => new
            {
                p.Nome,
                cpuPercent = Math.Round(p.CpuPercent, 1),
                p.WorkingSetBytes,
                p.Instancias
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void Linha(StringBuilder sb, string rotulo, string valor)
        => sb.AppendLine($"<tr><td>{E(rotulo)}</td><td>{E(valor)}</td></tr>");

    /// <summary>Nome de processo e titulo de janela sao conteudo arbitrario.</summary>
    private static string E(string? texto) => WebUtility.HtmlEncode(texto ?? string.Empty);

    private static string Classe(int nota) => nota switch
    {
        >= 75 => "bom",
        >= 50 => "medio",
        _ => "ruim"
    };

    private static string Severidade(FindingSeverity s) => s switch
    {
        FindingSeverity.Critico => "critico",
        FindingSeverity.Atencao => "atencao",
        _ => "info"
    };

    private const string Css = """
        :root { color-scheme: dark; }
        * { box-sizing: border-box; }
        body { margin: 0; padding: 32px 20px; background: #14161a; color: #edeff2;
               font: 15px/1.55 "Segoe UI", system-ui, sans-serif; }
        header, section, footer { max-width: 860px; margin: 0 auto 28px; }
        h1 { font-size: 26px; margin: 0; }
        h2 { font-size: 18px; margin: 0 0 14px; }
        h3 { font-size: 15px; margin: 0 0 6px; }
        p { margin: 0 0 8px; }
        .sub { color: #9aa3b0; font-size: 13px; }
        .nota { display: flex; align-items: center; gap: 20px; background: #1c1f26;
                border: 1px solid #2e333d; border-radius: 10px; padding: 20px; }
        .circulo { width: 78px; height: 78px; border-radius: 50%; display: grid; place-items: center;
                   font-size: 26px; font-weight: 700; flex: none; border: 3px solid; }
        .circulo.bom { border-color: #3fb950; color: #3fb950; }
        .circulo.medio { border-color: #d29922; color: #d29922; }
        .circulo.ruim { border-color: #f85149; color: #f85149; }
        .areas { display: grid; gap: 12px; }
        .area { display: grid; grid-template-columns: 190px 1fr 130px; gap: 14px; align-items: center; }
        .barra { background: #1c1f26; border-radius: 5px; height: 10px; overflow: hidden; }
        .preenche { height: 100%; }
        .preenche.bom { background: #3fb950; }
        .preenche.medio { background: #d29922; }
        .preenche.ruim { background: #f85149; }
        .area-nota { font-weight: 600; }
        .finding { background: #1c1f26; border: 1px solid #2e333d; border-left-width: 3px;
                   border-radius: 8px; padding: 14px 16px; margin-bottom: 10px; }
        .finding.critico { border-left-color: #f85149; }
        .finding.atencao { border-left-color: #d29922; }
        .finding.info { border-left-color: #00c2a8; }
        .finding p { color: #9aa3b0; font-size: 14px; }
        .acao { color: #00c2a8 !important; }
        .vazio { color: #3fb950; }
        table { width: 100%; border-collapse: collapse; background: #1c1f26;
                border: 1px solid #2e333d; border-radius: 8px; overflow: hidden; }
        td, th { padding: 9px 14px; border-bottom: 1px solid #2e333d; text-align: left; font-size: 14px; }
        th { color: #9aa3b0; font-weight: 600; }
        tr:last-child td { border-bottom: 0; }
        td:first-child { color: #9aa3b0; }
        footer p { color: #5e6672; font-size: 12px; }
        @media (max-width: 640px) {
          .area { grid-template-columns: 1fr; gap: 4px; }
          .nota { flex-direction: column; text-align: center; }
        }
        """;
}
