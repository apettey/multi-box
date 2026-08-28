using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace MultiBox.Core.Esi;

/// <summary>
/// Minimal ESI access for confirming character identity.
///
/// Log filenames already carry the character id and the banner carries the name, so this is
/// a verification step rather than a dependency: it confirms the id in the filename really
/// is the pilot named in the header, and fills in the portrait/corp details the UI shows.
/// Both endpoints used here are public - no OAuth, no scopes, nothing that could touch the
/// running client.
/// </summary>
public sealed class EsiClient : IDisposable
{
    private const string BaseUrl = "https://esi.evetech.net/latest";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public EsiClient(HttpClient? http = null, string userAgent = "MultiBox/1.0 (EVE log dashboard)")
    {
        _http = http ?? new HttpClient();
        _ownsClient = http is null;
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.Add("User-Agent", userAgent);
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    /// <summary>Resolves character names to ids via POST /universe/ids/.</summary>
    public async Task<IReadOnlyDictionary<string, long>> ResolveCharacterIdsAsync(
        IEnumerable<string> names, CancellationToken ct = default)
    {
        var list = names.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (list.Count == 0)
            return result;

        var payload = new StringContent(JsonSerializer.Serialize(list), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{BaseUrl}/universe/ids/?datasource=tranquility", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("characters", out var characters))
        {
            foreach (var c in characters.EnumerateArray())
            {
                if (c.TryGetProperty("name", out var n) && c.TryGetProperty("id", out var id))
                    result[n.GetString()!] = id.GetInt64();
            }
        }

        return result;
    }

    /// <summary>Fetches public character details for a known id.</summary>
    public async Task<EsiCharacter?> GetCharacterAsync(long characterId, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(
            $"{BaseUrl}/characters/{characterId}/?datasource=tranquility", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var dto = await response.Content.ReadFromJsonAsync<EsiCharacterDto>(cancellationToken: ct);
        return dto is null ? null : new EsiCharacter(characterId, dto.name ?? string.Empty, dto.corporation_id, dto.alliance_id);
    }

    /// <summary>
    /// Cross-checks the name in each log banner against the id in its filename.
    /// Returns the characters whose name and id disagree - normally empty, and a signal
    /// that a log folder holds files from a different account if it is not.
    /// </summary>
    public async Task<IReadOnlyList<string>> VerifyAsync(
        IReadOnlyDictionary<string, long> nameToIdFromLogs, CancellationToken ct = default)
    {
        var resolved = await ResolveCharacterIdsAsync(nameToIdFromLogs.Keys, ct);
        var mismatches = new List<string>();

        foreach (var (name, idFromLog) in nameToIdFromLogs)
        {
            if (resolved.TryGetValue(name, out var idFromEsi) && idFromEsi != idFromLog)
                mismatches.Add($"{name}: log says {idFromLog}, ESI says {idFromEsi}");
        }

        return mismatches;
    }

    public void Dispose()
    {
        if (_ownsClient)
            _http.Dispose();
    }

    private sealed record EsiCharacterDto(string? name, long corporation_id, long? alliance_id);
}

public sealed record EsiCharacter(long CharacterId, string Name, long CorporationId, long? AllianceId)
{
    public string PortraitUrl(int size = 128) =>
        $"https://images.evetech.net/characters/{CharacterId}/portrait?size={size}";
}
