namespace Lfmt.NetRunner.Services;

public static class IniParser
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = "";

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim().ToLowerInvariant();
                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var eqIndex = line.IndexOf('=');
            if (eqIndex <= 0 || currentSection.Length == 0)
                continue;

            var key = line[..eqIndex].Trim();
            var value = line[(eqIndex + 1)..].Trim();
            result[currentSection][key] = value;
        }

        return result;
    }

    public static string Serialize(Dictionary<string, Dictionary<string, string>> data)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (section, pairs) in data)
        {
            sb.AppendLine($"[{section}]");
            foreach (var (key, value) in pairs)
                sb.AppendLine($"{key} = {value}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
