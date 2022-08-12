using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;


[Bot("BreakBollinger")]
public class BreakBollinger : BotPanel
{
    BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _bol;
    public StrategyParameterInt _bolPeriod;
    public StrategyParameterDecimal _bolDev;

    public Aindicator _ma;
    StrategyParameterInt _maPeriod;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public BreakBollinger(string name, StartProgram startProgram) : base(name, startProgram)
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

        _maPeriod = CreateParameter("Moving period", 14, 50, 100, 1, "Robot parameters");
        _bolPeriod = CreateParameter("Bollinger period", 14, 50, 100, 1, "Robot parameters");
        _bolDev = CreateParameter("Bollinger deviation", 2.15m, 1, 4, 0.2m, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");

        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _bol = IndicatorsFactory.CreateIndicatorByName(nameClass: "Bollinger", name: name + "Bollinger", canDelete: false);
        _bol = (Aindicator)_tab.CreateCandleIndicator(_bol, nameArea: "Prime");
        _bol.ParametersDigit[0].Value = _bolPeriod.ValueInt;
        _bol.ParametersDigit[1].Value = _bolDev.ValueDecimal;
        _bol.Save();

        _ma = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "SMA", canDelete: false);
        _ma = (Aindicator)_tab.CreateCandleIndicator(_ma, nameArea: "Prime");
        _ma.ParametersDigit[0].Value = _maPeriod.ValueInt;
        _ma.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += BollMoving_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        BollMoving_ParametrsChangeByUser();
    }

    private void BollMoving_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_bol.ParametersDigit[0].Value != _bolPeriod.ValueInt ||
                        _bol.ParametersDigit[1].Value != _bolDev.ValueDecimal)
        {
            _bol.ParametersDigit[0].Value = _bolPeriod.ValueInt;
            _bol.ParametersDigit[1].Value = _bolDev.ValueDecimal;
            _bol.Reload();
            _bol.Save();
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

    public override string GetNameStrategyType()
    {
        return "BreakBollinger";
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
        if (_tab.CandlesAll == null)
        {
            return;
        }
        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (_maPeriod.ValueInt + 10 >= candles.Count || _bolPeriod.ValueInt + 10 >= candles.Count)
        {
            return;
        }

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        List<Position> positions = _tab.PositionsOpenAll;
        decimal lastMaFilter = _smaFilter.DataSeries[0].Last;
        decimal bol_up = _bol.DataSeries[0].Last;
        decimal bol_down = _bol.DataSeries[1].Last;
        decimal bol_center = _bol.DataSeries[2].Last;
        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal _slippage = 0;

        if (positions.Count == 0)
        {// enter logic
            if (bol_up > lastMaFilter && lastPrice <= bol_up)
            {
                if (BuySignalIsFiltered(candles) == false)
                {
                    _slippage = Slippage.ValueDecimal * bol_up / 100;
                    _tab.BuyAtStop(GetVolume(), bol_up + _slippage, bol_up, StopActivateType.HigherOrEqual, 1);
                }
            }
            if (bol_down < lastMaFilter && lastPrice >= bol_down)
            {
                if (SellSignalIsFiltered(candles) == false)
                {
                    _slippage = Slippage.ValueDecimal * bol_down / 100;
                    _tab.SellAtStop(GetVolume(), bol_down - _slippage, bol_down, StopActivateType.LowerOrEqyal, 1);
                }
            }
            if (lastPrice < lastMaFilter || BuySignalIsFiltered(candles))
            {
                _tab.BuyAtStopCancel();
            }
            if (lastPrice > lastMaFilter || SellSignalIsFiltered(candles))
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
                    stop_level = bol_center > lastMaFilter ? bol_center : lastMaFilter;
                    //   _tab.CloseAtStop(positions[i], stop_level, stop_level - _slippage.ValueInt * _tab.Securiti.PriceStep);
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage);
                }
                else if (positions[i].Direction == Side.Sell)
                {//logic to close short position
                    stop_level = bol_center < lastMaFilter && bol_center > 0 ? bol_center : lastMaFilter;
                    // _tab.CloseAtStop(positions[i], stop_level, stop_level + _slippage.ValueInt * _tab.Securiti.PriceStep);
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level + _slippage);
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
            //если режим работы робота не соответсвует направлению позициивозвращаем на верх true
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

