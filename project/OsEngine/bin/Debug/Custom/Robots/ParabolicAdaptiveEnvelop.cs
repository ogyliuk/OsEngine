using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;

[Bot("ParabolicAdaptiveEnvelop")]
public class ParabolicAdaptiveEnvelop : BotPanel
{
    private BotTabSimple _tab;


    public Aindicator _smaFilter;
    private StrategyParameterInt SmaLengthFilter;
    public StrategyParameterBool SmaPositionFilterIsOn;
    public StrategyParameterBool SmaSlopeFilterIsOn;
    private Aindicator _atr;
    private StrategyParameterInt AtrLenght;   
    public Aindicator _eR;
    private StrategyParameterInt ErLenght;

    public Aindicator _sma;
    private StrategyParameterInt SmaLength;

    public StrategyParameterString Regime;
    public StrategyParameterDecimal VolumeOnPosition;
    public StrategyParameterString VolumeRegime;
    public StrategyParameterInt VolumeDecimals;
    public StrategyParameterDecimal Slippage;

    private StrategyParameterTimeOfDay TimeStart;
    private StrategyParameterTimeOfDay TimeEnd;

    private StrategyParameterInt ExtremumForPeriod;

    private StrategyParameterDecimal DistLongInit;
    private StrategyParameterDecimal LongAdj;

    private StrategyParameterDecimal DistShortInit;
    private StrategyParameterDecimal ShortAdj;


    public ParabolicAdaptiveEnvelop(string name, StartProgram startProgram)
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

        ExtremumForPeriod = CreateParameter("Period for calculating the price order", 17, 10, 50, 4, "Robot parameters");

        DistLongInit = CreateParameter("Dist Long initial", 6, 1.0m, 50, 1, "Robot parameters");
        LongAdj = CreateParameter("Long adjust", 0.1m, 0.1m, 3, 0.1m, "Robot parameters");

        DistShortInit = CreateParameter("Dist Short initial", 6, 1.0m, 50, 1, "Robot parameters");
        ShortAdj = CreateParameter("Short adjust", 0.1m, 0.1m, 3, 0.1m, "Robot parameters");

        SmaLength = CreateParameter("Sma Length", 100, 50, 50, 400, "Robot parameters");

        AtrLenght = CreateParameter("Atr Lenght", 17, 10, 50, 1, "Robot parameters");
        ErLenght = CreateParameter("Er Lenght", 10, 10, 50, 1, "Robot parameters");

        SmaLengthFilter = CreateParameter("Sma Length Filter", 100, 10, 500, 1, "Filters");
        SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
        SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

        _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
        _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
        _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
        _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
        _smaFilter.Save();

        _sma = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma", canDelete: false);
        _sma = (Aindicator)_tab.CreateCandleIndicator(_sma, nameArea: "Prime");        
        _sma.ParametersDigit[0].Value = SmaLength.ValueInt;
        _sma.Save();

        _atr = IndicatorsFactory.CreateIndicatorByName("ATR", name + "ATR", false);
        _atr.ParametersDigit[0].Value = AtrLenght.ValueInt;
        _atr = (Aindicator)_tab.CreateCandleIndicator(_atr, "ATR_Area");
        _atr.Save();

        _eR = IndicatorsFactory.CreateIndicatorByName(nameClass: "EfficiencyRatio", name: name + "EfficiencyRatio", canDelete: false);
        _eR = (Aindicator)_tab.CreateCandleIndicator(_eR, nameArea: "EfficiencyRatio_area");
        _eR.ParametersDigit[0].Value = ErLenght.ValueInt;
        _eR.Save();

