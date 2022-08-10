using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Drawing;
using System.Collections.Generic;
using OsEngine.Charts.CandleChart.Elements;

[Bot("ImpulseSmaLR")]
public class ImpulseSmaLR : BotPanel
{
    BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    private Aindicator _LinearRegression;
    StrategyParameterDecimal _upChannel_dev;
    StrategyParameterDecimal _downChannel_dev;
    StrategyParameterInt _lenghtLR;

    public StrategyParameterBool UseRsi;

    Aindicator _Rsi;
    StrategyParameterInt _PeriodRsi;
    StrategyParameterDecimal UpLineValue;
    StrategyParameterDecimal DownLineValue;

    public LineHorisontal Upline;
    public LineHorisontal Downline;

    StrategyParameterString _regimeTrendFilter;

    public Aindicator _sma;
    StrategyParameterInt _periodSma;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public ImpulseSmaLR(string name, StartProgram startProgram) : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];

        Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
        VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
        VolumeDecimals = CreateParameter("Decimals Volume", 2, 1, 50, 4, "Base");
        VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");
        Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");

        TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
        TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

        _regimeTrendFilter = CreateParameter("Regime trend filter", "candle", new[] { "candle", "CenterLRC" }, "Base");

        _periodSma = CreateParameter("SMA period", 100, 50, 400, 10, "Robot parameters");

        _lenghtLR = CreateParameter("Lenght LR", 100, 50, 200, 20, "Robot parameters");
        _upChannel_dev = CreateParameter("Up channel deviation LR", 2, 1, 100, 5m, "Robot parameters");
        _downChannel_dev = CreateParameter("Down channel deviation LR", 2, 1, 100, 5m, "Robot parameters");
        UseRsi = CreateParameter("Use Rsi", false, "Robot parameters");
        _PeriodRsi = CreateParameter("Period Rsi indicator", 2, 1, 20, 1, "Robot parameters");
        UpLineValue = CreateParameter("Up Line Value", 65, 60.0m, 90, 0.5m, "Robot parameters");
        DownLineValue = CreateParameter("Down Line Value", 35, 10.0m, 40, 0.5m, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");

        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _LinearRegression = IndicatorsFactory.CreateIndicatorByName("LinearRegressionChannelFast_Indicator", name + "LinearRegressionChannel", false);
        _LinearRegression = (Aindicator)_tab.CreateCandleIndicator(_LinearRegression, "Prime");
        _LinearRegression.ParametersDigit[0].Value = _lenghtLR.ValueInt;
        _LinearRegression.ParametersDigit[1].Value = _upChannel_dev.ValueDecimal;
        _LinearRegression.ParametersDigit[2].Value = _downChannel_dev.ValueDecimal;
        _LinearRegression.Save();

        _Rsi = IndicatorsFactory.CreateIndicatorByName(nameClass: "RSI", name: name + "Rsi", canDelete: false);
        _Rsi = (Aindicator)_tab.CreateCandleIndicator(_Rsi, nameArea: "RsiArea");
        _Rsi.DataSeries[0].Color = System.Drawing.Color.Azure;

        Upline = new LineHorisontal("upline", "RsiArea", false)
        {
            Color = Color.Green,
            Value = 0,
        };

        Downline = new LineHorisontal("downline", "RsiArea", false)
        {
            Color = Color.Yellow,
            Value = 0
        };

        _Rsi.ParametersDigit[0].Value = _PeriodRsi.ValueInt;
        _Rsi.Save();

        Upline.Value = UpLineValue.ValueDecimal;
        Downline.Value = DownLineValue.ValueDecimal;

        _sma = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma", canDelete: false);
        _sma = (Aindicator)_tab.CreateCandleIndicator(_sma, nameArea: "Prime");
        _sma.ParametersDigit[0].Value = _periodSma.ValueInt;
        _sma.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += LRegBot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        LRegBot_ParametrsChangeByUser();
    }

    private void LRegBot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_LinearRegression.ParametersDigit[0].Value != _lenghtLR.ValueInt ||
        _LinearRegression.ParametersDigit[1].Value != _upChannel_dev.ValueDecimal ||
        _LinearRegression.ParametersDigit[2].Value != _downChannel_dev.ValueDecimal)
        {
            _LinearRegression.ParametersDigit[0].Value = _lenghtLR.ValueInt;
            _LinearRegression.ParametersDigit[1].Value = _upChannel_dev.ValueDecimal;
            _LinearRegression.ParametersDigit[2].Value = _downChannel_dev.ValueDecimal;
            _LinearRegression.Save();
            _LinearRegression.Reload();
        }

        if (_sma.ParametersDigit[0].Value != _periodSma.ValueInt)
        {
            _sma.ParametersDigit[0].Value = _periodSma.ValueInt;
            _sma.Reload();
            _sma.Save();
        }

        if (_Rsi.ParametersDigit[0].Value != _PeriodRsi.ValueInt)
        {
            _Rsi.ParametersDigit[0].Value = _PeriodRsi.ValueInt;
            _Rsi.Reload();
            _Rsi.Save();
        }

        Upline.Value = UpLineValue.ValueDecimal;
        Upline.Refresh();
        Downline.Value = DownLineValue.ValueDecimal;
        Downline.Refresh();

        if (_smaFilter.ParametersDigit[0].Value != SmaLengthFilter.ValueInt)
        {
            _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
            _smaFilter.Reload();
            _smaFilter.Save();
        }


        if (_smaFilter.DataSeries != null && _smaFilter.DataSeries.Count > 0)
        {
            if (!SmaPositionFilterIsOn.ValueBool)
            {
                _smaFilter.DataSeries[0].IsPaint = false;
            }
            else
            {
                _smaFilter.DataSeries[0].IsPaint = true;
            }
        }

    }

    private void StopOrActivateIndicators()
    {
        if (UseRsi.ValueBool
            != _Rsi.IsOn)
        {
            _Rsi.IsOn = UseRsi.ValueBool;
            _Rsi.Reload();
        }

        if (SmaPositionFilterIsOn.ValueBool
           != _smaFilter.IsOn && SmaSlopeFilterIsOn.ValueBool
           != _smaFilter.IsOn)
        {
            _smaFilter.IsOn = SmaPositionFilterIsOn.ValueBool;
            _smaFilter.Reload();

            _smaFilter.IsOn = SmaSlopeFilterIsOn.ValueBool;
            _smaFilter.Reload();
        }
    }

    public override string GetNameStrategyType()
    {
        return "ImpulseSmaLR";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (Regime.ValueString == "Off")
        {
            return;
        }

        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        if (_Rsi.DataSeries[0].Values == null)
        {
            return;
        }

        if (_Rsi.DataSeries[0].Values.Count < _Rsi.ParametersDigit[0].Value + 5)
        {
            return;

        }

        //if (candles[candles.Count - 1].TimeStart.Hour == 7) // для ММВБ в первый час не торгуем
        //{
        //    _tab.BuyAtStopCancel();
        //    _tab.SellAtStopCancel();
        //    return;
        //}

        if (_tab.CandlesAll == null)
        {
            return;
        }
        if (_lenghtLR.ValueInt >= candles.Count)
        {
            return;
        }

        if (UseRsi.ValueBool)
        {
            _tab.SetChartElement(Upline);
            _tab.SetChartElement(Downline);

            Upline.TimeEnd = candles[candles.Count - 1].TimeStart;
            Upline.Refresh();

            Downline.TimeEnd = candles[candles.Count - 1].TimeStart;
            Downline.Refresh();
        }

        List<Position> positions = _tab.PositionsOpenAll;
        decimal lastCandle = candles[candles.Count - 1].Close;
        decimal lr_up = _LinearRegression.DataSeries[0].Last;
        decimal flag = lastCandle;
        decimal lr_down = _LinearRegression.DataSeries[2].Last;
        decimal _slippage = 0;

        if (_regimeTrendFilter.ValueString == "CenterLRC")
        {
            flag = _LinearRegression.DataSeries[1].Last;
        }
        decimal lastMaFilter = _sma.DataSeries[0].Last;
        decimal lastRsi = _Rsi.DataSeries[0].Values[_Rsi.DataSeries[0].Values.Count - 1];

        if (positions.Count == 0)
        {// enter logic
            if (flag > lastMaFilter) //&& lastCandle > lastMaFilter)
            {
                _slippage = Slippage.ValueDecimal * lr_up / 100;

                if (UseRsi.ValueBool)
                {
                    if (lastRsi > Upline.Value)
                    {
                        if (!BuySignalIsFiltered(candles))
                            _tab.BuyAtStop(GetVolume(), lr_up + _slippage, lr_up, StopActivateType.HigherOrEqual, 1);
                    }
                }
                else
                {
                    if (!BuySignalIsFiltered(candles))
                        _tab.BuyAtStop(GetVolume(), lr_up + _slippage, lr_up, StopActivateType.HigherOrEqual, 1);
                }
            }
            if (flag < lastMaFilter) //&& lastCandle < lastMaFilter)
            {
                _slippage = Slippage.ValueDecimal * lr_down / 100;

                if (UseRsi.ValueBool)
                {
                    if (lastRsi < Downline.Value)
                    {
                        if (!SellSignalIsFiltered(candles))
                            _tab.SellAtStop(GetVolume(), lr_down - _slippage, lr_down, StopActivateType.LowerOrEqyal, 1);
                    }
                }
                else
                {
                    if (!SellSignalIsFiltered(candles))
                        _tab.SellAtStop(GetVolume(), lr_down - _slippage, lr_down, StopActivateType.LowerOrEqyal, 1);
                }
            }
            if (flag < lastMaFilter || BuySignalIsFiltered(candles))
            {
                _tab.BuyAtStopCancel();
            }
            if (flag > lastMaFilter || SellSignalIsFiltered(candles))
            {
                _tab.SellAtStopCancel();
            }

        }
        else
        {//exit logic
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State != PositionStateType.Open)
                {
                    continue;
                }
                decimal stop_level = 0;

                if (positions[i].Direction == Side.Buy)
                {// logic to close long position
                    stop_level = lr_down > lastMaFilter ? lr_down : lastMaFilter;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtStop(positions[i], stop_level, stop_level - _slippage);
                    //_tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage.ValueInt * _tab.Securiti.PriceStep);
                }
                else if (positions[i].Direction == Side.Sell)
                {//logic to close short position
                    stop_level = lr_up < lastMaFilter ? lr_up : lastMaFilter;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtStop(positions[i], stop_level, stop_level + _slippage);
                    // _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level + _slippage.ValueInt * _tab.Securiti.PriceStep);
                }

            }
        }
    }

    private void CancelStopsAndProfits()
    {
        List<Position> positions = _tab.PositionsOpenAll;

        for (int i = 0; i < positions.Count; i++)
        {
            Position pos = positions[i];

            pos.StopOrderIsActiv = false;
            pos.ProfitOrderIsActiv = false;
        }

        _tab.BuyAtStopCancel();
        _tab.SellAtStopCancel();
    }

    private bool BuySignalIsFiltered(List<Candle> candles)
    {

        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal lastSma = _smaFilter.DataSeries[0].Last;
        // фильтр для покупок
        if (Regime.ValueString == "Off" ||
            Regime.ValueString == "OnlyShort" ||
            Regime.ValueString == "OnlyClosePosition")
        {
            return true;
            //если режим работы робота не соответсвует направлению позиции
        }

        if (SmaPositionFilterIsOn.ValueBool)
        {
            if (_smaFilter.DataSeries[0].Last > lastPrice)
            {
                return true;
            }
            // если цена ниже последней сма - возвращаем на верх true
        }
        if (SmaSlopeFilterIsOn.ValueBool)
        {
            decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];

            if (lastSma < prevSma)
            {
                return true;
            }
            // если последняя сма ниже предыдущей сма - возвращаем на верх true
        }

        return false;
    }

    private bool SellSignalIsFiltered(List<Candle> candles)
    {
        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal lastSma = _smaFilter.DataSeries[0].Last;
        // фильтр для продаж
        if (Regime.ValueString == "Off" ||
            Regime.ValueString == "OnlyLong" ||
            Regime.ValueString == "OnlyClosePosition")
        {
            return true;
            //если режим работы робота не соответсвует направлению позиции
        }

        if (SmaPositionFilterIsOn.ValueBool)
        {
            if (lastSma < lastPrice)
            {
                return true;
            }
            // если цена выше последней сма - возвращаем на верх true
        }
        if (SmaSlopeFilterIsOn.ValueBool)
        {
            decimal prevSma = _smaFilter.DataSeries[0].Values[_smaFilter.DataSeries[0].Values.Count - 2];

            if (lastSma > prevSma)
            {
                return true;
            }
            // если последняя сма выше предыдущей сма - возвращаем на верх true
        }

        return false;
    }

    private decimal GetVolume()
    {
        decimal volume = VolumeOnPosition.ValueDecimal;

        if (VolumeRegime.ValueString == "Contract currency") // "Валюта контракта"
        {
            decimal contractPrice = TabsSimple[0].PriceBestAsk;
            volume = Math.Round(VolumeOnPosition.ValueDecimal / contractPrice, VolumeDecimals.ValueInt);
            return volume;
        }
        else if (VolumeRegime.ValueString == "Number of contracts")
        {
            return volume;
        }
        else //if (VolumeRegime.ValueString == "% of the total portfolio")
        {
            return Math.Round(_tab.Portfolio.ValueCurrent * (volume / 100) / _tab.PriceBestAsk / _tab.Securiti.Lot, VolumeDecimals.ValueInt);
        }
    }
}
