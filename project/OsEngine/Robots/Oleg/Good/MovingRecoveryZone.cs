using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OsEngine.Robots.Oleg.Good
{
    [Bot("MovingRecoveryZone")]
    public class MovingRecoveryZone : BotPanel
    {
        private BotTabSimple _bot;
        private decimal _balanceOnStart;
        private decimal _squeezeSize;
        private string _dealGuid;

        private Position _mainPosition;
        private Position _recoveryPosition;

        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterDecimal BollingerDeviation;
        private StrategyParameterInt BollingerSqueezePeriod;

        private StrategyParameterString Regime;
        private StrategyParameterDecimal RecoveryVolumeMultiplier;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal MinVolumeUSDT;

        private StrategyParameterDecimal CleanProfitPercent;
        private StrategyParameterDecimal FirstRecoveryDistanceInSqueezes;

        private bool MainPositionExists { get { return _mainPosition != null; } }
        private bool RecoveryPositionExists { get { return _recoveryPosition != null; } }
        private bool HasEntryOrdersSet { get { return _bot.PositionOpenerToStopsAll.Count > 0; } }

        public MovingRecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _dealGuid = String.Empty;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in Volume", 0, 0, 4, 1, "Base");
            RecoveryVolumeMultiplier = CreateParameter("Recovery volume multiplier", 0.8m, 0.8m, 0.9m, 0.05m, "Base");

            MinVolumeUSDT = CreateParameter("Min Volume USDT", 7m, 7m, 7m, 1m, "Base");
            BollingerLength = CreateParameter("BOLLINGER - Length", 20, 20, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("BOLLINGER - Deviation", 2m, 2m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezePeriod = CreateParameter("BOLLINGER - Squeeze period", 130, 130, 600, 5, "Robot parameters");
            
            CleanProfitPercent = CreateParameter("Clean Profit %", 0.1m, 0.1m, 1, 0.1m, "Base");
            FirstRecoveryDistanceInSqueezes = CreateParameter("First RECOVERY DISTANCE in SQUEEZEs", 2m, 2m, 10, 0.1m, "Base");

            _bollingerWithSqueeze = new BollingerWithSqueeze(name + "BollingerWithSqueeze", false);
            _bollingerWithSqueeze = (BollingerWithSqueeze)_bot.CreateCandleIndicator(_bollingerWithSqueeze, "Prime");
            _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
            _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
            _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezePeriod.ValueInt;
            _bollingerWithSqueeze.Save();

            _bot.CandleFinishedEvent += event_CandleClosed_SQUEEZE_FOUND;
            _bot.PositionOpeningSuccesEvent += event_PositionOpened_SET_ORDERS;
            _bot.PositionClosingSuccesEvent += event_PositionClosed_CONTINUE_OR_FINISH_DEAL;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
        }

        public override string GetNameStrategyType()
        {
            return "MovingRecoveryZone";
        }

        public override void ShowIndividualSettingsDialog() { }

        private void event_ParametersChangedByUser()
        {
            if (_bollingerWithSqueeze.Lenght != BollingerLength.ValueInt ||
                _bollingerWithSqueeze.Deviation != BollingerDeviation.ValueDecimal ||
                _bollingerWithSqueeze.SqueezePeriod != BollingerSqueezePeriod.ValueInt)
            {
                _bollingerWithSqueeze.Lenght = BollingerLength.ValueInt;
                _bollingerWithSqueeze.Deviation = BollingerDeviation.ValueDecimal;
                _bollingerWithSqueeze.SqueezePeriod = BollingerSqueezePeriod.ValueInt;
                _bollingerWithSqueeze.Reload();
                _bollingerWithSqueeze.Save();
            }
        }

        private void event_CandleClosed_SQUEEZE_FOUND(List<Candle> candles)
        {
            if (IsEnoughDataAndEnabledToTrade())
            {
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (lastCandleHasSqueeze)
                {
                    bool noDeal = !MainPositionExists && !RecoveryPositionExists && !HasEntryOrdersSet;
                    if (noDeal)
                    {
                        StartDeal();
                    }

                    bool recoveryInProgress = MainPositionExists && RecoveryPositionExists;
                    if (recoveryInProgress)
                    {
                        bool goodSizeRecovered = true; // TODO : calculate
                        if (goodSizeRecovered)
                        {
                            bool alreadyShiftedRecovery_SL = _recoveryPosition.StopOrderPrice != _mainPosition.ProfitOrderPrice;
                            if (alreadyShiftedRecovery_SL)
                            {
                                bool canShirtToBetterPlace = true; // TODO : calculate
                                if (canShirtToBetterPlace)
                                {
                                    // TODO : Shift recovery SL if in nice profit
                                }
                            }
                            else
                            {
                                // TODO : Shift recovery SL if in nice profit
                            }
                        }
                    }
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                RecognizeAndSetupPosition(p);

                if (MainPositionExists)
                {
                    if (RecoveryPositionExists)
                    {
                        SetBothPositionsTo_BE_PriceExit();
                    }
                    else
                    {
                        CancelOpposite_EP_Order();
                        SetMainPositionInitial_TP_Order();
                        SetFirstRecovery_EP_Order();
                    }
                }
            }
        }

        private void event_PositionClosed_CONTINUE_OR_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                if (IsMainPosition(p))
                {
                    if (!RecoveryPositionExists)
                    {
                        FinishDeal();
                    }
                    _mainPosition = null;
                }

                if (IsRecoveryPosition(p))
                {
                    if (!MainPositionExists)
                    {
                        FinishDeal();
                    }
                    else if (SomeLossRecovered())
                    {
                        SetMainPosition_TP_OnFullLossRecovery();
                        SetNextRecovery_EP_Order();
                    }
                    _recoveryPosition = null;
                }
            }
        }

        private void StartDeal()
        {
            _balanceOnStart = _bot.Portfolio.ValueCurrent;
            decimal squeezeUpBand = _bollingerWithSqueeze.ValuesUp.Last();
            decimal squeezeDownBand = _bollingerWithSqueeze.ValuesDown.Last();
            Set_EN_Order_LONG(squeezeUpBand);
            Set_EN_Order_SHORT(squeezeDownBand);
            _squeezeSize = squeezeUpBand - squeezeDownBand;
        }

        private void FinishDeal()
        {
            if (HasEntryOrdersSet)
            {
                _bot.BuyAtStopCancel();
                _bot.SellAtStopCancel();
            }
            _dealGuid = String.Empty;
        }

        private bool IsMainPosition(Position p)
        {
            return MainPositionExists && p.Number == _mainPosition.Number;
        }

        private bool IsRecoveryPosition(Position p)
        {
            return RecoveryPositionExists && p.Number == _recoveryPosition.Number;
        }

        private bool SomeLossRecovered()
        {
            bool longRecoveryMadeProfit = _recoveryPosition.Direction == Side.Buy && _recoveryPosition.EntryPrice < _recoveryPosition.ClosePrice;
            bool shortRecoveryMadeProfit = _recoveryPosition.Direction == Side.Sell && _recoveryPosition.EntryPrice > _recoveryPosition.ClosePrice;
            return longRecoveryMadeProfit || shortRecoveryMadeProfit;
        }

        private void RecognizeAndSetupPosition(Position p)
        {
            bool newDeal = _mainPosition == null;
            if (newDeal)
            {
                _mainPosition = p;
                _dealGuid = Guid.NewGuid().ToString();
            }
            else
            {
                _recoveryPosition = p;
            }
            p.DealGuid = _dealGuid;
        }

        private void CancelOpposite_EP_Order()
        {
            if (_mainPosition != null)
            {
                if (_mainPosition.Direction == Side.Buy)
                {
                    _bot.SellAtStopCancel();
                }
                else if (_mainPosition.Direction == Side.Sell)
                {
                    _bot.BuyAtStopCancel();
                }
            }
        }

        private void SetMainPositionInitial_TP_Order()
        {
            decimal TP_price = _mainPosition.Direction == Side.Buy ?
                Calc_TP_Price_LONG(_mainPosition.EntryPrice) :
                Calc_TP_Price_SHORT(_mainPosition.EntryPrice);
            Set_TP_Order(_mainPosition, TP_price);
        }

        private void SetMainPosition_TP_OnFullLossRecovery()
        {
            // TODO : calc right price here
            decimal fullLossRecovery_TP_Price = 0;
            Set_TP_Order(_mainPosition, fullLossRecovery_TP_Price);
        }

        private void SetFirstRecovery_EP_Order()
        {
            decimal mainPosition_EP_Price = _mainPosition.EntryPrice;
            if (_mainPosition.Direction == Side.Buy)
            {
                Set_EN_Order_SHORT(Calc_EP_FirstRecoveryPrice_LONG(mainPosition_EP_Price));
            }
            else if (_mainPosition.Direction == Side.Sell)
            {
                Set_EN_Order_LONG(Calc_EP_FirstRecoveryPrice_SHORT(mainPosition_EP_Price));
            }
        }

        private void SetNextRecovery_EP_Order()
        {
            if (_mainPosition.Direction == Side.Buy)
            {
                // TODO : calc right price here (consider recovered loss amount)
                decimal nextRecovery_EP_PriceShort = 0;
                Set_EN_Order_SHORT(nextRecovery_EP_PriceShort);
            }
            else if (_mainPosition.Direction == Side.Sell)
            {
                // TODO : calc right price here (consider recovered loss amount)
                decimal nextRecovery_EP_PriceLong = 0;
                Set_EN_Order_LONG(nextRecovery_EP_PriceLong);
            }
        }

        private void SetBothPositionsTo_BE_PriceExit()
        {
            decimal BE_price = Calc_BE_Price();
            Set_TP_Order(_mainPosition, BE_price);
            Set_SL_Order(_recoveryPosition, BE_price);
        }

        private void Set_TP_Order(Position p, decimal TP_price)
        {
            _bot.CloseAtProfit(p, TP_price, TP_price);
        }

        private void Set_SL_Order(Position p, decimal SL_price)
        {
            _bot.CloseAtStop(p, SL_price, SL_price);
        }

        private void Set_EN_Order_LONG(decimal entryPrice)
        {
            decimal buyCoinsVolume = GetNewAttemptCoinsVolume(Side.Buy);
            if (buyCoinsVolume > 0)
            {
                _bot.BuyAtStop(buyCoinsVolume, entryPrice, entryPrice, StopActivateType.HigherOrEqual, 100000);
            }
        }

        private void Set_EN_Order_SHORT(decimal entryPrice)
        {
            decimal sellCoinsVolume = GetNewAttemptCoinsVolume(Side.Sell);
            if (sellCoinsVolume > 0)
            {
                _bot.SellAtStop(sellCoinsVolume, entryPrice, entryPrice, StopActivateType.LowerOrEqyal, 100000);
            }
        }

        private decimal Calc_TP_Price_LONG(decimal entryPrice)
        {
            return entryPrice * (100 + CleanProfitPercent.ValueDecimal) / 100;
        }

        private decimal Calc_TP_Price_SHORT(decimal entryPrice)
        {
            return entryPrice * (100 - CleanProfitPercent.ValueDecimal) / 100;
        }

        private decimal Calc_EP_FirstRecoveryPrice_LONG(decimal mainEntryPrice)
        {
            return mainEntryPrice - _squeezeSize * FirstRecoveryDistanceInSqueezes.ValueDecimal;
        }

        private decimal Calc_EP_FirstRecoveryPrice_SHORT(decimal mainEntryPrice)
        {
            return mainEntryPrice + _squeezeSize * FirstRecoveryDistanceInSqueezes.ValueDecimal;
        }

        private decimal Calc_BE_Price()
        {
            return _recoveryPosition.Direction == Side.Buy ?
                Calc_BE_Price_MAIN_is_SHORT(_recoveryPosition.OpenVolume, _mainPosition.OpenVolume, _recoveryPosition.EntryPrice, _mainPosition.EntryPrice) :
                Calc_BE_Price_MAIN_is_LONG(_mainPosition.OpenVolume, _recoveryPosition.OpenVolume, _mainPosition.EntryPrice, _recoveryPosition.EntryPrice);
        }

        private decimal Calc_BE_Price_MAIN_is_LONG(decimal volLong, decimal volShort, decimal EP_Long, decimal EP_Short)
        {
            decimal fee = _bot.ComissionValue;
            return (volLong * EP_Long * (100 + fee) + volShort * EP_Short * (fee - 100)) / (volLong * (100 - fee) - volShort * (fee + 100));
        }

        private decimal Calc_BE_Price_MAIN_is_SHORT(decimal volLong, decimal volShort, decimal EP_Long, decimal EP_Short)
        {
            decimal fee = _bot.ComissionValue;
            return (volLong * EP_Long * (100 + fee) - volShort * EP_Short * (100 - fee)) / (volLong * (100 - fee) - volShort * (100 + fee));
        }

        private decimal GetNewAttemptCoinsVolume(Side side)
        {
            decimal coinsVolume = 0;
            if (HasMoneyForNewAttempt() && !IsMoneyForNewAttemptTooSmall())
            {
                decimal moneyAvailableForNewAttempt = GetNewAttemptMoneyNeeded();
                decimal moneyNeededForFee = moneyAvailableForNewAttempt / 100 * _bot.ComissionValue;
                decimal moneyLeftForCoins = moneyAvailableForNewAttempt - moneyNeededForFee;
                decimal price = side == Side.Buy ? TabsSimple[0].PriceBestAsk : TabsSimple[0].PriceBestBid;
                decimal volume = moneyLeftForCoins / price;
                coinsVolume = Math.Round(volume, VolumeDecimals.ValueInt);
            }
            return coinsVolume;
        }

        private bool IsMoneyForNewAttemptTooSmall()
        {
            return GetNewAttemptMoneyNeeded() < MinVolumeUSDT.ValueDecimal;
        }

        private bool HasMoneyForNewAttempt()
        {
            return GetFreeMoney() > GetNewAttemptMoneyNeeded();
        }

        private decimal GetNewAttemptMoneyNeeded()
        {
            bool hasOpenPositionsAlready = _bot.PositionsLast != null && _bot.PositionsLast.State == PositionStateType.Open;
            decimal moneyToCalcFrom = hasOpenPositionsAlready ? GetLastPositionEntryMoney() : _bot.Portfolio.ValueCurrent;
            return moneyToCalcFrom * RecoveryVolumeMultiplier.ValueDecimal;
        }

        private decimal GetLastPositionEntryMoney()
        {
            return _bot.PositionsLast != null ? GetPositionEntryMoney(_bot.PositionsLast) : 0;
        }

        private decimal GetFreeMoney()
        {
            decimal freeMoney = _balanceOnStart;
            _bot.PositionsOpenAll.ForEach(p => { freeMoney -= GetPositionEntryMoney(p); });
            return freeMoney;
        }

        private decimal GetPositionEntryMoney(Position p)
        {
            return p.OpenVolume * p.EntryPrice + p.CommissionTotal();
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool robotEnabled = Regime.ValueString == "On";
            bool enoughCandlesForBollinger = candlesCount > BollingerLength.ValueInt;
            bool enoughCandlesForBollingerSqueeze = candlesCount > BollingerSqueezePeriod.ValueInt;
            return robotEnabled && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze;
        }
    }
}
