using System.ComponentModel;
using System.Windows;

namespace GameBoost.App.Views;

public partial class ShellWindow : Window
{
    public ShellWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Bandeja ligada, quando existe. Com ela, fechar a janela esconde em vez
    /// de sair.
    /// </summary>
    public Services.BandejaDoSistema? Bandeja { get; set; }

    /// <summary>
    /// Fechar pelo X esconde para a bandeja, mas **só quando a bandeja está
    /// visível**: se ela não estiver, o X fecha de verdade, senão o app viraria
    /// um processo que não dá para encerrar pela interface.
    ///
    /// O aviso aparece uma vez. Programa que some sem dizer para onde foi é o
    /// motivo de as pessoas irem no Gerenciador de Tarefas.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.Current is App app && app.EsconderEmVezDeFechar)
        {
            e.Cancel = true;
            Hide();

            if (!_jaAvisou)
            {
                _jaAvisou = true;
                Bandeja?.Avisar("GameBoost continua aberto",
                    "Ele fica na bandeja, ao lado do relógio. Para sair de vez, clique com o "
                  + "botão direito no ícone e escolha Sair.");
            }

            return;
        }

        base.OnClosing(e);
    }

    private bool _jaAvisou;
}
