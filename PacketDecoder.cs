using System.Globalization;
using System.Text.RegularExpressions;

namespace MyAPRS
{
    internal static class PacketDecoder
    {
        internal static APRSData? Parse(string packet, bool yaesuMetres)
        {
            int separator = packet.IndexOf(':');
            if (separator < 0 || separator == packet.Length - 1) return null;
            string body = packet[(separator + 1)..];
            if (body[0] == (char)39 || body[0] == '`')
            {
                var destination = Regex.Match(packet[..separator], @">([^,]+)");
                return destination.Success ? ParseMicE(destination.Groups[1].Value, body, yaesuMetres) : null;
            }
            return body[0] is '!' or '=' or '@' or '/' ? ParseStandard(body) : null;
        }

        private static APRSData? ParseStandard(string raw)
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

        private static APRSData? ParseMicE(string dest, string body, bool isYaesuMetres)
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

    }

    internal sealed class APRSData
    {
        public double Lat, Lng, Altitude;
        public int Speed, Course;
    }
}
