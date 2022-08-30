﻿using OsEngine.Entity;
using OsEngine.Indicators;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace OsEngine.Charts.CandleChart.Indicators
{
    public class BollingerWithSqueeze : IIndicator
    {
        /// <summary>
        /// constructor
        /// конструктор
        /// </summary>
        /// <param name="uniqName">unique name/уникальное имя</param>
        /// <param name="canDelete">whether user can remove indicator from chart manually/можно ли пользователю удалить индикатор с графика вручную</param>
        public BollingerWithSqueeze(string uniqName, bool canDelete)
        {
            Name = uniqName;
            TypeIndicator = IndicatorChartPaintType.Column;
            ColorSqueeze = Color.White;
            ColorUp = Color.DodgerBlue;
            ColorDown = Color.DarkRed;
            Lenght = 20;
            Deviation = 2;
            SqueezePeriod = 130;
            PaintOn = true;
            CanDelete = canDelete;
            Load();
        }

        /// <summary>
        /// constructor without parameters.Indicator will not saved/конструктор без параметров. Индикатор не будет сохраняться
        /// used ONLY to create composite indicators/используется ТОЛЬКО для создания составных индикаторов
        /// Don't use it from robot creation layer/не используйте его из слоя создания роботов!
        /// </summary>
        /// <param name="canDelete">whether user can remove indicator from chart manually/можно ли пользователю удалить индикатор с графика вручную</param>
        public BollingerWithSqueeze(bool canDelete)
        {
            Name = Guid.NewGuid().ToString();
            TypeIndicator = IndicatorChartPaintType.Column;
            ColorSqueeze = Color.White;
            ColorUp = Color.DodgerBlue;
            ColorDown = Color.DarkRed;
            Lenght = 20;
            Deviation = 2;
            SqueezePeriod = 130;
            PaintOn = true;
            CanDelete = canDelete;
        }

        /// <summary>
        /// all indicator values
        /// все значения индикатора
        /// </summary>
        List<List<decimal>> IIndicator.ValuesToChart
        {
            get
            {
                List<List<decimal>> list = new List<List<decimal>>();
                list.Add(ValuesSqueezeFlag);
                return list;
            }
        }

        /// <summary>
        /// indicator colors
        /// цвета для индикатора
        /// </summary>
        List<Color> IIndicator.Colors
        {
            get
            {
                List<Color> colors = new List<Color>();
                colors.Add(ColorSqueeze);
                colors.Add(ColorSqueeze);
                return colors;
            }

        }

        /// <summary>
        /// whether indicator can be removed from chart. This is necessary so that robots can't be removed /можно ли удалить индикатор с графика. Это нужно для того чтобы у роботов нельзя было удалить 
        /// indicators he needs in trading/индикаторы которые ему нужны в торговле
        /// </summary>
        public bool CanDelete { get; set; }

        /// <summary>
        /// indicator type
        /// тип индикатора
        /// </summary>
        public IndicatorChartPaintType TypeIndicator
        { get; set; }

        /// <summary>
        /// name of data series on which indicator will be drawn
        /// имя серии данных на которой будет прорисован индикатор
        /// </summary>
        public string NameSeries
        { get; set; }

        /// <summary>
        /// name of data area where indicator will be drawn
        /// имя области данных на которой будет прорисовываться индикатор
        /// </summary>
        public string NameArea
        { get; set; }

        /// <summary>
        /// top bollinger line
        /// верхняя линия боллинжера
        /// </summary>
        public List<decimal> ValuesUp
        { get; set; }

        /// <summary>
        /// bottom line of bollinger
        /// нижняя линия боллинджера
        /// </summary>
        public List<decimal> ValuesDown
        { get; set; }

        /// <summary>
        /// bollinger bands withs
        /// ширина канала боллинджера
        /// </summary>
        public List<decimal> ValuesBandsWidth
        { get; set; }

        /// <summary>
        /// squeeze of bollinger
        /// сужения линий боллинджера
        /// </summary>
        public List<decimal> ValuesSqueezeFlag
        { get; set; }

        /// <summary>
        /// unique indicator name
        /// уникальное имя индикатора
        /// </summary>
        public string Name
        { get; set; }

        /// <summary>
        /// period length to calculate indicator
        /// длина расчёта индикатора
        /// </summary>
        public int Lenght
        { get; set; }

        /// <summary>
        /// deviation
        /// отклонение
        /// </summary>
        public decimal Deviation
        { get; set; }

        /// <summary>
        /// squeeze period length to calculate indicator
        /// длина расчёта индикатора сужения
        /// </summary>
        public int SqueezePeriod
        { get; set; }

        /// <summary>
        /// color of upper data series
        /// цвет верхней серии данных
        /// </summary>
        public Color ColorUp
        { get; set; }

        /// <summary>
        /// color of lower data series
        /// цвет нижней серии данных
        /// </summary>
        public Color ColorDown
        { get; set; }

        /// <summary>
        /// color of bollinger bands squeeze data series
        /// цвет сужения
        /// </summary>
        public Color ColorSqueeze
        { get; set; }

        /// <summary>
        /// is indicator tracing enabled
        /// включена ли прорисовка индикатора
        /// </summary>
        public bool PaintOn
        { get; set; }

        /// <summary>
        /// upload settings from file
        /// загрузить настройки из файла
        /// </summary>
        public void Load()
        {
            if (!File.Exists(@"Engine\" + Name + @".txt"))
            {
                return;
            }
            try
            {
                using (StreamReader reader = new StreamReader(@"Engine\" + Name + @".txt"))
                {
                    ColorUp = Color.FromArgb(Convert.ToInt32(reader.ReadLine()));
                    ColorDown = Color.FromArgb(Convert.ToInt32(reader.ReadLine()));
                    ColorSqueeze = Color.FromArgb(Convert.ToInt32(reader.ReadLine()));
                    Lenght = Convert.ToInt32(reader.ReadLine());
                    Deviation = Convert.ToDecimal(reader.ReadLine());
                    SqueezePeriod = Convert.ToInt32(reader.ReadLine());
                    PaintOn = Convert.ToBoolean(reader.ReadLine());
                    reader.Close();
                }
            }
            catch (Exception)
            {
                // send to log
                // отправить в лог
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
                using (StreamWriter writer = new StreamWriter(@"Engine\" + Name + @".txt", false))
                {
                    writer.WriteLine(ColorUp.ToArgb());
                    writer.WriteLine(ColorDown.ToArgb());
                    writer.WriteLine(ColorSqueeze.ToArgb());
                    writer.WriteLine(Lenght);
                    writer.WriteLine(Deviation);
                    writer.WriteLine(SqueezePeriod);
                    writer.WriteLine(PaintOn);
                    writer.Close();
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
        /// удалить файл настроек
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
            if (ValuesUp != null)
            {
                ValuesUp.Clear();
                ValuesDown.Clear();
                ValuesBandsWidth.Clear();
                ValuesSqueezeFlag.Clear();
            }
            _myCandles = null;
        }

        /// <summary>
        /// display settings window
        /// показать окно настроек
        /// </summary>
        public void ShowDialog()
        {
            BollingerWithSqueezeUi ui = new BollingerWithSqueezeUi(this);
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
            if (_myCandles == null)
            {
                return;
            }
            ProcessAll(_myCandles);

            if (NeadToReloadEvent != null)
            {
                NeadToReloadEvent(this);
            }
        }

        /// <summary>
        /// to upload new candles
        /// прогрузить новыми свечками
        /// </summary>
        public void Process(List<Candle> candles)
        {
            _myCandles = candles;
            if (ValuesDown != null && ValuesDown.Count + 1 == candles.Count)
            {
                ProcessOne(candles);
            }
            else if (ValuesDown != null && ValuesDown.Count == candles.Count)
            {
                ProcessLast(candles);
            }
            else
            {
                ProcessAll(candles);
            }
        }

        /// <summary>
        /// it's necessary to redraw indicator
        /// необходимо перерисовать индикатор
        /// </summary>
        public event Action<IIndicator> NeadToReloadEvent;

        /// <summary>
        /// candles used to build indicator
        /// свечи по которым строиться индикатор
        /// </summary>
        private List<Candle> _myCandles;

        /// <summary>
        /// load only last candle
        /// прогрузить только последнюю свечку
        /// </summary>
        private void ProcessOne(List<Candle> candles)
        {
            if (candles == null)
            {
                return;
            }

            if (ValuesDown == null)
            {
                ValuesUp = new List<decimal>();
                ValuesDown = new List<decimal>();
                ValuesBandsWidth = new List<decimal>();
                ValuesSqueezeFlag = new List<decimal>();
            }

            decimal[] value = GetValueSimple(candles, candles.Count - 1);
            ValuesUp.Add(value[0]);
            ValuesDown.Add(value[1]);
            ValuesBandsWidth.Add(value[2]);
            decimal squeezeColumnUpperPoint = candles.Last().Low < ValuesDown.Last() ? candles.Last().Low : ValuesDown.Last();
            ValuesSqueezeFlag.Add(IsSqueezeCandle(candles.Count - 1) ? squeezeColumnUpperPoint : 0);
        }

        /// <summary>
        /// to upload from the beginning
        /// прогрузить с самого начала
        /// </summary>
        private void ProcessAll(List<Candle> candles)
        {
            if (candles == null)
            {
                return;
            }
            ValuesUp = new List<decimal>();
            ValuesDown = new List<decimal>();
            ValuesBandsWidth = new List<decimal>();
            ValuesSqueezeFlag = new List<decimal>();

            decimal[][] newValues = new decimal[candles.Count][];

            for (int i = 0; i < candles.Count; i++)
            {
                newValues[i] = GetValueSimple(candles, i);
            }

            for (int i = 0; i < candles.Count; i++)
            {
                ValuesUp.Add(newValues[i][0]);
            }

            for (int i = 0; i < candles.Count; i++)
            {
                ValuesDown.Add(newValues[i][1]);
            }

            for (int i = 0; i < candles.Count; i++)
            {
                ValuesBandsWidth.Add(newValues[i][2]);
            }

            for (int i = 0; i < candles.Count; i++)
            {
                decimal squeezeColumnUpperPoint = candles[i].Low < ValuesDown[i] ? candles[i].Low : ValuesDown[i];
                ValuesSqueezeFlag.Add(IsSqueezeCandle(i) ? squeezeColumnUpperPoint : 0);
            }
        }

        /// <summary>
        /// overload last value
        /// перегрузить последнюю ячейку
        /// </summary>
        private void ProcessLast(List<Candle> candles)
        {
            if (candles == null)
            {
                return;
            }
            decimal[] value = GetValueSimple(candles, candles.Count - 1);
            ValuesUp[ValuesUp.Count - 1] = value[0];
            ValuesDown[ValuesDown.Count - 1] = value[1];
            ValuesBandsWidth[ValuesBandsWidth.Count - 1] = value[2];
            decimal squeezeColumnUpperPoint = candles.Last().Low < ValuesDown.Last() ? candles.Last().Low : ValuesDown.Last();
            ValuesSqueezeFlag[ValuesSqueezeFlag.Count - 1] = IsSqueezeCandle(candles.Count - 1) ? squeezeColumnUpperPoint : 0;
        }

        /// <summary>
        /// take indicator value by index
        /// взять значение индикатора по индексу
        /// </summary>
        private decimal[] GetValueSimple(List<Candle> candles, int index)
        {
            if (index - Lenght <= 0)
            {
                return new decimal[3];
            }

            decimal[] bollinger = new decimal[3];
            // 1 count SMA
            // 1 считаем СМА

            decimal valueSma = 0;

            for (int i = index - Lenght + 1; i < index + 1; i++)
            {
                // running through past periods and collecting values
                // бежим по прошлым периодам и собираем значения
                valueSma += candles[i].Close;
            }

            valueSma = valueSma / Lenght;
            // 2 count average deviation
            // 2 считаем среднее отклонение
            // find an array of deviations from mean
            // находим массив отклонений от средней
            decimal[] valueDev = new decimal[Lenght];
            for (int i = index - Lenght + 1, i2 = 0; i < index + 1; i++, i2++)
            {
                // running through past periods and collecting values
                // бежим по прошлым периодам и собираем значения
                valueDev[i2] = candles[i].Close - valueSma;
            }
            // square this array
            // возводим этот массив в квадрат
            for (int i = 0; i < valueDev.Length; i++)
            {
                valueDev[i] = Convert.ToDecimal(Math.Pow(Convert.ToDouble(valueDev[i]), 2));
            }
            // folding up
            // складываем

            double summ = 0;

            for (int i = 0; i < valueDev.Length; i++)
            {
                summ += Convert.ToDouble(valueDev[i]);
            }
            // divide amount by number of elements in sample( if on n-1,so if n> 30) 
            //делим полученную сумму на количество элементов в выборке (или на n-1, если n>30)
            if (Lenght > 30)
            {
                summ = summ / (Lenght - 1);
            }
            else
            {
                summ = summ / Lenght;
            }
            // calculating root.
            // вычисляем корень

            summ = Math.Sqrt(summ);
            // 3 count bollinger lines
            // 3 считаем линии боллинжера

            bollinger[0] = Math.Round(valueSma + Convert.ToDecimal(summ) * Deviation, 6);
            bollinger[1] = Math.Round(valueSma - Convert.ToDecimal(summ) * Deviation, 6);
            bollinger[2] = valueSma != 0 ? Math.Round((bollinger[0] - bollinger[1]) / valueSma, 6) : 0;

            return bollinger;
        }

        private bool IsSqueezeCandle(int candleIndex)
        {
            bool squeezeFound = false;
            int rangeStartIndex = candleIndex - SqueezePeriod + 1;
            bool squeezeCalculatable = ValuesBandsWidth != null && rangeStartIndex >= 0;
            if (squeezeCalculatable)
            {
                decimal currentWidth = ValuesBandsWidth[candleIndex];
                decimal minWidth = ValuesBandsWidth.GetRange(rangeStartIndex, SqueezePeriod).Min();
                squeezeFound = currentWidth == minWidth;
            }
            return squeezeFound;
        }
    }
}
