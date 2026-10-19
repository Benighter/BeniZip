using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BeniZip {
 public sealed class ProgressWindow {
  public readonly Window Window;
  readonly TextBlock current=new TextBlock(),percent=new TextBlock(),elapsed=new TextBlock(),remaining=new TextBlock(),speed=new TextBlock(),processed=new TextBlock();
  readonly ProgressBar bar=new ProgressBar();readonly Button cancel=new Button();readonly DispatcherTimer timer=new DispatcherTimer();readonly Stopwatch watch=Stopwatch.StartNew();
  readonly long total;int value;bool finished;
  public ProgressWindow(Window owner,string title,string source,long totalBytes,Action cancelAction){
   total=totalBytes;Window=new Window{Title=title+" — BeniZip",Owner=owner,Width=530,Height=372,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner,ShowInTaskbar=false,Background=Brushes.White,FontFamily=new FontFamily("Segoe UI"),FontSize=12};
   var panel=new StackPanel{Margin=new Thickness(25,20,25,20)};Window.Content=panel;
   panel.Children.Add(new TextBlock{Text=title,FontSize=19,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(38,52,63)),Margin=new Thickness(0,0,0,7)});
   panel.Children.Add(new TextBlock{Text=source??"",TextTrimming=TextTrimming.CharacterEllipsis,Foreground=Brushes.SlateGray,ToolTip=source,Margin=new Thickness(0,0,0,20)});
   current.Text="Preparing files…";current.TextTrimming=TextTrimming.CharacterEllipsis;current.Margin=new Thickness(0,0,0,10);panel.Children.Add(current);
   var row=new DockPanel{Margin=new Thickness(0,0,0,7)};percent.Text="0%";percent.HorizontalAlignment=HorizontalAlignment.Right;DockPanel.SetDock(percent,Dock.Right);row.Children.Add(percent);row.Children.Add(new TextBlock{Text="Overall progress",Foreground=Brushes.SlateGray});panel.Children.Add(row);
   bar.Maximum=100;bar.Height=12;bar.IsIndeterminate=true;bar.Foreground=new SolidColorBrush(Color.FromRgb(53,160,105));bar.Background=new SolidColorBrush(Color.FromRgb(237,241,244));bar.BorderThickness=new Thickness(0);panel.Children.Add(bar);
   processed.Text=total>0?"0 B of "+ArchiveEngine.FormatSize(total):"Waiting for archive progress…";processed.Foreground=Brushes.SlateGray;processed.FontSize=11;processed.Margin=new Thickness(0,8,0,16);panel.Children.Add(processed);
   var details=new Grid();details.ColumnDefinitions.Add(new ColumnDefinition());details.ColumnDefinitions.Add(new ColumnDefinition());details.RowDefinitions.Add(new RowDefinition{Height=new GridLength(25)});details.RowDefinitions.Add(new RowDefinition{Height=new GridLength(25)});
   details.Children.Add(Pair("Elapsed",elapsed));var right=Pair("Time left",remaining);Grid.SetColumn(right,1);details.Children.Add(right);var speedRow=Pair("Speed",speed);Grid.SetRow(speedRow,1);Grid.SetColumnSpan(speedRow,2);details.Children.Add(speedRow);panel.Children.Add(details);
   cancel.Content="Cancel";cancel.Width=98;cancel.Padding=new Thickness(10,7,10,7);cancel.HorizontalAlignment=HorizontalAlignment.Right;cancel.Margin=new Thickness(0,16,0,0);if(owner.Resources.Contains("Action"))cancel.Style=(Style)owner.Resources["Action"];cancel.Click+=(s,e)=>{cancel.IsEnabled=false;current.Text="Cancelling…";cancelAction();};panel.Children.Add(cancel);
   Window.Closing+=(s,e)=>{if(!finished){e.Cancel=true;cancel.IsEnabled=false;current.Text="Cancelling…";cancelAction();}};
   timer.Interval=TimeSpan.FromMilliseconds(250);timer.Tick+=(s,e)=>Refresh();timer.Start();Refresh();
  }
  static StackPanel Pair(string label,TextBlock value){var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(new TextBlock{Text=label+": ",Foreground=Brushes.SlateGray,MinWidth=70});value.FontWeight=FontWeights.SemiBold;row.Children.Add(value);return row;}
  public void Report(ProgressInfo info){value=Math.Max(value,Math.Max(0,Math.Min(100,info.Percent)));bar.IsIndeterminate=false;bar.Value=value;percent.Text=value+"%";if(!string.IsNullOrWhiteSpace(info.CurrentFile))current.Text=info.CurrentFile;Refresh();}
  void Refresh(){double seconds=watch.Elapsed.TotalSeconds;elapsed.Text=Duration(seconds);if(value>0&&seconds>=0.6&&value<100){remaining.Text="~ "+Duration(seconds*(100-value)/value);speed.Text=total>0?ArchiveEngine.FormatSize((long)(total*(value/100.0)/seconds))+"/s (est.)":"—";}else{remaining.Text=value==100?"Finishing…":"Estimating…";speed.Text="—";}if(total>0)processed.Text=ArchiveEngine.FormatSize((long)(total*(value/100.0)))+" of "+ArchiveEngine.FormatSize(total)+" (estimated)";else processed.Text=value>0?"Processing files…":"Preparing archive…";}
  static string Duration(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,Math.Min(seconds,31536000)));return t.TotalHours>=1?((int)t.TotalHours)+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00"):t.Minutes.ToString("00")+":"+t.Seconds.ToString("00");}
  public void Complete(){finished=true;timer.Stop();watch.Stop();Window.Close();}
 }
}
