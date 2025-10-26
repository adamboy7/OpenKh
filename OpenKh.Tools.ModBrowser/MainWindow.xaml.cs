using System.Text;
using System.Windows;
using OpenKh.Tools.ModBrowser.Models;
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

    private async void OnFollowClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new FollowUserWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var result = await viewModel.FollowUserAsync(dialog.UsernameInput);

        switch (result.Status)
        {
            case MainViewModel.FollowUserStatus.InvalidInput:
                MessageBox.Show(this, "Enter a valid GitHub username.", "Follow User", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.FollowUserStatus.NotFound:
                MessageBox.Show(this, $"The GitHub user \"{result.Username}\" could not be found.", "Follow User", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.FollowUserStatus.Failed:
                MessageBox.Show(this, "An error occurred while fetching repositories. Please try again later.", "Follow User", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
            case MainViewModel.FollowUserStatus.Success:
                var builder = new StringBuilder();
                builder.AppendLine($"Fetched {result.TotalRepositories} repositories for {result.Username}.");
                builder.AppendLine($"{result.AddedCount} new mods were added to the list.");
                if (result.AlreadyTrackedCount > 0)
                {
                    builder.AppendLine($"{result.AlreadyTrackedCount} repositories were already present.");
                }

                if (result.FailedCount > 0)
                {
                    builder.AppendLine($"{result.FailedCount} repositories could not be added.");
                }

                MessageBox.Show(this, builder.ToString(), "Follow User", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
        }
    }

    private async void OnUpdateModClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not ModEntry entry)
        {
            return;
        }

        var result = await viewModel.UpdateModAsync(entry);
        switch (result)
        {
            case MainViewModel.UpdateModResult.Success:
                MessageBox.Show(this, "The mod entry was updated successfully.", "Update Metadata", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            case MainViewModel.UpdateModResult.NotTracked:
                MessageBox.Show(this, "The selected mod could not be found in the local list.", "Update Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.UpdateModResult.NotFound:
                MessageBox.Show(this, "The repository could not be found on GitHub.", "Update Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.UpdateModResult.Offline:
                MessageBox.Show(this, "Unable to reach GitHub. Please check your connection and try again.", "Update Metadata", MessageBoxButton.OK, MessageBoxImage.Warning);
                break;
            case MainViewModel.UpdateModResult.Failed:
                MessageBox.Show(this, "An error occurred while updating the mod entry. Please try again later.", "Update Metadata", MessageBoxButton.OK, MessageBoxImage.Error);
                break;
        }
    }
}
