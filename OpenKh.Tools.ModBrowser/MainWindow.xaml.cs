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

    private async void OnAddModClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new AddModWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await viewModel.AddModAsync(dialog.RepositoryInput);

        switch (result)
        {
            case MainViewModel.AddModResult.Added:
                MessageBox.Show(this, "The mod was added successfully.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case MainViewModel.AddModResult.AlreadyExists:
                MessageBox.Show(this, "This mod is already listed.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case MainViewModel.AddModResult.InvalidInput:
                MessageBox.Show(this, "Enter a valid GitHub repository in the form author/repo or a GitHub URL.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.AddModResult.NotFound:
                MessageBox.Show(this, "The specified repository could not be found on GitHub.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.AddModResult.Failed:
                MessageBox.Show(this, "An error occurred while adding the mod. Please try again later.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }
    }
}
