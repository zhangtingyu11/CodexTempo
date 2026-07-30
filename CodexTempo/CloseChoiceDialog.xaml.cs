using System.Windows;

namespace CodexTempo;

public partial class CloseChoiceDialog : Window
{
    public CloseChoice? Choice { get; private set; }

    public CloseChoiceDialog()
    {
        InitializeComponent();
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.HideToTray;
        DialogResult = true;
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Choice = CloseChoice.Exit;
        DialogResult = true;
    }
}

public enum CloseChoice
{
    HideToTray,
    Exit
}
