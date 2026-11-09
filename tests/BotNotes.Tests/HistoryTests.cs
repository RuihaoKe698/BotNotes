using System;
using System.IO;
using System.Linq;
using System.Text;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class HistoryTests
 {
  [Fact]
  public void Snapshot_WritesUtf8BomFile_WithTimestampName()
  {
   using (var sb = new Sandbox())
   {
    string note = sb.WriteFile(sb.SaveDir, "note.md", "原来的内容");
    var now = new DateTime(2026, 11, 9, 14, 32, 5);
    var result = MainWindow.SaveHistorySnapshot(sb.BotNotesDir, note, "第一版", now, false);

    Assert.True(result.Saved);
    var index = MainWindow.ReadHistoryIndex(sb.BotNotesDir);
    Assert.Single(index);
    Assert.Equal(note, index[0].Path);
    string expected = Path.Combine(MainWindow.HistoryFolder(sb.BotNotesDir, index[0].Id), "20261109-143205.md");
    Assert.Equal(expected, result.Path);
    Assert.True(File.Exists(expected));

    byte[] bytes = File.ReadAllBytes(expected);
    Assert.True(bytes.Length >= 3);
    Assert.Equal(0xEF, bytes[0]);
    Assert.Equal(0xBB, bytes[1]);
    Assert.Equal(0xBF, bytes[2]);
    Assert.Equal("第一版", File.ReadAllText(expected, Encoding.UTF8));

    var listed = MainWindow.ListHistorySnapshots(sb.BotNotesDir, note);
    Assert.Single(listed);
    Assert.Equal(now, listed[0].Time);
    Assert.Equal("第一版".Length, listed[0].Length);
   }
  }

  [Fact]
  public void Snapshot_SkipsIdenticalContent_AndSuffixesSameSecond()
  {
   using (var sb = new Sandbox())
   {
    string note = sb.WriteFile(sb.SaveDir, "note.md", "内容");
    var now = new DateTime(2026, 11, 9, 14, 32, 5);
    var first = MainWindow.SaveHistorySnapshot(sb.BotNotesDir, note, "同一份", now, false);
    Assert.True(first.Saved);

    var same = MainWindow.SaveHistorySnapshot(sb.BotNotesDir, note, "同一份", now.AddSeconds(1), false);
    Assert.False(same.Saved);
    var folder = MainWindow.HistoryFolder(sb.BotNotesDir, MainWindow.ReadHistoryIndex(sb.BotNotesDir)[0].Id);
    Assert.Single(MainWindow.ListHistoryFiles(folder));

    var changed = MainWindow.SaveHistorySnapshot(sb.BotNotesDir, note, "改了", now, false);
    Assert.True(changed.Saved);
    Assert.EndsWith("20261109-143205-2.md", changed.Path);
    var files = MainWindow.ListHistoryFiles(folder);
    Assert.Equal(2, files.Count);
    Assert.Equal("20261109-143205-2.md", Path.GetFileName(files[0].Path));

    var forced = MainWindow.SaveHistorySnapshot(sb.BotNotesDir, note, "改了", now, true);
    Assert.True(forced.Saved);
    Assert.EndsWith("20261109-143205-3.md", forced.Path);
   }
  }

  [Fact]
  public void Index_ReusesIdPerPath_AndKeepsSnapshotsAfterRename()
  {
   using (var sb = new Sandbox())
   {
    string first = sb.WriteFile(sb.SaveDir, "first.md", "A");
    string second = sb.WriteFile(sb.SaveDir, "second.md", "B");
    var now = new DateTime(2026, 11, 9, 10, 0, 0);

    Assert.True(MainWindow.SaveHistorySnapshot(sb.BotNotesDir, first, "A1", now, false).Saved);
    string firstId = MainWindow.ReadHistoryIndex(sb.BotNotesDir)[0].Id;
    Assert.True(MainWindow.SaveHistorySnapshot(sb.BotNotesDir, first, "A2", now.AddSeconds(1), false).Saved);
    Assert.Equal(firstId, MainWindow.ReadHistoryIndex(sb.BotNotesDir)[0].Id);

    Assert.True(MainWindow.SaveHistorySnapshot(sb.BotNotesDir, second, "B1", now, false).Saved);
    var index = MainWindow.ReadHistoryIndex(sb.BotNotesDir);
    Assert.Equal(2, index.Count);
    string secondId = index.First(e => e.Path == second).Id;
    Assert.NotEqual(firstId, secondId);

    string renamed = Path.Combine(sb.SaveDir, "renamed.md");
    File.Move(first, renamed);
    Assert.True(MainWindow.RenameHistoryPath(sb.BotNotesDir, first, renamed));

    index = MainWindow.ReadHistoryIndex(sb.BotNotesDir);
    var moved = index.Single(e => e.Id == firstId);
    Assert.Equal(renamed, moved.Path);
    Assert.Equal(firstId, index.Single(e => e.Path == renamed).Id);
    Assert.Equal(2, MainWindow.ListHistorySnapshots(sb.BotNotesDir, renamed).Count);
    Assert.Empty(MainWindow.ListHistorySnapshots(sb.BotNotesDir, first));
   }
  }

  [Fact]
  public void Cleanup_KeepsRecentThirtyAndLastOfEachDay()
  {
   using (var sb = new Sandbox())
   {
    string note = sb.WriteFile(sb.SaveDir, "note.md", "内容");
    var index = MainWindow.ReadHistoryIndex(sb.BotNotesDir);
    var entry = MainWindow.EnsureHistoryEntry(index, sb.BotNotesDir, note);
    MainWindow.WriteHistoryIndex(sb.BotNotesDir, index);
    string folder = MainWindow.HistoryFolder(sb.BotNotesDir, entry.Id);
    var now = new DateTime(2026, 11, 9, 20, 0, 0);

    // 60 份，跨 40 天：每天中午一份，最近 20 天再加 18:00 一份。
    for (int day = 0; day < 40; day++)
     Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folder, "中午 " + day, now.Date.AddDays(-day).AddHours(12), true));
    for (int day = 0; day < 20; day++)
     Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folder, "傍晚 " + day, now.Date.AddDays(-day).AddHours(18), true));
    Assert.Equal(60, MainWindow.ListHistoryFiles(folder).Count);

    MainWindow.CleanupHistoryForNote(sb.BotNotesDir, entry.Id, now, 30, 30);
    var files = MainWindow.ListHistoryFiles(folder);
    Assert.Equal(45, files.Count);

    for (int day = 0; day < 15; day++)
    {
     Assert.Contains(files, f => f.Time == now.Date.AddDays(-day).AddHours(12));
     Assert.Contains(files, f => f.Time == now.Date.AddDays(-day).AddHours(18));
    }
    for (int day = 15; day < 20; day++)
    {
     Assert.DoesNotContain(files, f => f.Time == now.Date.AddDays(-day).AddHours(12));
     Assert.Contains(files, f => f.Time == now.Date.AddDays(-day).AddHours(18));
    }
    for (int day = 20; day < 30; day++)
     Assert.Contains(files, f => f.Time == now.Date.AddDays(-day).AddHours(12));
    for (int day = 30; day < 40; day++)
     Assert.DoesNotContain(files, f => f.Time.Date == now.Date.AddDays(-day));
   }
  }

  [Fact]
  public void Cleanup_DeletesOldestGlobally_ButKeepsOnePerNote()
  {
   using (var sb = new Sandbox())
   {
    string noteA = sb.WriteFile(sb.SaveDir, "a.md", "A");
    string noteB = sb.WriteFile(sb.SaveDir, "b.md", "B");
    var index = MainWindow.ReadHistoryIndex(sb.BotNotesDir);
    var entryA = MainWindow.EnsureHistoryEntry(index, sb.BotNotesDir, noteA);
    var entryB = MainWindow.EnsureHistoryEntry(index, sb.BotNotesDir, noteB);
    MainWindow.WriteHistoryIndex(sb.BotNotesDir, index);
    string folderA = MainWindow.HistoryFolder(sb.BotNotesDir, entryA.Id);
    string folderB = MainWindow.HistoryFolder(sb.BotNotesDir, entryB.Id);
    var now = new DateTime(2026, 11, 9, 20, 0, 0);
    string text = new string('a', 1000);

    // 两个编号交错，最旧的是 A0、B0……，最新的各留一份。
    for (int i = 0; i < 4; i++)
    {
     Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folderA, text + "A" + i, now.Date.AddDays(-8 + i * 2), true));
     Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folderB, text + "B" + i, now.Date.AddDays(-7 + i * 2), true));
    }
    var all = MainWindow.ListHistoryFiles(folderA).Concat(MainWindow.ListHistoryFiles(folderB)).ToArray();
    Assert.Equal(8, all.Length);
    long before = all.Sum(f => new FileInfo(f.Path).Length);
    Assert.True(before > 2500);

    MainWindow.CleanupHistoryAll(sb.BotNotesDir, now, 2500, 30, 30);

    var leftA = MainWindow.ListHistoryFiles(folderA);
    var leftB = MainWindow.ListHistoryFiles(folderB);
    Assert.Single(leftA);
    Assert.Single(leftB);
    Assert.Equal(now.Date.AddDays(-2), leftA[0].Time.Date);
    Assert.Equal(now.Date.AddDays(-1), leftB[0].Time.Date);
    long after = leftA.Concat(leftB).Sum(f => new FileInfo(f.Path).Length);
    Assert.True(after <= 2500);
    Assert.Equal(2, MainWindow.ReadHistoryIndex(sb.BotNotesDir).Count);
   }
  }

  [Fact]
  public void Cleanup_RemovesOldHistoryForMissingFile_ButKeepsRecentOrExisting()
  {
   using (var sb = new Sandbox())
   {
    string existing = sb.WriteFile(sb.SaveDir, "keep.md", "还在");
    string missingOld = Path.Combine(sb.SaveDir, "gone-old.md");
    string missingRecent = Path.Combine(sb.SaveDir, "gone-recent.md");
    var now = new DateTime(2026, 11, 9, 20, 0, 0);
    var index = MainWindow.ReadHistoryIndex(sb.BotNotesDir);
    var entryExisting = MainWindow.EnsureHistoryEntry(index, sb.BotNotesDir, existing);
    var entryOld = MainWindow.EnsureHistoryEntry(index, sb.BotNotesDir, missingOld);
    var entryRecent = MainWindow.EnsureHistoryEntry(index, sb.BotNotesDir, missingRecent);
    MainWindow.WriteHistoryIndex(sb.BotNotesDir, index);
    string folderExisting = MainWindow.HistoryFolder(sb.BotNotesDir, entryExisting.Id);
    string folderOld = MainWindow.HistoryFolder(sb.BotNotesDir, entryOld.Id);
    string folderRecent = MainWindow.HistoryFolder(sb.BotNotesDir, entryRecent.Id);

    Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folderExisting, "还在 31 天前", now.Date.AddDays(-31), true));
    Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folderOld, "旧文件 31 天前", now.Date.AddDays(-31), true));
    Assert.NotNull(MainWindow.WriteHistorySnapshotFile(folderRecent, "新文件 10 天前", now.Date.AddDays(-10), true));
    entryExisting.LastSnapshotUtc = now.Date.AddDays(-31).ToUniversalTime();
    entryOld.LastSnapshotUtc = now.Date.AddDays(-31).ToUniversalTime();
    entryRecent.LastSnapshotUtc = now.Date.AddDays(-10).ToUniversalTime();
    MainWindow.WriteHistoryIndex(sb.BotNotesDir, index);

    MainWindow.CleanupHistoryAll(sb.BotNotesDir, now, MainWindow.HistoryMaxBytes, 30, 30);

    Assert.False(Directory.Exists(folderOld));
    Assert.DoesNotContain(MainWindow.ReadHistoryIndex(sb.BotNotesDir), e => e.Id == entryOld.Id);
    Assert.True(Directory.Exists(folderRecent));
    Assert.Single(MainWindow.ListHistoryFiles(folderRecent));
    Assert.True(Directory.Exists(folderExisting));
    Assert.Single(MainWindow.ListHistoryFiles(folderExisting));
    Assert.Contains(MainWindow.ReadHistoryIndex(sb.BotNotesDir), e => e.Id == entryExisting.Id);
    Assert.Contains(MainWindow.ReadHistoryIndex(sb.BotNotesDir), e => e.Id == entryRecent.Id);
   }
  }

  [Fact]
  public void TimedSnapshot_RequiresFiveMinutesAndChangedContent()
  {
   using (var sb = new Sandbox())
   {
    string note = sb.WriteFile(sb.SaveDir, "note.md", "内容");
    var now = new DateTime(2026, 11, 9, 10, 0, 0);
    Assert.True(MainWindow.SaveHistorySnapshot(sb.BotNotesDir, note, "one", now, true).Saved);

    Assert.False(MainWindow.ShouldTakeTimedSnapshot(sb.BotNotesDir, note, "two", now.AddMinutes(4), 5));
    Assert.True(MainWindow.ShouldTakeTimedSnapshot(sb.BotNotesDir, note, "two", now.AddMinutes(5), 5));
    Assert.False(MainWindow.ShouldTakeTimedSnapshot(sb.BotNotesDir, note, "one", now.AddMinutes(5), 5));
   }
  }

  [Theory]
  [InlineData(1000, 500, false)]
  [InlineData(1000, 499, true)]
  [InlineData(400, 200, false)]
  [InlineData(400, 199, true)]
  [InlineData(5000, 3000, false)]
  [InlineData(300, 0, true)]
  [InlineData(250, 40, true)]
  public void BigShrink_RequiresMoreThanHalfAndMoreThanTwoHundred(int before, int after, bool expected)
  {
   string previous = new string('a', before);
   string current = new string('b', after);
   Assert.Equal(expected, MainWindow.ShouldSnapshotBigShrink(previous, current));
  }

  [Fact]
  public void BigShrink_NullPreviousDoesNotTrigger()
  {
   Assert.False(MainWindow.ShouldSnapshotBigShrink(null, "current"));
  }

  [Fact]
  public void TimeLabel_UsesTodayYesterdayThisYearAndCrossYear()
  {
   var now = new DateTime(2026, 11, 9, 14, 0, 0);
   Assert.Equal("今天 09:05", MainWindow.HistoryTimeLabel(new DateTime(2026, 11, 9, 9, 5, 0), now));
   Assert.Equal("昨天 09:10", MainWindow.HistoryTimeLabel(new DateTime(2026, 11, 8, 9, 10, 0), now));
   Assert.Equal("11月7日 18:00", MainWindow.HistoryTimeLabel(new DateTime(2026, 11, 7, 18, 0, 0), now));
   Assert.Equal("2025年11月7日 18:00", MainWindow.HistoryTimeLabel(new DateTime(2025, 11, 7, 18, 0, 0), now));
  }
 }
}
