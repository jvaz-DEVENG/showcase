using GameBoost.Core.Modules;
using GameBoost.Core.Modules.Tools;
using Xunit;

namespace GameBoost.Tests;

public sealed class QuickToolsCatalogTests
{
    [Fact]
    public void Catalogo_nao_tem_ids_repetidos()
    {
        var ids = QuickToolsCatalog.Todas().Select(t => t.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Toda_ferramenta_explica_como_desfazer()
    {
        // Seção 6: cada item precisa responder "o que faz, qual o risco, como desfazer".
        Assert.All(QuickToolsCatalog.Todas(), f =>
            Assert.False(string.IsNullOrWhiteSpace(f.ComoDesfazer), $"{f.Id} sem ComoDesfazer"));
    }

    [Fact]
    public void Ferramenta_que_nao_da_para_desfazer_pede_confirmacao()
    {
        // Esvaziar a Lixeira é o caso: depois dela não há volta.
        var lixeira = QuickToolsCatalog.Todas().Single(f => f.Id == QuickToolsCatalog.EsvaziarLixeira);

        Assert.NotNull(lixeira.Confirmacao);
        Assert.Contains("não poderão ser recuperados", lixeira.ComoDesfazer + lixeira.Confirmacao);
        Assert.Equal(RiskLevel.Medio, lixeira.Risco);
    }

    [Fact]
    public void Ferramenta_demorada_avisa_quanto_tempo_leva()
    {
        // Sem isso o usuário acha que travou e mata o app no meio de um sfc.
        Assert.All(QuickToolsCatalog.Todas().Where(f => f.Tipo == ToolKind.Demorada),
            f => Assert.False(string.IsNullOrWhiteSpace(f.Duracao), $"{f.Id} sem duracao"));
    }

    [Fact]
    public void Ferramenta_de_risco_medio_ou_alto_sempre_confirma_antes()
    {
        Assert.All(QuickToolsCatalog.Todas().Where(f => f.Risco != RiskLevel.Baixo),
            f => Assert.False(string.IsNullOrWhiteSpace(f.Confirmacao), $"{f.Id} nao confirma"));
    }

    [Fact]
    public void Ferramenta_de_leitura_nunca_pede_confirmacao_nem_admin()
    {
        // Só lê: pedir confirmação para isso seria ruído.
        Assert.All(QuickToolsCatalog.Todas().Where(f => f.Tipo == ToolKind.Leitura), f =>
        {
            Assert.Null(f.Confirmacao);
            Assert.False(f.PrecisaAdmin);
            Assert.Equal(RiskLevel.Baixo, f.Risco);
        });
    }

    [Theory]
    [InlineData(QuickToolsCatalog.PontoDeRestauracao)]
    [InlineData(QuickToolsCatalog.VerificarIntegridade)]
    [InlineData(QuickToolsCatalog.RepararImagem)]
    public void Ferramenta_que_mexe_no_sistema_exige_admin(string id)
    {
        Assert.True(QuickToolsCatalog.Todas().Single(f => f.Id == id).PrecisaAdmin);
    }

    [Fact]
    public void Catalogo_cobre_o_que_a_secao_5_13_pede()
    {
        var ids = QuickToolsCatalog.Todas().Select(f => f.Id).ToHashSet();

        foreach (var esperado in new[]
                 {
                     QuickToolsCatalog.ReiniciarExplorer, QuickToolsCatalog.ReiniciarVideo,
                     QuickToolsCatalog.LimparDns, QuickToolsCatalog.EsvaziarLixeira,
                     QuickToolsCatalog.LimpezaDeDisco, QuickToolsCatalog.PontoDeRestauracao,
                     QuickToolsCatalog.VerificarIntegridade, QuickToolsCatalog.TesteDeDisco,
                     QuickToolsCatalog.InfoDoSistema
                 })
        {
            Assert.Contains(esperado, ids);
        }
    }

    [Fact]
    public async Task Ferramenta_desconhecida_nao_derruba_o_servico()
    {
        var servico = new QuickToolsService(
            new FakeProcessosVazio(), new FakeMemoria(), new FakeLogger());

        var resultado = await servico.ExecutarAsync("inexistente", null, CancellationToken.None);

        Assert.False(resultado.Sucesso);
        Assert.Contains("desconhecida", resultado.Mensagem);
    }

    private sealed class FakeProcessosVazio : Core.Abstractions.IProcessService
    {
        public IReadOnlyList<Core.Abstractions.ProcessInfo> GetProcesses() => Array.Empty<Core.Abstractions.ProcessInfo>();
        public Core.Abstractions.ProcessInfo? GetProcess(int pid) => null;
        public bool TryCloseGracefully(int pid, TimeSpan timeout) => true;
        public bool SetAffinity(int pid, IReadOnlyList<int> nucleos) => true;
        public IReadOnlyList<int> GetAffinity(int pid) => Array.Empty<int>();
        public bool Kill(int pid) => true;
        public bool SetPriority(int pid, Core.Abstractions.ProcessPriority priority) => true;
        public Core.Abstractions.ProcessPriority? GetPriority(int pid) => null;
        public bool Start(string executablePath, string? arguments, string? workingDirectory) => true;
        public long TrimWorkingSet(int pid) => 0;
    }

    private sealed class FakeMemoria : Core.Abstractions.IMemoryService
    {
        public Core.Abstractions.MemorySnapshot GetSnapshot() => new(16_000_000_000, 8_000_000_000, 0);
        public bool PurgeStandbyList() => true;
    }
}
