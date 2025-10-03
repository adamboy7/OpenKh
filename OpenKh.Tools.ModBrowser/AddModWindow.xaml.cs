using System.Windows;

namespace OpenKh.Tools.ModBrowser;

public partial class AddModWindow : Window
{
    public AddModWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RepoTextBox.Focus();
    }

    public string? RepositoryInput { get; private set; }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        var input = RepoTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show(this, "Please enter a repository before continuing.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
            RepoTextBox.Focus();
            return;
        }

        RepositoryInput = input;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
