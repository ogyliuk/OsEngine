using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;


[Bot("BreakLinearRegressionChannel2")]
public class BreakLinearRegressionChannel2 : BotPanel
{
    BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;
    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _sma_long;
    StrategyParameterInt _periodSma_long;

    public Aindicator _sma_short;
    StrategyParameterInt _periodSma_short;

    public Aindicator _lrc_long;
    StrategyParameterDecimal _upChannel_dev_long;
    StrategyParameterDecimal _downChannel_dev_long;
    StrategyParameterInt _lenght_long;

    public Aindicator _lrc_short;
    StrategyParameterDecimal _upChannel_dev_short;
    StrategyParameterDecimal _downChannel_dev_short;
    StrategyParameterInt _lenght_short;    
    /// <summary>
    /// мувинг
    /// </summary>
    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;
   


    public BreakLinearRegressionChannel2(string name, StartProgram startProgram) : base(name, startProgram)
    {
        TabCreate(BotTabType.Simple);
        _tab = TabsSimple[0];

        Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort", "OnlyClosePosition" }, "Base");
        VolumeRegime = CreateParameter("Volume type", "Number of contracts", new[] { "Number of contracts", "Contract currency", "% of the total portfolio" }, "Base");
        VolumeDecimals = CreateParameter("Number of Digits after the decimal point in the volume", 2, 1, 50, 4, "Base");
        VolumeOnPosition = CreateParameter("Volume", 10, 1.0m, 50, 4, "Base");

        Slippage = CreateParameter("Slippage %", 0m, 0, 20, 1, "Base");

        TimeStart = CreateParameterTimeOfDay("Start Trade Time", 0, 0, 0, 0, "Base");
        TimeEnd = CreateParameterTimeOfDay("End Trade Time", 24, 0, 0, 0, "Base");

        _periodSma_long = CreateParameter("SMA period Long", 100, 50, 400, 10, "Robot parameters");
        _periodSma_short = CreateParameter("SMA period Short", 100, 50, 400, 10, "Robot parameters");

        _lenght_long = CreateParameter("Lenght LR Long", 100, 50, 200, 20, "Robot parameters");
        _upChannel_dev_long = CreateParameter("Up channel deviation Long", 2, 2, 4, .5m, "Robot parameters");
        _downChannel_dev_long = CreateParameter("Down channel deviation Long", 2, 2, 4, .5m, "Robot parameters");

        _lenght_short = CreateParameter("Lenght LR Short", 100, 50, 200, 20, "Robot parameters");
        _upChannel_dev_short = CreateParameter("Up channel deviation Short", 2, 2, 4, .5m, "Robot parameters");
        _downChannel_dev_short = CreateParameter("Down channel deviation Short", 2, 2, 4, .5m, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length Filter", 100, 10, 500, 1, "Filters");
        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");

        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");
        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _lrc_long = IndicatorsFactory.CreateIndicatorByName(nameClass: "LRC_indicator", name: name + "LRC_LONG", canDelete: false);
        _lrc_long = (Aindicator)_tab.CreateCandleIndicator(_lrc_long, nameArea: "Prime");
        _lrc_long.ParametersDigit[0].Value = _lenght_long.ValueInt;
        _lrc_long.ParametersDigit[1].Value = _upChannel_dev_long.ValueDecimal;
        _lrc_long.ParametersDigit[2].Value = _downChannel_dev_long.ValueDecimal;
        _lrc_long.Save();

        _lrc_short = IndicatorsFactory.CreateIndicatorByName(nameClass: "LRC_indicator", name: name + "LRC_SHORT", canDelete: false);
        _lrc_short = (Aindicator)_tab.CreateCandleIndicator(_lrc_short, nameArea: "Prime");
        _lrc_short.ParametersDigit[0].Value = _lenght_short.ValueInt;
        _lrc_short.ParametersDigit[1].Value = _upChannel_dev_short.ValueDecimal;
        _lrc_short.ParametersDigit[2].Value = _downChannel_dev_short.ValueDecimal;
        _lrc_short.Save();

        _sma_long = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Long", canDelete: false);
        _sma_long = (Aindicator)_tab.CreateCandleIndicator(_sma_long, nameArea: "Prime");
        _sma_long.ParametersDigit[0].Value = _periodSma_long.ValueInt;
        _sma_long.Save();

        _sma_short = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Short", canDelete: false);
        _sma_short = (Aindicator)_tab.CreateCandleIndicator(_sma_short, nameArea: "Prime");
        _sma_short.ParametersDigit[0].Value = _periodSma_short.ValueInt;
        _sma_short.Save();

        StopOrActivateIndicators();
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        ParametrsChangeByUser += LRegBot_ParametrsChangeByUser;
        LRegBot_ParametrsChangeByUser();
    }

    private void StopOrActivateIndicators()
    {

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
        


        if (_tab.CandlesAll == null)
        {
            return;
        }

      
        if (_lrc_long.DataSeries[0] == null || _lrc_long.DataSeries[2] == null ||_lenght_long.ValueInt >= candles.Count || _periodSma_long.ValueInt >= candles.Count)
        {
            return;
        }
        if (_lrc_short.DataSeries[0] == null && _lrc_short.DataSeries[2] == null || _lenght_short.ValueInt >= candles.Count || _periodSma_short.ValueInt >= candles.Count)
        {
            return;
        }
      

        decimal lr_up_long = _lrc_long.DataSeries[0].Last;
        decimal lr_down_long = _lrc_long.DataSeries[2].Last;
        decimal lastMaFilter_long = _sma_long.DataSeries[0].Last;       

        decimal lr_up_short = _lrc_short.DataSeries[0].Last;
        decimal lr_down_short = _lrc_short.DataSeries[2].Last;
        decimal lastMaFilter_short = _sma_short.DataSeries[0].Last;

        List<Position> positions = _tab.PositionsOpenAll;


        decimal _lastPrice = candles[candles.Count - 1].Close;
        decimal _slippage = 0;
        if (positions.Count == 0)
        {// enter logic




            if (lr_up_long > lastMaFilter_long
                && _lastPrice <= lr_up_long)
            {
                _slippage = Slippage.ValueDecimal * lr_up_long / 100;
                if (!BuySignalIsFiltered(candles))
                    _tab.BuyAtStop(GetVolume(), lr_up_long + _slippage, lr_up_long, StopActivateType.HigherOrEqual, 1);
            }
            if (lr_down_short < lastMaFilter_short
                && _lastPrice >= lr_down_short)
            {
                _slippage = Slippage.ValueDecimal * lr_down_short / 100;
                if (!SellSignalIsFiltered(candles))
                    _tab.SellAtStop(GetVolume(), lr_down_short - _slippage, lr_down_short, StopActivateType.LowerOrEqyal, 1);
            }
        }

            if (_lastPrice < lastMaFilter_long || BuySignalIsFiltered(candles))
            {
                _tab.BuyAtStopCancel();
            }
            if (_lastPrice > lastMaFilter_short || SellSignalIsFiltered(candles))
            {
                _tab.SellAtStopCancel();
            }
        
       else if (positions.Count != 0)
        {//exit logic
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State != PositionStateType.Open)
                {
                    continue;
                }
                if (positions[i].State == PositionStateType.ClosingFail)
                {
                    _tab.CloseAtMarket(positions[i], positions[i].OpenVolume);
                }
                decimal stop_level = 0;

                if (positions[i].Direction == Side.Buy)
                {// logic to close long position
                    stop_level = lr_down_long > lastMaFilter_long ? lr_down_long : lastMaFilter_long;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage);
                }
                else if (positions[i].Direction == Side.Sell)
                {//logic to close short position
                    stop_level = lr_up_short < lastMaFilter_short ? lr_up_short : lastMaFilter_short;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level + _slippage);
                }
            }
        }
    }
    #region filters
    // логика
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
    #endregion

    private void LRegBot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_lrc_long.ParametersDigit[0].Value != _lenght_long.ValueInt ||
        _lrc_long.ParametersDigit[1].Value != _upChannel_dev_long.ValueDecimal ||
        _lrc_long.ParametersDigit[2].Value != _downChannel_dev_long.ValueDecimal)
        {
            _lrc_long.ParametersDigit[0].Value = _lenght_long.ValueInt;
            _lrc_long.ParametersDigit[1].Value = _upChannel_dev_long.ValueDecimal;
            _lrc_long.ParametersDigit[2].Value = _downChannel_dev_long.ValueDecimal;
            _lrc_long.Reload();
            _lrc_long.Save();
        }

        if (_lrc_short.ParametersDigit[0].Value != _lenght_short.ValueInt ||
            _lrc_short.ParametersDigit[1].Value != _upChannel_dev_short.ValueDecimal ||
            _lrc_short.ParametersDigit[2].Value != _downChannel_dev_short.ValueDecimal)
        {
            _lrc_short.ParametersDigit[0].Value = _lenght_short.ValueInt;
            _lrc_short.ParametersDigit[1].Value = _upChannel_dev_short.ValueDecimal;
            _lrc_short.ParametersDigit[2].Value = _downChannel_dev_short.ValueDecimal;
            _lrc_short.Reload();
            _lrc_short.Save();
        }
        if (_sma_long.ParametersDigit[0].Value != _periodSma_long.ValueInt)
        {
            _sma_long.ParametersDigit[0].Value = _periodSma_long.ValueInt;
            _sma_long.Reload();
            _sma_long.Save();
        }
        if (_sma_short.ParametersDigit[0].Value != _periodSma_short.ValueInt)
        {
            _sma_short.ParametersDigit[0].Value = _periodSma_short.ValueInt;
            _sma_short.Reload();
            _sma_short.Save();
        }

        if (_smaFilter.DataSeries.Count == 0)
        {
            return;
        }

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

    public override string GetNameStrategyType()
    {
        return "BreakLinearRegressionChannel2";
    }

    public override void ShowIndividualSettingsDialog()
    {

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