        StopOrActivateIndicators();
        ParametrsChangeByUser += StrategyParabolicVma_ParametrsChangeByUser;
        _tab.CandleFinishedEvent += StrategyAdxVolatility_CandleFinishedEvent;
        _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;       
        StrategyParabolicVma_ParametrsChangeByUser();

    }

    private void StrategyParabolicVma_ParametrsChangeByUser()
    {
        StopOrActivateIndicators();

        if (_atr.ParametersDigit[0].Value != AtrLenght.ValueInt)
        {
            _atr.ParametersDigit[0].Value = AtrLenght.ValueInt;
            _atr.Reload();
            _atr.Save();
        }
        if (_eR.ParametersDigit[0].Value != ErLenght.ValueInt)
        {
            _eR.ParametersDigit[0].Value = ErLenght.ValueInt;
            _eR.Reload();
            _eR.Save();
        }
        if (_sma.ParametersDigit[0].Value != SmaLength.ValueInt)
        {
            _sma.ParametersDigit[0].Value = SmaLength.ValueInt;
            _sma.Reload();
            _sma.Save();
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
        return "ParabolicAdaptiveEnvelop";
    }

    public override void ShowIndividualSettingsDialog()
    {

    }

    // логика

    void StrategyAdxVolatility_CandleFinishedEvent(List<Candle> candles)
    {
        if (candles.Count < SmaLengthFilter.ValueInt + 1)
        {
            return;
        }

        #region filters
        if (SmaLengthFilter.ValueInt >= candles.Count)
        {
            return;
        }
        #endregion

        List<Position> positions = _tab.PositionsOpenAll;

        if (TimeStart.Value > _tab.TimeServerCurrent ||
            TimeEnd.Value < _tab.TimeServerCurrent)
        {
            CancelStopsAndProfits();
            return;
        }

        if (candles.Count < SmaLengthFilter.ValueInt)
        {
            return;
        }

        if (positions != null && positions.Count != 0)
        {
            TryClosePosition(positions[0], candles);
        }
        else
        {
            TryOpenPosition(candles);
        }
    }

    void _tab_PositionOpeningSuccesEvent(Position position)
    {
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
    
    private void TryOpenPosition(List<Candle> candles)
    {
        decimal lastPrice = candles[candles.Count - 1].Close;
        decimal lastSma = _sma.DataSeries[0].Values[candles.Count - 1];

        if (lastPrice >= lastSma)
        {
            decimal lineBuy = GetPriceToOpenPos(Side.Buy, candles, candles.Count - 1);

            if (lineBuy == 0)
            {
                return;
            }

            decimal priceOrder = lineBuy;
            decimal priceRedLine = lineBuy;
            decimal _slippage = Slippage.ValueDecimal * priceOrder / 100;
            if (BuySignalIsFiltered(candles) == false)
            {
                _tab.BuyAtStop(GetVolume(), priceOrder + _slippage, priceRedLine, StopActivateType.HigherOrEqual);
            }
        }

        // СЕЛЛ
        else if (lastPrice <= lastSma)
        {
            decimal lineSell = GetPriceToOpenPos(Side.Sell, candles, candles.Count - 1);

            if (lineSell == 0)
            {
                return;
            }

            decimal priceOrderSell = lineSell;
            decimal priceRedLineSell = lineSell;
            decimal _slippage = Slippage.ValueDecimal * priceOrderSell / 100;
            if (SellSignalIsFiltered(candles) == false)
            {
                _tab.SellAtStop(GetVolume(), priceOrderSell - _slippage, priceRedLineSell, StopActivateType.LowerOrEqyal);
            }
        }
    }

    private void TryClosePosition(Position position, List<Candle> candles)
    {
        if (position.Direction == Side.Buy)
        {
            decimal lineBuy = GetPriceToStopOrder(position.TimeCreate, position.Direction, candles, candles.Count - 1);

            if (lineBuy == 0)
            {
                return;
            }

            decimal priceOrder = lineBuy;
            decimal priceRedLine = lineBuy;
            decimal _slippage = Slippage.ValueDecimal * priceOrder / 100;
            _tab.CloseAtTrailingStop(position, priceRedLine, priceOrder - _slippage);

        }


        // СЕЛЛ
        if (position.Direction == Side.Sell)
        {
            decimal lineSell = GetPriceToStopOrder(position.TimeCreate, position.Direction, candles, candles.Count - 1);

            if (lineSell == 0)
            {
                return;
            }

            decimal priceOrderSell = lineSell;
            decimal priceRedLineSell = lineSell;
            decimal _slippage = Slippage.ValueDecimal * priceOrderSell / 100;
            _tab.CloseAtTrailingStop(position, priceRedLineSell, priceOrderSell + _slippage);
        }
    }

    private decimal GetPriceToOpenPos(Side side, List<Candle> candles, int index)
    {
        if (side == Side.Buy)
        {
            decimal price = 0;

            for (int i = index; i > 0 && i > index - ExtremumForPeriod.ValueInt; i--)
            {
                if (candles[i].High > price)
                {
                    price = candles[i].High;
                }
            }
            return price;
        }
        if (side == Side.Sell)
        {
            decimal price = decimal.MaxValue;
            for (int i = index; i > 0 && i > index - ExtremumForPeriod.ValueInt; i--)
            {
                if (candles[i].Low < price)
                {
                    price = candles[i].Low;
                }
            }
            return price;
        }

        return 0;
    }

    private decimal GetPriceToStopOrder(DateTime positionCreateTime, Side side, List<Candle> candles, int index)
    {
        if (candles == null ||
            candles.Count < 2)
        {
            return 0;
        }

        if (side == Side.Buy)
        { // рассчитываем цену стопа при Лонге
          // 1 находим максимум за время от открытия сделки и до текущего
            decimal maxHigh = 0;
            int indexIntro = 0;
            DateTime openPositionTime = positionCreateTime;

            if (openPositionTime == DateTime.MinValue)
            {
                openPositionTime = candles[index - 2].TimeStart;
            }

            for (int i = index; i > 0; i--)
            { // смотрим индекс свечи, после которой произошло открытие позы
                if (candles[i].TimeStart <= openPositionTime)
                {
                    indexIntro = i;
                    break;
                }
            }

            for (int i = indexIntro; i < index + 1; i++)
            { // смотрим максимум после открытия

                if (candles[i].High > maxHigh)
                {
                    maxHigh = candles[i].High;
                }
            }

            // 2 рассчитываем текущее отклонение для стопа

            decimal distanse = DistLongInit.ValueDecimal;

            for (int i = indexIntro; i < index + 1; i++)
            { // смотрим коэффициент

                DateTime lastTradeTime = candles[i].TimeStart;

                if (TimeStart.Value > lastTradeTime ||
                    TimeEnd.Value < lastTradeTime)
                {
                    continue;
                }

                decimal kauf = _eR.DataSeries[0].Values[i];

                if (kauf >= 0.6m)
                {
                    distanse -= 2.0m * LongAdj.ValueDecimal;
                }
                if (kauf >= 0.3m)
                {
                    distanse -= 1.0m * LongAdj.ValueDecimal;
                }
            }

            // 3 рассчитываем цену Стопа

            decimal lastAtr = _atr.DataSeries[0].Values[index];

            decimal priceStop = maxHigh - lastAtr * distanse;

            return Math.Round(priceStop, _tab.Securiti.Decimals);// 
        }

        if (side == Side.Sell)
        {
            // рассчитываем цену стопа при Шорте

            // 1 находим максимум за время от открытия сделки и до текущего
            decimal minLow = decimal.MaxValue;
            int indexIntro = 0;
            DateTime openPositionTime = positionCreateTime;

            if (openPositionTime == DateTime.MinValue)
            {
                openPositionTime = candles[index - 1].TimeStart;
            }

            for (int i = index; i > 0; i--)
            { // смотрим индекс свечи, после которой произошло открытие позы
                if (candles[i].TimeStart <= openPositionTime)
                {
                    indexIntro = i;
                    break;
                }
            }

            for (int i = indexIntro; i < index + 1; i++)
            { // смотрим Минимальный лой

                if (candles[i].Low < minLow)
                {
                    minLow = candles[i].Low;
                }

            }

            // 2 рассчитываем текущее отклонение для стопа

            decimal distanse = DistShortInit.ValueDecimal;

            for (int i = indexIntro; i < index + 1; i++)
            { // смотрим коэффициент

                DateTime lastTradeTime = candles[i].TimeStart;

                if (TimeStart.Value > lastTradeTime ||
                    TimeEnd.Value < lastTradeTime)
                {
                    continue;
                }

                decimal kauf = _eR.DataSeries[0].Values[i];

                if (kauf > 0.6m)
                {
                    distanse -= 2.0m * ShortAdj.ValueDecimal;
                }
                if (kauf > 0.3m)
                {
                    distanse -= 1.0m * ShortAdj.ValueDecimal;
                }
            }

            // 3 рассчитываем цену Стопа

            int pointCount = 0;

            decimal lastAtr = _atr.DataSeries[0].Values[index];

            decimal priceStop = Math.Round(minLow + lastAtr * distanse, pointCount);

            return Math.Round(priceStop, _tab.Securiti.Decimals);
        }

        return 0;
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
