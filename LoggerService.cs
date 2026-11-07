using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyAPRS;

internal static class Archive
{
    internal static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MyAPRS");
    internal static readonly string Data = Path.Combine(Root, "data");
    internal static readonly string StatusPath = Path.Combine(Data, "status.json");

    internal static (string Callsign, bool YaesuMetres) Settings()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Root, "settings.txt"));
        string callsign = lines.FirstOrDefault()?.Trim().ToUpperInvariant() ?? "";
        if (!Regex.IsMatch(callsign, @"\A[A-Z0-9]{1,6}(?:-(?:[1-9]|1[0-5]))?\z"))
            throw new InvalidDataException("Enter a valid callsign and optional SSID in MyAPRS Setup.");
        return (callsign, lines.ElementAtOrDefault(1) != "0");
    }

    internal static string LogPath(string callsign) => Path.Combine(Data, callsign + ".csv");

    internal static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
}

internal sealed record LoggerStatus(
    string State, string? Detail, DateTimeOffset? LastPacket, long Logged, DateTimeOffset Updated);

internal sealed class LoggerService : ServiceBase
{
    private readonly CancellationTokenSource shutdown = new();
    private Task? worker;
    private LoggerStatus status = new("Starting", null, null, 0, DateTimeOffset.UtcNow);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    public LoggerService() => ServiceName = "MyAPRS";

    protected override void OnStart(string[] args) => worker = Task.Run(async () =>
    {
        try
        {
            var settings = Archive.Settings();
            Directory.CreateDirectory(Archive.Data);
            Task connection = CollectAsync(settings.Callsign, settings.YaesuMetres, shutdown.Token);
            Task heartbeat = PublishAsync(shutdown.Token);
            try { await await Task.WhenAny(connection, heartbeat); }
            finally
            {
                shutdown.Cancel();
                try { await Task.WhenAll(connection, heartbeat); }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            try { WriteStatus(status with { State = "Failed", Detail = ex.Message }); }
            catch (Exception) { }
            Environment.Exit(1);
        }
    });

    protected override void OnStop()
    {
        shutdown.Cancel();
        worker?.Wait(TimeSpan.FromSeconds(8));
        try { WriteStatus(status with { State = "Stopped", Detail = null }); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task PublishAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do { WriteStatus(Volatile.Read(ref status)); }
        while (await timer.WaitForNextTickAsync(token));
    }

    private static void WriteStatus(LoggerStatus current)
    {
        string temporary = Archive.StatusPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(current with { Updated = DateTimeOffset.UtcNow }));
        File.Move(temporary, Archive.StatusPath, overwrite: true);
    }

    private async Task CollectAsync(string callsign, bool yaesuMetres, CancellationToken token)
    {
        string path = Archive.LogPath(callsign);
        string baseCall = callsign.Split('-')[0];
        string? lastPayload = null;
        long lastWritten = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                status = status with { State = "Connecting", Detail = null };
                using var client = new TcpClient();
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await client.ConnectAsync("rotate.aprs2.net", 14580, timeout.Token);
                }
                using NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.Latin1, leaveOpen: true);
                using var writer = new StreamWriter(stream, Encoding.Latin1, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(
                    $"user {baseCall} pass -1 vers MyAPRS 1.0 filter b/{callsign}".AsMemory(), token);

                while (true)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromMinutes(2));
                    string line = await reader.ReadLineAsync(timeout.Token)
                        ?? throw new IOException("APRS-IS closed the connection.");
                    if (line.StartsWith("# logresp ", StringComparison.OrdinalIgnoreCase))
                        status = status with { State = "Connected", Detail = null };
                    if (!line.StartsWith(callsign + ">", StringComparison.OrdinalIgnoreCase)) continue;

                    DateTimeOffset received = DateTimeOffset.UtcNow;
                    status = status with { State = "Connected", LastPacket = received };
                    APRSData? position = PacketDecoder.Parse(line, yaesuMetres);
                    if (position is null) continue;
                    int separator = line.IndexOf(':');
                    int pathStart = line.IndexOf(',', 0, separator);
                    string payload = line[..(pathStart < 0 ? separator : pathStart)] + line[separator..];
                    if (payload == lastPayload && Stopwatch.GetElapsedTime(lastWritten).TotalSeconds < 30) continue;

                    Append(path, received, position);
                    lastPayload = payload;
                    lastWritten = Stopwatch.GetTimestamp();
                    status = status with { Logged = status.Logged + 1 };
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                status = status with { State = "Reconnecting", Detail = "APRS-IS timed out." };
            }
            catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
            {
                status = status with { State = "Reconnecting", Detail = ex.Message };
            }
            await Task.Delay(RetryDelay, token);
        }
    }

    private static void Append(string path, DateTimeOffset timestamp, APRSData position)
    {
        using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(file);
        if (file.Length == 0) writer.WriteLine("time,lasttime,lat,lng,speed,course,altitude,comment");
        writer.WriteLine(FormattableString.Invariant(
            $"{timestamp:O},{timestamp:O},{position.Lat:F5},{position.Lng:F5},{position.Speed},{position.Course},{position.Altitude:F2},"));
    }
}
