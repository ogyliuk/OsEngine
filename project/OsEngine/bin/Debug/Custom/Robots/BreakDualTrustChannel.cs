using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

[Bot("BreakDualTrustChannel")]
public class BreakDualTrustChannel : BotPanel
{
    private BotTabSimple _tab;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    private Aindicator _dtt;
    private StrategyParameterInt ParamPeriodShort;
    private StrategyParameterInt ParamPeriodLong;
    private StrategyParameterDecimal ParamK1Long;
    private StrategyParameterDecimal ParamK2Short;
    private StrategyParameterInt Compress;

    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;

    public BreakDualTrustChannel(string name, StartProgram startProgram)
        : base(name, startProgram)
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

        ParamPeriodShort = CreateParameter("MdaySell", 3, 1, 30, 2, "Robot parameters");
        ParamPeriodLong = CreateParameter("NdayBuy", 3, 1, 30, 2, "Robot parameters");
        ParamK1Long = CreateParameter("K1Buy", 0.5m, 0.1m, 3, 0.1m, "Robot parameters");
        ParamK2Short = CreateParameter("K1Sell", 0.5m, 0.1m, 3, 0.1m, "Robot parameters");
        Compress = CreateParameter("Коэфф. сжатия", 4, 2, 20, 2, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length", 100, 10, 500, 1, "Filters");
        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _dtt = IndicatorsFactory.CreateIndicatorByName("DualTrustTrig_Indicator", name + "DualTrustTrig", false);
        _dtt = (Aindicator)_tab.CreateCandleIndicator(_dtt, "Prime");
        _dtt.ParametersDigit[0].Value = ParamPeriodShort.ValueInt;
        _dtt.ParametersDigit[1].Value = ParamPeriodLong.ValueInt;
        _dtt.ParametersDigit[2].Value = ParamK1Long.ValueDecimal;
        _dtt.ParametersDigit[3].Value = ParamK2Short.ValueDecimal;
        _dtt.ParametersDigit[4].Value = Compress.ValueInt;
        _dtt.Save();

        StopOrActivateIndicators();
        _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
        ParametrsChangeByUser += DualTrustParam_ParametrsChangeByUser;
        DualTrustParam_ParametrsChangeByUser();
    
    }

    private void DualTrustParam_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_dtt.ParametersDigit[0].Value != ParamPeriodShort.ValueInt ||
        _dtt.ParametersDigit[1].Value != ParamPeriodLong.ValueInt ||
        _dtt.ParametersDigit[2].Value != ParamK1Long.ValueDecimal ||
        _dtt.ParametersDigit[3].Value != ParamK2Short.ValueDecimal ||
        _dtt.ParametersDigit[4].Value != Compress.ValueInt)
        {
            _dtt.ParametersDigit[0].Value = ParamPeriodShort.ValueInt;
            _dtt.ParametersDigit[1].Value = ParamPeriodLong.ValueInt;
            _dtt.ParametersDigit[2].Value = ParamK1Long.ValueDecimal;
            _dtt.ParametersDigit[3].Value = ParamK2Short.ValueDecimal;
            _dtt.ParametersDigit[4].Value = Compress.ValueInt;
            _dtt.Reload();
            _dtt.Save();
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
        return "BreakDualTrustChannel";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    private void _tab_CandleFinishedEvent(List<Candle> candles)
    {
        // использование
        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (candles.Count < 20)
        {
            return;
        }

        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }

        List<Position> positions = _tab.PositionsOpenAll;

        if (positions.Count == 0)
        {
            TryOpenPosition(candles);
        }
        else
        {
            TryClosePosition(positions[0], candles);
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

    private void TryOpenPosition(List<Candle> candles)
    {
        decimal upChannel = _dtt.DataSeries[2].Values[_dtt.DataSeries[2].Values.Count - 1];
        decimal downChannel = _dtt.DataSeries[3].Values[_dtt.DataSeries[3].Values.Count - 1];

        if (upChannel == 0 ||
            downChannel == 0)
        {
            return;
        }

        bool signalBuy = candles[candles.Count - 1].Close > upChannel;
        bool signalShort = candles[candles.Count - 1].Close < downChannel;

        if (signalBuy) // При получении сигнала на вход в длинную позицию
        {
            if (!BuySignalIsFiltered(candles))//если метод возвращает false можно входить в сделку
                _tab.BuyAtMarket(GetVolume()); // Купить по рынку на открытии следующей свечки
        }
        else if (signalShort) // При получении сигнала на вход в короткую позицию
        {
            if (!SellSignalIsFiltered(candles))//если метод возвращает false можно входить в сделку
                _tab.SellAtMarket(GetVolume()); // Продать по рынку на открытии следующей свечки
        }
    }

    private void TryClosePosition(Position position, List<Candle> candles)
    {
        decimal upChannel = _dtt.DataSeries[2].Values[_dtt.DataSeries[2].Values.Count - 1];
        decimal downChannel = _dtt.DataSeries[3].Values[_dtt.DataSeries[3].Values.Count - 1];

        if (upChannel == 0 ||
            downChannel == 0)
        {
            return;
        }

        decimal extPrice = 0;

        if (position.Direction == Side.Buy)
        {            
            extPrice = downChannel;
            decimal _slippage = Slippage.ValueDecimal * extPrice / 100;

            _tab.CloseAtStop(position, extPrice, extPrice - _slippage);
        }
        else if (position.Direction == Side.Sell)
        {            
            extPrice = upChannel;
            decimal _slippage = Slippage.ValueDecimal * extPrice / 100;

            _tab.CloseAtStop(position, extPrice, extPrice + _slippage);
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
