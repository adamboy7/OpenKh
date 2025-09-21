using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace OpenKh.Command.SpawnPointExplorer;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the extracted KH2 data directory"
        };

        if (!string.IsNullOrEmpty(_viewModel.DataRoot))
        {
            dialog.SelectedPath = _viewModel.DataRoot;
        }

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            _viewModel.DataRoot = dialog.SelectedPath;
        }
    }

    private async void OnLoadClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnTreeSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _viewModel.SelectedTreeItem = e.NewValue;
    }

    private async void OnExportYamlClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem)
        {
            await _viewModel.ExportSelectionAsync(menuItem.DataContext);
        }
    }
}
