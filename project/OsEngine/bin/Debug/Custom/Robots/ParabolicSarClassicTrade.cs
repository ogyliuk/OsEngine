using System;
using System.Collections.Generic;
using OsEngine.Entity;
using OsEngine.Indicators;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;

namespace OsEngine.Robots.Error
{
    [Bot("ParabolicSarClassicTrade")]
    public class ParabolicSarClassicTrade : BotPanel
    {
        private BotTabSimple _tab;

        public StrategyParameterString Regime;
        public StrategyParameterDecimal VolumeOnPosition;
        public StrategyParameterString VolumeRegime;
        public StrategyParameterInt VolumeDecimals;
        public StrategyParameterDecimal Slippage;

        private StrategyParameterTimeOfDay TimeStart;
        private StrategyParameterTimeOfDay TimeEnd;

        public Aindicator _PS;
        private StrategyParameterDecimal _Step;
        private StrategyParameterDecimal _MaxStep;

        public Aindicator _smaFilter;
        private StrategyParameterInt SmaLengthFilter;
        public StrategyParameterBool SmaPositionFilterIsOn;
        public StrategyParameterBool SmaSlopeFilterIsOn;

        private decimal _lastPrice;
        private decimal _lastSar;

        public ParabolicSarClassicTrade(string name, StartProgram startProgram) : base(name, startProgram)
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

            _Step = CreateParameter("Step", 0.02m, 0.001m, 3, 0.001m, "Robot parameters");
            _MaxStep = CreateParameter("MaxStep", 0.2m, 0.01m, 1, 0.01m, "Robot parameters");

            SmaLengthFilter = CreateParameter("Sma Length Filter", 100, 10, 500, 1, "Filters");

            SmaPositionFilterIsOn = CreateParameter("Is SMA Filter On", false, "Filters");
            SmaSlopeFilterIsOn = CreateParameter("Is Sma Slope Filter On", false, "Filters");

            _smaFilter = IndicatorsFactory.CreateIndicatorByName(nameClass: "Sma", name: name + "Sma_Filter", canDelete: false);
            _smaFilter = (Aindicator)_tab.CreateCandleIndicator(_smaFilter, nameArea: "Prime");
            _smaFilter.DataSeries[0].Color = System.Drawing.Color.Azure;
            _smaFilter.ParametersDigit[0].Value = SmaLengthFilter.ValueInt;
            _smaFilter.Save();

            _PS = IndicatorsFactory.CreateIndicatorByName(nameClass: "ParabolicSAR", name: name + "Parabolic", canDelete: false);
            _PS = (Aindicator)_tab.CreateCandleIndicator(_PS, nameArea: "Prime");
            _PS.ParametersDigit[0].Value = _Step.ValueDecimal;
            _PS.ParametersDigit[1].Value = _MaxStep.ValueDecimal;
            _PS.Save();

            StopOrActivateIndicators();
            ParametrsChangeByUser += ParabolicSarClassicTrade_ParametrsChangeByUser;

            _tab.CandleFinishedEvent += _tab_CandleFinishedEvent;
            _tab.PositionOpeningSuccesEvent += _tab_PositionOpeningSuccesEvent;

        }

        private void _tab_PositionOpeningSuccesEvent(Position obj)
        {
            _tab.SellAtStopCancel();
            _tab.BuyAtStopCancel();
        }

        private void ParabolicSarClassicTrade_ParametrsChangeByUser()
        {
            StopOrActivateIndicators();

            if (_PS.ParametersDigit[0].Value != _Step.ValueDecimal ||
               _PS.ParametersDigit[1].Value != _MaxStep.ValueDecimal)
            {
                _PS.ParametersDigit[0].Value = _Step.ValueDecimal;
                _PS.ParametersDigit[1].Value = _MaxStep.ValueDecimal;
                _PS.Save();
                _PS.Reload();
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
            return "ParabolicSarClassicTrade";
        }

        public override void ShowIndividualSettingsDialog()
        {
            
        }

        //Logic
        private void _tab_CandleFinishedEvent(List<Candle> candles)
        {
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

            if (candles.Count < 20)
            {
                return;
            }

            _lastPrice = candles[candles.Count - 1].Close;
            _lastSar = _PS.DataSeries[0].Last;

            if (_lastSar == 0)
            {
                return;
            }

            List<Position> positions = _tab.PositionsOpenAll;

            if (positions.Count == 0)
            {
                if (BuySignalIsFiltered(candles) == false)
                {
                    if (_lastPrice > _lastSar)
                    {
                        return;
                    }

                    decimal _slippage = Slippage.ValueDecimal * _lastSar / 100;
                    _tab.BuyAtStopCancel();
                    _tab.BuyAtStop(GetVolume(),
                        _lastSar + _tab.Securiti.PriceStep + _slippage,
                        _lastSar + _tab.Securiti.PriceStep,
                        StopActivateType.HigherOrEqual);
                }
                if (SellSignalIsFiltered(candles) == false)
                {
                    if (_lastPrice < _lastSar)
                    {
                        return;
                    }

                    decimal _slippage = Slippage.ValueDecimal * _lastSar / 100;
                    _tab.SellAtStopCancel();
                    _tab.SellAtStop(GetVolume(),
                        _lastSar - _tab.Securiti.PriceStep - _slippage,
                        _lastSar - _tab.Securiti.PriceStep,
                        StopActivateType.LowerOrEqyal);
                }
            }
            else
            {
                _tab.SellAtStopCancel();
                _tab.BuyAtStopCancel();
                Position pos = positions[0];

                if (pos.CloseActiv == true && pos.CloseOrders != null && pos.CloseOrders.Count > 0)
                {
                    return;
                }

                if (pos.Direction == Side.Buy)
                {
                    decimal priceLine = _lastSar - _tab.Securiti.PriceStep;
                    decimal priceOrder = _lastSar - _tab.Securiti.PriceStep;
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
                    decimal priceLine = _lastSar + _tab.Securiti.PriceStep;
                    decimal priceOrder = _lastSar + _tab.Securiti.PriceStep;
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
}
