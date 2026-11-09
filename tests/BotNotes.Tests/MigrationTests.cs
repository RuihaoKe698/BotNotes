using System;
using System.IO;
using System.Linq;
using System.Text;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class MigrationTests
 {
  [Fact]
  public void LegacyLayout_IsBackedUpAndMovedOnce()
  {
   using (var sb = new Sandbox())
   {
    string data = sb.BotNotesDir;
    Directory.CreateDirectory(data);
    sb.WriteFile(data, "note.md", "# 第一篇\n正文一");
    sb.WriteFile(data, "note_2.md", "第二篇\n正文二");
    sb.WriteFile(data, "note_3.md", "   \n");
    sb.WriteFile(Path.Combine(data, "archive"), "归档一.md", "归档内容一");
    sb.WriteFile(Path.Combine(data, "archive"), "归档二.txt", "归档内容二");
    sb.WriteFile(data, "order.txt", "note_2.md\nnote.md\nnote_3.md\n");
    sb.WriteFile(Path.Combine(data, "backup-old"), "keep.txt", "旧备份");
    string fold = Convert.ToBase64String(Encoding.UTF8.GetBytes("## 标题\t0"));
    sb.WriteFile(data, "folds.txt", "note_2.md\t" + fold + "\nnote.md\t" + fold + "\n");
    sb.WriteFile(data, "settings.ini", "pinned=1\n");

    var result = MainWindow.MigrateStorage();

    Assert.True(result.Ran);
    Assert.False(result.BackupFailed);
    Assert.True(Directory.Exists(result.BackupDir));

    string backup = result.BackupDir;
    Assert.True(File.Exists(Path.Combine(backup, "note.md")));
    Assert.True(File.Exists(Path.Combine(backup, "note_2.md")));
    Assert.True(File.Exists(Path.Combine(backup, "note_3.md")));
    Assert.True(File.Exists(Path.Combine(backup, "archive", "归档一.md")));
    Assert.True(File.Exists(Path.Combine(backup, "archive", "归档二.txt")));
    Assert.True(File.Exists(Path.Combine(backup, "order.txt")));
    Assert.True(File.Exists(Path.Combine(backup, "folds.txt")));
    Assert.True(File.Exists(Path.Combine(backup, "settings.ini")));
    Assert.False(Directory.Exists(Path.Combine(backup, "backup-old")));

    string first = Path.Combine(sb.SaveDir, "第一篇.md");
    string second = Path.Combine(sb.SaveDir, "第二篇.md");
    Assert.Equal("正文一", File.ReadAllText(first).Split('\n')[1].Trim());
    Assert.Equal("正文二", File.ReadAllText(second).Split('\n')[1].Trim());
    Assert.False(File.Exists(Path.Combine(data, "note_3.md")));
    Assert.True(File.Exists(Path.Combine(sb.SaveDir, "归档一.md")));
    Assert.True(File.Exists(Path.Combine(sb.SaveDir, "归档二.txt")));

    string[] order = MainWindow.ParseOrderPaths(File.ReadAllLines(Path.Combine(data, "order.txt")), data).ToArray();
    Assert.Equal(new[] { second, first }, order);

    var folds = MainWindow.ParseFoldLines(File.ReadAllLines(Path.Combine(data, "folds.txt")), data);
    Assert.Contains(folds, x => x.Key == second && x.Value == "## 标题\t0");
    Assert.Contains(folds, x => x.Key == first && x.Value == "## 标题\t0");

    var settings = MainWindow.ParseSettings(File.ReadAllLines(Path.Combine(data, "settings.ini")), data);
    Assert.Equal(2, settings.StorageVersion);

    string backupName = Path.GetFileName(result.BackupDir);
    var again = MainWindow.MigrateStorage();

    Assert.False(again.Ran);
    Assert.Equal(1, Directory.GetDirectories(data, "backup-迁移前-*").Count(p => Path.GetFileName(p) == backupName));
    Assert.Single(Directory.GetFiles(sb.SaveDir, "第一篇.md"));
    Assert.Single(Directory.GetFiles(sb.SaveDir, "第二篇.md"));
   }
  }

  [Fact]
  public void BackupFailure_MovesNothing()
  {
   using (var sb = new Sandbox())
   {
    string note = sb.WriteFile(sb.BotNotesDir, "note.md", "# 不能动\n正文");
    using (new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
     var result = MainWindow.MigrateStorage();

     Assert.True(result.BackupFailed);
     Assert.True(result.Ran);
     Assert.True(File.Exists(note));
     Assert.False(Directory.Exists(sb.SaveDir));
     Assert.Empty(Directory.GetDirectories(sb.BotNotesDir, "backup-迁移前-*"));
     Assert.False(File.Exists(Path.Combine(sb.BotNotesDir, "settings.ini")) &&
                  MainWindow.HasStorage2(File.ReadAllLines(Path.Combine(sb.BotNotesDir, "settings.ini"))));
    }
   }
  }

  [Fact]
  public void SecondBackupOnTheSameDay_GetsUnderscoreTwo()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.BotNotesDir);
    string first = MainWindow.CreateMigrationBackup(sb.BotNotesDir);
    File.WriteAllText(Path.Combine(first, "keep.txt"), "already here");

    string second = MainWindow.CreateMigrationBackup(sb.BotNotesDir);

    Assert.EndsWith("_2", second);
    Assert.True(Directory.Exists(first));
    Assert.True(Directory.Exists(second));
    Assert.Empty(Directory.GetFiles(second, "keep.txt", SearchOption.AllDirectories));
   }
  }

  [Fact]
  public void CustomArchiveDir_BecomesSaveLocation_WithoutMovingItsExistingFiles()
  {
   using (var sb = new Sandbox())
   {
    string custom = Path.Combine(sb.Root, "custom-archive");
    Directory.CreateDirectory(custom);
    File.WriteAllText(Path.Combine(custom, "已经存在.md"), "留下的文件");
    sb.WriteFile(sb.BotNotesDir, "note.md", "# 新笔记\n正文");
    sb.WriteFile(sb.BotNotesDir, "settings.ini", "archiveDir=" + custom + "\n");

    var result = MainWindow.MigrateStorage();

    Assert.True(result.Ran);
    Assert.Equal("留下的文件", File.ReadAllText(Path.Combine(custom, "已经存在.md")));
    Assert.Equal("正文", File.ReadAllText(Path.Combine(custom, "新笔记.md")).Split('\n')[1].Trim());
    Assert.False(File.Exists(Path.Combine(sb.BotNotesDir, "note.md")));
    Assert.Equal(2, MainWindow.ParseSettings(File.ReadAllLines(Path.Combine(sb.BotNotesDir, "settings.ini")), sb.BotNotesDir).StorageVersion);
   }
  }

  [Fact]
  public void ArchiveFileNameCollision_GetsParenthesizedSuffix()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.SaveDir);
    File.WriteAllText(Path.Combine(sb.SaveDir, "归档.md"), "已经有的文件");
    sb.WriteFile(sb.BotNotesDir, "note.md", "正文");
    sb.WriteFile(Path.Combine(sb.BotNotesDir, "archive"), "归档.md", "归档里的文件");

    var result = MainWindow.MigrateStorage();

    Assert.True(result.Ran);
    Assert.Equal("已经有的文件", File.ReadAllText(Path.Combine(sb.SaveDir, "归档.md")));
    Assert.Equal("归档里的文件", File.ReadAllText(Path.Combine(sb.SaveDir, "归档 (2).md")));
   }
  }
 }
}
