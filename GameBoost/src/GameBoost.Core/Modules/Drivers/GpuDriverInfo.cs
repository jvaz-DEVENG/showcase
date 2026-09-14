using GameBoost.Core.Abstractions;
using GameBoost.Core.Logging;

namespace GameBoost.Core.Modules.Drivers;

public enum FabricanteGpu
{
    Desconhecido,
    Nvidia,
    Amd,
    Intel
}

/// <summary>Uma placa de vídeo e o driver instalado nela.</summary>
public sealed record PlacaDeVideo
{
    public required string Nome { get; init; }
    public required FabricanteGpu Fabricante { get; init; }

    /// <summary>Versão como o Windows registra: "32.0.15.7680".</summary>
    public required string VersaoDoDriver { get; init; }

    /// <summary>
    /// Versão como o fabricante anuncia: "576.80". Null quando não dá para
    /// traduzir — só a NVIDIA tem uma regra estável para isso.
    /// </summary>
    public string? VersaoDeMarketing { get; init; }

    public DateTime? DataDoDriver { get; init; }

    public int? MesesDeIdade => DataDoDriver is null
        ? null
        : (int)((DateTime.Now - DataDoDriver.Value).TotalDays / 30.44);

    public string PaginaOficial => Fabricante switch
    {
        FabricanteGpu.Nvidia => "https://www.nvidia.com/pt-br/geforce/drivers/",
        FabricanteGpu.Amd => "https://www.amd.com/pt/support",
        FabricanteGpu.Intel => "https://www.intel.com.br/content/www/br/pt/download-center/home.html",
        _ => "https://support.microsoft.com/pt-br/windows/atualizar-drivers-manualmente-no-windows-ec62f46c-ff14-c91d-eead-d7126dc1f7b6"
    };
}

/// <summary>
/// Detecção do driver de vídeo (seção 5.10).
///
/// **O GameBoost nunca instala driver.** Ele diz qual está instalado, há quanto
/// tempo, e abre a página oficial do fabricante. Baixar e instalar driver por
/// conta própria é como ferramenta de "otimização" vira vetor de malware, e o
/// ganho para o usuário seria zero — ele clica no mesmo link de qualquer jeito.
///
/// A leitura vem do registro, não do WMI. `Win32_VideoController` traria a
/// mesma informação, mas uma consulta WMI custa entre 200 ms e 2 s na primeira
/// chamada, e a página de Diagnóstico já tem orçamento apertado de tempo.
/// </summary>
public sealed class GpuDriverInfo
{
    /// <summary>Classe de dispositivo "Adaptadores de vídeo" no registro.</summary>
    private const string ClasseDeVideo =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private readonly IRegistryService _registro;
    private readonly IGameBoostLogger _log;

    public GpuDriverInfo(IRegistryService registro, IGameBoostLogger log)
    {
        _registro = registro;
        _log = log;
    }

    public IReadOnlyList<PlacaDeVideo> Listar()
    {
        var placas = new List<PlacaDeVideo>();

        foreach (var sub in _registro.GetSubKeyNames(RegistryRoot.LocalMachine, ClasseDeVideo))
        {
            // As subchaves de interesse são "0000", "0001"... O resto
            // (Configuration, Properties) não descreve adaptador.
            if (sub.Length != 4 || !sub.All(char.IsDigit))
                continue;

            var caminho = $@"{ClasseDeVideo}\{sub}";

            var nome = _registro.GetValue(RegistryRoot.LocalMachine, caminho, "DriverDesc") as string;
            var versao = _registro.GetValue(RegistryRoot.LocalMachine, caminho, "DriverVersion") as string;

            if (string.IsNullOrWhiteSpace(nome) || string.IsNullOrWhiteSpace(versao))
                continue;

            if (EhAdaptadorVirtual(nome))
                continue;

            var fornecedor = _registro.GetValue(RegistryRoot.LocalMachine, caminho, "ProviderName") as string;
            var fabricante = Identificar(fornecedor, nome);

            placas.Add(new PlacaDeVideo
            {
                Nome = nome,
                Fabricante = fabricante,
                VersaoDoDriver = versao,
                VersaoDeMarketing = Traduzir(fabricante, versao),
                DataDoDriver = LerData(caminho)
            });
        }

        if (placas.Count == 0)
            _log.Warn("drivers", "Listar", null, "nenhum adaptador de vídeo encontrado no registro");

        // GPU dedicada primeiro: em notebook com iGPU + dGPU, a que importa
        // para jogo é a dedicada, e ela costuma vir depois na ordem do registro.
        return placas
            .OrderBy(p => p.Fabricante == FabricanteGpu.Intel ? 1 : 0)
            .ToList();
    }

    private DateTime? LerData(string caminho)
    {
        var bruto = _registro.GetValue(RegistryRoot.LocalMachine, caminho, "DriverDate") as string;

        // O formato gravado é M-D-YYYY, sempre com este separador e sempre nesta
        // ordem, independente do idioma do Windows.
        if (bruto is not null
            && DateTime.TryParseExact(bruto, "M-d-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var data))
        {
            return data;
        }

        return null;
    }

    /// <summary>
    /// Descarta adaptador que não é placa de vídeo: display virtual do
    /// Hyper-V, o adaptador básico que o Windows usa antes do driver real,
    /// monitor de sessão remota, captura de tela de software.
    ///
    /// Isto não é preciosismo. Todos esses drivers vêm carimbados com
    /// 21/06/2006, a data que a Microsoft usa para driver embutido no Windows.
    /// Sem o filtro, a tela anunciaria "seu driver de vídeo tem 242 meses" numa
    /// máquina cujo driver real foi instalado no mês passado.
    /// </summary>
    internal static bool EhAdaptadorVirtual(string nome)
    {
        string[] marcadores =
        {
            "hyper-v", "basic display", "basic render", "vídeo básico", "video basico",
            "remote display", "rdp", "citrix", "vmware", "virtualbox", "parsec",
            "meta virtual", "idd", "indirect display", "oray", "sunshine", "usb display"
        };

        return marcadores.Any(m => nome.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private static FabricanteGpu Identificar(string? fornecedor, string nome)
    {
        var texto = $"{fornecedor} {nome}".ToLowerInvariant();

        if (texto.Contains("nvidia"))
            return FabricanteGpu.Nvidia;

        if (texto.Contains("advanced micro") || texto.Contains("amd") || texto.Contains("radeon"))
            return FabricanteGpu.Amd;

        if (texto.Contains("intel"))
            return FabricanteGpu.Intel;

        return FabricanteGpu.Desconhecido;
    }

    /// <summary>
    /// Traduz a versão do Windows para a que o fabricante anuncia.
    ///
    /// Só a NVIDIA tem regra estável: os cinco últimos dígitos da versão, com
    /// ponto antes dos dois finais. "32.0.15.7680" vira "576.80". AMD e Intel
    /// não têm correspondência previsível entre a versão do driver e o nome
    /// comercial (Adrenalin 25.x), então aqui devolve null em vez de chutar.
    /// </summary>
    internal static string? Traduzir(FabricanteGpu fabricante, string versao)
    {
        if (fabricante != FabricanteGpu.Nvidia)
            return null;

        var digitos = new string(versao.Where(char.IsDigit).ToArray());

        if (digitos.Length < 5)
            return null;

        var ultimos = digitos[^5..];
        return $"{ultimos[..3]}.{ultimos[3..]}";
    }
}
