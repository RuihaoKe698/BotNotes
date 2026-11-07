using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class AutoStartTests
 {
  [Theory]
  [InlineData("DotNotes")]
  [InlineData("NotchNotes")]
  public void EnabledOldRunValue_BecomesBotNotes(string oldName)
  {
   using (var sb = new Sandbox())
   {
    Reg.SetRun(oldName, "\"C:\\old\\" + oldName + ".exe\"");

    MainWindow.MigrateOldAutoStart();

    Assert.Null(Reg.GetRun(oldName));
    Assert.Equal(MainWindow.RunCommand(), Reg.GetRun(MainWindow.RunName));
    Assert.True(MainWindow.AutoStartOn());
   }
  }

  [Fact]
  public void DisabledOldRunValue_IsRemovedButDoesNotEnableBotNotes()
  {
   using (var sb = new Sandbox())
   {
    Reg.SetRun("DotNotes", "\"C:\\old\\DotNotes.exe\"");
    Reg.SetApproved("DotNotes", new byte[] { 1, 0, 0, 0 });

    MainWindow.MigrateOldAutoStart();

    Assert.Null(Reg.GetRun("DotNotes"));
    Assert.Null(Reg.GetApproved("DotNotes"));
    Assert.Null(Reg.GetRun(MainWindow.RunName));
    Assert.False(MainWindow.AutoStartOn());
   }
  }

  [Fact]
  public void AbsentOldRunValue_ChangesNothing()
  {
   using (var sb = new Sandbox())
   {
    MainWindow.MigrateOldAutoStart();
    Assert.False(Reg.RunKeyExists());
    Assert.Null(Reg.GetRun(MainWindow.RunName));

    MainWindow.FixAutoStartPath();
    Assert.False(Reg.RunKeyExists());
    Assert.Null(Reg.GetRun(MainWindow.RunName));
   }
  }

  [Fact]
  public void FixAutoStartPath_RewritesStalePath()
  {
   using (var sb = new Sandbox())
   {
    Reg.SetRun(MainWindow.RunName, "\"C:\\stale\\BotNotes.exe\"");

    Assert.True(MainWindow.AutoStartPathChanged(Reg.GetRun(MainWindow.RunName), MainWindow.RunCommand()));
    MainWindow.FixAutoStartPath();

    Assert.Equal(MainWindow.RunCommand(), Reg.GetRun(MainWindow.RunName));
    Assert.True(MainWindow.AutoStartOn());
   }
  }

  [Fact]
  public void FixAutoStartPath_LeavesDisabledEntryAlone()
  {
   using (var sb = new Sandbox())
   {
    Reg.SetRun(MainWindow.RunName, "\"C:\\stale\\BotNotes.exe\"");
    Reg.SetApproved(MainWindow.RunName, new byte[] { 1, 0, 0, 0 });

    MainWindow.FixAutoStartPath();

    Assert.Equal("\"C:\\stale\\BotNotes.exe\"", Reg.GetRun(MainWindow.RunName));
    Assert.False(MainWindow.AutoStartOn());
   }
  }

  [Fact]
  public void SetAutoStartValue_TurnsOnAndOffAndClearsApprovedFlag()
  {
   using (var sb = new Sandbox())
   {
    Reg.SetRun(MainWindow.RunName, "\"C:\\old\\BotNotes.exe\"");
    Reg.SetApproved(MainWindow.RunName, new byte[] { 1, 0, 0, 0 });

    Assert.Null(MainWindow.SetAutoStartValue(true));
    Assert.Equal(MainWindow.RunCommand(), Reg.GetRun(MainWindow.RunName));
    Assert.Null(Reg.GetApproved(MainWindow.RunName));
    Assert.True(MainWindow.AutoStartOn());

    Assert.Null(MainWindow.SetAutoStartValue(false));
    Assert.Null(Reg.GetRun(MainWindow.RunName));
    Assert.False(MainWindow.AutoStartOn());
   }
  }
 }
}
