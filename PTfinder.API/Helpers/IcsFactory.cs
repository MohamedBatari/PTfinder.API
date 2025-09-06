using System.Text;

namespace PTfinder.API.Helpers;

public static class IcsFactory
{
    public static (string FileName, string ContentType, byte[] Bytes) CreateBookingIcs(
        string title, string description, DateTime startUtc, DateTime endUtc, string location, string uid)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR");
        sb.AppendLine("VERSION:2.0");
        sb.AppendLine("PRODID:-//PTfinderNow//EN");
        sb.AppendLine("CALSCALE:GREGORIAN");
        sb.AppendLine("METHOD:PUBLISH");
        sb.AppendLine("BEGIN:VEVENT");
        sb.AppendLine($"UID:{uid}");
        sb.AppendLine($"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
        sb.AppendLine($"DTSTART:{startUtc:yyyyMMdd'T'HHmmss'Z'}");
        sb.AppendLine($"DTEND:{endUtc:yyyyMMdd'T'HHmmss'Z'}");
        sb.AppendLine($"SUMMARY:{Escape(title)}");
        sb.AppendLine($"DESCRIPTION:{Escape(description)}");
        sb.AppendLine($"LOCATION:{Escape(location)}");
        sb.AppendLine("END:VEVENT");
        sb.AppendLine("END:VCALENDAR");

        return ("booking.ics", "text/calendar; method=PUBLISH", Encoding.UTF8.GetBytes(sb.ToString()));

        static string Escape(string s) =>
            s.Replace(@"\", @"\\").Replace(";", @"\;").Replace(",", @"\,").Replace("\n", "\\n");
    }
}

