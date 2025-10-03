using System.Windows;

namespace OpenKh.Tools.ModBrowser;

public partial class AddModWindow : Window
{
    public AddModWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RepositoryTextBox.Focus();
    }

    public string RepositoryInput => RepositoryTextBox.Text.Trim();

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RepositoryTextBox.Text))
        {
            MessageBox.Show(this, "Please enter a repository before continuing.", "Add Mod", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
