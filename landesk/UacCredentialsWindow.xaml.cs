using System.Windows;

namespace LanDesk;

public partial class UacCredentialsWindow : Window
{
    public string Username => UsernameBox.Text ?? string.Empty;
    public string Password => PasswordBox.Password ?? string.Empty;

    public UacCredentialsWindow()
    {
        InitializeComponent();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
