using System.Globalization;
using System.IO.Compression;
using System.Xml;
using Microsoft.VisualBasic.FileIO;

namespace MyAPRS;

internal static class KmzBuilder
{
    internal static int Export(string callsign, DateTimeOffset start, DateTimeOffset end, string output)
    {
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        int count = 0;
        try
        {
            using (var input = Archive.OpenShared(Archive.LogPath(callsign)))
            using (var csv = new TextFieldParser(input) { Delimiters = new[] { "," } })
            using (var file = new FileStream(temporary, FileMode.CreateNew))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            using (var xml = XmlWriter.Create(zip.CreateEntry("doc.kml").Open(),
                new XmlWriterSettings { CloseOutput = true }))
            {
                const string kml = "http://www.opengis.net/kml/2.2";
                xml.WriteStartElement("kml", kml);
                xml.WriteStartElement("Document", kml);
                xml.WriteElementString("name", kml, $"{callsign} APRS Track");
                xml.WriteStartElement("Placemark", kml);
                xml.WriteStartElement("LineString", kml);
                xml.WriteElementString("altitudeMode", kml, "absolute");
                xml.WriteStartElement("coordinates", kml);
                while (!csv.EndOfData)
                {
                    string[]? fields;
                    try { fields = csv.ReadFields(); }
                    catch (MalformedLineException) { continue; }
                    if (fields is not { Length: >= 7 } ||
                        !DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var timestamp) ||
                        timestamp < start || timestamp >= end ||
                        !Number(fields[2], out double latitude) || latitude is < -90 or > 90 ||
                        !Number(fields[3], out double longitude) || longitude is < -180 or > 180 ||
                        !Number(fields[6], out double altitude)) continue;

                    xml.WriteString(FormattableString.Invariant($"{longitude},{latitude},{altitude}\n"));
                    count++;
                }
                xml.WriteEndDocument();
            }
            if (count < 2) throw new InvalidDataException("A KMZ track needs at least two positions in the selected date range.");
            File.Move(temporary, output, overwrite: true);
            return count;
        }
        finally { File.Delete(temporary); }
    }

    private static bool Number(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
