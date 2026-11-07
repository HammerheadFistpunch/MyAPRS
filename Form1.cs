using System.Text.Json;

namespace MyAPRS;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 2000 };
    private readonly MainForm window = new();

    public TrayApplication(bool silent)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open MyAPRS", null, (_, _) => ShowWindow());
        menu.Items.Add("Exit tray app", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowWindow();
        window.FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            window.Hide();
        };
        refresh.Tick += (_, _) => UpdateStatus();
        UpdateStatus();
        refresh.Start();
        if (!silent) ShowWindow();
    }

    private void ShowWindow()
    {
        window.Show();
        window.WindowState = FormWindowState.Normal;
        window.Activate();
    }

    private void UpdateStatus()
    {
        LoggerStatus? status = null;
        try
        {
            using var file = Archive.OpenShared(Archive.StatusPath);
            status = JsonSerializer.Deserialize<LoggerStatus>(file);
            if (status is not null && status.State != "Stopped" &&
                DateTimeOffset.UtcNow - status.Updated > TimeSpan.FromSeconds(8))
                status = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        tray.Text = $"MyAPRS: {status?.State ?? "Logger unavailable"}";
        tray.Icon = status?.State switch
        {
            "Connected" => SystemIcons.Information,
            "Starting" or "Connecting" or "Reconnecting" => SystemIcons.Warning,
            _ => SystemIcons.Error
        };
        window.Display(status);
    }

    protected override void ExitThreadCore()
    {
        refresh.Dispose();
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Dispose();
        window.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class MainForm : Form
{
    private readonly Label state = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly DateTimePicker from = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today.AddDays(-1) };
    private readonly DateTimePicker through = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };
    private readonly Button export = new() { Text = "Export KMZ…", AutoSize = true };

    internal MainForm()
    {
        Text = "MyAPRS";
        ClientSize = new Size(475, 260);
        StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(16)
        };
        panel.Controls.AddRange(new Control[] {
            state, new Label { Text = "From (local date)", AutoSize = true }, from,
            new Label { Text = "Through (inclusive)", AutoSize = true }, through, export
        });
        Controls.Add(panel);
        export.Click += async (_, _) => await ExportAsync();
    }

    internal void Display(LoggerStatus? status)
    {
        string last = status?.LastPacket?.ToLocalTime().ToString("g") ?? "—";
        state.Text = $"Logger: {status?.State ?? "Unavailable"}\nLast station packet: {last}\n" +
            $"Positions logged since service start: {status?.Logged.ToString() ?? "—"}" +
            (status?.Detail is { Length: > 0 } detail ? $"\n{detail}" : "");
    }

    private async Task ExportAsync()
    {
        try
        {
            DateTime start = from.Value.Date;
            DateTime end = through.Value.Date.AddDays(1);
            if (end <= start) throw new InvalidOperationException("Choose an end date on or after the start date.");
            string call = Archive.Settings().Callsign;
            using var dialog = new SaveFileDialog {
                Filter = "Google Earth KMZ (*.kmz)|*.kmz", DefaultExt = "kmz",
                FileName = $"{call}_{start:yyyy-MM-dd}.kmz"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            export.Enabled = false;
            int count = await Task.Run(() => KmzBuilder.Export(call, start, end, dialog.FileName));
            MessageBox.Show(this, $"Exported {count} positions.", "MyAPRS");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MyAPRS export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { export.Enabled = true; }
    }
}
