using System.IO;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class DataFolderTests
 {
  [Fact]
  public void FreshStart_UsesBotNotesFolderWithoutCreatingIt()
  {
   using (var sb = new Sandbox())
   {
    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
    Assert.False(Directory.Exists(sb.BotNotesDir));

    MainWindow.MigrateOldVersions();

    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
    Assert.False(Directory.Exists(sb.BotNotesDir));
   }
  }

  [Fact]
  public void MigratesFromDotNotes()
  {
   using (var sb = new Sandbox())
   {
    sb.WriteFile(sb.DotNotesDir, "note.md", "from dotnotes");

    MainWindow.MigrateOldVersions();

    Assert.False(Directory.Exists(sb.DotNotesDir));
    Assert.Equal("from dotnotes", File.ReadAllText(Path.Combine(sb.BotNotesDir, "note.md")));
    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
   }
  }

  [Fact]
  public void MigratesFromNotchNotesWindows()
  {
   using (var sb = new Sandbox())
   {
    sb.WriteFile(sb.NotchNotesWindowsDir, "note.md", "from notchnotes");

    MainWindow.MigrateOldVersions();

    Assert.False(Directory.Exists(sb.NotchNotesWindowsDir));
    Assert.Equal("from notchnotes", File.ReadAllText(Path.Combine(sb.BotNotesDir, "note.md")));
    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
   }
  }

  [Fact]
  public void BothOldFoldersPresent_MovesOnlyThePreferredOne()
  {
   using (var sb = new Sandbox())
   {
    sb.WriteFile(sb.DotNotesDir, "note.md", "dotnotes");
    sb.WriteFile(sb.NotchNotesWindowsDir, "note.md", "notchnotes");

    MainWindow.MigrateOldVersions();

    Assert.False(Directory.Exists(sb.DotNotesDir));
    Assert.True(Directory.Exists(sb.NotchNotesWindowsDir));
    Assert.Equal("dotnotes", File.ReadAllText(Path.Combine(sb.BotNotesDir, "note.md")));
    Assert.Equal("notchnotes", File.ReadAllText(Path.Combine(sb.NotchNotesWindowsDir, "note.md")));
   }
  }

  [Fact]
  public void EmptyNewFolder_IsReplacedByAnOldFolderDuringMigration()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.BotNotesDir);
    sb.WriteFile(sb.DotNotesDir, "note.md", "dotnotes");

    // 空的新文件夹当不存在，所以迁移前会先选还留着内容的旧文件夹。
    Assert.Equal(sb.DotNotesDir, MainWindow.DataDir());

    MainWindow.MigrateOldVersions();

    Assert.False(Directory.Exists(sb.DotNotesDir));
    Assert.Equal("dotnotes", File.ReadAllText(Path.Combine(sb.BotNotesDir, "note.md")));
    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
   }
  }

  [Fact]
  public void EmptyNewFolderWithNoOldFolders_StaysAsIs()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.BotNotesDir);

    MainWindow.MigrateOldVersions();

    Assert.True(Directory.Exists(sb.BotNotesDir));
    Assert.Empty(Directory.GetFileSystemEntries(sb.BotNotesDir));
    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
   }
  }

  [Fact]
  public void NewFolderWithContent_IsNeverOverwritten()
  {
   using (var sb = new Sandbox())
   {
    sb.WriteFile(sb.BotNotesDir, "note.md", "current");
    sb.WriteFile(sb.DotNotesDir, "note.md", "old");

    MainWindow.MigrateOldVersions();

    Assert.Equal("current", File.ReadAllText(Path.Combine(sb.BotNotesDir, "note.md")));
    Assert.True(Directory.Exists(sb.DotNotesDir));
    Assert.Equal("old", File.ReadAllText(Path.Combine(sb.DotNotesDir, "note.md")));
    Assert.Equal(sb.BotNotesDir, MainWindow.DataDir());
   }
  }
 }
}
