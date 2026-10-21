namespace OrbLink.Storage;

public sealed class AppPaths
{
    public AppPaths(string? root = null)
    {
        Root = root ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OrbLink");
        Received = Path.Combine(Root, "Received");
        Logs = Path.Combine(Root, "Logs");
        Database = Path.Combine(Root, "ams-transfer.db");
        Certificate = Path.Combine(Root, "device.pfx");
        Appearance = Path.Combine(Root, "Appearance");
        Outgoing = Path.Combine(Root, "Outgoing");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Received);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Appearance);
        Directory.CreateDirectory(Outgoing);
    }

    public string Root { get; }
    public string Received { get; }
    public string Logs { get; }
    public string Database { get; }
    public string Certificate { get; }
    public string Appearance { get; }
    public string Outgoing { get; }
}
