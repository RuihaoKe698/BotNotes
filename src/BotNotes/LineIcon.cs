using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace NotchNotesWindows {
// 自绘线条图标（界面全部图标，2026-11-09 统一为 icons.txt 这一版）：在 24×24 的格子里画，按 size 缩放；线宽固定不随缩放变，圆头圆角。
// 颜色跟着所在按钮的文字色走（TextElement.Foreground），悬停、开关时换色不用另外处理。
static class LineIcon {
 internal const double Stroke=1.35;

 // strokes：描线的路径；dots：实心的小圆点（列表的圆点），格式为 "x,y,r;x,y,r"。
 public static FrameworkElement Make(string strokes,string dots=null,double size=16) {
  var g=new Grid { Width=size, Height=size, VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center };
  var scale=new ScaleTransform(size/24,size/24); scale.Freeze();
  if(strokes!=null) {
   var geo=Geometry.Parse(strokes).Clone(); geo.Transform=scale;
   var p=new Path { Data=geo, StrokeThickness=Stroke, StrokeStartLineCap=PenLineCap.Round, StrokeEndLineCap=PenLineCap.Round, StrokeLineJoin=PenLineJoin.Round };
   Follow(p,Shape.StrokeProperty); g.Children.Add(p);
  }
  if(dots!=null) {
   var group=new GeometryGroup();
   foreach(var d in dots.Split(';')) { var v=d.Split(','); group.Children.Add(new EllipseGeometry(new Point(Num(v[0]),Num(v[1])),Num(v[2]),Num(v[2]))); }
   group.Transform=scale;
   var p=new Path { Data=group }; Follow(p,Shape.FillProperty); g.Children.Add(p);
  }
  return g;
 }
 static double Num(string s) { return double.Parse(s,System.Globalization.CultureInfo.InvariantCulture); }
 static void Follow(Shape s,DependencyProperty dp) { s.SetBinding(dp,new Binding { Path=new PropertyPath(TextElement.ForegroundProperty), RelativeSource=RelativeSource.Self }); }

 // 标题按钮：H 加一个小下拉箭头。
 public static FrameworkElement Heading() {
  var row=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
  row.Children.Add(Make(H));
  var chev=Make("M7 10 L12 15 L17 10",null,9); chev.Margin=new Thickness(1,1,0,0); row.Children.Add(chev);
  return row;
 }

 public const string
  H="M6 5.5 V18.5 M17 5.5 V18.5 M6 12 H17",
  Bold="M7 5.5 H13 A3.25 3.25 0 0 1 13 12 H7 Z M7 12 H14 A3.25 3.25 0 0 1 14 18.5 H7 Z",
  Italic="M10.5 5.5 H17.5 M6.5 18.5 H13.5 M14.5 5.5 L9.5 18.5",
  Strike="M16 7.5 C15.3 6.2 13.9 5.5 12 5.5 C9.6 5.5 8 6.7 8 8.5 C8 9.9 9 10.7 10.6 11.2 M4.5 12 H19.5 M8 16.5 C8.7 17.8 10.1 18.5 12 18.5 C14.4 18.5 16 17.3 16 15.5 C16 14.6 15.6 13.9 14.9 13.4",
  Code="M9 7 L4 12 L9 17 M15 7 L20 12 L15 17",
  Link="M10 8 H7.5 A4 4 0 0 0 7.5 16 H10 M14 8 H16.5 A4 4 0 0 1 16.5 16 H14 M8.5 12 H15.5",
  Quote="M5 6 V18 M10 7.5 H19 M10 12 H19 M10 16.5 H15.5",
  Bullets="M10 7 H19.5 M10 12 H19.5 M10 17 H19.5",
  BulletDots="5.5,7,1.3;5.5,12,1.3;5.5,17,1.3",
  Numbers="M4.5 5.6 L6.5 4 V10.5 M4 14.6 C4.2 13.4 5.1 12.7 6.2 12.7 C7.4 12.7 8.3 13.5 8.3 14.7 C8.3 15.6 7.8 16.2 7 16.9 L4 19.5 H8.5 M12 7.25 H20 M12 16.1 H20",
  Tasks="M3.5 7.5 L5.6 9.6 L9.2 5.6 M12.5 7.5 H20 M5.25 13.25 H7.75 A1.5 1.5 0 0 1 9.25 14.75 V17.25 A1.5 1.5 0 0 1 7.75 18.75 H5.25 A1.5 1.5 0 0 1 3.75 17.25 V14.75 A1.5 1.5 0 0 1 5.25 13.25 Z M12.5 16 H20",
  Pencil="M15.2 4.8 L19.2 8.8 L8.6 19.4 L3.8 20.2 L4.6 15.4 Z M13.2 6.8 L17.2 10.8",
  Book="M12 6.5 C9.5 5 6.5 4.5 3.5 5.5 V18.5 C6.5 17.5 9.5 18 12 19.5 C14.5 18 17.5 17.5 20.5 18.5 V5.5 C17.5 4.5 14.5 5 12 6.5 Z M12 6.5 V19.5",
  MindMap="M4 9.5 H7 A1.5 1.5 0 0 1 8.5 11 V13 A1.5 1.5 0 0 1 7 14.5 H4 A1.5 1.5 0 0 1 2.5 13 V11 A1.5 1.5 0 0 1 4 9.5 Z M8.5 12 C11.5 12 11.5 5.5 14.5 5.5 H21 M8.5 12 H21 M8.5 12 C11.5 12 11.5 18.5 14.5 18.5 H21",

  // 用户确认的统一线条图标（.oil-ui/icons-unify/icons.txt，2026-11-09）。括号里是显示尺寸。
  Collapse="M3.5 4.8 H20.5 M6.5 19.2 L12 13.2 L17.5 19.2", // 16
  Pin="M3 9.4 L9.4 3 M5.2 8.8 L8.8 5.2 L15.9 8.9 L8.9 15.9 Z M12.4 12.4 L20.8 20.8", // 16
  Settings="M10.24 5.43 L10.34 3.46 A8.7 8.7 0 0 1 13.66 3.46 L13.76 5.43 A6.8 6.8 0 0 1 16.81 7.19 L18.57 6.29 A8.7 8.7 0 0 1 20.23 9.17 L18.57 10.24 A6.8 6.8 0 0 1 18.57 13.76 L20.23 14.83 A8.7 8.7 0 0 1 18.57 17.71 L16.81 16.81 A6.8 6.8 0 0 1 13.76 18.57 L13.66 20.54 A8.7 8.7 0 0 1 10.34 20.54 L10.24 18.57 A6.8 6.8 0 0 1 7.19 16.81 L5.43 17.71 A8.7 8.7 0 0 1 3.77 14.83 L5.43 13.76 A6.8 6.8 0 0 1 5.43 10.24 L3.77 9.17 A8.7 8.7 0 0 1 5.43 6.29 L7.19 7.19 A6.8 6.8 0 0 1 10.24 5.43 Z M12 9.1 A2.9 2.9 0 1 1 12 14.9 A2.9 2.9 0 1 1 12 9.1", // 16
  Fullscreen="M3.5 8.5 V4.5 H8.5 M15.5 4.5 H20.5 V8.5 M20.5 15.5 V19.5 H15.5 M8.5 19.5 H3.5 V15.5", // 16
  ExitFullscreen="M3.5 8.5 H8.5 V4.5 M15.5 4.5 V8.5 H20.5 M20.5 15.5 H15.5 V19.5 M8.5 19.5 V15.5 H3.5", // 16
  // 历史版本：圆形钟面在左上方留口，缺口处一个逆时针箭头；两根指针约指 12 点和 4 点半。
  History="M9.09 4.21 A8.5 8.5 0 1 1 4.3 8.61 M6.9 7.28 L4.3 8.61 L3.64 5.76 M12 7.7 V12.2 L15.8 14.4", // 16
  // 干净桌面：一台显示器（轮廓 + 支架 + 底座），右上角一颗小四角星。
  CleanDesktop="M4.8 5.8 H14.4 A1.8 1.8 0 0 1 16.2 7.6 V13.8 A1.8 1.8 0 0 1 14.4 15.6 H4.8 A1.8 1.8 0 0 1 3 13.8 V7.6 A1.8 1.8 0 0 1 4.8 5.8 Z M9.6 15.6 V18.2 M6.3 20.2 H12.9 M20 2.9 C20.3 3.95 20.85 4.5 21.9 4.75 C20.85 5 20.3 5.55 20 6.6 C19.7 5.55 19.15 5 18.1 4.75 C19.15 4.5 19.7 3.95 20 2.9 Z", // 16
  // 咖啡杯两种状态共用杯身，开启时只多两缕热气，杯身位置不变。
  Cup="M3 5.5 H16.3 V15.6 A5 5 0 0 1 11.3 20.6 H8 A5 5 0 0 1 3 15.6 Z M16.3 8.9 H18 A2.85 2.85 0 0 1 18 14.6 H16.3", // 16
  CupSteam="M3 5.5 H16.3 V15.6 A5 5 0 0 1 11.3 20.6 H8 A5 5 0 0 1 3 15.6 Z M16.3 8.9 H18 A2.85 2.85 0 0 1 18 14.6 H16.3 M7.7 5.1 C5.7 4.55 9.3 3.85 7.3 3 M12.2 5.1 C10.2 4.55 13.8 3.85 11.8 3", // 16
  Add="M12 4.5 V19.5 M4.5 12 H19.5", // 16
  Open="M3.5 10 V6.5 H9 L11 8.5 H18.5 V10 M3.5 10 H20.5 L18 19 H3.5 Z", // 16
  ChevronLeft="M15 5.5 L8 12 L15 18.5", // 12
  ChevronRight="M9 5.5 L16 12 L9 18.5", // 12
  Close="M5.5 5.5 L18.5 18.5 M18.5 5.5 L5.5 18.5", // 9
  ChevronDown="M6 9 L12 15 L18 9", // 12
  Copy="M8.5 3.5 H18.5 V15 M5.5 6.5 H16.5 V20.5 H5.5 Z", // 14
  Cut="M4.5 4.5 L16.6 16.6 M19.5 4.5 L7.4 16.6 M6.5 16.1 A2.1 2.1 0 1 1 6.5 20.3 A2.1 2.1 0 1 1 6.5 16.1 M17.5 16.1 A2.1 2.1 0 1 1 17.5 20.3 A2.1 2.1 0 1 1 17.5 16.1", // 14
  Delete="M4.5 6.8 H19.5 M9 6.8 V4.3 H15 V6.8 M6.5 6.8 L7.5 20.2 H16.5 L17.5 6.8 M10.2 10.5 V16.5 M13.8 10.5 V16.5", // 14
  Fit="M3.8 9 V3.8 H9 M15 3.8 H20.2 V9 M20.2 15 V20.2 H15 M9 20.2 H3.8 V15 M9.2 9.2 H14.8 V14.8 H9.2 Z", // 14
  CopyImage="M8.5 3.5 H18.5 V15 M5.5 6.5 H16.5 V20.5 H5.5 Z", // 14
  Export="M4.5 13.5 V19.5 H19.5 V13.5 M12 4.5 V15.2 M7.5 9 L12 4.5 L16.5 9", // 14
  Present="M5.5 4.5 H18.5 A1.5 1.5 0 0 1 20 6 V14.5 A1.5 1.5 0 0 1 18.5 16 H5.5 A1.5 1.5 0 0 1 4 14.5 V6 A1.5 1.5 0 0 1 5.5 4.5 Z M9.8 7.6 L14.8 10.25 L9.8 12.9 Z M12 16 V20 M8.5 20 H15.5", // 14
  ExitPresent="M5.5 4.5 H18.5 A1.5 1.5 0 0 1 20 6 V14.5 A1.5 1.5 0 0 1 18.5 16 H5.5 A1.5 1.5 0 0 1 4 14.5 V6 A1.5 1.5 0 0 1 5.5 4.5 Z M9.5 8 L14.5 13 M14.5 8 L9.5 13 M12 16 V20 M8.5 20 H15.5", // 14
  Edit="M15.2 4.8 L19.2 8.8 L8.6 19.4 L3.8 20.2 L4.6 15.4 Z M13.2 6.8 L17.2 10.8", // 14
  Minus="M5 12 H19", // 14
  Plus="M12 5 V19 M5 12 H19", // 14
  Folder="M3.5 6.8 H9.2 L11.2 8.8 H20.5 V19 H3.5 Z", // 16
  Check="M4.5 12.5 L9.5 17.5 L19.5 6.5", // 14
  Note="M6.5 3.5 H14.5 L18.5 7.5 V20.5 H6.5 Z M14.5 3.5 V7.5 H18.5 M9.5 12 H15.5 M9.5 16 H14"; // 13
}
}
