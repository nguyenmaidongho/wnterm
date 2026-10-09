using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WNTerm.App.Services;
using WNTerm.Models;
using WNTerm.Services;
using Xunit;

namespace WNTerm.Tests;

public class SnippetTests : IDisposable
{
    private readonly string _dir;
    private readonly AppPaths _paths;
    private static readonly DateTime T0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public SnippetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "WNTermSnip_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _paths = new AppPaths(_dir, _dir);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Snippet S(string id, string cmd, DateTime? upd = null, string? vm = null) =>
        new() { Id = id, Name = cmd, Command = cmd, VmId = vm, CreatedAt = T0, UpdatedAt = upd ?? T0 };

    private static SnippetTombstone Tomb(string id, DateTime at) => new() { Id = id, DeletedAt = at };

    private static SnippetMergeResult Merge(Snippet[] l, SnippetTombstone[] lt, Snippet[]? r, SnippetTombstone[]? rt, DateTime? now = null) =>
        SnippetMerge.Merge(l, lt, r, rt, now ?? T0.AddDays(10));

    // ===== Store =====

    [Fact]
    public void Store_Save_StampsNewAndEdited_AndRaisesEvent()
    {
        var store = new SnippetStore(_paths) { Clock = () => T0 };
        int events = 0;
        store.ChangedByUser += () => events++;

        var a = new Snippet { Id = "a", Name = "n", Command = "ls" };
        store.Upsert(a);
        Assert.Equal(1, events);
        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal(T0, loaded[0].UpdatedAt);
        Assert.True(loaded[0].AutoEnter);
        Assert.Null(loaded[0].VmId);

        // Lưu lại không đổi → không phát sự kiện, giữ UpdatedAt.
        store.Clock = () => T0.AddHours(1);
        store.Save(store.Load());
        Assert.Equal(1, events);
        Assert.Equal(T0, store.Load()[0].UpdatedAt);

        // Sửa → UpdatedAt mới + sự kiện.
        var edit = store.Load()[0];
        edit.Command = "ls -la";
        store.Upsert(edit);
        Assert.Equal(2, events);
        Assert.Equal(T0.AddHours(1), store.Load()[0].UpdatedAt);
    }

    [Fact]
    public void Store_Delete_WritesTombstone_AndRecreateRemovesIt()
    {
        var store = new SnippetStore(_paths) { Clock = () => T0 };
        store.Upsert(new Snippet { Id = "a", Command = "ls" });
        store.Clock = () => T0.AddHours(2);
        store.Delete("a");
        Assert.Empty(store.Load());
        var t = Assert.Single(store.LoadTombstones());
        Assert.Equal("a", t.Id);
        Assert.Equal(T0.AddHours(2), t.DeletedAt);

        store.Clock = () => T0.AddHours(3);
        store.Upsert(new Snippet { Id = "a", Command = "ls again" });
        Assert.Empty(store.LoadTombstones());
    }

    [Fact]
    public void Store_SaveSynced_RaisesReplacedExternally_NotChangedByUser()
    {
        var store = new SnippetStore(_paths);
        int user = 0, ext = 0;
        store.ChangedByUser += () => user++;
        store.ReplacedExternally += () => ext++;
        store.SaveSynced(new[] { S("a", "ls") }, new[] { Tomb("b", T0) });
        Assert.Equal(0, user);
        Assert.Equal(1, ext);
        Assert.Single(store.Load());
        Assert.Equal(T0, store.Load()[0].UpdatedAt); // giữ nguyên dấu thời gian
    }

    [Fact]
    public void Store_Tombstones_PrunedAfter180Days()
    {
        var store = new SnippetStore(_paths) { Clock = () => T0.AddDays(200) };
        store.SaveSynced(Array.Empty<Snippet>(), new[] { Tomb("old", T0), Tomb("new", T0.AddDays(100)) });
        Assert.Equal(new[] { "new" }, store.LoadTombstones().Select(t => t.Id));
    }

