using System.Windows;
using OpenKh.Tools.ModBrowser.ViewModels;

namespace OpenKh.Tools.ModBrowser;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
