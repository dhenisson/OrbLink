using System.Windows;
using OrbLink.Network;

namespace OrbLink.App;

public partial class TextSendWindow : Window
{
    private readonly NetworkCoordinator _network;
    public TextSendWindow(NetworkCoordinator network)
    {
        InitializeComponent();
        _network = network;
        Loaded += (_, _) => MessageTextBox.Focus();
    }

    private async void OnSend(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MessageTextBox.Text)) return;
        if (!SendButton.IsEnabled) return;
        SendButton.IsEnabled = false;
        try
        {
            var peers = await _network.GetAvailablePeersForSendAsync();
            if (peers.Count == 0) { StatusText.Text = "Nenhum computador online"; return; }
            await _network.SendTextAsync(peers[0], MessageTextBox.Text);
            DialogResult = true;
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { SendButton.IsEnabled = true; }
    }
}