    [Fact]
    public void Store_ForVm_OwnFirst_ThenGlobal_HidesOtherVms()
    {
        var all = new List<Snippet> { S("g", "zz-global"), S("v1", "bb", vm: "vm1"), S("v2", "aa", vm: "vm2"), S("v1b", "aa", vm: "VM1") };
        var list = SnippetStore.ForVm(all, "vm1");
        Assert.Equal(new[] { "v1b", "v1", "g" }, list.Select(s => s.Id));
        Assert.Equal(new[] { "g" }, SnippetStore.ForVm(all, "gone-vm").Select(s => s.Id));
    }

    [Fact]
    public void Store_FileIsAtomicAndLoadsCorruptAsEmpty()
    {
        File.WriteAllText(_paths.SnippetsFile, "{ not json");
        var store = new SnippetStore(_paths);
        Assert.Empty(store.Load());
        store.Upsert(new Snippet { Id = "a", Command = "x" });
        Assert.Single(store.Load());
        Assert.False(File.Exists(_paths.SnippetsFile + ".tmp"));
    }

    // ===== Merge =====

    [Fact]
    public void Merge_Add_FromEitherSide()
    {
        var r = Merge(new[] { S("a", "A") }, Array.Empty<SnippetTombstone>(), new[] { S("b", "B") }, null);
        Assert.Equal(new[] { "a", "b" }, r.Snippets.Select(s => s.Id));
        Assert.Equal(1, r.Added);
        Assert.True(r.LocalChanged);
    }

    [Fact]
    public void Merge_Edit_NewerWins_BothDirections()
    {
        var r = Merge(new[] { S("a", "old") }, Array.Empty<SnippetTombstone>(), new[] { S("a", "new", T0.AddHours(1)) }, null);
        Assert.Equal("new", r.Snippets.Single().Command);
        Assert.Equal(1, r.Updated);

        r = Merge(new[] { S("a", "local-new", T0.AddHours(2)) }, Array.Empty<SnippetTombstone>(), new[] { S("a", "remote", T0.AddHours(1)) }, null);
        Assert.Equal("local-new", r.Snippets.Single().Command);
        Assert.False(r.LocalChanged);
    }

    [Fact]
    public void Merge_EditConflict_SameTimestamp_LocalWins()
    {
        var r = Merge(new[] { S("a", "local") }, Array.Empty<SnippetTombstone>(), new[] { S("a", "remote") }, null);
        Assert.Equal("local", r.Snippets.Single().Command);
    }

    [Fact]
    public void Merge_RemoteTombstone_DeletesOlderLocal()
    {
        var r = Merge(new[] { S("a", "A") }, Array.Empty<SnippetTombstone>(), Array.Empty<Snippet>(), new[] { Tomb("a", T0.AddHours(1)) });
        Assert.Empty(r.Snippets);
        Assert.Equal(1, r.Deleted);
        Assert.Equal("a", r.Tombstones.Single().Id);
    }

    [Fact]
    public void Merge_LocalTombstone_DeletesOlderRemote_AndIsKept()
    {
        var r = Merge(Array.Empty<Snippet>(), new[] { Tomb("a", T0.AddHours(1)) }, new[] { S("a", "A") }, null);
        Assert.Empty(r.Snippets);
        Assert.Equal(T0.AddHours(1), r.Tombstones.Single().DeletedAt);
    }

    [Fact]
    public void Merge_TombstoneWinsOnTie()
    {
        var r = Merge(new[] { S("a", "A", T0) }, Array.Empty<SnippetTombstone>(), null, new[] { Tomb("a", T0) });
        Assert.Empty(r.Snippets);
        r = Merge(Array.Empty<Snippet>(), new[] { Tomb("a", T0) }, new[] { S("a", "A", T0) }, null);
        Assert.Empty(r.Snippets);
    }

