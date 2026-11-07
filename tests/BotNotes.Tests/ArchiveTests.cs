using System;
using System.IO;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class ArchiveTests
 {
  static readonly DateTime Time = new DateTime(2024, 1, 2, 3, 4, 5);

  [Fact]
  public void Destination_UsesTitleAndTimestamp()
  {
   using (var sb = new Sandbox())
   {
    string archive = Path.Combine(sb.Root, "archive");
    Directory.CreateDirectory(archive);

    string dest = MainWindow.ArchiveDestination(archive, "# 标题\n正文", Time);

    Assert.Equal(Path.Combine(archive, "标题_2024-01-02_03-04-05.md"), dest);
   }
  }

  [Fact]
  public void Destination_AddsNumericSuffixesOnCollision()
  {
   using (var sb = new Sandbox())
   {
    string archive = Path.Combine(sb.Root, "archive");
    Directory.CreateDirectory(archive);

    string first = MainWindow.ArchiveDestination(archive, "Title", Time);
    File.WriteAllText(first, "");
    string second = MainWindow.ArchiveDestination(archive, "Title", Time);
    File.WriteAllText(second, "");
    string third = MainWindow.ArchiveDestination(archive, "Title", Time);

    Assert.Equal(Path.Combine(archive, "Title_2024-01-02_03-04-05.md"), first);
    Assert.Equal(Path.Combine(archive, "Title_2024-01-02_03-04-05_2.md"), second);
    Assert.Equal(Path.Combine(archive, "Title_2024-01-02_03-04-05_3.md"), third);
   }
  }

  [Fact]
  public void Title_ReplacesIllegalFileNameCharactersAndCollapsesWhitespace()
  {
   using (var sb = new Sandbox())
   {
    string title = MainWindow.TitleOf("a/b:c test?\nnext line");
    Assert.Equal("a b c test", title);

    string archive = Path.Combine(sb.Root, "archive");
    Directory.CreateDirectory(archive);
    string dest = MainWindow.ArchiveDestination(archive, "a/b:c test?", Time);
    string name = Path.GetFileName(dest);
    foreach (char c in Path.GetInvalidFileNameChars()) Assert.DoesNotContain(c.ToString(), name);
    Assert.Equal("a b c test_2024-01-02_03-04-05.md", name);

    // Markdown 的强调符号会像正文标题那样被去掉。
    Assert.Equal("bold", MainWindow.TitleOf("**bold**"));
   }
  }

  [Fact]
  public void Title_HandlesEmptyAndLongTitles()
  {
   Assert.Equal("未命名笔记", MainWindow.TitleOf("   \n"));
   Assert.Equal(new string('a', 60), MainWindow.TitleOf(new string('a', 70)));
   Assert.Equal("Title", MainWindow.TitleOf("Title..."));
  }

  [Fact]
  public void ArchiveDirUnderOldRoot_IsRewrittenToNewRoot()
  {
   using (var sb = new Sandbox())
   {
    string oldArchive = Path.Combine(sb.DotNotesDir, "archive");
    Assert.Equal(Path.Combine(sb.BotNotesDir, "archive"), MainWindow.RewriteArchiveDir(oldArchive, sb.BotNotesDir));

    string olderArchive = Path.Combine(sb.NotchNotesWindowsDir, "archive");
    Assert.Equal(Path.Combine(sb.BotNotesDir, "archive"), MainWindow.RewriteArchiveDir(olderArchive, sb.BotNotesDir));
   }
  }

  [Fact]
  public void ArchiveDirRewrite_AfterMigrationUsesTheMovedFolder()
  {
   using (var sb = new Sandbox())
   {
    sb.WriteFile(sb.DotNotesDir, "note.md", "old");
    string oldArchive = Path.Combine(sb.DotNotesDir, "archive");

    MainWindow.MigrateOldVersions();

    Assert.False(Directory.Exists(sb.DotNotesDir));
    Assert.Equal(Path.Combine(sb.BotNotesDir, "archive"), MainWindow.RewriteArchiveDir(oldArchive, sb.BotNotesDir));
   }
  }

  [Fact]
  public void ArchiveDirRewrite_DoesNothingWhenOldRootStillExists()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.DotNotesDir);
    string oldArchive = Path.Combine(sb.DotNotesDir, "archive");

    Assert.Equal(oldArchive, MainWindow.RewriteArchiveDir(oldArchive, sb.BotNotesDir));
   }
  }

  [Fact]
  public void ArchiveDirRewrite_LeavesUnrelatedAndNullPathsAlone()
  {
   using (var sb = new Sandbox())
   {
    string other = Path.Combine(sb.Root, "elsewhere", "archive");
    Assert.Equal(other, MainWindow.RewriteArchiveDir(other, sb.BotNotesDir));
    Assert.Null(MainWindow.RewriteArchiveDir(null, sb.BotNotesDir));
   }
  }
 }
}
