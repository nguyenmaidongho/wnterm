// SnippetMergeCli - runs the REAL WNTerm.Core snippet merge (SnippetMerge.Merge / LwwMerge) so it can be fuzz-compared
// with the JavaScript implementation.
//
// stdin  (one JSON document):
//   { "local":  { "snippets": [...], "deletedSnippets": [...] },     // any other vault fields are ignored
//     "remote": { "snippets": [...], "deletedSnippets": [...] },
//     "now":    "2026-10-09T12:00:00Z" }                              // ISO-8601; used for tombstone pruning (180 days)
//   snippet   = { "id","name","command","vmId"|null,"autoEnter","createdAt","updatedAt" }
//   tombstone = { "id","deletedAt" }
//   Missing "snippets"/"deletedSnippets" = empty. "remote" may be null/absent (= no remote vault).
//
// stdout (one JSON document, compact):
//   { "version": 2, "sessions": [], "deleted": [],            // sessions are NOT merged by this tool
//     "snippets": [...],                                      // merged live snippets, sorted by lower-case id (ordinal)
//     "deletedSnippets": [...],                               // merged tombstones (pruned, sorted by id)
//     "stats": { "added": n, "updated": n, "deleted": n, "localChanged": bool } }
//
// Rules (see LwwMerge/SnippetMerge): per id the candidate with the greatest timestamp wins among
// local snippet(updatedAt ?? createdAt), remote snippet, local tombstone(deletedAt), remote tombstone(deletedAt).
// Ties: local tombstone > remote tombstone > local snippet > remote snippet (tombstone wins when deletedAt >= updatedAt).
// A tombstone is emitted only if it won and is <= 180 days older than "now"; ids are lower-cased.
// Exit code 0 on success, 1 on bad input (message on stderr).

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using WNTerm.Services;

try
{
    string input;
    using (var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false)))
        input = reader.ReadToEnd();

    using var doc = JsonDocument.Parse(input);
    var root = doc.RootElement;
    VaultData Read(string name)
    {
        if (!root.TryGetProperty(name, out var e) || e.ValueKind != JsonValueKind.Object) return new VaultData();
        var v = e.Deserialize<VaultData>() ?? new VaultData();
        v.Snippets ??= new();
        v.DeletedSnippets ??= new();
        return v;
    }
    bool hasRemote = root.TryGetProperty("remote", out var re) && re.ValueKind == JsonValueKind.Object;
    var local = Read("local");
    var remote = Read("remote");
    DateTime now = root.TryGetProperty("now", out var ne) && ne.ValueKind == JsonValueKind.String
        ? DateTime.Parse(ne.GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
        : DateTime.UtcNow;

    var r = SnippetMerge.Merge(local.Snippets, local.DeletedSnippets,
        hasRemote ? remote.Snippets : null, hasRemote ? remote.DeletedSnippets : null, now);

    var merged = new VaultData { Snippets = r.Snippets, DeletedSnippets = r.Tombstones };
    var node = JsonSerializer.SerializeToNode(merged)!.AsObject();
    node["stats"] = new System.Text.Json.Nodes.JsonObject
    {
        ["added"] = r.Added, ["updated"] = r.Updated, ["deleted"] = r.Deleted, ["localChanged"] = r.LocalChanged
    };
    node.Remove("savedAt");
    node.Remove("device");

    var opts = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    using var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
    stdout.Write(node.ToJsonString(opts));
    stdout.Write('\n');
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("SnippetMergeCli: " + ex.Message);
    return 1;
}
