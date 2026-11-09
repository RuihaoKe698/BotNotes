using System;
using System.IO;
using System.Text;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class NoteStorageTests
 {
  [Fact]
  public void FileName_ReplacesIllegalCharactersAndCollapsesWhitespace()
  {
   string name = MainWindow.NoteFileName(MainWindow.TitleOf("a/b:c test?\nnext line"));
   Assert.Equal("a b c test.md", name);
   foreach (char c in Path.GetInvalidFileNameChars()) Assert.DoesNotContain(c.ToString(), name);

   Assert.Equal("bold.md", MainWindow.NoteFileName(MainWindow.TitleOf("**bold**")));
  }

  [Fact]
  public void FileName_TruncatesToFortyCharacters_AndRemovesTrailingDots()
  {
   string title = MainWindow.TitleOf(new string('a', 70));
   Assert.Equal(new string('a', 40), title);
   string name = MainWindow.NoteFileName(new string('b', 50) + "...");
   Assert.Equal(new string('b', 40) + ".md", name);

   Assert.Equal("Title", MainWindow.TitleOf("Title..."));
   Assert.Equal("Title.md", MainWindow.NoteFileName("Title..."));
  }

  [Fact]
  public void EmptyTitle_BecomesWeiMingMing()
  {
   Assert.Equal("未命名", MainWindow.TitleOf("   \n"));
   Assert.Equal("未命名.md", MainWindow.NoteFileName(""));
   Assert.Equal("未命名.md", MainWindow.NoteFileName("   ..."));
  }

  [Fact]
  public void DuplicateTitles_GetParenthesizedSuffix()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.SaveDir);
    string first = MainWindow.UniqueFile(sb.SaveDir, "Title.md");
    File.WriteAllText(first, "");
    string second = MainWindow.UniqueFile(sb.SaveDir, "Title.md");
    File.WriteAllText(second, "");
    string third = MainWindow.UniqueFile(sb.SaveDir, "Title.md");

    Assert.Equal(Path.Combine(sb.SaveDir, "Title.md"), first);
    Assert.Equal(Path.Combine(sb.SaveDir, "Title (2).md"), second);
    Assert.Equal(Path.Combine(sb.SaveDir, "Title (3).md"), third);
   }
  }

  [Fact]
  public void NewDraftNames_UseSpaceNumberWithoutParentheses()
  {
   using (var sb = new Sandbox())
   {
    Directory.CreateDirectory(sb.SaveDir);
    string first = MainWindow.NewDraftPath(sb.SaveDir);
    File.WriteAllText(first, "");
    string second = MainWindow.NewDraftPath(sb.SaveDir);
    File.WriteAllText(second, "");
    string third = MainWindow.NewDraftPath(sb.SaveDir);

    Assert.Equal(Path.Combine(sb.SaveDir, "未命名.md"), first);
    Assert.Equal(Path.Combine(sb.SaveDir, "未命名 2.md"), second);
    Assert.Equal(Path.Combine(sb.SaveDir, "未命名 3.md"), third);
   }
  }

  [Fact]
  public void ShouldRename_OnlyWhenTitleChanges_AndOnlyInsideSaveDir()
  {
   using (var sb = new Sandbox())
   {
    string inside = Path.Combine(sb.SaveDir, "note.md");
    string outside = Path.Combine(sb.Root, "elsewhere", "note.md");

    Assert.False(MainWindow.ShouldRename(inside, "Same", "Same", sb.SaveDir));
    Assert.True(MainWindow.ShouldRename(inside, "New", "Old", sb.SaveDir));
    Assert.False(MainWindow.ShouldRename(outside, "New", "Old", sb.SaveDir));
    Assert.False(MainWindow.ShouldRename(inside, "New", "Old", Path.Combine(sb.SaveDir, "sub")));
    Assert.False(MainWindow.ShouldRename(MainWindow.DraftKey, "New", "Old", sb.SaveDir));
   }
  }

  [Fact]
  public void GbkFile_RoundTripsAsGbkBytes()
  {
   using (var sb = new Sandbox())
   {
    string path = Path.Combine(sb.Root, "gbk.txt");
    var gbk = Encoding.GetEncoding(936);
    byte[] original = gbk.GetBytes("中文笔记");
    File.WriteAllBytes(path, original);

    string text, error = MainWindow.ReadTextFile(path, out text, out var encoding, out var bom);
    Assert.Null(error);
    Assert.Equal("中文笔记", text);
    Assert.Equal(936, encoding.CodePage);
    Assert.False(bom);

    MainWindow.WriteTextFile(path, text, encoding, bom);
    Assert.Equal(original, File.ReadAllBytes(path));
   }
  }

  [Fact]
  public void GbkFile_WithEmoji_SwitchesToUtf8BomWithoutLosingText()
  {
   using (var sb = new Sandbox())
   {
    string path = Path.Combine(sb.Root, "gbk-emoji.txt");
    var gbk = Encoding.GetEncoding(936);
    File.WriteAllBytes(path, gbk.GetBytes("原内容"));

    string text, error = MainWindow.ReadTextFile(path, out text, out var encoding, out var bom);
    Assert.Null(error);
    Assert.Equal(936, encoding.CodePage);

    string edited = "中文 😀 生僻字：𠀀";
    bool switched = MainWindow.EnsureWritableEncoding(edited, ref encoding, ref bom);
    Assert.True(switched);
    Assert.Equal(65001, encoding.CodePage);
    Assert.True(bom);

    MainWindow.WriteTextFile(path, edited, encoding, bom);
    byte[] written = File.ReadAllBytes(path);
    Assert.True(written.Length >= 3);
    Assert.Equal(0xEF, written[0]);
    Assert.Equal(0xBB, written[1]);
    Assert.Equal(0xBF, written[2]);

    string again, readError = MainWindow.ReadTextFile(path, out again, out var readEncoding, out var readBom);
    Assert.Null(readError);
    Assert.Equal(edited, again);
    Assert.Equal(65001, readEncoding.CodePage);
    Assert.True(readBom);
   }
  }

  [Fact]
  public void GbkFile_WithOnlyGbkCharacters_StaysGbk()
  {
   using (var sb = new Sandbox())
   {
    string path = Path.Combine(sb.Root, "gbk-ok.txt");
    var gbk = Encoding.GetEncoding(936);
    byte[] original = gbk.GetBytes("原内容");
    File.WriteAllBytes(path, original);

    string text, error = MainWindow.ReadTextFile(path, out text, out var encoding, out var bom);
    Assert.Null(error);
    string edited = "中文、标点和数字 123";
    bool switched = MainWindow.EnsureWritableEncoding(edited, ref encoding, ref bom);
    Assert.False(switched);
    Assert.Equal(936, encoding.CodePage);
    Assert.False(bom);

    MainWindow.WriteTextFile(path, edited, encoding, bom);
    Assert.Equal(gbk.GetBytes(edited), File.ReadAllBytes(path));

    string again, readError = MainWindow.ReadTextFile(path, out again, out var readEncoding, out var readBom);
    Assert.Null(readError);
    Assert.Equal(edited, again);
    Assert.Equal(936, readEncoding.CodePage);
    Assert.False(readBom);
   }
  }

  [Fact]
  public void UnicodeEncoding_IsNeverSwitched()
  {
   Encoding utf8 = new UTF8Encoding(false);
   bool bom = false;
   Assert.False(MainWindow.EnsureWritableEncoding("😀", ref utf8, ref bom));
   Assert.Equal(65001, utf8.CodePage);
   Assert.False(bom);
  }

  [Fact]
  public void Utf8BomFile_KeepsBomWhenWrittenBack()
  {
   using (var sb = new Sandbox())
   {
    string path = Path.Combine(sb.Root, "bom.md");
    byte[] prefix = new UTF8Encoding(true).GetPreamble();
    byte[] body = Encoding.UTF8.GetBytes("带 BOM 的笔记");
    File.WriteAllBytes(path, Combine(prefix, body));

    string text, error = MainWindow.ReadTextFile(path, out text, out var encoding, out var bom);
    Assert.Null(error);
    Assert.Equal("带 BOM 的笔记", text);
    Assert.True(bom);
    Assert.Equal(65001, encoding.CodePage);

    MainWindow.WriteTextFile(path, text, encoding, bom);
    byte[] written = File.ReadAllBytes(path);
    Assert.True(written.Length > 3);
    Assert.Equal(0xEF, written[0]);
    Assert.Equal(0xBB, written[1]);
    Assert.Equal(0xBF, written[2]);
    Assert.DoesNotContain(Directory.GetFiles(sb.Root), p => Path.GetFileName(p).StartsWith(".~"));
   }
  }

  [Fact]
  public void OrderFile_UsesAbsolutePaths_AndRecognizesLegacyNames()
  {
   using (var sb = new Sandbox())
   {
    string first = Path.Combine(sb.SaveDir, "第一篇.md");
    string second = Path.Combine(sb.SaveDir, "第二篇.md");
    string[] lines = MainWindow.SerializeOrderPaths(new[] { first, MainWindow.DraftKey, second, first });
    Assert.Equal(new[] { first, second }, lines);

    var parsed = MainWindow.ParseOrderPaths(new[] { "note_2.md", "note.md", "note_2.md", "not-a-note.md", first }, sb.BotNotesDir);
    Assert.Equal(Path.Combine(sb.BotNotesDir, "note_2.md"), parsed[0]);
    Assert.Equal(Path.Combine(sb.BotNotesDir, "note.md"), parsed[1]);
    Assert.Equal(first, parsed[2]);

    Assert.True(MainWindow.IsLegacyNoteName("note.md"));
    Assert.True(MainWindow.IsLegacyNoteName("note_12.md"));
    Assert.False(MainWindow.IsLegacyNoteName("note_extra.md"));
   }
  }

  static byte[] Combine(byte[] a, byte[] b)
  {
   var r = new byte[a.Length + b.Length];
   Buffer.BlockCopy(a, 0, r, 0, a.Length);
   Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
   return r;
  }
 }
}
