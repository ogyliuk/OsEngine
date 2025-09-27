using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace OsEngine.OlegTools
{
    public static class EquityDrawer
    {
        public static void DoJob(
            string inputDataFilePath = @"F:\WORK\Projects\TENOR\MyCustomOsEngine\OsEngine\project\OsEngine\OlegTools\EquityDrawerData.txt", 
            string outputPictureFilePath = @"C:\Users\Oleg_Gyliuk\Desktop\equity.png")
        {
            SortedDictionary<long, decimal> tradesByTimes = new SortedDictionary<long, decimal>();
            foreach (string line in File.ReadAllLines(inputDataFilePath))
            {
                if (!line.Contains(',')) continue;

                string[] strings = line.Split(',');

                long timestamp = long.Parse(strings[0].Trim());
                decimal percent = decimal.Parse(strings[1].Replace('.', ',').Trim());

                while (tradesByTimes.ContainsKey(timestamp))
                {
                    timestamp += 1;
                }

                tradesByTimes.Add(timestamp, percent);
            }

            DrawEquityGraph(tradesByTimes, outputPictureFilePath);

            Console.WriteLine("Chart saved to file: " + outputPictureFilePath);
        }

        private static void DrawEquityGraph(SortedDictionary<long, decimal> tradesByTimes, string outputFile)
        {
            if (tradesByTimes == null || tradesByTimes.Count == 0)
            {
                Console.WriteLine("No data to build the chart.");
                return;
            }

            // --- Константы ---
            const decimal START_CAPITAL_USD = 1000m;  // начальный депозит
            const decimal GRID_STEP_USD = 50m;       // шаг горизонтальной сетки (в долларах)

            // --- Сортировка данных ---
            var ordered = tradesByTimes.OrderBy(kv => kv.Key).ToList();
            var times = ordered.Select(kv => DateTimeOffset.FromUnixTimeMilliseconds(kv.Key).UtcDateTime).ToList();

            // --- Расчёт эквити ---
            List<decimal> equityUsd = new List<decimal>();
            decimal currentDepoInPercents = 100m;
            foreach (var kv in ordered)
            {
                currentDepoInPercents += currentDepoInPercents / 100m * kv.Value;
                equityUsd.Add(START_CAPITAL_USD * currentDepoInPercents / 100m);
            }

            decimal min = equityUsd.Min();
            decimal max = equityUsd.Max();
            decimal range = max - min;
            if (range <= 0) range = 1;

            // --- Размеры холста ---
            int width = 4000;
            int height = 2000;
            int margin = 80;

            using (Bitmap bmp = new Bitmap(width, height))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Black);

                // --- Горизонтальная сетка ---
                using (Pen gridPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1))
                using (Font gridFont = new Font("Arial", 16, FontStyle.Bold))
                using (Brush gridTextBrush = new SolidBrush(Color.Gray))
                {
                    // округляем границы сетки
                    decimal gridMin = Math.Floor(min / GRID_STEP_USD) * GRID_STEP_USD;
                    decimal gridMax = Math.Ceiling(max / GRID_STEP_USD) * GRID_STEP_USD;

                    for (decimal level = gridMin; level <= gridMax + 0.001m; level += GRID_STEP_USD)
                    {
                        float y = (float)(height - margin - (level - min) / range * (height - 2 * margin));
                        g.DrawLine(gridPen, margin, y, width - margin, y);

                        string label = "$" + level.ToString("F0");
                        g.DrawString(label, gridFont, gridTextBrush, 10, y - 12);
                    }
                }

                // --- Оси ---
                using (Pen axisPen = new Pen(Color.Gray, 1))
                {
                    g.DrawLine(axisPen, margin, height - margin, width - margin, height - margin); // X
                    g.DrawLine(axisPen, margin, margin, margin, height - margin); // Y
                }

                // --- Линия эквити ---
                PointF[] points = new PointF[equityUsd.Count];
                for (int i = 0; i < equityUsd.Count; i++)
                {
                    float x = margin + (float)i / (equityUsd.Count - 1) * (width - 2 * margin);
                    float y = (float)(height - margin - (equityUsd[i] - min) / range * (height - 2 * margin));
                    points[i] = new PointF(x, y);
                }

                using (Pen linePen = new Pen(Color.Lime, 2))
                    g.DrawLines(linePen, points);

                // --- Точки сделок ---
                using (SolidBrush gainBrush = new SolidBrush(Color.Lime))
                using (SolidBrush lossBrush = new SolidBrush(Color.Red))
                {
                    float pointSize = 10f;
                    for (int i = 0; i < points.Length; i++)
                    {
                        Brush brush = gainBrush;
                        if (i > 0 && equityUsd[i] < equityUsd[i - 1])
                            brush = lossBrush;

                        g.FillEllipse(brush, points[i].X - pointSize / 2, points[i].Y - pointSize / 2, pointSize, pointSize);
                    }
                }

                // --- Квартальные линии ---
                using (Pen quarterPen = new Pen(Color.DarkOrange, 1))
                {
                    quarterPen.DashStyle = DashStyle.Dash;
                    DateTime start = new DateTime(times.First().Year, ((times.First().Month - 1) / 3) * 3 + 1, 1);
                    DateTime end = times.Last();

                    List<DateTime> quarterMarks = new List<DateTime>();
                    DateTime q = start;
                    while (q <= end)
                    {
                        quarterMarks.Add(q);
                        q = q.AddMonths(3);
                    }

                    using (Font font = new Font("Arial", 18, FontStyle.Bold))
                    using (Brush textBrush = new SolidBrush(Color.Orange))
                    {
                        foreach (DateTime quarter in quarterMarks)
                        {
                            int idx = times.FindIndex(t => t >= quarter);
                            if (idx == -1) continue;

                            float x = points[Math.Max(0, idx)].X;
                            g.DrawLine(quarterPen, x, margin, x, height - margin);

                            int qNum = ((quarter.Month - 1) / 3) + 1;
                            string label = string.Format("Q{0} {1}", qNum, quarter.Year);

                            g.TranslateTransform(x + 5, height - margin + 20);
                            g.RotateTransform(-90);
                            g.DrawString(label, font, textBrush, 0, 0);
                            g.ResetTransform();
                        }
                    }
                }

                // --- Информационные подписи ---
                using (Font font = new Font("Arial", 20, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString("Start: $" + START_CAPITAL_USD.ToString("F2"), font, textBrush, 10, 10);
                    g.DrawString("End:   $" + equityUsd.Last().ToString("F2"), font, textBrush, 10, 40);
                    g.DrawString("Min:   $" + min.ToString("F2"), font, textBrush, 10, 70);
                    g.DrawString("Max:   $" + max.ToString("F2"), font, textBrush, 10, 100);
                }

                // --- Сохраняем и открываем ---
                bmp.Save(outputFile, System.Drawing.Imaging.ImageFormat.Png);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(outputFile) { UseShellExecute = true });
            }
        }
    }
}
