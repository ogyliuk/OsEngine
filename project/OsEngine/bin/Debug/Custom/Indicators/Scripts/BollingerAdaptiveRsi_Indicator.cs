using System;
using System.Collections.Generic;
using System.Drawing;
using OsEngine.Entity;
using OsEngine.Indicators;

namespace OsEngine.Robots.FoundBots.Indicators
{
    class BollingerAdaptiveRsi_Indicator : Aindicator
    {
        private IndicatorParameterInt _lengthRsi;
        private IndicatorParameterInt _lengthLookBack;
        private IndicatorParameterBool _mode;

        private IndicatorDataSeries _seriesBollingerUp;
        private IndicatorDataSeries _seriesBollingerDown;
        private IndicatorDataSeries _seriesRsi;
        private IndicatorDataSeries _seriesAdaptiveRsi;

        private Aindicator _rsi;
        private Aindicator _al;

        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                _lengthRsi = CreateParameterInt("Rsi period", 14);
                _lengthLookBack = CreateParameterInt("Look Back", 5);
                _mode = CreateParameterBool("Use adaptive rsi", true);

                _seriesBollingerUp = CreateSeries("Bollinger Up", Color.OrangeRed, IndicatorChartPaintType.Point, true);
                _seriesBollingerDown = CreateSeries("Bollinger Down", Color.CornflowerBlue, IndicatorChartPaintType.Point, true);
                _seriesRsi = CreateSeries("Rsi", Color.DeepSkyBlue, IndicatorChartPaintType.Line, true);
                _seriesAdaptiveRsi = CreateSeries("Adaptive Rsi", Color.WhiteSmoke, IndicatorChartPaintType.Line, true);

                _al = IndicatorsFactory.CreateIndicatorByName("AdaptiveLookBack", Name + "AdaptiveLookBack", false);
                ((IndicatorParameterInt)_al.Parameters[0]).Bind(_lengthLookBack);
                ProcessIndicator("Look Back", _al);

