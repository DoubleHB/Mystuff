using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiScout.Models;

namespace ApiScout.Services;

/// <summary>The "move my data to another PC" file.</summary>
public sealed class BackupFile
{
    public string App { get; set; } = ""; // "ApiScout" in a real export - how Import tells it from any other json
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; }
    public List<string> Favourites { get; set; } = [];
    public Dictionary<string, List<string>> Tags { get; set; } = [];
    public Dictionary<string, string> Notes { get; set; } = [];
    public Dictionary<string, List<string>> Collections { get; set; } = [];
    /// <summary>Saved keys and edited test requests: AES-256-GCM under a key derived from the passphrase. Null = exported without them.</summary>
    public string? Secrets { get; set; }
    public string? Salt { get; set; }
    public int Iterations { get; set; }
    public int SecretCount { get; set; }
}

public sealed class BackupSecrets
{
    public Dictionary<string, string> MyKeys { get; set; } = [];
    public Dictionary<string, ApiTestRequest> TestRequests { get; set; } = [];
    /// <summary>Request variables travel with the keys: ApiScout cannot know whether someone put a token in one.</summary>
    public Dictionary<string, Dictionary<string, string>> Variables { get; set; } = [];
}

public sealed record ImportSummary(int Favourites, int Tagged, int Notes, int Keys, int TestRequests, int KeptLocal, bool SecretsSkipped)
{
    public int CollectionEntries { get; init; }

    public override string ToString() =>
        (CollectionEntries > 0 ? $"{CollectionEntries:N0} collection entr{(CollectionEntries == 1 ? "y" : "ies")} added. " : "") +
        $"Imported {Favourites:N0} favourite(s), tags for {Tagged:N0} API(s), {Notes:N0} note(s), {Keys:N0} key(s) and {TestRequests:N0} saved test request(s)." +
        (KeptLocal > 0 ? $" {KeptLocal:N0} item(s) already on this PC were kept as they are." : "") +
        (SecretsSkipped ? " The saved keys in the file were skipped (no passphrase given)." : "");
}

/// <summary>
/// Saved keys are tied to this Windows account (DPAPI), so they cannot simply be copied to another PC.
/// Export decrypts them and re-encrypts them with a passphrase; import does the reverse. Without a
/// passphrase the keys stay out of the file altogether.
/// </summary>
public static class Backup
{
    private const int DefaultIterations = 310_000, MaxIterations = 5_000_000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Export(Store store, string? passphrase)
    {
        var file = new BackupFile
        {
            App = "ApiScout",
            ExportedAt = DateTime.Now,
            Favourites = [.. store.User.Favourites.Order()],
            Tags = store.User.Tags.ToDictionary(p => p.Key, p => p.Value.ToList()),
            Notes = new(store.User.Notes),
            Collections = store.User.Collections.ToDictionary(p => p.Key, p => p.Value.ToList()),
        };
        if (!string.IsNullOrEmpty(passphrase))
        {
            var secrets = new BackupSecrets();
            foreach (var key in store.User.MyKeys.Keys)
                if (store.GetMyKey(key) is { Length: > 0 } plain) secrets.MyKeys[key] = plain;
            foreach (var key in store.User.TestRequests.Keys)
                if (store.GetTestRequest(key) is { } request) secrets.TestRequests[key] = request;
            secrets.Variables = store.User.Variables.ToDictionary(p => p.Key, p => new Dictionary<string, string>(p.Value));

            var salt = RandomNumberGenerator.GetBytes(16);
            file.Salt = Convert.ToBase64String(salt);
            file.Iterations = DefaultIterations;
            file.SecretCount = secrets.MyKeys.Count + secrets.TestRequests.Count;
            file.Secrets = Convert.ToBase64String(Seal(JsonSerializer.SerializeToUtf8Bytes(secrets), passphrase, salt, DefaultIterations));
        }
        return JsonSerializer.Serialize(file, Json);
    }

