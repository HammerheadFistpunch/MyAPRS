using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;

namespace AprsScraper
{
    public class KmzBuilderForm : Form
    {
        private string logFile;
        private string callsign;
        private Label lblStatus = null!;
        private ProgressBar progressBar = null!;

        public KmzBuilderForm(string logFile, string callsign)
        {
            this.logFile = logFile;
            this.callsign = callsign;
            
            SetupUI();
            
            // Start the build process automatically as soon as the form appears
            this.Shown += async (s, e) => await BuildKmzAutomatedAsync();
        }

        private void SetupUI()
        {
            this.Text = $"Building KMZ for {callsign}...";
            this.Size = new Size(350, 130);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ControlBox = false; // Prevent user from closing it manually while it works

            lblStatus = new Label { Text = "Initializing...", Location = new Point(20, 20), Width = 300 };
            
            progressBar = new ProgressBar { 
                Location = new Point(20, 50), 
                Width = 290, 
                Style = ProgressBarStyle.Marquee // Gives that infinite loading animation
            };

            this.Controls.AddRange(new Control[] { lblStatus, progressBar });
        }

        private async Task BuildKmzAutomatedAsync()
        {
            try
            {
                if (!File.Exists(logFile))
                {
                    MessageBox.Show("Log file not found. Cannot build KMZ.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                lblStatus.Text = "Reading log data...";
                string[] lines = await File.ReadAllLinesAsync(logFile);
                
                if (lines.Length <= 1)
                {
                    MessageBox.Show("Not enough data logged to create a map.", "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                lblStatus.Text = "Generating KML...";
                string kmlContent = GenerateKml(lines);

                lblStatus.Text = "Zipping into KMZ...";
                string outputDir = Path.GetDirectoryName(logFile) ?? AppDomain.CurrentDomain.BaseDirectory;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string kmzPath = Path.Combine(outputDir, $"{callsign.Replace("-", "_")}_Track_{timestamp}.kmz");

                await CreateKmzAsync(kmlContent, kmzPath);

                lblStatus.Text = "Done! Opening folder...";
                
                // Open Windows Explorer and highlight the newly created file
                Process.Start("explorer.exe", $"/select,\"{kmzPath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to build KMZ: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // Ensure the entire application (both this form and MainForm) shuts down
                Application.Exit();
            }
        }

        private string GenerateKml(string[] csvLines)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<kml xmlns=\"http://www.opengis.net/kml/2.2\">");
            sb.AppendLine("<Document>");
            sb.AppendLine($"  <name>{callsign} APRS Track</name>");
            
            // Add a simple style for the line
            sb.AppendLine("  <Style id=\"trackLine\">");
            sb.AppendLine("    <LineStyle>");
            sb.AppendLine("      <color>ff0000ff</color> ");
            sb.AppendLine("      <width>4</width>");
            sb.AppendLine("    </LineStyle>");
            sb.AppendLine("  </Style>");

            sb.AppendLine("  <Placemark>");
            sb.AppendLine($"    <name>{callsign} Path</name>");
            sb.AppendLine("    <styleUrl>#trackLine</styleUrl>");
            sb.AppendLine("    <LineString>");
            sb.AppendLine("      <extrude>1</extrude>");
            sb.AppendLine("      <tessellate>1</tessellate>");
            sb.AppendLine("      <altitudeMode>absolute</altitudeMode>");
            sb.AppendLine("      <coordinates>");

            // Skip the header row (i = 1)
            for (int i = 1; i < csvLines.Length; i++)
            {
                string[] parts = csvLines[i].Split(',');
                if (parts.Length >= 7)
                {
                    // CSV Format: time, lasttime, lat, lng, speed, course, altitude, comment
                    string lat = parts[2];
                    string lng = parts[3];
                    string alt = parts[6]; 
                    
                    sb.AppendLine($"        {lng},{lat},{alt}");
                }
            }

            sb.AppendLine("      </coordinates>");
            sb.AppendLine("    </LineString>");
            sb.AppendLine("  </Placemark>");
            
            sb.AppendLine("</Document>");
            sb.AppendLine("</kml>");

            return sb.ToString();
        }

        private async Task CreateKmzAsync(string kmlContent, string outputPath)
        {
            // Delete if it somehow already exists
            if (File.Exists(outputPath)) File.Delete(outputPath);

            using (FileStream zipToOpen = new FileStream(outputPath, FileMode.Create))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    ZipArchiveEntry kmlEntry = archive.CreateEntry("doc.kml");
                    using (StreamWriter writer = new StreamWriter(kmlEntry.Open()))
                    {
                        await writer.WriteAsync(kmlContent);
                    }
                }
            }
        }
    }
}