using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Attributes;

[Bot("BreakAlmaChannel")]
public class BreakAlmaChannel : BotPanel
{
    BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _almaChanel;
    public StrategyParameterInt _sizeWindows;
    public StrategyParameterInt _sigma;
    public StrategyParameterDecimal _offset;
    public StrategyParameterInt _periodSma;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public BreakAlmaChannel(string name, StartProgram startProgram) : base(name, startProgram)
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

        _sizeWindows = CreateParameter("Size windows", 24, 24, 60, 6, "Robot parameters");
        _sigma = CreateParameter("Sigma", 6, 6, 20, 1, "Robot parameters");
        _offset = CreateParameter("Offset", 1.5m, 0.05m, 4, 0.05m, "Robot parameters");
        _periodSma = CreateParameter("Period Sma", 60, 60, 300, 50, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");
        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _almaChanel = IndicatorsFactory.CreateIndicatorByName("ALMA_Chanel_Indicator", name: name + "ALMA_Chanel_Indicator", canDelete: false);
        _almaChanel = (Aindicator)_tab.CreateCandleIndicator(_almaChanel, nameArea: "Prime");
        _almaChanel.ParametersDigit[0].Value = _sizeWindows.ValueInt;
        _almaChanel.ParametersDigit[1].Value = _sigma.ValueInt;
        _almaChanel.ParametersDigit[2].Value = _offset.ValueDecimal;
        _almaChanel.ParametersDigit[3].Value = _periodSma.ValueInt;
        _almaChanel.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += ALMA_Chanel_bot_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        ALMA_Chanel_bot_ParametrsChangeByUser();

    }

    private void ALMA_Chanel_bot_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_almaChanel.ParametersDigit[0].Value != _sizeWindows.ValueInt || _almaChanel.ParametersDigit[1].Value != _sigma.ValueInt ||
            _almaChanel.ParametersDigit[2].Value != _offset.ValueDecimal || _almaChanel.ParametersDigit[3].Value != _periodSma.ValueInt)
        {
            _almaChanel.ParametersDigit[0].Value = _sizeWindows.ValueInt;
            _almaChanel.ParametersDigit[1].Value = _sigma.ValueInt;
            _almaChanel.ParametersDigit[2].Value = _offset.ValueDecimal;
            _almaChanel.ParametersDigit[3].Value = _periodSma.ValueInt;
        }

        _almaChanel.Reload();
        _almaChanel.Save();

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
        return "BreakAlmaChannel";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (Regime.ValueString == "Off") return;

        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (_tab.CandlesAll == null) return;

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        if (candles.Count < _periodSma.ValueInt + 5 || candles.Count < _sizeWindows.ValueInt + 5) return;

        decimal lastPrice = candles[candles.Count - 1].Close;

        decimal almaUp = _almaChanel.DataSeries[0].Last;
        decimal almaDown = _almaChanel.DataSeries[1].Last;
        decimal lastMa = _almaChanel.DataSeries[2].Last;
        decimal _slippage = 0;
        List<Position> positions = _tab.PositionsOpenAll;

        if (positions.Count == 0)
        {
            //Console.WriteLine("lastTime: {0} positions.Count: {1}", lastTime, positions.Count);

            //enter logic

            if (almaUp > lastMa && lastPrice <= almaUp)
            {
                _slippage = Slippage.ValueDecimal * almaUp / 100;
                if (!BuySignalIsFiltered(candles)) //если метод возвращает False можно открывать позицию. BuySignalIsFiltered(candles) == false
                    _tab.BuyAtStop(GetVolume(), almaUp + _slippage, almaUp, StopActivateType.HigherOrEqual, 1);
            }
            if (almaDown < lastMa && lastPrice >= almaDown)
            {
                _slippage = Slippage.ValueDecimal * almaDown / 100;
                if (SellSignalIsFiltered(candles) == false)
                    _tab.SellAtStop(GetVolume(), almaDown - _slippage, almaDown, StopActivateType.LowerOrEqyal, 1);
            }
            if (lastPrice < lastMa || BuySignalIsFiltered(candles))
            {
                _tab.BuyAtStopCancel();
            }
            if (lastPrice > lastMa || SellSignalIsFiltered(candles))
            {
                _tab.SellAtStopCancel();
            }
        }

        else
        {//exit logic 
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State != PositionStateType.Open) continue;
                if (positions[i].State == PositionStateType.ClosingFail) _tab.CloseAtMarket(positions[i], positions[i].OpenVolume);

                decimal stop_level = 0;
                if (positions[i].Direction == Side.Buy)
                {//logic to close long position
                    stop_level = almaDown;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level - _slippage);
                    continue;
                }
                if (positions[i].Direction == Side.Sell)
                {//logic to close short position
                    stop_level = almaUp;
                    _slippage = Slippage.ValueDecimal * stop_level / 100;
                    _tab.CloseAtTrailingStop(positions[i], stop_level, stop_level + _slippage);
                    continue;
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

