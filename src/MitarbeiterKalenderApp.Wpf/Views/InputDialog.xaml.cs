using System.Windows;

namespace MitarbeiterKalenderApp.Wpf.Views;

public partial class InputDialog : Window
{
    public string Prompt { get; }
    public string Value { get; private set; }

    public InputDialog(string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Prompt = prompt;
        Value = defaultValue;
        DataContext = this;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
