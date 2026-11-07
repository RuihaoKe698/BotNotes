using System;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Controls;

namespace NotchNotesWindows {
// 自绘线条图标（格式工具栏、保持唤醒）：在 24×24 的格子里画，按 size 缩放；线宽固定不随缩放变，圆头圆角。
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
  Cup="M4 8.5 H16 V13.5 A4.5 4.5 0 0 1 11.5 18 H8.5 A4.5 4.5 0 0 1 4 13.5 Z M16 10 H17.75 A2.75 2.75 0 0 1 17.75 15.5 H16",
  CupSteam="M4 8.5 H16 V13.5 A4.5 4.5 0 0 1 11.5 18 H8.5 A4.5 4.5 0 0 1 4 13.5 Z M16 10 H17.75 A2.75 2.75 0 0 1 17.75 15.5 H16 M8 2.5 C7.3 3.4 8.7 4.1 8 5.5 M12 2.5 C11.3 3.4 12.7 4.1 12 5.5";
}
}
