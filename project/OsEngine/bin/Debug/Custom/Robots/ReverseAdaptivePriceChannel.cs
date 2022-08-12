using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;

[Bot("ReverseAdaptivePriceChannel")]
public class ReverseAdaptivePriceChannel : BotPanel
{
    private BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    public Aindicator _APC;
    private StrategyParameterInt AdxPeriod;
    private StrategyParameterInt Ratio;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public ReverseAdaptivePriceChannel(string name, StartProgram startProgram)
        : base(name, startProgram)
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

        AdxPeriod = CreateParameter("Ronco Period", 14, 2, 300, 12, "Robot parameters");
        Ratio = CreateParameter("Ratio", 100, 50, 300, 10, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length Filter", 100, 10, 500, 1, "Filters");

        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _APC = IndicatorsFactory.CreateIndicatorByName("AdaptivePriceChannel_Indicator", name + "APC", false);
        _APC = (Aindicator)_tab.CreateCandleIndicator(_APC, "Prime");
        _APC.ParametersDigit[0].Value = AdxPeriod.ValueInt;
        _APC.ParametersDigit[1].Value = Ratio.ValueInt;
        _APC.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += RoncoParam_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        RoncoParam_ParametrsChangeByUser();
        _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;
    }

    private void _tab_PositionOpeningSuccesEvent(Position obj)
    {
        _tab.SellAtStopCancel();
        _tab.BuyAtStopCancel();
    }

    private void RoncoParam_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_APC.ParametersDigit[0].Value != AdxPeriod.ValueInt ||
                _APC.ParametersDigit[1].Value != Ratio.ValueInt)
        {
            _APC.ParametersDigit[0].Value = AdxPeriod.ValueInt;
            _APC.ParametersDigit[1].Value = Ratio.ValueInt;
            _APC.Save();
            _APC.Reload();
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
        return "ReverseAdaptivePriceChannel";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (candles.Count < AdxPeriod.ValueInt + 10 ||
            candles.Count < 50)
        {
            return;
        }

        decimal upChannel = _APC.DataSeries[0].Last;
        decimal downChannel = _APC.DataSeries[1].Last;

        if (upChannel == 0 || downChannel == 0)
        {
            return;
        }

        List<Position> positions = _tab.PositionsOpenAll;

        if (positions.Count == 0)
        {
            if (BuySignalIsFiltered(candles) == false)
            {
                decimal _slippage = Slippage.ValueDecimal * upChannel / 100;
                _tab.BuyAtStopCancel();
                _tab.BuyAtStop(GetVolume(),
                    upChannel + _tab.Securiti.PriceStep + _slippage,
                    upChannel + _tab.Securiti.PriceStep,
                    StopActivateType.HigherOrEqual);
            }
            if (SellSignalIsFiltered(candles) == false)
            {
                decimal _slippage = Slippage.ValueDecimal * downChannel / 100;
                _tab.SellAtStopCancel();
                _tab.SellAtStop(GetVolume(),
                    downChannel - _tab.Securiti.PriceStep - _slippage,
                    downChannel - _tab.Securiti.PriceStep,
                    StopActivateType.LowerOrEqyal);
            }
        }
        else
        {
            _tab.SellAtStopCancel();
            _tab.BuyAtStopCancel();
            Position pos = positions[0];

            if (positions.Count > 1)
            {

            }

            if (pos.CloseActiv == true)
            {
                return;
            }

            if (pos.Direction == Side.Buy)
            {
                decimal priceLine = downChannel - _tab.Securiti.PriceStep;
                decimal priceOrder = downChannel - _tab.Securiti.PriceStep;
                decimal _slippage = Slippage.ValueDecimal * priceOrder / 100;

                if (SellSignalIsFiltered(candles) == false)
                {
                    _tab.SellAtStopCancel();
                    _tab.SellAtStop(GetVolume(),
                    priceOrder - _slippage,
                    priceLine,
                    StopActivateType.LowerOrEqyal);
                }

                _tab.CloseAtStop(pos, priceLine, priceOrder - _slippage);
            }
            else if (pos.Direction == Side.Sell)
            {
                decimal priceLine = upChannel + _tab.Securiti.PriceStep;
                decimal priceOrder = upChannel + _tab.Securiti.PriceStep;
                decimal _slippage = Slippage.ValueDecimal * priceOrder / 100;

                if (BuySignalIsFiltered(candles) == false)
                {
                    _tab.BuyAtStopCancel();
                    _tab.BuyAtStop(GetVolume(),
                    priceOrder + _slippage,
                    priceLine,
                    StopActivateType.HigherOrEqual);
                }
                _tab.CloseAtStop(pos, priceLine, priceOrder + _slippage);
            }
        }
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
