using GameBoost.Core.Modules;
using GameBoost.Core.Modules.DiskAnalyzer;
using Xunit;

namespace GameBoost.Tests;

public sealed class DiskNodeTests
{
    private static DiskNode Pasta(string nome, DiskNode? pai = null)
    {
        var no = pai is null ? new DiskNode(nome) : new DiskNode(nome, ehPasta: true, pai);
        pai?.Filhos.Add(no);
        return no;
    }

    private static DiskNode Arquivo(string nome, long bytes, DiskNode pai, DiskCategory categoria = DiskCategory.Outros)
    {
        var no = new DiskNode(nome, ehPasta: false, pai)
        {
            TamanhoProprio = bytes,
            Categoria = categoria
        };
        pai.Filhos.Add(no);
        return no;
    }

    [Fact]
    public void Consolidar_soma_a_subarvore_de_baixo_para_cima()
    {
        var raiz = Pasta(@"C:\");
        var sub = Pasta("jogos", raiz);
        var subsub = Pasta("cs2", sub);

        Arquivo("a.bin", 100, subsub);
        Arquivo("b.bin", 200, subsub);
        Arquivo("c.bin", 50, sub);
        Arquivo("d.bin", 7, raiz);

        raiz.Consolidar();

        Assert.Equal(300, subsub.Tamanho);
        Assert.Equal(350, sub.Tamanho);
        Assert.Equal(357, raiz.Tamanho);
        Assert.Equal(4, raiz.TotalDeArquivos);
    }

    [Fact]
    public void Pasta_vazia_tem_tamanho_zero()
    {
        var raiz = Pasta(@"C:\");
        Pasta("vazia", raiz);

        raiz.Consolidar();

        Assert.Equal(0, raiz.Tamanho);
        Assert.Equal(0, raiz.TotalDeArquivos);
    }

    [Fact]
    public void Arvore_muito_profunda_nao_estoura_a_pilha()
    {
        // Consolidar e iterativo de proposito: node_modules e pastas de build
        // chegam a profundidades que quebrariam uma versao recursiva.
        var raiz = Pasta(@"C:\");
        var atual = raiz;

        for (var i = 0; i < 20_000; i++)
            atual = Pasta($"n{i}", atual);

        Arquivo("fundo.bin", 42, atual);

        raiz.Consolidar();

        Assert.Equal(42, raiz.Tamanho);
        Assert.Equal(1, raiz.TotalDeArquivos);
    }

    [Fact]
    public void Pasta_herda_a_categoria_que_ocupa_mais_espaco()
    {
        var raiz = Pasta(@"C:\");
        var mista = Pasta("mista", raiz);

        Arquivo("filme.mkv", 10_000, mista, DiskCategory.Videos);
        Arquivo("foto.jpg", 10, mista, DiskCategory.Imagens);

        raiz.Consolidar();

        Assert.Equal(DiskCategory.Videos, mista.Categoria);
    }

    [Fact]
    public void Maiores_arquivos_vem_ordenados_e_sem_pasta()
    {
        var raiz = Pasta(@"C:\");
        var sub = Pasta("sub", raiz);

        Arquivo("pequeno.bin", 10, raiz);
        Arquivo("grande.bin", 9000, sub);
        Arquivo("medio.bin", 500, sub);

        raiz.Consolidar();

        var maiores = raiz.MaioresArquivos(3).ToList();

        Assert.Equal(new[] { "grande.bin", "medio.bin", "pequeno.bin" }, maiores.Select(n => n.Nome));
        Assert.All(maiores, n => Assert.False(n.EhPasta));
    }

    [Fact]
    public void Maiores_pastas_nao_incluem_a_propria_raiz()
    {
        var raiz = Pasta(@"C:\");
        var sub = Pasta("sub", raiz);
        Arquivo("x.bin", 1000, sub);

        raiz.Consolidar();

        var pastas = raiz.MaioresPastas(5).ToList();

        Assert.DoesNotContain(raiz, pastas);
        Assert.Contains(sub, pastas);
    }
}

public sealed class DiskCategoriesTests
{
    [Theory]
    [InlineData(@"D:\SteamLibrary\steamapps\common\cs2\game.dll", DiskCategory.Jogos)]
    [InlineData(@"C:\Users\User\Videos\filme.mkv", DiskCategory.Videos)]
    [InlineData(@"C:\Users\User\Pictures\foto.jpg", DiskCategory.Imagens)]
    [InlineData(@"C:\Users\User\Music\musica.flac", DiskCategory.Musica)]
    [InlineData(@"C:\Users\User\Documents\contrato.pdf", DiskCategory.Documentos)]
    [InlineData(@"D:\repo\src\Program.cs", DiskCategory.Codigo)]
    public void Classifica_pela_extensao_e_pela_pasta(string caminho, DiskCategory esperada)
    {
        Assert.Equal(esperada, DiskCategories.Classificar(caminho, 1024 * 1024));
    }

    [Fact]
    public void Pasta_de_jogo_vence_a_extensao()
    {
        // Um .mp4 dentro do jogo e recurso do jogo, nao filme do usuario.
        var caminho = @"D:\SteamLibrary\steamapps\common\jogo\video\intro.mp4";

        Assert.Equal(DiskCategory.Jogos, DiskCategories.Classificar(caminho, 50_000_000));
    }

    [Fact]
    public void Executavel_pequeno_nao_conta_como_instalador()
    {
        // Senao todo .exe de programa viraria "instalador esquecido".
        var caminho = @"C:\Program Files\App\app.exe";

        Assert.NotEqual(DiskCategory.Instaladores, DiskCategories.Classificar(caminho, 200 * 1024));
        Assert.Equal(DiskCategory.Instaladores, DiskCategories.Classificar(caminho, 80L * 1024 * 1024));
    }

    [Fact]
    public void Arquivo_dentro_do_windows_e_sistema()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.Equal(DiskCategory.Sistema,
            DiskCategories.Classificar(Path.Combine(windows, "System32", "algo.dll"), 1024));
    }

    [Fact]
    public void Toda_categoria_tem_nome_e_cor()
    {
        foreach (var categoria in Enum.GetValues<DiskCategory>())
        {
            Assert.False(string.IsNullOrWhiteSpace(DiskCategories.Nome(categoria)));
            Assert.Matches("^#FF[0-9A-Fa-f]{6}$", DiskCategories.Cor(categoria));
        }
    }
}
