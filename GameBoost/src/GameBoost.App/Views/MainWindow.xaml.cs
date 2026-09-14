using System.Windows;
using System.Windows.Controls;
using GameBoost.App.ViewModels;

namespace GameBoost.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Botao "?" de cada item: o que faz, qual o risco, como desfazer (secao 6).
    /// Vira painel lateral nas proximas fases; por ora o texto ja e o mesmo.
    /// </summary>
    private void ExplicarItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ActionItemViewModel item })
            return;

        MessageBox.Show(item.Explicacao, item.Titulo, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
