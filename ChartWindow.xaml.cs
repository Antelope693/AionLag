using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AionPingLite
{
    public partial class ChartWindow : Window
    {
        public ChartWindow()
        {
            InitializeComponent();
        }

        public void UpdateData(List<Tuple<DateTime, int>> history)
        {
            if (history == null || history.Count == 0)
            {
                StatsText.Text = "暂无数据。";
                return;
            }

            var recent = history.Where(x => (DateTime.Now - x.Item1).TotalMinutes <= 10).OrderBy(x => x.Item1).ToList();
            if (recent.Count == 0) return;

            int avgPing = (int)recent.Average(x => x.Item2);
            int maxPing = recent.Max(x => x.Item2);
            int minPing = recent.Min(x => x.Item2);

            double elapsedMin = Math.Max(0.1, (DateTime.Now - recent.First().Item1).TotalMinutes);
            double expectedPings = Math.Max(1, (elapsedMin * 60) / 10.0);
            double loss = Math.Max(0, 1.0 - (recent.Count / expectedPings)) * 100;

            StatsText.Text = $"平均延迟: {avgPing}ms  |  最高延迟: {maxPing}ms  |  丢包率: {loss:F1}% (过去 {elapsedMin:F1} 分钟)";

            DrawChart(recent, avgPing, maxPing, minPing);
        }

        private void DrawChart(List<Tuple<DateTime, int>> data, int avgPing, int maxPing, int minPing)
        {
            ChartCanvas.Children.Clear();
            YAxisCanvas.Children.Clear();
            XAxisCanvas.Children.Clear();

            if (data.Count < 2) return;

            double width = ChartCanvas.ActualWidth;
            if (width == 0) width = 360; 
            double height = ChartCanvas.ActualHeight;
            if (height == 0) height = 160;

            double maxXTime = (data.Last().Item1 - DateTime.MinValue).TotalSeconds;
            double minXTime = (data.First().Item1 - DateTime.MinValue).TotalSeconds;
            double timeSpan = maxXTime - minXTime;
            
            if (timeSpan <= 0) timeSpan = 1; // Fallback to avoid division by zero

            // Y axis centered around avg
            double maxDelta = Math.Max(maxPing - avgPing, avgPing - minPing) * 1.2;
            if (maxDelta < 10) maxDelta = 10;
            
            double maxYVal = avgPing + maxDelta;
            double minYVal = Math.Max(0, avgPing - maxDelta);
            double valSpan = maxYVal - minYVal;

            var polyline = new Polyline
            {
                Stroke = Brushes.Cyan,
                StrokeThickness = 2
            };

            foreach (var pt in data)
            {
                double t = (pt.Item1 - DateTime.MinValue).TotalSeconds;
                double x = ((t - minXTime) / timeSpan) * width;
                double y = height - (((pt.Item2 - minYVal) / valSpan) * height);
                // Clamp y visually if it goes slightly out of bounds due to rounding
                y = Math.Max(0, Math.Min(height, y));
                polyline.Points.Add(new Point(x, y));
            }

            // Draw Y-Axis Grid & Labels (Avg, Max, Min)
            DrawYGridLine(avgPing, true, height, valSpan, minYVal, width);
            DrawYGridLine((int)maxYVal, false, height, valSpan, minYVal, width);
            DrawYGridLine((int)minYVal, false, height, valSpan, minYVal, width);
            
            // Draw X-Axis Labels (Start and End)
            DrawXLabel(data.First().Item1.ToString("HH:mm:ss"), 0);
            DrawXLabel(data.Last().Item1.ToString("HH:mm:ss"), width - 50);

            ChartCanvas.Children.Add(polyline);
        }
        
        private void DrawYGridLine(int val, bool isCenter, double height, double valSpan, double minYVal, double width)
        {
            double y = height - (((val - minYVal) / valSpan) * height);
            
            // Limit text boundary
            if (y > height - 10) y = height - 15;
            if (y < 10) y = 0;

            var line = new Line
            {
                X1 = 0, Y1 = y, X2 = width, Y2 = y,
                Stroke = isCenter ? Brushes.LightGray : Brushes.DimGray,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection(new double[] { 4, 4 })
            };
            ChartCanvas.Children.Add(line);

            var text = new TextBlock
            {
                Text = val.ToString(),
                Foreground = isCenter ? Brushes.White : Brushes.Gray,
                FontSize = 10
            };
            Canvas.SetTop(text, y);
            Canvas.SetRight(text, 5);
            YAxisCanvas.Children.Add(text);
        }

        private void DrawXLabel(string textStr, double left)
        {
            var text = new TextBlock
            {
                Text = textStr,
                Foreground = Brushes.Gray,
                FontSize = 10
            };
            Canvas.SetLeft(text, left);
            Canvas.SetTop(text, 2);
            XAxisCanvas.Children.Add(text);
        }
    }
}
