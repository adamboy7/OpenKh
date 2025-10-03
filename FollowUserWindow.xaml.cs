using System.Windows;

namespace OpenKh.Tools.ModBrowser;

public partial class FollowUserWindow : Window
{
    public FollowUserWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => UsernameTextBox.Focus();
    }

    public string? UsernameInput { get; private set; }

    private void OnFollowClick(object sender, RoutedEventArgs e)
    {
        var input = UsernameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            MessageBox.Show(this, "Please enter a username before continuing.", "Follow User", MessageBoxButton.OK, MessageBoxImage.Warning);
            UsernameTextBox.Focus();
            return;
        }

        UsernameInput = input;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
