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

        return true;
    }

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