                _rsi = IndicatorsFactory.CreateIndicatorByName("RSI", Name + "RSI", false);
                ((IndicatorParameterInt)_rsi.Parameters[0]).Bind(_lengthRsi);
                ProcessIndicator("Rsi", _rsi);
            }
            else
            {

            }
        }

        public override void OnProcess(List<Candle> candles, int index)
        {
            if (_al.DataSeries[0].Last == 0)
            {
                return;
            }

            if (index < _lengthRsi.ValueInt)
            {
                return;
            }

            if (_al.DataSeries[0].Values[index] == 0)
            {
                return;
            }

            _seriesRsi.Values[index] = _rsi.DataSeries[0].Values[index];
            _seriesAdaptiveRsi.Values[index] = GetRsi(candles, index, Convert.ToInt32(_al.DataSeries[0].Values[index]));

            List<decimal> values = _rsi.DataSeries[0].Values;

            if (_mode.ValueBool)
            {
                values = _seriesAdaptiveRsi.Values;
            }

            _seriesBollingerUp.Values[index] = GetUpBollinger(index, values);
            _seriesBollingerDown.Values[index] = GetDownBollinger(index, values);
        }

        private decimal GetRsi(List<Candle> candles, int index, int lenght)
        {
            if (index - lenght - 1 <= 0)
            {
                return 0;
            }

            int startIndex = 1;

            if (index > 150)
            {
                startIndex = index - 150;
            }

            decimal[] priceChangeHigh = new decimal[candles.Count];
            decimal[] priceChangeLow = new decimal[candles.Count];

            decimal[] priceChangeHighAverage = new decimal[candles.Count];
            decimal[] priceChangeLowAverage = new decimal[candles.Count];

            for (int i = startIndex; i < candles.Count; i++)
            {
                if (candles[i].Close - candles[i - 1].Close > 0)
                {
                    priceChangeHigh[i] = candles[i].Close - candles[i - 1].Close;
                    priceChangeLow[i] = 0;
                }
                else
                {
                    priceChangeLow[i] = candles[i - 1].Close - candles[i].Close;
                    priceChangeHigh[i] = 0;
                }

                MovingAverageHard(priceChangeHigh, priceChangeHighAverage, lenght, i);
                MovingAverageHard(priceChangeLow, priceChangeLowAverage, lenght, i);
            }

            decimal averageHigh = priceChangeHighAverage[index];
            decimal averageLow = priceChangeLowAverage[index];

            decimal rsi;

            if (averageHigh != 0 &&
                averageLow != 0)
            {
                rsi = 100 * (1 - averageLow / (averageLow + averageHigh));
            }
            else
            {
                rsi = 0;
            }

            return Math.Round(rsi, 4);
        }

        private void MovingAverageHard(decimal[] valuesSeries, decimal[] moving, int length, int index)
        {
            if (index == length)
            { // это первое значение. Рассчитываем как простую машку

                decimal lastMoving = 0;

                for (int i = index; i > index - 1 - length; i--)
                {
                    lastMoving += valuesSeries[i];
                }
                lastMoving = lastMoving / length;

                moving[index] = lastMoving;
            }
            else if (index > length)
            {
                // decimal a = 2.0m / (length * 2 - 0.15m);

                decimal a = Math.Round(2.0m / (length * 2), 4);

                decimal lastValueMoving = moving[index - 1];

                decimal lastValueSeries = Math.Round(valuesSeries[index], 4);

                decimal nowValueMoving;

                //if (lastValueSeries != 0)
                // {
                nowValueMoving = Math.Round(lastValueMoving + a * (lastValueSeries - lastValueMoving), 4);
                // }
                // else
                // {
                //     nowValueMoving = lastValueMoving;
                // }

                moving[index] = nowValueMoving;
            }
        }

        private decimal GetUpBollinger(int index, List<decimal> values)
        {
            int lenght = 100;
            int deviation = 2;

            if (index - lenght - 1 <= 0)
            {
                return 0;
            }

            // 1 считаем СМА

            double valueSma = 0;

            for (int i = index - lenght + 1; i < index + 1; i++)
            {
                // бежим по прошлым периодам и собираем значения
                valueSma += Convert.ToDouble(values[i]);
            }

            valueSma = valueSma / lenght;

            // 2 считаем среднее отклонение

            // находим массив отклонений от средней
            double[] valueDev = new double[lenght];
            for (int i = index - lenght + 1, i2 = 0; i < index + 1; i++, i2++)
            {
                // бежим по прошлым периодам и собираем значения
                valueDev[i2] = Convert.ToDouble(values[i]) - valueSma;
            }

            // возводим этот массив в квадрат
            for (int i = 0; i < valueDev.Length; i++)
            {
                valueDev[i] = Math.Pow(Convert.ToDouble(valueDev[i]), 2);
            }

            // складываем

            double summ = 0;

            for (int i = 0; i < valueDev.Length; i++)
            {
                summ += Convert.ToDouble(valueDev[i]);
            }

            //делим полученную сумму на количество элементов в выборке (или на n-1, если n>30)
            if (lenght > 30)
            {
                summ = summ / (lenght - 1);
            }
            else
            {
                summ = summ / lenght;
            }
            // вычисляем корень

            summ = Math.Sqrt(summ);

            // 3 считаем линии боллинжера

            double result = valueSma + summ * deviation;

            return Convert.ToDecimal(Math.Round(result, 4));
        }

        private decimal GetDownBollinger(int index, List<decimal> values)
        {
            int lenght = 100;
            int deviation = 2;

            if (index - lenght - 1 <= 0)
            {
                return 0;
            }

            // 1 считаем СМА

            double valueSma = 0;

            for (int i = index - lenght + 1; i < index + 1; i++)
            {
                // бежим по прошлым периодам и собираем значения
                valueSma += Convert.ToDouble(values[i]);
            }

            valueSma = valueSma / lenght;

            // 2 считаем среднее отклонение

            // находим массив отклонений от средней
            double[] valueDev = new double[lenght];
            for (int i = index - lenght + 1, i2 = 0; i < index + 1; i++, i2++)
            {
                // бежим по прошлым периодам и собираем значения
                valueDev[i2] = Convert.ToDouble(values[i]) - valueSma;
            }

            // возводим этот массив в квадрат
            for (int i = 0; i < valueDev.Length; i++)
            {
                valueDev[i] = Math.Pow(Convert.ToDouble(valueDev[i]), 2);
            }

            // складываем

            double summ = 0;

            for (int i = 0; i < valueDev.Length; i++)
            {
                summ += Convert.ToDouble(valueDev[i]);
            }

            //делим полученную сумму на количество элементов в выборке (или на n-1, если n>30)
            if (lenght > 30)
            {
                summ = summ / (lenght - 1);
            }
            else
            {
                summ = summ / lenght;
            }
            // вычисляем корень

            summ = Math.Sqrt(summ);

            // 3 считаем линии боллинжера

            double result = valueSma - summ * deviation;

            return Convert.ToDecimal(Math.Round(result, 4));
        }

    }
}