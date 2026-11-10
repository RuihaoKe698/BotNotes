using System;

namespace NotchNotesWindows {
// 设置里“关于”一节用到的地址。BotNotes 自己不联网：这些地址只在用户点按钮时交给浏览器打开。
// 换项目地址时改 ProjectUrl，README.md 里的两处链接要一起改。
static class About {
 internal const string ProjectUrl="https://github.com/RuihaoKe698/BotNotes";
 internal static string ReleasesUrl { get { return ProjectUrl+"/releases/latest"; } }
 internal static string FeedbackUrl { get { return ProjectUrl+"/issues"; } }
}
}
