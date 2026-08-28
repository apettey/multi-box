using System.Text.Json;

namespace MultiBox.Core.Config;

/// <summary>
/// Reads an existing "EVE-O Preview.json" and copies its window layout across, so the
/// panels can be positioned against the same client rectangles you already arranged.
///
/// eve-o-preview is serialised by Newtonsoft, which writes System.Drawing.Point either as
/// an object ({"X":1,"Y":2}) or as its TypeConverter string ("1,2") depending on version;
/// both forms are accepted here.
/// </summary>
public static class EveOPreviewImport
{
    /// <summary>Default install location of eve-o-preview's config.</summary>
    public static string DefaultConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "Local", "EVE-O Preview", "EVE-O Preview.json");

    public static bool TryImport(string path, MultiBoxConfig target)
    {
        if (!File.Exists(path))
            return false;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        if (root.TryGetProperty("ClientLayout", out var layouts) && layouts.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in layouts.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    continue;
                target.ClientLayout[entry.Name] = new ClientLayout(
                    GetInt(entry.Value, "X"),
                    GetInt(entry.Value, "Y"),
                    GetInt(entry.Value, "Width"),
                    GetInt(entry.Value, "Height"),
                    entry.Value.TryGetProperty("IsMaximized", out var max) && max.ValueKind == JsonValueKind.True);
            }
        }

        if (root.TryGetProperty("FlatLayout", out var flat) && flat.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in flat.EnumerateObject())
            {
                if (TryReadPoint(entry.Value, out var pt))
                    target.FlatLayout[entry.Name] = pt;
            }
        }

        ImportAliases(root, target);
        ImportCycleGroups(root, target);

        return true;
    }

    private static void ImportAliases(JsonElement root, MultiBoxConfig target)
    {
        if (!root.TryGetProperty("PerClientAliases", out var aliases) ||
            aliases.ValueKind != JsonValueKind.Object)
            return;

        foreach (var entry in aliases.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String &&
                entry.Value.GetString() is { Length: > 0 } alias)
                target.Aliases[MultiBoxConfig.CharacterFromKey(entry.Name)] = alias;
        }
    }

    /// <summary>
    /// Reads CycleGroupNForwardHotkeys / BackwardHotkeys / ClientsOrder for groups 1..5.
    ///
    /// ClientsOrder is a map of window title to a 1-based position. Ties are legal in
    /// eve-o's format and are broken by insertion order, so the sort has to be stable -
    /// OrderBy is, and that is the only reason this is not a plain Sort.
    /// </summary>
    private static void ImportCycleGroups(JsonElement root, MultiBoxConfig target)
    {
        target.EnsureCycleGroups();

        for (var i = 1; i <= MultiBoxConfig.MaxCycleGroups; i++)
        {
            var group = target.CycleGroups[i - 1];

            group.ForwardHotkeys = ReadHotkeys(root, $"CycleGroup{i}ForwardHotkeys");
            group.BackwardHotkeys = ReadHotkeys(root, $"CycleGroup{i}BackwardHotkeys");

            if (!root.TryGetProperty($"CycleGroup{i}ClientsOrder", out var order) ||
                order.ValueKind != JsonValueKind.Object)
                continue;

            group.Members = order.EnumerateObject()
                .Select(p => (Name: MultiBoxConfig.CharacterFromKey(p.Name),
                              Position: p.Value.TryGetInt32(out var v) ? v : int.MaxValue))
                .OrderBy(p => p.Position)
                .Select(p => p.Name)
                .ToList();
        }
    }

    /// <summary>eve-o writes hotkeys as either a single string or an array of them.</summary>
    private static List<string> ReadHotkeys(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return new List<string>();

        return value.ValueKind switch
        {
            JsonValueKind.String => Split(value.GetString()),
            JsonValueKind.Array => value.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .SelectMany(e => Split(e.GetString()))
                .ToList(),
            _ => new List<string>()
        };
    }

    private static List<string> Split(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .ToList();

    private static int GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static bool TryReadPoint(JsonElement element, out Pt point)
    {
        point = Pt.Zero;

        if (element.ValueKind == JsonValueKind.Object)
        {
            point = new Pt(GetInt(element, "X"), GetInt(element, "Y"));
            return true;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var parts = element.GetString()?.Split(',');
            if (parts is { Length: 2 } && int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
            {
                point = new Pt(x, y);
                return true;
            }
        }

        return false;
    }
}
