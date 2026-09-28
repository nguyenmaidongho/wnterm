using System;
using System.IO;
using System.Linq;
using SNTerm.Models;
using SNTerm.Services;
using Xunit;

namespace SNTerm.Tests;

public class MobaRealTest
{
    [Fact]
    public void TestMobaWorkspaceFile_All167Imported()
    {
        string p = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../mobaxx.mxtsessions"));
        Assert.True(File.Exists(p), "File not found: " + p);

        string tempDir = Path.Combine(Path.GetTempPath(), "MobaTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var store = new SessionStore(new AppPaths(tempDir, tempDir));
            var importer = new SessionImporter(store);
            var exportFile = importer.ReadAndValidateFile(p);
            Assert.Equal("mobaxterm-sessions", exportFile.Format);
            Assert.Equal(167, exportFile.Sessions.Count);

            var res = importer.Import(exportFile, null, ConflictResolution.Skip);
            Assert.Equal(167, res.ImportedCount);
            Assert.Equal(0, res.SkippedCount);

            var loaded = store.Load(out _);
            Assert.Equal(167, loaded.Count);

            // Verify default username converted
            Assert.All(loaded, s => Assert.NotEqual("<default>", s.Username, StringComparer.OrdinalIgnoreCase));

            // Verify key files
            var withKey = loaded.Where(s => !string.IsNullOrEmpty(s.KeyFilePath)).ToList();
            Assert.Equal(30, withKey.Count);
            Assert.All(withKey, s => Assert.Equal(@"F:\keylogin.ppk", s.KeyFilePath));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}