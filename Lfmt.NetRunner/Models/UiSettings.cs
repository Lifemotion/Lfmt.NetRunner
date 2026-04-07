namespace Lfmt.NetRunner.Models;

public class UiSettings
{
    public int RefreshIntervalSeconds { get; set; } = 15;
    public int BuildTimeoutSeconds { get; set; } = 600;
    public int JournalLines { get; set; } = 50;
    public string TimeZone { get; set; } = "UTC";
    public string TimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public long MaxUploadMb { get; set; } = 100;

    public static UiSettings FromIni(Dictionary<string, Dictionary<string, string>> ini)
    {
        var s = new UiSettings();
        if (!ini.TryGetValue("ui", out var ui)) return s;

        if (ui.TryGetValue("refresh_interval", out var r) && int.TryParse(r, out var rv)) s.RefreshIntervalSeconds = rv;
        if (ui.TryGetValue("build_timeout", out var b) && int.TryParse(b, out var bv)) s.BuildTimeoutSeconds = bv;
        if (ui.TryGetValue("journal_lines", out var j) && int.TryParse(j, out var jv)) s.JournalLines = jv;
        if (ui.TryGetValue("timezone", out var tz)) s.TimeZone = tz;
        if (ui.TryGetValue("time_format", out var tf)) s.TimeFormat = tf;
        if (ui.TryGetValue("max_upload_mb", out var m) && long.TryParse(m, out var mv)) s.MaxUploadMb = mv;

        return s;
    }

    public Dictionary<string, Dictionary<string, string>> ToIni() => new()
    {
        ["ui"] = new()
        {
            ["refresh_interval"] = RefreshIntervalSeconds.ToString(),
            ["build_timeout"] = BuildTimeoutSeconds.ToString(),
            ["journal_lines"] = JournalLines.ToString(),
            ["timezone"] = TimeZone,
            ["time_format"] = TimeFormat,
            ["max_upload_mb"] = MaxUploadMb.ToString(),
        }
    };

    public string FormatTime(DateTimeOffset? dt)
    {
        if (dt == null) return "Never";
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
            var local = TimeZoneInfo.ConvertTime(dt.Value, tz);
            return local.ToString(TimeFormat);
        }
        catch
        {
            return dt.Value.ToString(TimeFormat);
        }
    }
}
