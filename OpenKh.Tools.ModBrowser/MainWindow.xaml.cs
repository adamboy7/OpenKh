using System.Threading;
using System.Threading.Tasks;
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

    private async void AddModButton_Click(object sender, RoutedEventArgs e)
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

        var repositoryInput = dialog.RepositoryInput;
        if (string.IsNullOrWhiteSpace(repositoryInput))
        {
            return;
        }

        AddModStatus status;
        try
        {
            status = await viewModel.AddModAsync(repositoryInput, CancellationToken.None);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        switch (status)
        {
            case AddModStatus.Success:
                MessageBox.Show(this, "The mod was added successfully.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case AddModStatus.AlreadyExists:
                MessageBox.Show(this, "That repository already exists in mods.json.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case AddModStatus.InvalidRepository:
                MessageBox.Show(this, "Enter a repository in the form \"author/repo\" or a GitHub URL.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case AddModStatus.RepositoryNotFound:
                MessageBox.Show(this, "The repository could not be found on GitHub.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
            case AddModStatus.ModsFileUnavailable:
                MessageBox.Show(this, "A mods.json file could not be located. Ensure it exists alongside the application.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
            default:
                MessageBox.Show(this, "An unexpected error occurred while adding the mod.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }
    }
}
