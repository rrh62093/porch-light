using System.Text;

namespace PorchLight.Api.Services;

/// <summary>Builds a minimal RFC 5545 calendar file for one scheduled favor.</summary>
public static class Ics
{
    public static string Event(Guid uid, int sequence, DateTimeOffset start, TimeSpan duration,
        string summary, string description, string url)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(Fold(s)).Append("\r\n");
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Porch Light//Favors//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("BEGIN:VEVENT");
        Line($"UID:{uid}@porchlight");
        // A rescheduled favor keeps the same UID with a higher SEQUENCE, so calendars update it.
        Line($"SEQUENCE:{sequence}");
        Line($"DTSTAMP:{Utc(DateTimeOffset.UtcNow)}");
        Line($"DTSTART:{Utc(start)}");
        Line($"DTEND:{Utc(start + duration)}");
        Line($"SUMMARY:{Escape(summary)}");
        Line($"DESCRIPTION:{Escape(description)}");
        Line($"URL:{url}");
        Line("BEGIN:VALARM");
        Line("TRIGGER:-PT1H");
        Line("ACTION:DISPLAY");
        Line($"DESCRIPTION:{Escape(summary)}");
        Line("END:VALARM");
        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    private static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    private static string Escape(string s) => s
        .Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
        .Replace("\r\n", "\\n").Replace("\n", "\\n");

    /// <summary>Lines longer than 75 octets are folded with CRLF + space.</summary>
    private static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var count = 0;
        foreach (var ch in line)
        {
            var len = Encoding.UTF8.GetByteCount(ch.ToString());
            if (count + len > 74) { sb.Append("\r\n "); count = 1; }
            sb.Append(ch);
            count += len;
        }
        return sb.ToString();
    }
}
