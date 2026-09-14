namespace GameBoost.Core.Modules.Tools;

/// <summary>Catálogo das ferramentas rápidas (seção 5.13).</summary>
public static class QuickToolsCatalog
{
    public const string ReiniciarExplorer = "reiniciar-explorer";
    public const string ReiniciarVideo = "reiniciar-video";
    public const string LimparDns = "limpar-dns";
    public const string EsvaziarLixeira = "esvaziar-lixeira";
    public const string LimpezaDeDisco = "limpeza-de-disco";
    public const string PontoDeRestauracao = "ponto-de-restauracao";
    public const string VerificarIntegridade = "verificar-integridade";
    public const string RepararImagem = "reparar-imagem";
    public const string TesteDeDisco = "teste-de-disco";
    public const string InfoDoSistema = "info-do-sistema";

    public static IReadOnlyList<QuickTool> Todas() => new[]
    {
        new QuickTool
        {
            Id = ReiniciarExplorer,
            Categoria = "Interface",
            Titulo = "Reiniciar o Explorer",
            Descricao = "Recarrega a barra de tarefas e o menu Iniciar. Resolve barra travada, "
                      + "ícone sumido e menu que não abre.",
            ComoDesfazer = "Não é preciso: o Explorer volta sozinho em segundos. "
                         + "Suas janelas de pasta abertas fecham.",
            Confirmacao = "A barra de tarefas vai sumir por alguns segundos e as janelas de pasta "
                        + "abertas serão fechadas. Continuar?"
        },

        new QuickTool
        {
            Id = ReiniciarVideo,
            Categoria = "Interface",
            Titulo = "Reiniciar o driver de vídeo",
            Descricao = "O mesmo que Win+Ctrl+Shift+B. Resolve tela preta, artefato e travamento "
                      + "de vídeo sem reiniciar o PC.",
            ComoDesfazer = "Não é preciso: o driver recarrega sozinho.",
            Confirmacao = "A tela vai piscar e ficar preta por um instante.\n\n"
                        + "Feche jogos e programas de vídeo antes: alguns não sobrevivem ao "
                        + "reinício do driver. Continuar?",
            Risco = RiskLevel.Medio
        },

        new QuickTool
        {
            Id = LimparDns,
            Categoria = "Rede",
            Titulo = "Limpar o cache de DNS",
            Descricao = "Esquece os endereços de sites guardados. Resolve site que não abre "
                      + "depois de trocar de servidor ou de rede.",
            ComoDesfazer = "Não é preciso: o cache se refaz sozinho ao navegar."
        },

        new QuickTool
        {
            Id = EsvaziarLixeira,
            Categoria = "Espaço",
            Titulo = "Esvaziar a Lixeira",
            Descricao = "Remove de vez o que está na Lixeira.",
            ComoDesfazer = "Não há. Depois disso os arquivos não voltam.",
            Confirmacao = "Os arquivos na Lixeira serão apagados de vez e não poderão ser "
                        + "recuperados. Continuar?",
            Risco = RiskLevel.Medio
        },

        new QuickTool
        {
            Id = LimpezaDeDisco,
            Categoria = "Espaço",
            Titulo = "Abrir a Limpeza de Disco do Windows",
            Descricao = "A ferramenta do próprio Windows, que alcança coisas que o GameBoost "
                      + "não mexe, como instalações anteriores do Windows.",
            Tipo = ToolKind.Atalho,
            ComoDesfazer = "O GameBoost só abre a ferramenta. O que acontece lá é decidido por você."
        },

        new QuickTool
        {
            Id = PontoDeRestauracao,
            Categoria = "Segurança",
            Titulo = "Criar ponto de restauração",
            Descricao = "Uma foto do sistema para voltar atrás se algo der errado depois.",
            PrecisaAdmin = true,
            Duracao = "costuma levar de 30 segundos a 2 minutos",
            ComoDesfazer = "O ponto fica salvo. Para usá-lo, abra Restauração do Sistema no Windows.",
            Confirmacao = "Criar um ponto de restauração agora?\n\n"
                        + "Por padrão o Windows só aceita um ponto a cada 24 horas: se já houver "
                        + "um recente, ele pode recusar."
        },

        new QuickTool
        {
            Id = VerificarIntegridade,
            Categoria = "Segurança",
            Titulo = "Verificar arquivos do Windows (sfc)",
            Descricao = "Procura arquivos de sistema corrompidos e repara o que encontrar.",
            Tipo = ToolKind.Demorada,
            PrecisaAdmin = true,
            Duracao = "de 5 a 20 minutos",
            ComoDesfazer = "Não é preciso: a verificação só repara arquivos do próprio Windows, "
                         + "e não toca nos seus arquivos.",
            Confirmacao = "A verificação leva de 5 a 20 minutos e usa bastante disco.\n\n"
                        + "Não é para rodar antes de jogar. Continuar?"
        },

        new QuickTool
        {
            Id = RepararImagem,
            Categoria = "Segurança",
            Titulo = "Reparar a imagem do Windows (DISM)",
            Descricao = "Conserta a base que o sfc usa para reparar. Vale quando o sfc encontra "
                      + "erro e não consegue corrigir.",
            Tipo = ToolKind.Demorada,
            PrecisaAdmin = true,
            Duracao = "de 10 a 30 minutos, e usa internet",
            ComoDesfazer = "Não é preciso.",
            Confirmacao = "O DISM baixa arquivos do Windows Update e leva de 10 a 30 minutos.\n\n"
                        + "Rode isto quando o sfc encontrar erro que não consegue corrigir. Continuar?"
        },

        new QuickTool
        {
            Id = TesteDeDisco,
            Categoria = "Diagnóstico",
            Titulo = "Testar a velocidade do disco",
            Descricao = "Mede leitura e escrita sequencial com um arquivo temporário de 1 GB. "
                      + "Serve para saber se o disco é SSD de verdade e se está saudável.",
            Tipo = ToolKind.Demorada,
            Duracao = "de 10 a 60 segundos, conforme o disco",
            ComoDesfazer = "Não é preciso: o arquivo de teste é apagado no fim, inclusive se o "
                         + "teste falhar.",
            Confirmacao = "O teste grava 1 GB temporário no disco do sistema e apaga em seguida.\n\n"
                        + "Feche programas pesados para a medição não sair distorcida. Continuar?"
        },

        new QuickTool
        {
            Id = InfoDoSistema,
            Categoria = "Diagnóstico",
            Titulo = "Informações do sistema",
            Descricao = "Processador, memória, placa de vídeo, discos e versão do Windows, "
                      + "num texto pronto para copiar e mandar a quem dá suporte.",
            Tipo = ToolKind.Leitura,
            ComoDesfazer = "Não altera nada: só lê."
        }
    };
}
