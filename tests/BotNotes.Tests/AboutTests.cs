using System;
using NotchNotesWindows;
using Xunit;

namespace BotNotes.Tests
{
 public class AboutTests
 {
  [Fact]
  public void Links_are_https_pages_under_the_project_address()
  {
   foreach (var url in new[] { About.ProjectUrl, About.ReleasesUrl, About.FeedbackUrl })
   {
    Uri parsed;
    Assert.True(Uri.TryCreate(url, UriKind.Absolute, out parsed), url);
    Assert.Equal("https", parsed.Scheme);
    Assert.StartsWith(About.ProjectUrl, url);
   }
  }
 }
}
