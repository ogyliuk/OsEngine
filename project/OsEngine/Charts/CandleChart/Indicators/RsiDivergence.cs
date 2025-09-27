using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms.DataVisualization.Charting;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Charts.CandleChart.Indicators
{
    /// <summary>
    /// RSI DIVERGENCE indicator
    /// Relative Strength Index with showing divergences. Индикатор
    /// </summary>
    public class RsiDivergence : IMultiElementIndicator
    {
        private readonly ReaderWriterLockSlim _rwLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly int NUM_LINES_IN_ONE_DIVERGENCE = 7;

        private static readonly string PRIME_AREA_NAME = "Prime";
        private static readonly Color UP_DIVERGENCE_COLOR = Color.Green;
        private static readonly Color DOWN_DIVERGENCE_COLOR = Color.Red;
        private static readonly int DIVERGENCE_LINE_WIDTH = 2;

        /// <summary>
        /// list of elements which needs to be painted on the chart
        /// список элементов, которые должны быть отрисованы на чарте
        /// </summary>
        public List<IndicatorElement> Elements
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    List<decimal> rsiValuesCopy = new List<decimal>(RsiValues);
                    List<ChartLineSegmentObject> upLineSegmentsCopy = new List<ChartLineSegmentObject>(UpDivergenceLineSegments);
                    List<ChartLineSegmentObject> downLineSegmentsCopy = new List<ChartLineSegmentObject>(DownDivergenceLineSegments);
                    return new List<IndicatorElement>()
                    {
                        new IndicatorElement(ColorBase, rsiValuesCopy, IndicatorChartPaintType.Line),
                        new IndicatorElement(UP_DIVERGENCE_COLOR, upLineSegmentsCopy, IndicatorChartPaintType.LineSegments, fullReloadOnNewCandle: true),
                        new IndicatorElement(DOWN_DIVERGENCE_COLOR, downLineSegmentsCopy, IndicatorChartPaintType.LineSegments, fullReloadOnNewCandle: true)
                    };
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
        }

        public string Regime { get; set; }
        public int NumCandlesLeftToFindPeak { get; set; }
        public int NumCandlesRightToFindPeak { get; set; }
        public int MinDivergenceSize { get; set; }
        public int MaxDivergenceSize { get; set; }

        /// <summary>
        /// all indicator values
        /// все значения индикатора
        /// </summary>
        List<List<decimal>> IIndicator.ValuesToChart { get; }

        /// <summary>
        /// indicator colors
        /// цвета для индикатора
        /// </summary>
        List<Color> IIndicator.Colors { get; }

        /// <summary>
        /// whether indicator can be removed from chart. This is necessary so that robots can't be removed /можно ли удалить индикатор с графика. Это нужно для того чтобы у роботов нельзя было удалить 
        /// indicators he needs in trading/индикаторы которые ему нужны в торговле
        /// </summary>
        public bool CanDelete { get; set; }

        /// <summary>
        /// indicator drawing type
        /// тип индикатора
        /// </summary>
        public IndicatorChartPaintType TypeIndicator { get; set; }

        /// <summary>
        /// unique indicator name
        /// уникальное имя индикатор
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// name of data series on which indicator will be drawn
        /// имя серии данных на которой индикатор прорисовывается
        /// </summary>
        public string NameSeries { get; set; }

        /// <summary>
        /// name of data area where indicator will be drawn
        /// имя области данных на которой индикатор прорисовывается
        /// </summary>
        public string NameArea { get; set; }

        /// <summary>
        /// indicator calculation length
        /// длинна расчёта индикатора
        /// </summary>
        public int Length { get; set; }

        /// <summary>
        /// need to show divergences (slows down performance)
        /// надо отрисовывать дивергенции на чарте (замедляет перформанс)
        /// </summary>
        public bool DisplayDivergences { get; set; }

        /// <summary>
        /// number of UP divergences to display on chart
        /// кол-во бычьих дивергенций которые надо нарисовать на чарте
        /// </summary>
        public int NumberUpDivergencesToStoreAndDisplay { get; set; }

        /// <summary>
        /// number of DOWN divergences to display on chart
        /// кол-во медвежих дивергенций которые надо нарисовать на чарте
        /// </summary>
        public int NumberDownDivergencesToStoreAndDisplay { get; set; }

        /// <summary>
        /// indicator color
        /// цвет индикатора
        /// </summary>
        public Color ColorBase { get; set; } = ColorTranslator.FromHtml("#9144b0");

        /// <summary>
        /// is indicator tracing enabled
        /// включена ли прорисовка индикатора
        /// </summary>
        public bool PaintOn { get; set; }

        /// <summary>
        /// RSI indicator values
        /// данные индикатора RSI
        /// </summary>
        public List<decimal> RsiValues { get; set; }

        /// <summary>
        /// Average candle GAINS list
        /// Средние ПРИБЫЛИ свечей
        /// </summary>
        private List<decimal> AvgGains { get; set; }

        /// <summary>
        /// Average candle LOSSES list
        /// Средние УБЫТКИ свечей
        /// </summary>
        private List<decimal> AvgLosses { get; set; }

        /// <summary>
        /// RSI peaks LOW (true/false)
        /// RSI пики НИЖНИЕ (true/false)
        /// </summary>
        private List<bool> RsiPeaksLow { get; set; }

        /// <summary>
        /// RSI peaks HIGH (true/false)
        /// RSI пики ВЕРХНИЕ (true/false)
        /// </summary>
        private List<bool> RsiPeaksHigh { get; set; }

        /// <summary>
        /// UP divergences
        /// лонговые дивергенции
        /// </summary>
        public List<RsiDivergenceObject> UpDivergences { get; set; }

        /// <summary>
        /// DOWN divergences
        /// шортовые дивергенции
        /// </summary>
        public List<RsiDivergenceObject> DownDivergences { get; set; }

        /// <summary>
        /// UP divergence lines
        /// линии лонговых дивергенций
        /// </summary>
        private List<ChartLineSegmentObject> UpDivergenceLineSegments
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    if (!this.DisplayDivergences)
                    {
                        return new List<ChartLineSegmentObject>();
                    }

                    var upDivergencesSnapshot = this.UpDivergences.ToList();
                    if (NumberUpDivergencesToStoreAndDisplay > 0)
                    {
                        return upDivergencesSnapshot.SelectMany(ud => ud.AllLineSegments).Where(l => l.Visible).Reverse().Take(NumberUpDivergencesToStoreAndDisplay * NUM_LINES_IN_ONE_DIVERGENCE).Reverse().ToList();
                    }
                    return upDivergencesSnapshot.SelectMany(ud => ud.AllLineSegments).Where(l => l.Visible).ToList();
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// DOWN divergence lines
        /// линии шортовых дивергенций
        /// </summary>
        private List<ChartLineSegmentObject> DownDivergenceLineSegments
        {
            get
            {
                _rwLock.EnterReadLock();
                try
                {
                    if (!this.DisplayDivergences)
                    {
                        return new List<ChartLineSegmentObject>();
                    }

                    var downDivergencesSnapshot = this.DownDivergences.ToList();
                    if (NumberDownDivergencesToStoreAndDisplay > 0)
                    {
                        return downDivergencesSnapshot.SelectMany(dd => dd.AllLineSegments).Where(l => l.Visible).Reverse().Take(NumberDownDivergencesToStoreAndDisplay * NUM_LINES_IN_ONE_DIVERGENCE).Reverse().ToList();
                    }
                    return downDivergencesSnapshot.SelectMany(dd => dd.AllLineSegments).Where(l => l.Visible).ToList();
                }
                finally
                {
                    _rwLock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// candles to calculate indicator
        /// свечи по которым строиться индикатор
        /// </summary>
        private List<Candle> _myCandles;

        /// <summary>
        /// indicator needs to be redrawn
        /// требуется перерисовать индикатор
        /// </summary>
        public event Action<IIndicator> NeadToReloadEvent;

        /// <summary>
        /// constructor without parameters.Indicator will not saved/конструктор без параметров. Индикатор не будет сохраняться
        /// used ONLY to create composite indicators/используется ТОЛЬКО для создания составных индикаторов
        /// Don't use it from robot creation layer/не используйте его из слоя создания роботов!
        /// </summary>
        /// <param name="canDelete">whether user can remove indicator from chart manually/можно ли пользователю удалить индикатор с графика вручную</param>
        public RsiDivergence(bool canDelete) : this(uniqueName: Guid.NewGuid().ToString(), canDelete: canDelete) { }

        /// <summary>
        /// constructor with parameters. Indicator will be saved
        /// конструктор с параметрами. Индикатор будет сохраняться
        /// </summary>
        /// <param name="uniqueName">unique name/уникальное имя</param>
        /// <param name="canDelete">whether user can remove indicator from chart manually/можно ли пользователю удалить индикатор с графика вручную</param>
        public RsiDivergence(string uniqueName, bool canDelete)
        {
            this.Name = uniqueName;
            this.TypeIndicator = IndicatorChartPaintType.MultiElement;
            this.RsiValues = new List<decimal>();
            this.AvgGains = new List<decimal>();
            this.AvgLosses = new List<decimal>();
            this.RsiPeaksLow = new List<bool>();
            this.RsiPeaksHigh = new List<bool>();
            this.UpDivergences = new List<RsiDivergenceObject>();
            this.DownDivergences = new List<RsiDivergenceObject>();
            this.Length = 5;
            this.DisplayDivergences = true;
            this.NumberUpDivergencesToStoreAndDisplay = -1; // Display all
            this.NumberDownDivergencesToStoreAndDisplay = -1; // Display all
            this.ColorBase = Color.Green;
            this.NumCandlesLeftToFindPeak = 5;
            this.NumCandlesRightToFindPeak = 5;
            this.MinDivergenceSize = 5;
            this.MaxDivergenceSize = 60;
            this.PaintOn = true;
            this.CanDelete = canDelete;
            this.Load();
        }

        /// <summary>
        /// upload settings from file
        /// загрузить настройки из файла
        /// </summary>
        public void Load()
        {
            if (File.Exists(@"Engine\" + Name + @".txt"))
            {
                try
                {
                    using (StreamReader reader = new StreamReader(@"Engine\" + Name + @".txt"))
                    {
                        this.ColorBase = Color.FromArgb(Convert.ToInt32(reader.ReadLine()));
                        this.Length = Convert.ToInt32(reader.ReadLine());
                        this.NumCandlesLeftToFindPeak = Convert.ToInt32(reader.ReadLine());
                        this.NumCandlesRightToFindPeak = Convert.ToInt32(reader.ReadLine());
                        this.MinDivergenceSize = Convert.ToInt32(reader.ReadLine());
                        this.MaxDivergenceSize = Convert.ToInt32(reader.ReadLine());
                        reader.Close();
                    }
                }
                catch (Exception)
                {
                    // send to log
                    // отправить в лог
                }
            }
        }

        /// <summary>
        /// save settings to file
        /// сохранить настройки в файл
        /// </summary>
        public void Save()
        {
            try
            {
                if (!String.IsNullOrWhiteSpace(Name))
                {
                    using (StreamWriter writer = new StreamWriter(@"Engine\" + Name + @".txt", false))
                    {
                        writer.WriteLine(this.ColorBase.ToArgb());
                        writer.WriteLine(this.Length);
                        writer.WriteLine(this.NumCandlesLeftToFindPeak);
                        writer.WriteLine(this.NumCandlesRightToFindPeak);
                        writer.WriteLine(this.MinDivergenceSize);
                        writer.WriteLine(this.MaxDivergenceSize);
                        writer.Close();
                    }
                }
            }
            catch (Exception)
            {
                // send to log
                // отправить в лог
            }
        }

        /// <summary>
        /// delete file with settings
        /// удалить файл с настройками
        /// </summary>
        public void Delete()
        {
            if (File.Exists(@"Engine\" + Name + @".txt"))
            {
                File.Delete(@"Engine\" + Name + @".txt");
            }
        }

        /// <summary>
        /// delete data
        /// удалить данные
        /// </summary>
        public void Clear()
        {
            if (this.RsiValues != null) this.RsiValues.Clear();
            if (this.AvgGains != null) this.AvgGains.Clear();
            if (this.AvgLosses != null) this.AvgLosses.Clear();
            if (this.RsiPeaksLow != null) this.RsiPeaksLow.Clear();
            if (this.RsiPeaksHigh != null) this.RsiPeaksHigh.Clear();
            _rwLock.EnterReadLock();
            try
            {
                if (this.UpDivergences != null) this.UpDivergences.Clear();
                if (this.DownDivergences != null) this.DownDivergences.Clear();
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
            this._myCandles = null;
        }

        /// <summary>
        /// display settings window
        /// показать окно настроек
        /// </summary>
        public void ShowDialog()
        {
            RsiDivergenceUi ui = new RsiDivergenceUi(this);
            ui.ShowDialog();
            if (ui.IsChange && _myCandles != null)
            {
                Reload();
            }
        }

        /// <summary>
        /// reload indicator
        /// перезагрузить индикатор
        /// </summary>
        public void Reload()
        {
            if (_myCandles != null)
            {
                ProcessAllCandles(_myCandles);
                if (this.NeadToReloadEvent != null)
                {
                    this.NeadToReloadEvent(this);
                }
            }
        }

        /// <summary>
        /// to upload new candles
        /// прогрузить новыми свечками
        /// </summary> 
        public void Process(List<Candle> candles)
        {
            if (candles != null)
            {
                this._myCandles = candles;

                if (IsFirstRun())
                {
                    ProcessAllCandles(candles);
                }
                else if (IsNewCandleAdded(candles))
                {
                    ProcessLastCandle(candles, mode: HandleResultMode.ADD);
                }
                else if (IsLastCandleUpdated(candles))
                {
                    ProcessLastCandle(candles, mode: HandleResultMode.UPDATE);
                }
            }
        }

        /// <summary>
        /// to upload from the beginning
        /// прогрузить с самого начала
        /// </summary>
        private void ProcessAllCandles(List<Candle> candles)
        {
            for (int i = 0; i < candles.Count; i++)
            {
                CalculateIndicator(candles, i, HandleResultMode.ADD);
            }
        }

        /// <summary>
        /// Calculates only last candle, but differentiate the way to save result (add or update)
        /// Пересчитывает только последнюю свечу, но может по-разному сохранять результат (добавлять или обновлять)
        /// </summary>
        private void ProcessLastCandle(List<Candle> candles, HandleResultMode mode)
        {
            CalculateIndicator(candles, candles.Count - 1, mode);
        }

        private bool IsRsiCalculated(int candleIndex)
        {
            return AvgGains != null && AvgLosses != null && 
                candleIndex < AvgGains.Count && candleIndex < AvgLosses.Count && 
                (AvgGains[candleIndex] + AvgLosses[candleIndex]) > 0;
        }

        private void CalculateIndicator(List<Candle> candles, int candleIndex, HandleResultMode mode)
        {
            SaveRsiPeakLowValue(mode, false);
            SaveRsiPeakHighValue(mode, false);
            Candle candle = candles[candleIndex];
            int minCalculatableCandleIndex = this.Length;
            bool calculationPossible = candleIndex >= minCalculatableCandleIndex;
            if (calculationPossible && !IsRsiCalculated(candleIndex))
            {
                // 1. Calculate RSI
                decimal avgGain, avgLoss;
                int prevCandleIndex = candleIndex - 1;
                candle.PreviousCandle = candles[prevCandleIndex];
                if (!IsRsiCalculated(prevCandleIndex))
                {
                    decimal gainSum = 0m;
                    decimal lossSum = 0m;
                    int rsiRangeStartIndex = candleIndex - this.Length;
                    List<Candle> rsiCandlesRange = candles.GetRange(rsiRangeStartIndex, this.Length + 1);
                    for (int i = 1; i < rsiCandlesRange.Count; i++)
                    {
                        gainSum += rsiCandlesRange[i].Gain;
                        lossSum += rsiCandlesRange[i].Loss;
                    }
                    avgGain = gainSum / this.Length;
                    avgLoss = lossSum / this.Length;
                }
                else
                {
                    avgGain = (AvgGains[prevCandleIndex] * (this.Length - 1) + candle.Gain) / this.Length;
                    avgLoss = (AvgLosses[prevCandleIndex] * (this.Length - 1) + candle.Loss) / this.Length;
                }
                decimal candleRSI = Math.Round(avgLoss == 0 ? 100 : 100 - (100 / (1 + avgGain / avgLoss)), 2);
                SaveRsiValue(mode, candleRSI);
                SaveAvgGainValue(mode, avgGain);
                SaveAvgLossValue(mode, avgLoss);

                // 2. LOW peak lost? -> cancel this peak and disable divergence with it
                int prevRsiLowPeakIndex = FindNearestLeftPeakIndex(RsiPeakType.LOW, candles);
                if (prevRsiLowPeakIndex > -1)
                {
                    bool stillPeak = IsRsiLowPeak(candles, prevRsiLowPeakIndex);
                    if (!stillPeak)
                    {
                        RsiPeaksLow[prevRsiLowPeakIndex] = false;
                        _rwLock.EnterReadLock();
                        try
                        {
                            ChartLineSegmentObject upDivergenceRsiLine = this.UpDivergences.Select(up => up.RsiDivergenceLine).Where(ud => ud.PointTo.XValue == prevRsiLowPeakIndex).FirstOrDefault();
                            if (upDivergenceRsiLine != null)
                            {
                                upDivergenceRsiLine.Visible = false;
                            }
                            ChartLineSegmentObject upDivergenceCandlesLine = this.UpDivergences.Select(up => up.CandlesDivergenceLine).Where(ud => ud.PointTo.XValue == prevRsiLowPeakIndex).FirstOrDefault();
                            if (upDivergenceCandlesLine != null)
                            {
                                upDivergenceCandlesLine.Visible = false;
                            }
                        }
                        finally
                        {
                            _rwLock.ExitReadLock();
                        }
                    }
                }

                // 3. HIGH peak lost? -> cancel this peak and disable divergence with it
                int prevRsiHighPeakIndex = FindNearestLeftPeakIndex(RsiPeakType.HIGH, candles);
                if (prevRsiHighPeakIndex > -1)
                {
                    bool stillPeak = IsRsiHighPeak(candles, prevRsiHighPeakIndex);
                    if (!stillPeak)
                    {
                        RsiPeaksHigh[prevRsiHighPeakIndex] = false;
                        _rwLock.EnterReadLock();
                        try
                        {
                            ChartLineSegmentObject downDivergenceRsiLine = this.DownDivergences.Select(dd => dd.RsiDivergenceLine).Where(dd => dd.PointTo.XValue == prevRsiHighPeakIndex).FirstOrDefault();
                            if (downDivergenceRsiLine != null)
                            {
                                downDivergenceRsiLine.Visible = false;
                            }
                            ChartLineSegmentObject downDivergenceCandlesLine = this.DownDivergences.Select(up => up.CandlesDivergenceLine).Where(dd => dd.PointTo.XValue == prevRsiHighPeakIndex).FirstOrDefault();
                            if (downDivergenceCandlesLine != null)
                            {
                                downDivergenceCandlesLine.Visible = false;
                            }
                        }
                        finally
                        {
                            _rwLock.ExitReadLock();
                        }
                    }
                }

                // 4. New LOW peak found? -> save this peak and find for divergence with it
                bool newLowPeakFound = IsRsiLowPeak(candles, candleIndex);
                if (newLowPeakFound)
                {
                    if (IsUpDivergencesEnabled())
                    {
                        prevRsiLowPeakIndex = FindLeftPeakIndexWhichOnMinMaxDivergenceDistance(RsiPeakType.LOW, candleIndex);
                        if (prevRsiLowPeakIndex > -1)
                        {
                            Candle prevLowPeakCandle = candles[prevRsiLowPeakIndex];
                            Candle newLowPeakCandle = candles[candleIndex];
                            decimal prevLowPeakCandleRSI = this.RsiValues[prevRsiLowPeakIndex];
                            decimal newLowPeakCandleRSI = this.RsiValues[candleIndex];
                            bool priceMakesLowerLow = prevLowPeakCandle.Low > newLowPeakCandle.Low;
                            bool rsiMakesHigherLow = prevLowPeakCandleRSI < newLowPeakCandleRSI;
                            if (priceMakesLowerLow && rsiMakesHigherLow)
                            {
                                DataPoint newUpDivergenceRsiPointTo = new DataPoint(candleIndex, (double)newLowPeakCandleRSI);
                                DataPoint newUpDivergenceCandlePointTo = new DataPoint(candleIndex, (double)newLowPeakCandle.Low);
                                _rwLock.EnterReadLock();
                                try
                                {
                                    RsiDivergenceObject upDivergence = this.UpDivergences.Where(ud => ud.RsiDivergenceLine.PointFrom.XValue == candleIndex).FirstOrDefault();
                                    if (upDivergence == null)
                                    {
                                        SaveUpDivergence(new RsiDivergenceObject(
                                            type: DivergenceType.UP,
                                            divergenceStartCandle: prevLowPeakCandle,
                                            rsiDivergenceLine: new ChartLineSegmentObject(
                                                areaName: this.NameArea,
                                                color: UP_DIVERGENCE_COLOR,
                                                lineWidth: DIVERGENCE_LINE_WIDTH,
                                                pointFrom: new DataPoint(prevRsiLowPeakIndex, (double)prevLowPeakCandleRSI),
                                                pointTo: newUpDivergenceRsiPointTo),
                                            candlesDivergenceLine: new ChartLineSegmentObject(
                                                areaName: PRIME_AREA_NAME,
                                                color: UP_DIVERGENCE_COLOR,
                                                lineWidth: DIVERGENCE_LINE_WIDTH,
                                                pointFrom: new DataPoint(prevRsiLowPeakIndex, (double)prevLowPeakCandle.Low),
                                                pointTo: newUpDivergenceCandlePointTo
                                        )));
                                    }
                                    else
                                    {
                                        upDivergence.RsiDivergenceLine.PointTo = newUpDivergenceRsiPointTo;
                                        upDivergence.CandlesDivergenceLine.PointTo = newUpDivergenceCandlePointTo;
                                        upDivergence.RsiDivergenceLine.Visible = true;
                                        upDivergence.CandlesDivergenceLine.Visible = true;
                                    }
                                }
                                finally
                                {
                                    _rwLock.ExitReadLock();
                                }
                            }
                        }
                    }
                    RsiPeaksLow[candleIndex] = true;
                }

                // 5. New HIGH peak found? -> save this peak and find for divergence with it
                bool newHighPeakFound = IsRsiHighPeak(candles, candleIndex);
                if (newHighPeakFound)
                {
                    if (IsDownDivergencesEnabled())
                    {
                        prevRsiHighPeakIndex = FindLeftPeakIndexWhichOnMinMaxDivergenceDistance(RsiPeakType.HIGH, candleIndex);
                        if (prevRsiHighPeakIndex > -1)
                        {
                            Candle prevHighPeakCandle = candles[prevRsiHighPeakIndex];
                            Candle newHighPeakCandle = candles[candleIndex];
                            decimal prevHighPeakCandleRSI = this.RsiValues[prevRsiHighPeakIndex];
                            decimal newHighPeakCandleRSI = this.RsiValues[candleIndex];
                            bool priceMakesHigherHigh = newHighPeakCandle.High > prevHighPeakCandle.High;
                            bool rsiMakesLowerHigh = newHighPeakCandleRSI < prevHighPeakCandleRSI;
                            if (priceMakesHigherHigh && rsiMakesLowerHigh)
                            {
                                DataPoint newDownDivergenceRsiPointTo = new DataPoint(candleIndex, (double)newHighPeakCandleRSI);
                                DataPoint newDownDivergenceCandlePointTo = new DataPoint(candleIndex, (double)newHighPeakCandle.High);
                                _rwLock.EnterReadLock();
                                try
                                {
                                    RsiDivergenceObject downDivergence = this.DownDivergences.Where(dd => dd.RsiDivergenceLine.PointFrom.XValue == candleIndex).FirstOrDefault();
                                    if (downDivergence == null)
                                    {
                                        SaveDownDivergence(new RsiDivergenceObject(
                                            type: DivergenceType.DOWN,
                                            divergenceStartCandle: prevHighPeakCandle,
                                            rsiDivergenceLine: new ChartLineSegmentObject(
                                                areaName: this.NameArea,
                                                color: DOWN_DIVERGENCE_COLOR,
                                                lineWidth: DIVERGENCE_LINE_WIDTH,
                                                pointFrom: new DataPoint(prevRsiHighPeakIndex, (double)prevHighPeakCandleRSI),
                                                pointTo: newDownDivergenceRsiPointTo),
                                            candlesDivergenceLine: new ChartLineSegmentObject(
                                                areaName: PRIME_AREA_NAME,
                                                color: DOWN_DIVERGENCE_COLOR,
                                                lineWidth: DIVERGENCE_LINE_WIDTH,
                                                pointFrom: new DataPoint(prevRsiHighPeakIndex, (double)prevHighPeakCandle.High),
                                                pointTo: newDownDivergenceCandlePointTo
                                        )));
                                    }
                                    else
                                    {
                                        downDivergence.RsiDivergenceLine.PointTo = newDownDivergenceRsiPointTo;
                                        downDivergence.CandlesDivergenceLine.PointTo = newDownDivergenceCandlePointTo;
                                        downDivergence.RsiDivergenceLine.Visible = true;
                                        downDivergence.CandlesDivergenceLine.Visible = true;
                                    }
                                }
                                finally
                                {
                                    _rwLock.ExitReadLock();
                                }
                            }
                        }
                    }
                    RsiPeaksHigh[candleIndex] = true;
                }
            }
            else
            {
                SaveRsiValue(mode, 0);
                SaveAvgGainValue(mode, 0);
                SaveAvgLossValue(mode, 0);
            }
        }

        private void SaveUpDivergence(RsiDivergenceObject divergence)
        {
            this.UpDivergences.Add(divergence);
            if (this.UpDivergences.Count > this.NumberUpDivergencesToStoreAndDisplay)
            {
                int oldestDisabledDivergenceIndex = GetOldestDisabledDivergenceIndex(this.UpDivergences);
                this.UpDivergences.RemoveAt(oldestDisabledDivergenceIndex >= 0 ? oldestDisabledDivergenceIndex : 0);
            }
        }

        private void SaveDownDivergence(RsiDivergenceObject divergence)
        {
            this.DownDivergences.Add(divergence);
            if (this.DownDivergences.Count > this.NumberDownDivergencesToStoreAndDisplay)
            {
                int oldestDisabledDivergenceIndex = GetOldestDisabledDivergenceIndex(this.DownDivergences);
                this.DownDivergences.RemoveAt(oldestDisabledDivergenceIndex >= 0 ? oldestDisabledDivergenceIndex : 0);
            }
        }

        private void SaveRsiValue(HandleResultMode mode, decimal rsiValue)
        {
            switch (mode)
            {
                case HandleResultMode.ADD:
                    this.RsiValues.Add(rsiValue);
                    break;
                case HandleResultMode.UPDATE:
                    this.RsiValues[this.RsiValues.Count - 1] = rsiValue;
                    break;
            }
        }

        private void SaveAvgGainValue(HandleResultMode mode, decimal avgGain)
        {
            switch (mode)
            {
                case HandleResultMode.ADD:
                    this.AvgGains.Add(avgGain);
                    break;
                case HandleResultMode.UPDATE:
                    this.AvgGains[this.AvgGains.Count - 1] = avgGain;
                    break;
            }
        }

        private void SaveAvgLossValue(HandleResultMode mode, decimal avgLoss)
        {
            switch (mode)
            {
                case HandleResultMode.ADD:
                    this.AvgLosses.Add(avgLoss);
                    break;
                case HandleResultMode.UPDATE:
                    this.AvgLosses[this.AvgLosses.Count - 1] = avgLoss;
                    break;
            }
        }

        private void SaveRsiPeakLowValue(HandleResultMode mode, bool rsiPeakLow)
        {
            switch (mode)
            {
                case HandleResultMode.ADD:
                    this.RsiPeaksLow.Add(rsiPeakLow);
                    break;
                case HandleResultMode.UPDATE:
                    this.RsiPeaksLow[this.RsiPeaksLow.Count - 1] = rsiPeakLow;
                    break;
            }
        }

        private void SaveRsiPeakHighValue(HandleResultMode mode, bool rsiPeakHigh)
        {
            switch (mode)
            {
                case HandleResultMode.ADD:
                    this.RsiPeaksHigh.Add(rsiPeakHigh);
                    break;
                case HandleResultMode.UPDATE:
                    this.RsiPeaksHigh[this.RsiPeaksHigh.Count - 1] = rsiPeakHigh;
                    break;
            }
        }

        private int GetOldestDisabledDivergenceIndex(List<RsiDivergenceObject> divergences)
        {
            int oldestDisabledDivergenceIndex = -1;
            for (int i = 0; i < divergences.Count; i++)
            {
                if (!divergences[i].Enabled)
                {
                    oldestDisabledDivergenceIndex = i;
                    break;
                }
            }
            return oldestDisabledDivergenceIndex;
        }

        private int FindNearestLeftPeakIndex(RsiPeakType peakType, List<Candle> candles)
        {
            for (int i = candles.Count - 1; i >= 0; i--)
            {
                if (peakType == RsiPeakType.LOW ? RsiPeaksLow[i] : RsiPeaksHigh[i])
                {
                    return i;
                }
            }
            return -1;
        }

        private int FindLeftPeakIndexWhichOnMinMaxDivergenceDistance(RsiPeakType peakType, int newPeakCandleIndex)
        {
            int nearestLeftPeakSearchRangeEndIndex = newPeakCandleIndex - this.MinDivergenceSize;
            int nearestLeftPeakSearchRangeStartIndex = newPeakCandleIndex - this.MaxDivergenceSize;
            for (int i = nearestLeftPeakSearchRangeEndIndex; i >= 0 && i >= nearestLeftPeakSearchRangeStartIndex; i--)
            {
                if (peakType == RsiPeakType.LOW ? RsiPeaksLow[i] : RsiPeaksHigh[i])
                {
                    return i;
                }
            }
            return -1;
        }

        private bool IsRsiLowPeak(List<Candle> candles, int rsiNewPeakCandleCandidateIndex)
        {
            return IsRsiPeak(RsiPeakType.LOW, candles, rsiNewPeakCandleCandidateIndex);
        }

        private bool IsRsiHighPeak(List<Candle> candles, int rsiNewPeakCandleCandidateIndex)
        {
            return IsRsiPeak(RsiPeakType.HIGH, candles, rsiNewPeakCandleCandidateIndex);
        }

        private bool IsRsiPeak(RsiPeakType peakType, List<Candle> candles, int rsiNewPeakCandleCandidateIndex)
        {
            bool peakFound = true;
            decimal newPeakCandleCandidateRSI = this.RsiValues[rsiNewPeakCandleCandidateIndex];

            // Check LEFT
            for (int i = 1; i <= this.NumCandlesLeftToFindPeak; i++)
            {
                int candleIndexToCheckPeakAgainstOf = rsiNewPeakCandleCandidateIndex - i;
                if (IsPeakConditionBreached(peakType, newPeakCandleCandidateRSI, candleIndexToCheckPeakAgainstOf))
                {
                    peakFound = false;
                    break;
                }
            }

            // Check RIGHT
            bool lastCandleIsPeakCandidate = rsiNewPeakCandleCandidateIndex == candles.Count - 1;
            if (!lastCandleIsPeakCandidate)
            {
                for (int i = 1; i <= this.NumCandlesRightToFindPeak; i++)
                {
                    int candleIndexToCheckPeakAgainstOf = rsiNewPeakCandleCandidateIndex + i;
                    if (candleIndexToCheckPeakAgainstOf < candles.Count)
                    {
                        if (IsPeakConditionBreached(peakType, newPeakCandleCandidateRSI, candleIndexToCheckPeakAgainstOf))
                        {
                            peakFound = false;
                            break;
                        }
                    }
                }
            }

            return peakFound;
        }

        private bool IsPeakConditionBreached(RsiPeakType peakType, decimal newPeakCandleCandidateRSI, int candleIndexToCheckPeakAgainstOf)
        {
            return peakType == RsiPeakType.LOW ?
                newPeakCandleCandidateRSI > this.RsiValues[candleIndexToCheckPeakAgainstOf] :
                newPeakCandleCandidateRSI < this.RsiValues[candleIndexToCheckPeakAgainstOf];
        }

        private bool IsUpDivergencesEnabled()
        {
            return this.Regime == "On" || this.Regime == "OnlyLong";
        }

        private bool IsDownDivergencesEnabled()
        {
            return this.Regime == "On" || this.Regime == "OnlyShort";
        }

        private bool IsFirstRun()
        {
            return this.RsiValues == null || this.RsiValues.Count == 0;
        }

        private bool IsNewCandleAdded(List<Candle> candles)
        {
            return candles.Count > this.RsiValues.Count;
        }

        private bool IsLastCandleUpdated(List<Candle> candles)
        {
            return candles.Count == this.RsiValues.Count;
        }

        private enum RsiPeakType
        {
            LOW,
            HIGH
        }

        private enum HandleResultMode
        {
            ADD,
            UPDATE
        }
    }

    public enum DivergenceType
    {
        UP,
        DOWN
    }

    public class RsiDivergenceObject
    {
        private static readonly int ENTRY_LEVEL_LINE_LENGTH_AFTER_DIVERGENCE_FINISH = 5;
        private static readonly int VERTICAL_RSI_TO_CANDLES_LINE_WIDTH = 1;
        private static readonly Color VERTICAL_RSI_TO_CANDLES_LINE_COLOR = Color.White;
        private static readonly ChartDashStyle VERTICAL_RSI_TO_CANDLES_LINE_STYLE = ChartDashStyle.Dash;
        private static readonly DateTime Jan1St1970 = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public long Id { get { return ToMillis(DivergenceStartCandle.TimeStart.ToUniversalTime()); } }
        public string Thumbprint { get { return String.Format("{0}_{1}_{2}_{3}", RsiDivergenceLine.PointFrom.XValue, RsiDivergenceLine.PointFrom.YValues[0], RsiDivergenceLine.PointTo.XValue, RsiDivergenceLine.PointTo.YValues[0]); } }
        public DivergenceType Type { get; private set; }
        public Candle DivergenceStartCandle { get; }
        public ChartLineSegmentObject RsiDivergenceLine { get; }
        public ChartLineSegmentObject CandlesDivergenceLine { get; }
        public List<ChartLineSegmentObject> AllLineSegments
        {
            get
            {
                return new List<ChartLineSegmentObject>(BuildVerticalRsiToCandlesLines())
                {
                    RsiDivergenceLine, 
                    CandlesDivergenceLine
                };
            }
        }
        public bool Enabled { get { return RsiDivergenceLine.Visible && CandlesDivergenceLine.Visible; } }
        public decimal EntryLevel { get { return Type == DivergenceType.UP ? GetDivergenceStartCandleBodyDown() : GetDivergenceStartCandleBodyUp(); } }
        public decimal LossLevel { get { return Type == DivergenceType.UP ? GetDivergenceFinishCandleLow() : GetDivergenceFinishCandleHigh(); } }
        public int IndexTo { get { return (int)this.CandlesDivergenceLine.PointTo.XValue; } }
        public int IndexFrom { get { return (int)this.CandlesDivergenceLine.PointFrom.XValue; } }

        public RsiDivergenceObject(DivergenceType type, Candle divergenceStartCandle, ChartLineSegmentObject rsiDivergenceLine, ChartLineSegmentObject candlesDivergenceLine)
        {
            Type = type;
            DivergenceStartCandle = divergenceStartCandle;
            RsiDivergenceLine = rsiDivergenceLine;
            CandlesDivergenceLine = candlesDivergenceLine;
        }

        private List<ChartLineSegmentObject> BuildVerticalRsiToCandlesLines()
        {
            return new List<ChartLineSegmentObject>()
            {
                new ChartLineSegmentObject(
                    areaName: RsiDivergenceLine.NameArea,
                    color: VERTICAL_RSI_TO_CANDLES_LINE_COLOR,
                    lineWidth: VERTICAL_RSI_TO_CANDLES_LINE_WIDTH,
                    pointFrom: RsiDivergenceLine.PointFrom,
                    pointTo: new DataPoint(RsiDivergenceLine.PointFrom.XValue, 100)
                ) { Visible = RsiDivergenceLine.Visible, LineStyle = VERTICAL_RSI_TO_CANDLES_LINE_STYLE },
                new ChartLineSegmentObject(
                    areaName: RsiDivergenceLine.NameArea,
                    color: VERTICAL_RSI_TO_CANDLES_LINE_COLOR,
                    lineWidth: VERTICAL_RSI_TO_CANDLES_LINE_WIDTH,
                    pointFrom: RsiDivergenceLine.PointTo,
                    pointTo: new DataPoint(RsiDivergenceLine.PointTo.XValue, 100)
                ) { Visible = RsiDivergenceLine.Visible, LineStyle = VERTICAL_RSI_TO_CANDLES_LINE_STYLE },
                new ChartLineSegmentObject(
                    areaName: CandlesDivergenceLine.NameArea,
                    color: VERTICAL_RSI_TO_CANDLES_LINE_COLOR,
                    lineWidth: VERTICAL_RSI_TO_CANDLES_LINE_WIDTH,
                    pointFrom: CandlesDivergenceLine.PointFrom,
                    pointTo: new DataPoint(CandlesDivergenceLine.PointFrom.XValue, 0)
                ) { Visible = CandlesDivergenceLine.Visible, LineStyle = VERTICAL_RSI_TO_CANDLES_LINE_STYLE },
                new ChartLineSegmentObject(
                    areaName: CandlesDivergenceLine.NameArea,
                    color: VERTICAL_RSI_TO_CANDLES_LINE_COLOR,
                    lineWidth: VERTICAL_RSI_TO_CANDLES_LINE_WIDTH,
                    pointFrom: CandlesDivergenceLine.PointTo,
                    pointTo: new DataPoint(CandlesDivergenceLine.PointTo.XValue, 0)
                ) { Visible = CandlesDivergenceLine.Visible, LineStyle = VERTICAL_RSI_TO_CANDLES_LINE_STYLE },
                new ChartLineSegmentObject(
                    areaName: CandlesDivergenceLine.NameArea,
                    color: Color.Yellow,
                    lineWidth: 1,
                    pointFrom: new DataPoint(CandlesDivergenceLine.PointFrom.XValue, (double)EntryLevel),
                    pointTo: new DataPoint(CandlesDivergenceLine.PointTo.XValue + ENTRY_LEVEL_LINE_LENGTH_AFTER_DIVERGENCE_FINISH, (double)EntryLevel)
                ) { Visible = CandlesDivergenceLine.Visible, LineStyle = ChartDashStyle.Dot }
            };
        }

        private decimal GetDivergenceStartCandleBodyUp()
        {
            return DivergenceStartCandle.Open > DivergenceStartCandle.Close ? DivergenceStartCandle.Open : DivergenceStartCandle.Close;
        }

        private decimal GetDivergenceStartCandleBodyDown()
        {
            return DivergenceStartCandle.Open < DivergenceStartCandle.Close ? DivergenceStartCandle.Open : DivergenceStartCandle.Close;
        }

        private decimal GetDivergenceFinishCandleLow()
        {
            return (decimal)this.CandlesDivergenceLine.PointTo.YValues[0];
        }

        private decimal GetDivergenceFinishCandleHigh()
        {
            return (decimal)this.CandlesDivergenceLine.PointTo.YValues[0];
        }

        private long ToMillis(DateTime date)
        {
            return (long)(date - Jan1St1970).TotalMilliseconds;
        }

        public decimal CalculateAngleStrength(bool logValue = false)
        {
            double priceFrom = CandlesDivergenceLine.PointFrom.YValues[0];
            double priceTo = CandlesDivergenceLine.PointTo.YValues[0];
            double rsiFrom = RsiDivergenceLine.PointFrom.YValues[0];
            double rsiTo = RsiDivergenceLine.PointTo.YValues[0];
            double indexFrom = CandlesDivergenceLine.PointFrom.XValue;
            double indexTo = CandlesDivergenceLine.PointTo.XValue;

            int divergenceLength = (int)(indexTo - indexFrom);
            if (divergenceLength > 0)
            {
                decimal rsiChange = Type == DivergenceType.DOWN ? (decimal)rsiFrom - (decimal)rsiTo : (decimal)rsiTo - (decimal)rsiFrom;
                decimal priceChange = Type == DivergenceType.DOWN ? (decimal)priceTo - (decimal)priceFrom : (decimal)priceFrom - (decimal)priceTo;
                if (priceChange > 0 && rsiChange > 0)
                {
                    // Оба в относительных единицах [0, 1+]
                    decimal priceRelative = priceChange / (decimal)priceFrom;
                    decimal rsiRelative = rsiChange / 100m;

                    // Геометрическое среднее (более справедливо чем произведение)
                    decimal geometricMean = (decimal)Math.Sqrt((double)(priceRelative * rsiRelative));

                    decimal strength = geometricMean * 100m; // *100 для удобных чисел

                    // Учитываем длину
                    // decimal lengthPenalty = 1m / (decimal)Math.Sqrt(divergenceLength);
                    // strength = strength * lengthPenalty;

                    if (logValue)
                    {
                        Console.WriteLine("Price: {0:F2}% | RSI: {1:F2}% | Length: {2} bars", priceRelative * 100, rsiRelative * 100, divergenceLength);
                        Console.WriteLine("Geometric mean: {0:F6}", geometricMean);
                        Console.WriteLine("Divergence strength: {0:F4}", strength);
                    }

                    return strength;
                }
            }

            return 0;
        }
    }

    public class ChartLineSegmentObject : ILineSegmentIndicatorChartValue
    {
        public Color Color { get; private set; }
        public int LineWidth { get; private set; }
        public ChartDashStyle LineStyle { get; set; }
        public bool LabelEnabled { get; }
        public bool Visible { get; set; }
        public string NameArea { get; private set; }
        public DataPoint PointFrom { get; private set; }
        public DataPoint PointTo { get; set; }

        public ChartLineSegmentObject(string areaName, Color color, int lineWidth, DataPoint pointFrom, DataPoint pointTo)
        {
            this.Visible = true;
            this.LabelEnabled = false;
            this.Color = color;
            this.LineWidth = lineWidth;
            this.LineStyle = ChartDashStyle.Solid;
            this.NameArea = areaName;
            this.PointFrom = pointFrom;
            this.PointTo = pointTo;
        }
    }
}