    [Fact]
    public void Merge_RecreatedLater_BeatsTombstone_AndTombstoneIsDropped()
    {
        var r = Merge(new[] { S("a", "again", T0.AddHours(5)) }, Array.Empty<SnippetTombstone>(), Array.Empty<Snippet>(), new[] { Tomb("a", T0.AddHours(1)) });
        Assert.Equal("again", r.Snippets.Single().Command);
        Assert.Empty(r.Tombstones);

        // Máy chủ có bản mới hơn dấu xóa ở máy này.
        r = Merge(Array.Empty<Snippet>(), new[] { Tomb("a", T0.AddHours(1)) }, new[] { S("a", "again", T0.AddHours(5)) }, null);
        Assert.Equal("again", r.Snippets.Single().Command);
        Assert.Empty(r.Tombstones);
        Assert.Equal(1, r.Added);
        Assert.True(r.LocalChanged);
    }

    [Fact]
    public void Merge_PrunesOldTombstones()
    {
        var now = T0.AddDays(400);
        var r = SnippetMerge.Merge(Array.Empty<Snippet>(), new[] { Tomb("old", T0), Tomb("fresh", now.AddDays(-5)) },
            Array.Empty<Snippet>(), new[] { Tomb("old2", T0.AddDays(10)) }, now);
        Assert.Equal(new[] { "fresh" }, r.Tombstones.Select(t => t.Id));
        Assert.True(r.LocalChanged); // dấu xóa cũ bị bỏ khỏi file local
    }

    [Fact]
    public void Merge_NullRemote_KeepsLocalUnchanged()
    {
        var r = Merge(new[] { S("a", "A") }, new[] { Tomb("z", T0) }, null, null);
        Assert.Single(r.Snippets);
        Assert.Single(r.Tombstones);
        Assert.False(r.LocalChanged);
    }

    [Fact]
    public void Merge_IsIdempotent_AndSymmetricInContent()
    {
        var l = new[] { S("a", "A1", T0.AddHours(1)), S("b", "B", T0) };
        var lt = new[] { Tomb("c", T0.AddHours(3)) };
        var rr = new[] { S("a", "A2", T0.AddHours(2)), S("c", "C", T0.AddHours(1)), S("d", "D") };
        var rt = new[] { Tomb("b", T0.AddHours(4)) };
        var m1 = Merge(l, lt, rr, rt);
        var m2 = Merge(rr, rt, l, lt);
        string Key(SnippetMergeResult m) => string.Join("|", m.Snippets.Select(s => s.Id + ":" + s.Command)) + "#" + string.Join("|", m.Tombstones.Select(t => t.Id + ":" + t.DeletedAt.Ticks));
        Assert.Equal(Key(m1), Key(m2));
        var m3 = Merge(m1.Snippets.ToArray(), m1.Tombstones.ToArray(), rr, rt);
        Assert.Equal(Key(m1), Key(m3));
        Assert.Equal("A2", m1.Snippets.Single(s => s.Id == "a").Command);
        Assert.DoesNotContain(m1.Snippets, s => s.Id is "b" or "c");
    }

    // ===== Vault JSON =====

    [Fact]
    public void Vault_Deserialize_IgnoresUnknown_MissingSnippetsMeanEmpty_AndContractFields()
    {
        string json = """
        {"version":2,"sessions":[],"deleted":[],"futureField":{"x":1},
         "snippets":[{"id":"a","name":"n","command":"ls","vmId":null,"autoEnter":false,"createdAt":"2026-10-01T00:00:00Z","updatedAt":"2026-10-02T00:00:00.123Z","extra":true}],
         "deletedSnippets":[{"id":"b","deletedAt":"2026-10-03T00:00:00Z","x":1}]}
        """;
        var v = JsonSerializer.Deserialize<VaultData>(json)!;
        Assert.Single(v.Snippets);
        Assert.False(v.Snippets[0].AutoEnter);
        Assert.Equal("b", v.DeletedSnippets.Single().Id);

        var old = JsonSerializer.Deserialize<VaultData>("""{"version":2,"sessions":[],"deleted":[]}""")!;
        Assert.Empty(old.Snippets);
        Assert.Empty(old.DeletedSnippets);

        using var back = JsonDocument.Parse(JsonSerializer.Serialize(v));
        var s0 = back.RootElement.GetProperty("snippets")[0];
        foreach (var f in new[] { "id", "name", "command", "vmId", "autoEnter", "createdAt", "updatedAt" }) Assert.True(s0.TryGetProperty(f, out _), f);
        Assert.True(back.RootElement.GetProperty("deletedSnippets")[0].TryGetProperty("deletedAt", out _));
    }

