using OsEngine.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OsEngine.Indicators;

//https://ru.tradingview.com/script/MYt1DbuR-ALMA-Channel/ source code


public class ALMA_Chanel_Indicator : Aindicator
{

    private IndicatorParameterInt _sizeWindows;
    private IndicatorParameterInt _sigma;
    private IndicatorParameterDecimal _offset;

    private IndicatorParameterInt _periodSma;

    private IndicatorParameterString _priceCaclUpSeriesAlma;
    private IndicatorParameterString _priceCaclDownSeriesAlma;

    IndicatorDataSeries _AlmaUpSeries;
    IndicatorDataSeries _AlmaDownSeries;
    IndicatorDataSeries _SmaSeries;

    private Aindicator _Sma;

    int sizeWindows;
    decimal sigma;
    decimal offset;
    string candleCloseHigh;
    string candleCloseLow;
    decimal AlmaUp;
    decimal AlmaDown;

    public override void OnStateChange(IndicatorState state)
    {
        if (state == IndicatorState.Configure)
        {
            _sizeWindows = CreateParameterInt("Size windows", 30);
            _sigma = CreateParameterInt("Sigma", 6);
            _offset = CreateParameterDecimal("Offset", 0.85m);
            _periodSma = CreateParameterInt("Length Sma", 200);
            _priceCaclUpSeriesAlma = CreateParameterStringCollection("Price calc Up series ALMA", "High", Entity.CandlePointsArray);
            _priceCaclDownSeriesAlma = CreateParameterStringCollection("Price cacl Down series ALMA", "Low", Entity.CandlePointsArray);

            candleCloseHigh = _priceCaclUpSeriesAlma.ValueString;
            candleCloseLow = _priceCaclDownSeriesAlma.ValueString;           

            _AlmaUpSeries = CreateSeries("Alma Up", System.Drawing.Color.Green, IndicatorChartPaintType.Line, true);
            _AlmaDownSeries = CreateSeries("Alma Down", System.Drawing.Color.Red, IndicatorChartPaintType.Line, true);

            _SmaSeries = CreateSeries("SmaSeries", System.Drawing.Color.DarkGray, IndicatorChartPaintType.Point, true);

            _Sma = IndicatorsFactory.CreateIndicatorByName("Sma", Name + "SMA", false);
            ((IndicatorParameterInt)_Sma.Parameters[0]).Bind(_periodSma);
            ProcessIndicator("SMA", _Sma);
        }
    }

    public override void OnProcess(List<Candle> candles, int index)
    {
        sizeWindows = _sizeWindows.ValueInt != 0 ? _sizeWindows.ValueInt : 0;
        sigma = _sigma.ValueInt != 0 ? _sigma.ValueInt : 1;
        offset = _offset.ValueDecimal;

        candleCloseHigh = _priceCaclUpSeriesAlma.ValueString;
        candleCloseLow = _priceCaclDownSeriesAlma.ValueString;

        if (index <= _sizeWindows.ValueInt + 10 || index <= _sigma.ValueInt + 10) return;

        AlmaUp = GetCalcAlma(candles, index, candleCloseHigh, CalcDeviation(candles, index));
        AlmaDown = GetCalcAlma(candles, index, candleCloseLow, CalcDeviation(candles, index));

        if (index < _periodSma.ValueInt) return;

        _SmaSeries.Values[index] = _Sma.DataSeries[0].Values[index];

        if(AlmaUp != 0)
        {
            _AlmaUpSeries.Values[index] = AlmaUp;
        }
        else
        {
            _AlmaUpSeries.Values[index] = _AlmaUpSeries.Values[index-1];
        }
        
        if(AlmaDown != 0)
        {
            _AlmaDownSeries.Values[index] = AlmaDown;
        }
        else
        {
            _AlmaDownSeries.Values[index] = _AlmaDownSeries.Values[index - 1];
        }
    }

    public decimal GetCalcAlma(List<Candle> candles, int index, string _candlePriceClose, decimal deviation)
    {
        if (deviation != 0 && _candlePriceClose == "Low") deviation = -deviation;

        decimal m = offset * (sizeWindows - 1.0m);
        decimal s = sizeWindows / sigma;

        decimal weight = 0;
        decimal _numerator = 0;
        decimal _denominator = 0;

        for (int i = 0; i < sizeWindows; i++)
        {
            weight = (-((i - m) * (i - m)) / (2 * s * s));
            weight = (decimal)Math.Exp((double)weight);
            _numerator = _numerator + (weight * (candles[index - sizeWindows + 1 + i].GetPoint(_candlePriceClose) + deviation));
            _denominator = _denominator + weight;
        }

        if(_denominator == 0)
        {
            return 0;
        }

        return _numerator / _denominator;
    }

    decimal CalcDeviation(List<Candle> candles, int index)
    {
        int sizeWindows = _sizeWindows.ValueInt;

        decimal smaOfDeviation = 0;

        for (int i = index - sizeWindows; i < index; i++)
        {
            smaOfDeviation += candles[i].High - candles[i].Low;
        }
        return smaOfDeviation / sizeWindows;    
    }
       
}