    public static BackupFile Read(string json)
    {
        BackupFile? file;
        try { file = JsonSerializer.Deserialize<BackupFile>(json); }
        catch (JsonException) { file = null; }
        if (file is null || file.App != "ApiScout") throw new InvalidDataException("This is not an ApiScout export file.");
        // a hand-edited file may say null where a list belongs
        file.Favourites = [.. (file.Favourites ?? []).Where(f => !string.IsNullOrEmpty(f))];
        file.Tags = (file.Tags ?? []).Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value.Where(t => !string.IsNullOrWhiteSpace(t)).ToList());
        file.Notes = (file.Notes ?? []).Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value);
        file.Collections = (file.Collections ?? []).Where(p => p.Value is not null && p.Key.Trim().Length > 0).ToDictionary(p => p.Key, p => p.Value.Where(k => !string.IsNullOrEmpty(k)).ToList());
        // the count comes from the file: refuse one that would keep PBKDF2 busy for hours
        if (file.Secrets is not null && file.Iterations is < 0 or > MaxIterations) throw new InvalidDataException("The export file asks for an unreasonable amount of key stretching - it is damaged or not from ApiScout.");
        return file;
    }

    /// <summary>Merges into what is already there: nothing on this PC is overwritten or removed.</summary>
    /// <exception cref="CryptographicException">Wrong passphrase (or a damaged file).</exception>
    public static ImportSummary Import(Store store, BackupFile file, string? passphrase)
    {
        int favourites = 0, tagged = 0, notes = 0, keys = 0, requests = 0, kept = 0, collected = 0;

        BackupSecrets? secrets = null;
        bool skipped = file.Secrets is not null && string.IsNullOrEmpty(passphrase);
        if (file.Secrets is not null && !string.IsNullOrEmpty(passphrase))
        {
            // decrypt first, so a wrong passphrase changes nothing
            var plain = Open(Convert.FromBase64String(file.Secrets), passphrase, Convert.FromBase64String(file.Salt ?? ""), file.Iterations > 0 ? file.Iterations : DefaultIterations);
            try { secrets = JsonSerializer.Deserialize<BackupSecrets>(plain) ?? new(); }
            catch (JsonException) { throw new InvalidDataException("The encrypted part of the file opened, but what is inside is not ApiScout data."); }
            secrets.MyKeys = (secrets.MyKeys ?? []).Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary(p => p.Key, p => p.Value);
            secrets.TestRequests = (secrets.TestRequests ?? []).Where(p => p.Value?.Url is not null).ToDictionary(p => p.Key, p => p.Value);
        }

        foreach (var f in file.Favourites) if (store.User.Favourites.Add(f)) favourites++;
        foreach (var (key, tags) in file.Tags)
        {
            var have = store.User.Tags.GetValueOrDefault(key) ?? [];
            var merged = have.Concat(tags).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
            if (merged.Count > have.Count) { store.User.Tags[key] = merged; tagged++; }
        }
        foreach (var (key, note) in file.Notes)
        {
            if (!store.User.Notes.TryGetValue(key, out var local) || local.Length == 0) { store.User.Notes[key] = note; notes++; }
            else if (local != note) kept++;
        }
        foreach (var (name, apiKeys) in file.Collections)
        {
            // same name = same collection: what the file has is added to the end
            var mine = store.User.Collections.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
            if (!store.User.Collections.TryGetValue(mine, out var have)) store.User.Collections[mine] = have = [];
            foreach (var k in apiKeys) if (!have.Contains(k)) { have.Add(k); collected++; }
        }
        store.SaveUser();

        if (secrets is not null)
        {
            foreach (var (key, value) in secrets.MyKeys)
            {
                if (store.GetMyKey(key) is { Length: > 0 } local) { if (local != value) kept++; }
                else { store.SetMyKey(key, value); keys++; }
            }
            foreach (var (key, map) in secrets.Variables ?? [])
            {
                if (map is null) continue;
                if (!store.User.Variables.TryGetValue(key, out var mine)) store.User.Variables[key] = mine = new(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, value) in map) mine.TryAdd(name, value ?? ""); // a value already set on this PC stays
            }
            store.SaveUser();
            foreach (var (key, request) in secrets.TestRequests)
            {
                if (store.GetTestRequest(key) is not null) kept++;
                else { store.SetTestRequest(key, request); requests++; }
            }
        }
        return new(favourites, tagged, notes, keys, requests, kept, skipped) { CollectionEntries = collected };
    }

    // layout: 12-byte nonce | 16-byte tag | ciphertext
    private static byte[] Seal(byte[] plain, string passphrase, byte[] salt, int iterations)
    {
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(key);
        return [.. nonce, .. tag, .. cipher];
    }

    private static byte[] Open(byte[] sealedBytes, string passphrase, byte[] salt, int iterations)
    {
        if (sealedBytes.Length < 28 || salt.Length == 0) throw new CryptographicException("The encrypted part of the file is damaged.");
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), salt, iterations, HashAlgorithmName.SHA256, 32);
        var plain = new byte[sealedBytes.Length - 28];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(sealedBytes.AsSpan(0, 12), sealedBytes.AsSpan(28), sealedBytes.AsSpan(12, 16), plain);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        return plain;
    }
}
