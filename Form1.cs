using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AprsScraper
{
    public class MainForm : Form
    {
        private TextBox txtCallsign = null!, txtPasscode = null!;
        private CheckBox chkYaesuBug = null!, chkStartup = null!;
        private Button btnStart = null!, btnStop = null!, btnOpenFolder = null!, btnExport = null!;
        private Label lblStatus = null!, lblStats = null!;
        private NotifyIcon trayIcon = null!;

        private bool isRunning = false;
        private TcpClient? tcpClient = null;
        private CancellationTokenSource? cts;
        
        private string logsDir = "";
        private string logFile = "";

        private string lastPacketKey = "";
        private DateTime lastPacketTime = DateTime.MinValue;
        
        private int packetsReceived = 0;
        private int packetsLogged = 0;

        public MainForm(bool startSilent)
        {
            InitializePaths(); //
            SetupUI(); //
            
            // AUTO-START LOGIC:
            // Check if startup was enabled in previous sessions.
            if (chkStartup.Checked)
            {
                if (startSilent)
                {
                    this.WindowState = FormWindowState.Minimized;
                    this.ShowInTaskbar = false; 
                }
                
                // Trigger tracking immediately upon launch if preference is set
                _ = StartTracking(); //
            }
        }

        private void InitializePaths()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            DirectoryInfo? dir = new DirectoryInfo(path);

            if (path.Contains("bin") || path.Contains("Debug"))
            {
                while (dir != null && (dir.Name == "bin" || dir.Name == "Debug" || dir.Name.StartsWith("net") || dir.Name == "publish"))
                {
                    dir = dir.Parent;
                }
            }

            string root = dir?.FullName ?? path;
            logsDir = Path.Combine(root, "logs");
            logFile = Path.Combine(logsDir, "aprs_log.csv");
        }

        private void SetupUI()
        {
            this.Text = "APRS-IS Live Tracker";
            this.Size = new Size(420, 560); //
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            Label lblCall = new Label { Text = "Callsign (e.g. KI7NNK-9):", Location = new Point(20, 20), AutoSize = true };
            txtCallsign = new TextBox { Location = new Point(180, 18), Width = 180 };

            Label lblPass = new Label { Text = "Passcode (tied to base call):", Location = new Point(20, 60), AutoSize = true };
            txtPasscode = new TextBox { Text = "-1", Location = new Point(190, 58), Width = 170 };

            LinkLabel lnkPasscode = new LinkLabel { 
                Text = "To get your passcode click here", 
                Location = new Point(190, 85), 
                AutoSize = true,
                LinkColor = Color.Blue
            };
            lnkPasscode.LinkClicked += (s, e) => {
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                        FileName = "https://apps.magicbug.co.uk/passcode/",
                        UseShellExecute = true
                    });
                } catch { }
            };

            chkYaesuBug = new CheckBox { Text = "Yaesu FTM-400 Metres Bug (Alt is Metres, not Feet)", Location = new Point(20, 110), Width = 340, Checked = true };
            
            chkStartup = new CheckBox { Text = "Run silently on Windows startup", Location = new Point(20, 140), Width = 340 };

            // Load saved settings from Registry
            LoadSettings();

            chkStartup.CheckedChanged += (s, e) => SetStartup(chkStartup.Checked);

            btnStart = new Button { Text = "Start Tracking", Location = new Point(20, 180), Width = 170, Height = 40, BackColor = Color.LightGreen };
            btnStart.Click += async (s, e) => await StartTracking();

            btnStop = new Button { Text = "Stop", Location = new Point(210, 180), Width = 170, Height = 40, Enabled = false };
            btnStop.Click += BtnStop_Click;

            // NEW: Dedicated Export Button
            btnExport = new Button { Text = "Export KMZ Now", Location = new Point(20, 230), Width = 360, Height = 40, BackColor = Color.LightBlue };
            btnExport.Click += (s, e) => {
                KmzBuilderForm kmzForm = new KmzBuilderForm(logFile, txtCallsign.Text);
                kmzForm.Show(); 
            };

            btnOpenFolder = new Button { Text = "Open Root Logs Folder", Location = new Point(20, 280), Width = 360, Height = 30 };
            btnOpenFolder.Click += (s, e) => {
                Directory.CreateDirectory(logsDir);
                System.Diagnostics.Process.Start("explorer.exe", logsDir);
            };

            lblStatus = new Label { Text = "Status: Idle", Location = new Point(20, 320), Width = 360, Height = 80, ForeColor = Color.DarkBlue };
            
            lblStats = new Label { 
                Text = "Received: 0   |   Logged: 0   |   Last: None", 
                Location = new Point(20, 420), 
                Width = 360, 
                Font = new Font(this.Font, FontStyle.Bold) 
            };

            this.Controls.AddRange(new Control[] { lblCall, txtCallsign, lblPass, txtPasscode, lnkPasscode, chkYaesuBug, chkStartup, btnStart, btnStop, btnExport, btnOpenFolder, lblStatus, lblStats });

            trayIcon = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), Text = "APRS Tracker", Visible = true };
            
            trayIcon.DoubleClick += (s, e) => { 
                this.Show(); 
                this.WindowState = FormWindowState.Normal; 
                this.ShowInTaskbar = true; 
                this.Activate(); 
            };
            
            this.FormClosing += MainForm_FormClosing;
        }

        private async void BtnStop_Click(object? sender, EventArgs e)
        {
            btnStop.Enabled = false; 
            StopScraper(); //

            await Task.Delay(500);

            DialogResult result = MessageBox.Show(
                "Tracking stopped. Would you like to generate a KMZ map file for this trip?", 
                "Create KMZ?", 
                MessageBoxButtons.YesNo, 
                MessageBoxIcon.Question);
            
            if (result == DialogResult.Yes)
            {
                KmzBuilderForm kmzForm = new KmzBuilderForm(logFile, txtCallsign.Text);
                kmzForm.Show(); 
            }
        }

        private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && isRunning)
            {
                e.Cancel = true; 
                this.Hide();     
                trayIcon.ShowBalloonTip(2000, "APRS Tracker", "Running in the background. Double-click tray icon to restore.", ToolTipIcon.Info);
                return;
            }

            trayIcon.Visible = false;
            StopScraper();
        }

        private void StopScraper()
        {
            if (isRunning)
            {
                isRunning = false;
                cts?.Cancel();
                tcpClient?.Close();
                UpdateStatus("Stopped."); //
                
                if (InvokeRequired) Invoke(new Action(() => { btnStart.Enabled = true; btnStop.Enabled = false; }));
                else { btnStart.Enabled = true; btnStop.Enabled = false; }
            }
        }

        private void LoadSettings()
        {
            try {
                using RegistryKey? appKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\AprsTracker");
                if (appKey != null) {
                    txtCallsign.Text = appKey.GetValue("Callsign", "")?.ToString() ?? "";
                    txtPasscode.Text = appKey.GetValue("Passcode", "-1")?.ToString() ?? "-1";
                    
                    if (bool.TryParse(appKey.GetValue("YaesuBug", "True")?.ToString(), out bool yBug))
                        chkYaesuBug.Checked = yBug;
                }

                using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                if (runKey?.GetValue("AprsTracker") != null) {
                    chkStartup.Checked = true;
                }
            } catch { }
        }

        private void SaveSettings()
        {
            try {
                using RegistryKey rk = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\AprsTracker");
                rk.SetValue("Callsign", txtCallsign.Text.Trim());
                rk.SetValue("Passcode", txtPasscode.Text.Trim());
                rk.SetValue("YaesuBug", chkYaesuBug.Checked.ToString());
            } catch { }
        }

        private void SetStartup(bool enable)
        {
            try {
                RegistryKey? rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk != null) {
                    if (enable) rk.SetValue("AprsTracker", $"\"{Application.ExecutablePath}\" --silent");
                    else rk.DeleteValue("AprsTracker", false);
                }
            } catch { }
        }

        private async Task WriteInitialHeaderSafeAsync()
        {
            Directory.CreateDirectory(logsDir);
            if (!File.Exists(logFile)) 
            {
                try 
                {
                    using FileStream fs = new FileStream(logFile, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
                    using StreamWriter sw = new StreamWriter(fs, Encoding.UTF8);
                    await sw.WriteAsync("time,lasttime,lat,lng,speed,course,altitude,comment\n");
                } 
                catch (IOException) { }
            }
        }

        private async Task StartTracking()
        {
            if (string.IsNullOrWhiteSpace(txtCallsign.Text)) return;

            SaveSettings(); //

            isRunning = true;
            cts = new CancellationTokenSource();
            
            if (InvokeRequired) Invoke(new Action(() => { btnStart.Enabled = false; btnStop.Enabled = true; }));
            else { btnStart.Enabled = false; btnStop.Enabled = true; }
            
            await WriteInitialHeaderSafeAsync();
            
            string fullCall = txtCallsign.Text.Trim().ToUpper();
            string baseCall = fullCall.Contains("-") ? fullCall.Split('-')[0] : fullCall;
            string passcode = txtPasscode.Text.Trim();
            Encoding latin1 = Encoding.GetEncoding("iso-8859-1");

            while (isRunning && !cts.IsCancellationRequested)
            {
                try
                {
                    UpdateStatus("Connecting...");
                    tcpClient = new TcpClient();
                    
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
                    {
                        connectCts.CancelAfter(10000); 
                        await tcpClient.ConnectAsync("rotate.aprs2.net", 14580, connectCts.Token);
                    }
                    
                    using NetworkStream stream = tcpClient.GetStream();
                    stream.ReadTimeout = 120000; 
                    
                    using StreamReader reader = new StreamReader(stream, latin1);
                    using StreamWriter writer = new StreamWriter(stream, latin1) { AutoFlush = true };

                    string loginLine = $"user {baseCall} pass {passcode} vers CSharpTracker 1.0 filter p/{fullCall}";
                    await writer.WriteLineAsync(loginLine.AsMemory(), cts.Token);
                    
                    UpdateStatus($"Connected. Listening for {fullCall}...");

                    while (isRunning && tcpClient.Connected && !cts.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync(cts.Token);
                        if (line == null) break;
                        
                        packetsReceived++;
                        RefreshStatsUI(); //

                        if (line.StartsWith("#")) {
                            UpdateStatus($"Server: {line}"); 
                            continue; 
                        } 

                        if (line.StartsWith(fullCall + ">")) {
                            UpdateStatus($"MATCH: {line}");
                            await ProcessPacketAsync(line); //
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (isRunning) {
                        UpdateStatus($"Conn Error: {ex.Message}. Retrying in 10s...");
                        try { await Task.Delay(10000, cts.Token); } catch { break; }
                    }
                }
            }
            StopScraper(); 
        }

        private async Task ProcessPacketAsync(string raw)
        {
            try {
                int colonIdx = raw.IndexOf(':');
                if (colonIdx == -1) return;

                string header = raw.Substring(0, colonIdx);
                string body = raw.Substring(colonIdx + 1);

                Match destMatch = Regex.Match(header, @">([^,]+)");
                string dest = destMatch.Success ? destMatch.Groups[1].Value : "";

                APRSData? pos = null;
                if (body.Length > 0) {
                    char type = body[0];
                    if (type == '`' || type == '\'') 
                        pos = ParseMicE(dest, body, chkYaesuBug.Checked); //
                    else if (type == '!' || type == '=' || type == '@' || type == '/') 
                        pos = ParseStandard(raw); //
                }

                if (pos != null) await WriteRowSafeAsync(pos); //
            } catch (Exception ex) {
                UpdateStatus($"PROCESS ERROR: {ex.Message}");
            }
        }

        private async Task WriteRowSafeAsync(APRSData r)
        {
            string key = $"{r.Lat},{r.Lng},{r.Speed},{r.Course}";
            DateTime now = DateTime.Now;

            if (key == lastPacketKey && (now - lastPacketTime).TotalSeconds < 30) return;

            lastPacketKey = key;
            lastPacketTime = now;

            string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string safeComment = "\"" + r.Comment.Replace("\"", "\"\"") + "\"";
            
            string line = string.Format(CultureInfo.InvariantCulture, 
                "{0},{1},{2:F5},{3:F5},{4},{5},{6:F2},{7}\n", 
                ts, ts, r.Lat, r.Lng, r.Speed, r.Course, r.Altitude, safeComment);

            try {
                using (FileStream fs = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (StreamWriter sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    await sw.WriteAsync(line);
                }
                packetsLogged++;
                RefreshStatsUI();
            } catch { }
        }

        private void RefreshStatsUI()
        {
            string text = $"Received: {packetsReceived}   |   Logged: {packetsLogged}   |   Last: {DateTime.Now:HH:mm:ss}";
            if (InvokeRequired) Invoke(new Action(() => lblStats.Text = text));
            else lblStats.Text = text;
        }

        private APRSData? ParseStandard(string raw)
        {
            try {
                Match mPos = Regex.Match(raw, @"(\d{4}\.\d+)([NS]).?(\d{5}\.\d+)([EW])");
                if (!mPos.Success) return null;
                
                var culture = CultureInfo.InvariantCulture;
                double lat = Math.Round(double.Parse(mPos.Groups[1].Value.Substring(0, 2), culture) + 
                             double.Parse(mPos.Groups[1].Value.Substring(2), culture) / 60.0, 5);
                if (mPos.Groups[2].Value == "S") lat = -lat;
                
                double lng = Math.Round(double.Parse(mPos.Groups[3].Value.Substring(0, 3), culture) + 
                             double.Parse(mPos.Groups[3].Value.Substring(3), culture) / 60.0, 5);
                if (mPos.Groups[4].Value == "W") lng = -lng;

                int crs = 0, spd = 0;
                Match mCrsSpd = Regex.Match(raw, @"[^\d](\d{3})/(\d{3})");
                if (mCrsSpd.Success) {
                    crs = int.Parse(mCrsSpd.Groups[1].Value);
                    spd = (int)Math.Round(int.Parse(mCrsSpd.Groups[2].Value) * 1.852);
                }
                
                double alt = 0;
                Match mAlt = Regex.Match(raw, @"/A=(\d+)");
                if (mAlt.Success) alt = Math.Round(double.Parse(mAlt.Groups[1].Value, culture) * 0.3048, 2);

                return new APRSData { Lat = lat, Lng = lng, Speed = spd, Course = crs, Altitude = alt };
            } catch { return null; }
        }

        private APRSData? ParseMicE(string dest, string body, bool isYaesuMetres)
        {
            try {
                string d = dest.Split('-')[0];
                if (d.Length < 6) return null;

                int[] digits = new int[6];
                for (int i = 0; i < 6; i++) {
                    char c = char.ToUpper(d[i]);
                    digits[i] = (c >= '0' && c <= '9') ? c - '0' : (c >= 'A' && c <= 'J') ? c - 'A' : (c >= 'Q' && c <= 'Y') ? c - 'P' : 0;
                }
                
                bool south = "LZ".Contains(char.ToUpper(d[3])) || (d[3] >= 'a' && d[3] <= 'j');
                bool lonOff = "PQRSTUVWXYZ".Contains(char.ToUpper(d[4]));
                bool west = "PQRSTUVWXYZ".Contains(char.ToUpper(d[5]));
                
                double lat = Math.Round((digits[0]*10+digits[1]) + (digits[2]*10+digits[3]+(digits[4]*10+digits[5])/100.0)/60.0, 5);
                if (south) lat = -lat;

                int lonDeg = (body[1]-28) + (lonOff?100:0);
                if (lonDeg >= 180 && lonDeg <= 189) lonDeg -= 80; else if (lonDeg >= 190 && lonDeg <= 199) lonDeg -= 190;
                double lng = Math.Round(lonDeg + ((body[2]-28) + (body[3]-28)/100.0)/60.0, 5);
                if (west) lng = -lng;

                int spdKnots = ((body[4]-28)*10) + ((body[5]-28)/10);
                if (spdKnots >= 800) spdKnots -= 800;
                int spd = (int)Math.Round(Math.Max(0, spdKnots) * 1.852);
                int crs = ((body[5]-28)%10)*100 + (body[6]-28);
                if (crs >= 400) crs -= 400;

                double alt = 0;
                if (body.Length > 8) {
                    Match mAlt = Regex.Match(body.Substring(8), @"([^}]{3})\}");
                    if (mAlt.Success) {
                        string a = mAlt.Groups[1].Value;
                        int altFt = (a[0]-33)*8281 + (a[1]-33)*91 + (a[2]-33) - 10000;
                        alt = isYaesuMetres ? altFt : Math.Round(altFt * 0.3048, 2);
                    }
                }
                return new APRSData { Lat = lat, Lng = lng, Speed = spd, Course = crs, Altitude = alt };
            } catch { return null; }
        }

        private void UpdateStatus(string message) {
            if (InvokeRequired) Invoke(new Action(() => lblStatus.Text = message));
            else lblStatus.Text = message;
        }
    }
    
    public class APRSData 
    { 
        public double Lat, Lng, Altitude; 
        public int Speed, Course; 
        public string Comment = ""; 
    }
}