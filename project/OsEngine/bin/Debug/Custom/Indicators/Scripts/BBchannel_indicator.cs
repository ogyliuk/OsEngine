using OsEngine.Entity;
using OsEngine.Indicators;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CustomIndicators.Scripts
{

    public class BBchannel_indicator : Aindicator
    {
        private IndicatorParameterInt Lenght; // количество свечей для рассчета


        private IndicatorDataSeries UpChannel;
        private IndicatorDataSeries DownChannel;
        private IndicatorParameterString TwoPoint; // Какую прямую строим по двум точкам UpChannel или DownChannel


        private decimal extr1 = 0; // мин или макс 1
        private int index_extr1 = 0; //индекс точки1

        private decimal extr2 = 0; // мин или макс 2
        private int index_extr2 = 0; //индекс точки2

        private decimal extr_cross = 0; // мин или макс противоположный
        private int index_extr_cross = 0; //индекс extr_cross

        private decimal k; //угол наклона канала
        private decimal b_main;//смещение основного канала
        private decimal b_slave;//смещение второстепенного канала

        private void calc_extr_main(List<Candle> candles, int index)
        {//находим экстремумы

            if (TwoPoint.ValueString == "Max")
            {
                //Находим минимальную точку extr_cross 
                extr_cross = candles[index].Low;
                index_extr_cross = index;

                for (int i = index; i >= index - Lenght.ValueInt; i--)
                {
                    if (extr_cross > candles[i].Low)
                    {
                        extr_cross = candles[i].Low;
                        index_extr_cross = i;
                    }
                }

                //Находим максимальную точку слева от extr_cross 
                extr1 = candles[index_extr_cross].High;
                index_extr1 = index_extr_cross;

                for (int i = index - Lenght.ValueInt; i < index_extr_cross; i++)
                {
                    if (extr1 < candles[i].High)
                    {
                        extr1 = candles[i].High;
                        index_extr1 = i;
                    }
                }

                //Находим максимальную точку справа от extr_cross 
                extr2 = candles[index_extr_cross].High;
                index_extr2 = index_extr_cross;

                for (int i = index_extr_cross; i <= index; i++)
                {
                    if (extr2 < candles[i].High)
                    {
                        extr2 = candles[i].High;
                        index_extr2 = i;
                    }
                }
            }

            if (TwoPoint.ValueString == "Min")
            {
                //Находим максимальную точку extr_cross 
                extr_cross = candles[index].High;
                index_extr_cross = index;

                for (int i = index - 1; i >= index - Lenght.ValueInt; i--)
                {
                    if (extr_cross < candles[i].High)
                    {
                        extr_cross = candles[i].High;
                        index_extr_cross = i;
                    }
                }

                //Находим минимальную точку слева от extr_cross 
                extr1 = candles[index_extr_cross].Low;
                index_extr1 = index_extr_cross;

                for (int i = index - Lenght.ValueInt; i < index_extr_cross; i++)
                {
                    if (extr1 > candles[i].Low)
                    {
                        extr1 = candles[i].Low;
                        index_extr1 = i;
                    }
                }

                //Находим минимальную точку справа от extr_cross 
                extr2 = candles[index_extr_cross].Low;
                index_extr2 = index_extr_cross;

                for (int i = index_extr_cross; i <= index; i++)
                {
                    if (extr2 > candles[i].Low)
                    {
                        extr2 = candles[i].Low;
                        index_extr2 = i;
                    }
                }
            }
        }


        private void calc_koef_main_line()
        {
            decimal zn = index_extr2 - index_extr1;

            k = zn != 0 ? -(extr1 - extr2) / (index_extr2 - index_extr1) : 0;
            b_main = extr1 - k * index_extr1;
        }
        private void calc_koef_slave_line()
        {
            decimal y4 = k * index_extr_cross + b_main;
            if (TwoPoint.ValueString == "Min")
            {

                b_slave = b_main + Math.Abs(y4 - extr_cross);//extr_cross - k * index_extr_cross;
            }
            else
            {
                b_slave = b_main - Math.Abs(y4 - extr_cross);// extr_cross - k * index_extr_cross;
            }
        }
        public override void OnProcess(List<Candle> candles, int index)
        {
            if (index < Lenght.ValueInt)
            {
                return;
            }



            calc_extr_main(candles, index);
            calc_koef_main_line();
            calc_koef_slave_line();

            // UpChannel.Values[index - Lenght.ValueInt] = 0;
            //  DownChannel.Values[index - Lenght.ValueInt] = 0;


            if (TwoPoint.ValueString == "Min")
            {
                // for (int i = index; i > index - Lenght.ValueInt; i--)
                //  {
                UpChannel.Values[index] = k * index + b_slave;
                DownChannel.Values[index] = k * index + b_main;
                //  }
            }
            else
            {
                //decimal zn;
                // for (int i = index; i > index - Lenght.ValueInt; i--)
                // {
                //zn = index_extr2 - index_extr1;
                UpChannel.Values[index] = k * index + b_main;//zn != 0 ? (index_extr2 * extr1 - index_extr1 * extr2 - (extr1 - extr2) * i) / (index_extr2 - index_extr1) : 0; //k * i + b_main;
                DownChannel.Values[index] = k * index + b_slave;
                //  }
            }
        }


        public override void OnStateChange(IndicatorState state)
        {
            if (state == IndicatorState.Configure)
            {
                TwoPoint = CreateParameterStringCollection("Two points channel", "Max", new List<string>() { "Max", "Min" });
                Lenght = CreateParameterInt("Lenght", 25);
                UpChannel = CreateSeries("UpChannel", Color.Green, IndicatorChartPaintType.Line, true);
                // UpChannel.CanReBuildHistoricalValues = true;
                DownChannel = CreateSeries("DownChannel", Color.Red, IndicatorChartPaintType.Line, true);
                // DownChannel.CanReBuildHistoricalValues = true;

            }
        }

    }

}
