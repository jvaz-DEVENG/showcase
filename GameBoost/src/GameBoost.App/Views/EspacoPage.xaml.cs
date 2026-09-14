using System.Windows.Controls;
using GameBoost.App.ViewModels;

namespace GameBoost.App.Views;

public partial class EspacoPage : UserControl
{
    public EspacoPage()
    {
        InitializeComponent();

        // O treemap avisa por evento, e nao por binding: navegar e passar o
        // mouse sao interacoes visuais, nao estado a ser observado.
        Mapa.AoEntrarNaPasta += no => (DataContext as EspacoPageViewModel)?.Entrar(no);
        Mapa.AoPassarOMouse += no => (DataContext as EspacoPageViewModel)?.MostrarNoRodape(no);
    }
}