    [Fact]
    public void Vault_WithHundredsOfSnippets_StaysFarBelowServerLimit()
    {
        var v = new VaultData();
        for (int i = 0; i < 500; i++) v.Snippets.Add(S(Guid.NewGuid().ToString(), new string('x', 200) + i, vm: Guid.NewGuid().ToString()));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(v).Length < 400_000); // server max_vault_bytes mặc định 5 MiB
    }

    // ===== LwwMerge (dùng chung với VM) =====

    [Fact]
    public void Lww_SessionTie_PrefersLiveOverTombstone()
    {
        Assert.Equal(LwwKind.Local, LwwMerge.Resolve(T0, T0, T0, T0).kind);
        Assert.Equal(LwwKind.Remote, LwwMerge.Resolve(null, T0, T0, T0).kind);
        Assert.Equal(LwwKind.LocalTombstone, LwwMerge.Resolve(null, null, T0, T0).kind);
        Assert.Equal(LwwKind.RemoteTombstone, LwwMerge.Resolve(T0, null, null, T0.AddSeconds(1)).kind);
    }

    // ===== Theo dõi lệnh vừa gõ =====

    private static string? Typed(params string[] chunks)
    {
        var t = new TypedLineTracker();
        foreach (var c in chunks) t.Feed(c);
        return t.LastCommand;
    }

    [Fact]
    public void Tracker_PlainTyping_CommitsOnEnter()
    {
        Assert.Equal("ls -la", Typed("l", "s", " ", "-", "l", "a", "\r"));
        Assert.Null(Typed("ls"));
    }

    [Fact]
    public void Tracker_Backspace_And_Paste()
    {
        Assert.Equal("lx", Typed("l", "s", "\u007f", "x", "\r"));
        Assert.Equal("echo hi", Typed("echo hi\r"));
        Assert.Equal("second", Typed("first\rsecond\r"));
    }

    [Fact]
    public void Tracker_HistoryTabEscapeCtrl_AreUnreliable()
    {
        Assert.Null(Typed("\u001b[A", "\r"));            // mũi tên lên
        Assert.Null(Typed("ls", "\t", "\r"));             // Tab
        Assert.Null(Typed("ls", "\u001b[D", "x", "\r"));  // mũi tên trái
        Assert.Null(Typed("ls", "\u0017", "\r"));         // Ctrl+W
        Assert.Null(Typed("ls", "\u0012", "\r"));         // Ctrl+R
    }

    [Fact]
    public void Tracker_UnreliableLine_ClearsPreviousCommand_ThenRecovers()
    {
        var t = new TypedLineTracker();
        t.Feed("ls\r");
        Assert.Equal("ls", t.LastCommand);
        t.Feed("\u001b[A"); t.Feed("\r");
        Assert.Null(t.LastCommand);
        t.Feed("pwd\r");
        Assert.Equal("pwd", t.LastCommand);
    }

    [Fact]
    public void Tracker_EmptyEnter_KeepsLast_CtrlC_ResetsLine()
    {
        var t = new TypedLineTracker();
        t.Feed("ls\r"); t.Feed("\r");
        Assert.Equal("ls", t.LastCommand);
        t.Feed("junk"); t.Feed("\u0003"); t.Feed("ok\r");
        Assert.Equal("ok", t.LastCommand);
        t.MarkUnreliable(); t.Feed("\r");
        Assert.Null(t.LastCommand);
    }
}
