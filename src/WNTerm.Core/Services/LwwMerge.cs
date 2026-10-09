using System;
using System.Collections.Generic;
using System.Linq;
using WNTerm.Models;

namespace WNTerm.Services;

public enum LwwKind { Local, Remote, LocalTombstone, RemoteTombstone }

/// <summary>
/// Quy tắc "sự kiện mới nhất thắng" dùng chung cho VM và snippet: mỗi mục (theo Id) có tối đa 4 ứng viên
/// (bản ở máy này, bản trên máy chủ, dấu xóa ở máy này, dấu xóa trên máy chủ); ứng viên có thời điểm lớn nhất thắng.
/// Hòa thời điểm: bản ở máy này &gt; bản máy chủ &gt; dấu xóa (không bao giờ xóa khi không chắc) —
/// trừ khi tieFavorsTombstone (snippet): dấu xóa thắng nếu deletedAt &gt;= updatedAt.
/// </summary>
public static class LwwMerge
{
    public static (LwwKind kind, DateTime at) Resolve(DateTime? local, DateTime? remote, DateTime? localTomb, DateTime? remoteTomb, bool tieFavorsTombstone = false)
    {
        var cands = new List<(DateTime at, int rank, LwwKind kind)>();
        if (local is DateTime l) cands.Add((l, tieFavorsTombstone ? 1 : 3, LwwKind.Local));
        if (remote is DateTime r) cands.Add((r, tieFavorsTombstone ? 0 : 2, LwwKind.Remote));
        if (localTomb is DateTime lt) cands.Add((lt, tieFavorsTombstone ? 3 : 1, LwwKind.LocalTombstone));
        if (remoteTomb is DateTime rt) cands.Add((rt, tieFavorsTombstone ? 2 : 0, LwwKind.RemoteTombstone));
        var win = cands.OrderByDescending(c => c.at).ThenByDescending(c => c.rank).First();
        return (win.kind, win.at);
    }
}

public record SnippetMergeResult(List<Snippet> Snippets, List<SnippetTombstone> Tombstones, int Added, int Updated, int Deleted, bool LocalChanged);

/// <summary>Gộp snippet máy này với bản trên máy chủ (thuần hàm, không đụng đĩa) — xem <see cref="LwwMerge"/>.</summary>
public static class SnippetMerge
{
    public static SnippetMergeResult Merge(IEnumerable<Snippet> local, IEnumerable<SnippetTombstone> localTombs,
        IEnumerable<Snippet>? remote, IEnumerable<SnippetTombstone>? remoteTombs, DateTime nowUtc)
    {
        static string K(string id) => id.Trim().ToLowerInvariant();
        var localLive = local.Where(s => !string.IsNullOrWhiteSpace(s.Id)).GroupBy(s => K(s.Id)).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EffectiveUpdatedAt).First());
        var remoteLive = (remote ?? Enumerable.Empty<Snippet>()).Where(s => !string.IsNullOrWhiteSpace(s.Id)).GroupBy(s => K(s.Id)).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.EffectiveUpdatedAt).First());
        var lt = localTombs.Where(t => !string.IsNullOrWhiteSpace(t.Id)).GroupBy(t => K(t.Id)).ToDictionary(g => g.Key, g => g.Max(t => t.DeletedAt));
        var rt = (remoteTombs ?? Enumerable.Empty<SnippetTombstone>()).Where(t => !string.IsNullOrWhiteSpace(t.Id)).GroupBy(t => K(t.Id)).ToDictionary(g => g.Key, g => g.Max(t => t.DeletedAt));

        var outList = new List<Snippet>();
        var outTombs = new List<SnippetTombstone>();
        int added = 0, updated = 0, deleted = 0;
        bool changed = false;

        foreach (var id in localLive.Keys.Concat(remoteLive.Keys).Concat(lt.Keys).Concat(rt.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            localLive.TryGetValue(id, out var l);
            remoteLive.TryGetValue(id, out var r);
            var (kind, at) = LwwMerge.Resolve(l?.EffectiveUpdatedAt, r?.EffectiveUpdatedAt,
                lt.TryGetValue(id, out var a) ? a : null, rt.TryGetValue(id, out var b) ? b : null, tieFavorsTombstone: true);
            switch (kind)
            {
                case LwwKind.Local: outList.Add(l!); break;
                case LwwKind.Remote:
                    outList.Add(r!);
                    if (l == null) added++;
                    else if (l.ContentFingerprint != r!.ContentFingerprint) updated++;
                    changed = true;
                    break;
                default:
                    if (l != null) { deleted++; changed = true; }
                    if ((nowUtc - at).TotalDays <= TrashStore.TombstoneDays)
                        outTombs.Add(new SnippetTombstone { Id = id, DeletedAt = at });
                    else if (lt.ContainsKey(id)) changed = true;
                    break;
            }
        }
        // Dấu xóa local nay không còn (bị bản mới hơn thắng, hoặc quá hạn) → file local cần ghi lại.
        if (!changed && lt.Keys.Any(k => !outTombs.Any(t => t.Id == k))) changed = true;
        return new SnippetMergeResult(outList, outTombs.OrderBy(t => t.Id, StringComparer.Ordinal).ToList(), added, updated, deleted, changed);
    }
}
