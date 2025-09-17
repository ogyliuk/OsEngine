using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        /// <summary>
        /// list of elements which needs to be painted on the chart
        /// список элементов, которые должны быть отрисованы на чарте
        /// </summary>
        public List<IndicatorElement> Elements
        {
            get
            {
                return new List<IndicatorElement>()
                {
                    new IndicatorElement(ColorBase, RsiValues, IndicatorChartPaintType.Line),
                    new IndicatorElement(Color.Green, UpDivergences, IndicatorChartPaintType.LineSegments, fullReloadOnNewCandle: true),
                    new IndicatorElement(Color.Red, DownDivergences, IndicatorChartPaintType.LineSegments, fullReloadOnNewCandle: true)
                };
            }
        }

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
        public List<decimal> RsiValues { get; set; } = new List<decimal>();

        /// <summary>
        /// UP divergences
        /// лонговые дивергенции
        /// </summary>
        public List<Tuple<DataPoint, DataPoint>> UpDivergences { get; set; } = new List<Tuple<DataPoint, DataPoint>>();

        /// <summary>
        /// DOWN divergences
        /// шортовые дивергенции
        /// </summary>
        public List<Tuple<DataPoint, DataPoint>> DownDivergences { get; set; } = new List<Tuple<DataPoint, DataPoint>>();

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
            this.Length = 5;
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
            if (this.UpDivergences != null) this.UpDivergences.Clear();
            if (this.DownDivergences != null) this.DownDivergences.Clear();
            if (_myCandles != null) for (int i = 0; i < _myCandles.Count; i++) _myCandles[i] = CleanupCandleRsiData(_myCandles[i]);
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
                for (int i = 0; i < _myCandles.Count; i++) _myCandles[i] = CleanupCandleRsiData(_myCandles[i]);
                ProcessAll(_myCandles);
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

                if (this.RsiValues != null && this.RsiValues.Count + 1 == candles.Count)
                {
                    ProcessOne(candles);
                }
                else if (this.RsiValues != null && this.RsiValues.Count == candles.Count)
                {
                    ProcessLast(candles);
                }
                else
                {
                    ProcessAll(candles);
                }
            }
        }

        /// <summary>
        /// load only last candle
        /// прогрузить только последнюю свечку
        /// </summary>
        private void ProcessOne(List<Candle> candles)
        {
            if (candles != null)
            {
                decimal lastCandleRsiValue;
                Calculate(candles, candles.Count - 1, out lastCandleRsiValue);
                if (this.RsiValues == null)
                {
                    this.RsiValues = new List<decimal>() { lastCandleRsiValue };
                }
                else
                {
                    this.RsiValues.Add(lastCandleRsiValue);
                }
            }
        }

        /// <summary>
        /// to upload from the beginning
        /// прогрузить с самого начала
        /// </summary>
        private void ProcessAll(List<Candle> candles)
        {
            if (candles != null)
            {
                this.RsiValues = new List<decimal>();
                this.UpDivergences = new List<Tuple<DataPoint, DataPoint>>();
                this.DownDivergences = new List<Tuple<DataPoint, DataPoint>>();
                for (int i = 0; i < candles.Count; i++)
                {
                    decimal rsiValue;
                    Calculate(candles, i, out rsiValue);
                    this.RsiValues.Add(rsiValue);
                }
            }
        }

        /// <summary>
        /// overload last value
        /// перегрузить последнее значение
        /// </summary>
        private void ProcessLast(List<Candle> candles)
        {
            if (candles != null)
            {
                decimal rsiValue;
                Calculate(candles, candles.Count - 1, out rsiValue);
                this.RsiValues[this.RsiValues.Count - 1] = rsiValue;
            }
        }

        private void Calculate(List<Candle> candles, int candleIndex, out decimal rsi)
        {
            rsi = 0;
            Candle candle = candles[candleIndex];
            int minCalculatableCandleIndex = this.Length;
            bool calculationPossible = candleIndex >= minCalculatableCandleIndex;
            if (calculationPossible && !candle.IsRSICalculated)
            {
                // 1. Calculate RSI
                candle.PreviousCandle = candles[candleIndex - 1];
                if (!candle.PreviousCandle.IsRSICalculated)
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
                    candle.AvgGain = gainSum / this.Length;
                    candle.AvgLoss = lossSum / this.Length;
                }
                else
                {
                    candle.AvgGain = (candle.PreviousCandle.AvgGain * (this.Length - 1) + candle.Gain) / this.Length;
                    candle.AvgLoss = (candle.PreviousCandle.AvgLoss * (this.Length - 1) + candle.Loss) / this.Length;
                }
                candle.RSI = rsi = Math.Round(candle.AvgLoss == 0 ? 100 : 100 - (100 / (1 + candle.AvgGain / candle.AvgLoss)), 2);

                // 2. Find RSI peaks
                int rsiNewPeakCandleCandidateIndex = candleIndex - this.NumCandlesRightToFindPeak;
                bool rsiNewPeakPossible = rsiNewPeakCandleCandidateIndex > this.NumCandlesLeftToFindPeak + this.Length;
                if (rsiNewPeakPossible)
                {
                    bool newRsiLowPeakFound = IsRsiLowPeakFound(candles, rsiNewPeakCandleCandidateIndex);
                    bool newRsiHighPeakFound = IsRsiHighPeakFound(candles, rsiNewPeakCandleCandidateIndex);
                    candles[rsiNewPeakCandleCandidateIndex].IsRSIPeakLow = newRsiLowPeakFound;
                    candles[rsiNewPeakCandleCandidateIndex].IsRSIPeakHigh = newRsiHighPeakFound;

                    // 3. RSI UP divergences search
                    if (newRsiLowPeakFound)
                    {
                        int prevRsiLowPeakIndex = FindNearestLeftPeakIndex(RsiPeakType.LOW, candles, rsiNewPeakCandleCandidateIndex);
                        if (prevRsiLowPeakIndex > -1)
                        {
                            Candle prevLowPeakCandle = candles[prevRsiLowPeakIndex];
                            Candle newLowPeakCandle = candles[rsiNewPeakCandleCandidateIndex];
                            bool priceMakesLowerLow = prevLowPeakCandle.Low > newLowPeakCandle.Low;
                            bool rsiMakesHigherLow = prevLowPeakCandle.RSI < newLowPeakCandle.RSI;
                            if (priceMakesLowerLow && rsiMakesHigherLow)
                            {
                                prevLowPeakCandle.IsUpDivergenceStart = true;
                                newLowPeakCandle.IsUpDivergenceEnd = true;
                                this.UpDivergences.Add(new Tuple<DataPoint, DataPoint>(
                                    new DataPoint(prevRsiLowPeakIndex, (double)prevLowPeakCandle.RSI), 
                                    new DataPoint(rsiNewPeakCandleCandidateIndex, (double)newLowPeakCandle.RSI)
                                ));
                            }
                        }
                    }

                    // 4. RSI DOWN divergences search
                    if (newRsiHighPeakFound)
                    {
                        int prevRsiHighPeakIndex = FindNearestLeftPeakIndex(RsiPeakType.HIGH, candles, rsiNewPeakCandleCandidateIndex);
                        if (prevRsiHighPeakIndex > -1)
                        {
                            Candle prevHighPeakCandle = candles[prevRsiHighPeakIndex];
                            Candle newHighPeakCandle = candles[rsiNewPeakCandleCandidateIndex];
                            bool priceMakesHigherHigh = newHighPeakCandle.High > prevHighPeakCandle.High;
                            bool rsiMakesLowerHigh = newHighPeakCandle.RSI < prevHighPeakCandle.RSI;
                            if (priceMakesHigherHigh && rsiMakesLowerHigh)
                            {
                                prevHighPeakCandle.IsDownDivergenceStart = true;
                                newHighPeakCandle.IsDownDivergenceEnd = true;
                                this.DownDivergences.Add(new Tuple<DataPoint, DataPoint>(
                                    new DataPoint(prevRsiHighPeakIndex, (double)prevHighPeakCandle.RSI),
                                    new DataPoint(rsiNewPeakCandleCandidateIndex, (double)newHighPeakCandle.RSI)
                                ));
                            }
                        }
                    }
                }
            }
        }

        private int FindNearestLeftPeakIndex(RsiPeakType peakType, List<Candle> candles, int newPeakCandleIndex)
        {
            int nearestLeftPeakSearchRangeEndIndex = newPeakCandleIndex - this.MinDivergenceSize;
            int nearestLeftPeakSearchRangeStartIndex = newPeakCandleIndex - this.MaxDivergenceSize;
            for (int i = nearestLeftPeakSearchRangeEndIndex; i >= 0 && i >= nearestLeftPeakSearchRangeStartIndex; i--)
            {
                if (peakType == RsiPeakType.LOW ? candles[i].IsRSIPeakLow : candles[i].IsRSIPeakHigh)
                {
                    return i;
                }
            }
            return -1;
        }

        private bool IsRsiLowPeakFound(List<Candle> candles, int rsiNewPeakCandleCandidateIndex)
        {
            return IsRsiPeakFound(RsiPeakType.LOW, candles, rsiNewPeakCandleCandidateIndex);
        }

        private bool IsRsiHighPeakFound(List<Candle> candles, int rsiNewPeakCandleCandidateIndex)
        {
            return IsRsiPeakFound(RsiPeakType.HIGH, candles, rsiNewPeakCandleCandidateIndex);
        }

        private bool IsRsiPeakFound(RsiPeakType peakType, List<Candle> candles, int rsiNewPeakCandleCandidateIndex)
        {
            bool peakFound = true;
            Candle rsiNewPeakCandleCandidate = candles[rsiNewPeakCandleCandidateIndex];
            for (int j = 1; j <= this.NumCandlesLeftToFindPeak; j++)
            {
                int candleIndexToCheckPeakAgainstOf = rsiNewPeakCandleCandidateIndex - j;
                if (IsPeakConditionBreached(peakType, candles, rsiNewPeakCandleCandidate, candleIndexToCheckPeakAgainstOf))
                {
                    peakFound = false;
                    break;
                }
            }
            for (int j = 1; j <= this.NumCandlesRightToFindPeak; j++)
            {
                int candleIndexToCheckPeakAgainstOf = rsiNewPeakCandleCandidateIndex + j;
                if (IsPeakConditionBreached(peakType, candles, rsiNewPeakCandleCandidate, candleIndexToCheckPeakAgainstOf))
                {
                    peakFound = false;
                    break;
                }
            }
            return peakFound;
        }

        private bool IsPeakConditionBreached(RsiPeakType peakType, List<Candle> candles, Candle rsiNewPeakCandleCandidate, int candleIndexToCheckPeakAgainstOf)
        {
            return peakType == RsiPeakType.LOW ?
                rsiNewPeakCandleCandidate.RSI > candles[candleIndexToCheckPeakAgainstOf].RSI :
                rsiNewPeakCandleCandidate.RSI < candles[candleIndexToCheckPeakAgainstOf].RSI;
        }

        private Candle CleanupCandleRsiData(Candle candle)
        {
            candle.PreviousCandle = null;
            candle.RSI = 0;
            candle.AvgGain = 0;
            candle.AvgLoss = 0;
            candle.IsRSIPeakLow = false;
            candle.IsRSIPeakHigh = false;
            candle.IsUpDivergenceStart = false;
            candle.IsUpDivergenceEnd = false;
            candle.IsDownDivergenceStart = false;
            candle.IsDownDivergenceEnd = false;
            return candle;
        }

        private enum RsiPeakType
        {
            LOW,
            HIGH
        }
    }
}
