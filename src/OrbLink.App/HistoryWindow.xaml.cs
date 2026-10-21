using System.Windows;
using OrbLink.Storage;

namespace OrbLink.App;

public partial class HistoryWindow : Window
{
    private readonly SqliteStore _store;
    public HistoryWindow(SqliteStore store)
    {
        InitializeComponent();
        _store = store;
        Loaded += async (_, _) =>
        {
            var records = await _store.GetRecentTransfersAsync(200);
            HistoryGrid.ItemsSource = records.Select(record => new
            {
                record.Name, record.RemoteDeviceName,
                Direction = record.Direction == Core.TransferDirection.Sent ? "Enviado" : "Recebido",
                Status = record.Status switch
                {
                    Core.TransferStatus.Completed => "Concluído",
                    Core.TransferStatus.Failed => "Falhou",
                    Core.TransferStatus.Cancelled => "Cancelado",
                    Core.TransferStatus.InProgress => "Em andamento",
                    _ => "Pendente"
                },
                Size = record.Size >= 1048576 ? $"{record.Size / 1048576d:0.#} MB" : $"{record.Size / 1024d:0.#} KB",
                Date = record.CreatedUtc.LocalDateTime.ToString("dd/MM/yyyy HH:mm")
            }).ToArray();
        };
    }
}
