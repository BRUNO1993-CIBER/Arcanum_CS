using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arcanum.Core;

public class VaultEntry
{
    [JsonPropertyName("service")]    public string  Service   { get; set; } = "";
    [JsonPropertyName("login")]      public string  Login     { get; set; } = "";
    [JsonPropertyName("password")]   public string  Password  { get; set; } = "";
    [JsonPropertyName("url")]        public string  Url       { get; set; } = "";
    [JsonPropertyName("notes")]      public string  Notes     { get; set; } = "";
    [JsonPropertyName("totp_seed")]  public string? TotpSeed  { get; set; }
    [JsonPropertyName("id")]         public string  Id        { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("created_at")] public string  CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("updated_at")] public string  UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");

    public void Touch() => UpdatedAt = DateTime.UtcNow.ToString("O");
}

internal class VaultData
{
    [JsonPropertyName("vault_version")] public string           VaultVersion { get; set; } = "1.0";
    [JsonPropertyName("created_at")]    public string           CreatedAt    { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("updated_at")]    public string           UpdatedAt    { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("entries")]       public List<VaultEntry> Entries      { get; set; } = [];
}

public class Vault
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly VaultData _data;

    public Vault()                 => _data = new VaultData();
    private Vault(VaultData data)  => _data = data;

    public IReadOnlyList<VaultEntry> Entries     => _data.Entries;
    public int                       EntryCount  => _data.Entries.Count;

    public static Vault FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<VaultData>(json)
            ?? throw new InvalidOperationException("JSON inválido.");
        return new Vault(data);
    }

    public string ToJson() => JsonSerializer.Serialize(_data, _jsonOpts);

    public void Add(VaultEntry entry)
    {
        _data.Entries.Add(entry);
        _data.UpdatedAt = DateTime.UtcNow.ToString("O");
    }

    public bool Update(string id, Action<VaultEntry> apply)
    {
        var entry = _data.Entries.Find(e => e.Id == id);
        if (entry is null) return false;
        apply(entry);
        entry.Touch();
        _data.UpdatedAt = DateTime.UtcNow.ToString("O");
        return true;
    }

    public bool Delete(string id)
    {
        int removed = _data.Entries.RemoveAll(e => e.Id == id);
        if (removed > 0) _data.UpdatedAt = DateTime.UtcNow.ToString("O");
        return removed > 0;
    }

    public List<VaultEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [.. _data.Entries];
        var q = query.ToLowerInvariant();
        return _data.Entries.FindAll(e =>
            e.Service.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Login.Contains(q, StringComparison.OrdinalIgnoreCase)   ||
            e.Url.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}
