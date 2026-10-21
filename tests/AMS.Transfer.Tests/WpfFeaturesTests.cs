using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AMS.Transfer.App;
using AMS.Transfer.App.Services;
using AMS.Transfer.Core;
using AMS.Transfer.Network;
using AMS.Transfer.Storage;

namespace AMS.Transfer.Tests;

[CollectionDefinition("WPF", DisableParallelization = true)]
public sealed class WpfCollection { }

[Collection("WPF")]
public sealed class WpfFeaturesTests
{
    [Fact]
    public Task ClipboardFormatsShortcutsManagedIconsNativeDragAndUiRender() => OnSta(async () =>
    {
        var application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var dictionary in new[] { "Colors", "Controls", "Icons" })
            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"/OrbLink;component/Themes/{dictionary}.xaml", UriKind.Relative) });
        using var fixture = new TestStorage();
        await fixture.Store.InitializeAsync();
        var appearance = new AppearanceService(fixture.Paths, fixture.Store);
        await appearance.InitializeAsync();
        var image = MakeImage();
        if (Environment.GetEnvironmentVariable("ORBLINK_NATIVE_CLIPBOARD_TEST") == "1")
            VerifyNativeClipboardWithTextBackup(image);

        // The same IDataObject used by Windows Clipboard: PNG and native bitmap, no global monitor.
        var data = ClipboardImages.CreateImageDataObject(image);
        Assert.True(data.GetDataPresent(DataFormats.Bitmap));
        Assert.True(data.GetDataPresent("PNG"));
        AssertImage(ClipboardImages.ReadImage(data)!);
        var nativeBitmapOnly = new System.Windows.DataObject();
        nativeBitmapOnly.SetImage(image);
        nativeBitmapOnly.SetData("PNG", new byte[] { 1, 2, 3 });
        AssertImage(ClipboardImages.ReadImage(nativeBitmapOnly)!);
        Assert.Null(ClipboardImages.ReadImage(new System.Windows.DataObject(DataFormats.UnicodeText, "texto")));
        Assert.True(PanelShortcuts.ShouldPasteImage(Key.V, ModifierKeys.Control, true, false));
        Assert.False(PanelShortcuts.ShouldPasteImage(Key.V, ModifierKeys.Control, false, false));
        Assert.False(PanelShortcuts.ShouldPasteImage(Key.V, ModifierKeys.Control, true, true));
        Assert.False(PanelShortcuts.ShouldPasteImage(Key.V, ModifierKeys.Control | ModifierKeys.Shift, true, false));

        var original = Path.Combine(fixture.Paths.Root, "original.png");
        SavePng(original, image);
        var imported = await appearance.Icons.ImportAsync(original);
        File.Delete(original);
        await appearance.SaveAsync(new AppearanceSettings { CustomIconFile = imported, OrbSize = 80 });
        var restarted = new AppearanceService(fixture.Paths, fixture.Store);
        await restarted.InitializeAsync();
        Assert.Equal(imported, restarted.Current.CustomIconFile);
        AssertImage((BitmapSource)restarted.GetIcon());

        var icoPath = Path.Combine(fixture.Paths.Root, "custom.ico");
        using (var icon = TrayIconFactory.Create())
        using (var stream = File.Create(icoPath)) icon.Save(stream);
        var importedIco = await restarted.Icons.ImportAsync(icoPath);
        Assert.NotNull(restarted.Icons.TryLoad(importedIco));
        File.Delete(Path.Combine(fixture.Paths.Appearance, imported));
        Assert.IsType<DrawingImage>(restarted.GetIcon());
        var invalid = Path.Combine(fixture.Paths.Root, "invalid.png");
        await File.WriteAllTextAsync(invalid, "not an image");
        await Assert.ThrowsAnyAsync<Exception>(() => restarted.Icons.ImportAsync(invalid));
        Assert.Null(restarted.Icons.TryLoad("missing.png"));
        Assert.Null(restarted.Icons.TryLoad("../invalid.png"));
        var oversizedIcon = Path.Combine(fixture.Paths.Root, "oversized.png");
        using (var file = File.Create(oversizedIcon)) file.SetLength(4 * 1024 * 1024 + 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => restarted.Icons.ImportAsync(oversizedIcon));
        await restarted.SaveAsync(new AppearanceSettings());
        Assert.Null(restarted.Current.CustomIconFile);
        Assert.IsType<DrawingImage>(restarted.GetIcon());

        var receivedPath = Path.Combine(fixture.Paths.Received, "Captura-ação.png");
        SavePng(receivedPath, image);
        var dropData = NativeFileDrag.CreateDataObject(receivedPath);
        Assert.Throws<FileNotFoundException>(() => NativeFileDrag.CreateDataObject(Path.Combine(fixture.Paths.Received, "missing.bin")));
        Assert.Equal(new[] { receivedPath }, dropData.GetData(DataFormats.FileDrop));
        var ole = (System.Runtime.InteropServices.ComTypes.IDataObject)dropData;
        var format = new System.Runtime.InteropServices.ComTypes.FORMATETC
        {
            cfFormat = 15, dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
            lindex = -1, tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
        };
        Assert.Equal(0, ole.QueryGetData(ref format)); // Real CF_HDROP contract for Explorer / Outlook.
        ole.GetData(ref format, out var medium);
        try
        {
            Assert.Equal(System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, medium.tymed);
            Assert.Equal(1u, DragQueryFile(medium.unionmember, uint.MaxValue, null, 0));
            var fileName = new System.Text.StringBuilder(32768);
            Assert.True(DragQueryFile(medium.unionmember, 0, fileName, (uint)fileName.Capacity) > 0);
            Assert.Equal(receivedPath, fileName.ToString());
        }
        finally { ReleaseStgMedium(ref medium); }

        var network = new NetworkCoordinator(fixture.Store, fixture.Paths, new LocalLog(fixture.Paths));
        var sender = new ClipboardImageSender(fixture.Paths, network);
        var panel = new MainWindow(fixture.Store, network, restarted, sender);
        await panel.ReloadAsync();
        Render(panel, "painel-vazio.png");
        var record = new TransferRecord(Guid.NewGuid(), "Captura.png", TransferKind.Image, TransferDirection.Received,
            TransferStatus.Completed, new FileInfo(receivedPath).Length, Guid.NewGuid(), "Notebook pessoal", receivedPath, DateTimeOffset.Now);
        await fixture.Store.AddTransferAsync(record);
        var pdf = Path.Combine(fixture.Paths.Received, "Proposta.pdf");
        await File.WriteAllTextAsync(pdf, "fixture");
        await fixture.Store.AddTransferAsync(record with { Id = Guid.NewGuid(), Name = "Proposta.pdf", Kind = TransferKind.File, LocalPath = pdf, Size = 2400000 });
        await panel.ReloadAsync();
        Assert.Equal(2, ((ListBox)panel.FindName("InboxList")).Items.Count);
        Render(panel, "painel-recebidos.png");
        var settings = new SettingsWindow(restarted, network);
        Render(settings, "configuracoes.png");
        ((ScrollViewer)settings.FindName("AppearanceScroll")).ScrollToBottom();
        Render(settings, "configuracoes-comportamento.png");
        var orb = new BubbleWindow(fixture.Store, network, new LocalLog(fixture.Paths), restarted, sender);
        Render(orb, "orb.png");
        var previewDirectory = Environment.GetEnvironmentVariable("ORBLINK_PREVIEW_DIR");
        if (!string.IsNullOrEmpty(previewDirectory))
        {
            using var icon = TrayIconFactory.Create();
            using var stream = File.Create(Path.Combine(previewDirectory, "OrbLink.ico"));
            icon.Save(stream);
        }
        panel.Closing += (_, _) => panel.OnDeactivated(panel, EventArgs.Empty);
        orb.Close(); settings.Close(); panel.Close();
        await network.DisposeAsync();
        await VerifySavedPeerUiAsync(fixture);
        application.Shutdown();
    });

    private static async Task VerifySavedPeerUiAsync(TestStorage local)
    {
        using var remote = new TestStorage(); await remote.Store.InitializeAsync();
        var identityA = await local.Store.GetOrCreateIdentityAsync();
        var identityB = await remote.Store.GetOrCreateIdentityAsync();
        using var certificate = await new CertificateService(remote.Paths, remote.Store).GetOrCreateAsync();
        var port = FreePort();
        const string tokenAB = "ui-test-ab", tokenBA = "ui-test-ba";
        await local.Store.UpsertPeerAsync(new PairedDevice(identityB.Id, "PC pareado sem UDP", "127.0.0.1", port,
            CertificateService.GetFingerprint(certificate), tokenAB, tokenBA, DateTimeOffset.UtcNow.AddDays(-1)));
        await remote.Store.UpsertPeerAsync(new PairedDevice(identityA.Id, "local", "127.0.0.1", 45832,
            new string('A', 64), tokenBA, tokenAB, DateTimeOffset.UtcNow));
        await using var server = new TransferServer(identityB, certificate, remote.Store, remote.Paths,
            new NetworkOptions { TransferPort = port }, new LocalLog(remote.Paths));
        await server.StartAsync();
        await using var network = new NetworkCoordinator(local.Store, local.Paths, new LocalLog(local.Paths),
            new NetworkOptions { TransferPort = 0, EnableDiscovery = false, EnableHeartbeat = false });
        await network.StartAsync();
        Assert.Empty(await network.GetOnlinePeersAsync());
        var window = new DevicesWindow(network, local.Store);
        try
        {
            var refresh = (System.Windows.Controls.Button)window.FindName("RefreshButton");
            refresh.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (!refresh.IsEnabled && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            Assert.True(refresh.IsEnabled);
            Assert.Empty(network.DiscoveredDevices);
            var rows = ((ListBox)window.FindName("DevicesList")).Items;
            var row = Assert.IsType<DevicesWindow.DeviceRow>(Assert.Single(rows.Cast<object>()));
            Assert.Contains("● Online", row.Display); Assert.Contains("Pareado", row.Display);
            Render(window, "computadores-fallback.png");
        }
        finally { window.Close(); }
    }

    [Fact]
    public Task EncodesClipboardImageAndTransfersPngOverRealPairedHttps() => OnSta(async () =>
    {
        using var a = new TestStorage();
        using var b = new TestStorage();
        await a.Store.InitializeAsync(); await b.Store.InitializeAsync();
        var identityA = await a.Store.GetOrCreateIdentityAsync();
        var identityB = await b.Store.GetOrCreateIdentityAsync();
        using var certA = await new CertificateService(a.Paths, a.Store, new TestProtector()).GetOrCreateAsync();
        using var certB = await new CertificateService(b.Paths, b.Store, new TestProtector()).GetOrCreateAsync();
        var port = FreePort();
        await using var server = new TransferServer(identityB, certB, b.Store, b.Paths,
            new NetworkOptions { TransferPort = port }, new LocalLog(b.Paths));
        server.PairingRequested += request => server.AcceptPairAsync(request.RequestId);
        var activity = new System.Collections.Concurrent.ConcurrentQueue<TransferActivity>();
        server.ReceiveActivity += activity.Enqueue;
        var received = new TaskCompletionSource<TransferRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.ItemReceived += record => { received.TrySetResult(record); return Task.CompletedTask; };
        await server.StartAsync();
        var client = new TransferClient(identityA, FreePort(), CertificateService.GetFingerprint(certA), a.Store, new LocalLog(a.Paths));
        var peer = await client.PairAsync(new DiscoveredDevice(identityB.Id, "PC receptor", "127.0.0.1", port,
            CertificateService.GetFingerprint(certB), DateTimeOffset.UtcNow));
        var sender = new ClipboardImageSender(a.Paths, _ => Task.FromResult<IReadOnlyList<PairedDevice>>([peer]), client.SendFileAsync);
        await sender.SendAsync(MakeImage());
        var record = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TransferKind.Image, record.Kind);
        Assert.EndsWith(".png", record.Name);
        AssertImage(MainWindow.LoadBitmap(record.LocalPath!));
        Assert.Empty(Directory.GetFiles(a.Paths.Outgoing));
        Assert.Contains(activity, item => item.IsActive && item.Fraction == 0);
        Assert.Contains(activity, item => !item.IsActive);
        Assert.Single(await a.Store.GetRecentTransfersAsync());
        Assert.Single(await b.Store.GetRecentTransfersAsync());

        var offline = new ClipboardImageSender(a.Paths, _ => Task.FromResult<IReadOnlyList<PairedDevice>>([]), client.SendFileAsync);
        await Assert.ThrowsAsync<InvalidOperationException>(() => offline.SendAsync(MakeImage()));
        Assert.Empty(Directory.GetFiles(a.Paths.Outgoing));
        var failing = new ClipboardImageSender(a.Paths, _ => Task.FromResult<IReadOnlyList<PairedDevice>>([peer]),
            (_, _, _, _) => throw new IOException("injected send failure"));
        await Assert.ThrowsAsync<IOException>(() => failing.SendAsync(MakeImage()));
        Assert.Empty(Directory.GetFiles(a.Paths.Outgoing));
    });

    private static void VerifyNativeClipboardWithTextBackup(BitmapSource image)
    {
        // Opt-in diagnostic. Never replace rich/opaque user data, and never overwrite a later copy.
        var originalSequence = GetClipboardSequenceNumber();
        var original = System.Windows.Clipboard.GetDataObject();
        var backup = new System.Windows.DataObject();
        if (original is not null)
            foreach (var format in original.GetFormats(false))
            {
                if (original.GetData(format, false) is not string text)
                    throw new InvalidOperationException("Teste nativo não iniciado: clipboard não contém somente texto. Conteúdo preservado.");
                backup.SetData(format, text, false);
            }
        Assert.Equal(originalSequence, GetClipboardSequenceNumber());
        uint ownedSequence = originalSequence;
        try
        {
            ClipboardImages.CopyImage(image);
            ownedSequence = GetClipboardSequenceNumber();
            Assert.True(System.Windows.Clipboard.ContainsImage());
            AssertImage(ClipboardImages.ReadCurrentImage()!);
            System.Windows.Clipboard.SetImage(image);
            ownedSequence = GetClipboardSequenceNumber();
            var bitmap = ClipboardImages.ReadCurrentImage();
            Assert.NotNull(bitmap);
            Assert.Equal(128, bitmap.PixelWidth);
            Assert.Equal(80, bitmap.PixelHeight);
        }
        finally
        {
            if (GetClipboardSequenceNumber() == ownedSequence)
            {
                if (original is null) System.Windows.Clipboard.Clear();
                else System.Windows.Clipboard.SetDataObject(backup, true);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr hdrop, uint index, System.Text.StringBuilder? buffer, uint length);

    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);

    private static BitmapSource MakeImage()
    {
        const int w = 128, h = 80;
        var pixels = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var index = (y * w + x) * 4;
            pixels[index] = (byte)(120 + x); pixels[index + 1] = (byte)(30 + y);
            pixels[index + 2] = 40; pixels[index + 3] = x == 0 ? (byte)0 : (byte)255;
        }
        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
        source.Freeze(); return source;
    }

    private static void AssertImage(BitmapSource source)
    {
        Assert.Equal(128, source.PixelWidth); Assert.Equal(80, source.PixelHeight);
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[128 * 80 * 4]; bitmap.CopyPixels(pixels, 128 * 4, 0);
        Assert.Equal(0, pixels[3]); Assert.Equal(255, pixels[7]);
        Assert.Equal(121, pixels[4]);
    }

    private static void SavePng(string path, BitmapSource source)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static void Render(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(window.Width, window.Height));
        content.Arrange(new Rect(0, 0, window.Width, window.Height)); content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var directory = Environment.GetEnvironmentVariable("ORBLINK_PREVIEW_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory); SavePng(Path.Combine(directory, name), bitmap);
    }

    private static Task OnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static int FreePort()
    {
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop(); return port;
    }
}
