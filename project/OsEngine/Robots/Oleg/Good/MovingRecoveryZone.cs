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
        private static readonly String STATIC = "Static";
        private static readonly String REINVESTMENT = "Reinvestment";

        private BotTabSimple _bot;
        private string _dealGuid;
        private decimal _squeezeSize;
        private decimal _balanceOnStart;
        private decimal _availableMoney;
        private Position _mainPosition;
        private Position _recoveryPosition;

        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterInt BollingerSqueezePeriod;
        private StrategyParameterDecimal BollingerDeviation;

        private StrategyParameterString Regime;
        private StrategyParameterString RecoveryVolumeMode;
        private StrategyParameterDecimal RecoveryVolumeMultiplier;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal MinVolumeUSDT;
        private StrategyParameterDecimal WantedCleanProfitPercent;
        private StrategyParameterDecimal StartRecoveryDistanceInSqueezes;

        private bool MainPositionExists { get { return _mainPosition != null; } }
        private bool RecoveryPositionExists { get { return _recoveryPosition != null; } }
        private bool HasEntryOrdersSet { get { return _bot.PositionOpenerToStopsAll.Count > 0; } }
        private decimal SqueezeUpBand { get { return _bollingerWithSqueeze.ValuesUp.Last(); } }
        private decimal SqueezeDownBand { get { return _bollingerWithSqueeze.ValuesDown.Last(); } }

        public MovingRecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _dealGuid = String.Empty;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in VOLUME", 0, 0, 4, 1, "Base");
            MinVolumeUSDT = CreateParameter("Min VOLUME USDT", 7m, 7m, 7m, 1m, "Base");
            RecoveryVolumeMode = CreateParameter("Recovery VOLUME MODE", STATIC, new[] { STATIC, REINVESTMENT }, "Base");
            RecoveryVolumeMultiplier = CreateParameter("Recovery VOLUME MULTIPLIER", 0.8m, 0.8m, 0.9m, 0.05m, "Base");
            StartRecoveryDistanceInSqueezes = CreateParameter("Start recovery DISTANCE in SQUEEZEs", 2m, 2m, 10, 0.1m, "Base");
            WantedCleanProfitPercent = CreateParameter("Wanted CLEAN PROFIT %", 0.1m, 0.1m, 1, 0.1m, "Base");

            BollingerLength = CreateParameter("BOLLINGER - Length", 20, 20, 50, 2, "Robot parameters");
            BollingerDeviation = CreateParameter("BOLLINGER - Deviation", 2m, 2m, 3m, 0.1m, "Robot parameters");
            BollingerSqueezePeriod = CreateParameter("BOLLINGER - Squeeze period", 130, 130, 600, 5, "Robot parameters");

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

            bool volumeMultiplierValid = RecoveryVolumeMultiplier.ValueDecimal > 0 && RecoveryVolumeMultiplier.ValueDecimal < 1;
            if (!volumeMultiplierValid)
            {
                throw new Exception("Volume multiplier value is INVALID! It must be > 0 and < 1");
            }
        }

        private void event_CandleClosed_SQUEEZE_FOUND(List<Candle> candles)
        {
            if (IsEnoughDataAndEnoughMoneyAndEnabledToTrade())
            {
                bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
                if (lastCandleHasSqueeze)
                {
                    bool noDeal = !MainPositionExists && !RecoveryPositionExists && !HasEntryOrdersSet;
                    if (noDeal)
                    {
                        InitNewDealParameters();
                        SetNewDealEntryOrders();
                    }

                    bool recoveringDeal = MainPositionExists && RecoveryPositionExists;
                    if (recoveringDeal)
                    {
                        bool betterSqueezeFound = MoveRecoveryTrailing_SL_IfPossible();
                        if (betterSqueezeFound)
                        {
                            _squeezeSize = SqueezeUpBand - SqueezeDownBand;
                        }
                    }
                }
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && p.State == PositionStateType.Open)
            {
                LockMoneySpentForPosition(p);
                RecognizeAndSetupPosition(p);
                SwapPositionsIfNeeded();

                if (MainPositionExists)
                {
                    if (RecoveryPositionExists)
                    {
                        SetBothPositionsTo_BE_PriceExit();
                    }
                    else
                    {
                        CancelOpposite_EP_Order();
                        SetMainPosition_TP_Order_Initial();
                        SetRecoveryPosition_EP_Order(_mainPosition.EntryPrice);
                    }
                }
            }
        }

        private void event_PositionClosed_CONTINUE_OR_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                UnlockMoneyReleasedByPosition(p);

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
                    if (MainPositionExists)
                    {
                        SetMainPosition_TP_Order_ToFullLossRecovery();
                        SetRecoveryPosition_EP_Order(_recoveryPosition.ClosePrice);
                    }
                    else
                    {
                        FinishDeal();
                    }
                    _recoveryPosition = null;
                }
            }
        }

        private void InitNewDealParameters()
        {
            _balanceOnStart = _bot.Portfolio.ValueCurrent;
            _availableMoney = _bot.Portfolio.ValueCurrent;
            _squeezeSize = SqueezeUpBand - SqueezeDownBand;
        }

        private void SetNewDealEntryOrders()
        {
            Set_EN_Order_LONG(SqueezeUpBand);
            Set_EN_Order_SHORT(SqueezeDownBand);
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

        private void LockMoneySpentForPosition(Position p)
        {
            _availableMoney -= ConvertCoinsToMoney(p.OpenVolume, p.EntryPrice);
        }

        private void UnlockMoneyReleasedByPosition(Position p)
        {
            _availableMoney += CalcPositionCleanProfitMoney(p);
        }

        private bool MoveRecoveryTrailing_SL_IfPossible()
        {
            bool recoveryIsLong = _recoveryPosition.Direction == Side.Buy;
            decimal cur_SL_Price = _recoveryPosition.StopOrderPrice;
            decimal new_SL_Price = recoveryIsLong ? SqueezeDownBand : SqueezeUpBand;
            bool canShiftToBetterPlace = recoveryIsLong ? new_SL_Price > cur_SL_Price : new_SL_Price < cur_SL_Price;
            if (canShiftToBetterPlace)
            {
                decimal potentialRecoveryCleanProfit = CalcPositionCleanProfitMoney(_recoveryPosition, new_SL_Price);
                if (potentialRecoveryCleanProfit > 0)
                {
                    Set_SL_Order(_recoveryPosition, new_SL_Price);
                    return true;
                }
            }
            return false;
        }

        private bool IsMainPosition(Position p)
        {
            return MainPositionExists && p.Number == _mainPosition.Number;
        }

        private bool IsRecoveryPosition(Position p)
        {
            return RecoveryPositionExists && p.Number == _recoveryPosition.Number;
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

        private void SwapPositionsIfNeeded()
        {
            if (MainPositionExists && RecoveryPositionExists)
            {
                bool recoveryPositionLarger = _mainPosition.OpenVolume < _recoveryPosition.OpenVolume;
                if (recoveryPositionLarger)
                {
                    Position bufferPosition = _mainPosition;
                    _mainPosition = _recoveryPosition;
                    _recoveryPosition = bufferPosition;
                }
            }
        }

        private void CancelOpposite_EP_Order()
        {
            if (_mainPosition != null)
            {
                _bot.SellAtStopCancel();
                _bot.BuyAtStopCancel();
            }
        }

        private void SetMainPosition_TP_Order_Initial()
        {
            decimal moneyIn = CalcInitialMoneyVolume_MainPosition();
            decimal wantedCleanProfitMoney = moneyIn * WantedCleanProfitPercent.ValueDecimal / 100;
            decimal TP_price = _mainPosition.Direction == Side.Buy ?
                Calc_TP_Price_LONG_TakeWantedCleanProfit_MONEY(_mainPosition, wantedCleanProfitMoney) :
                Calc_TP_Price_SHORT_TakeWantedCleanProfit_MONEY(_mainPosition, wantedCleanProfitMoney);
            Set_TP_Order(_mainPosition, TP_price);
        }

        private void SetMainPosition_TP_Order_ToFullLossRecovery()
        {
            Set_TP_Order(_mainPosition, Calc_TP_Price_MainPosition_FinishDealOnBreakEven());
        }

        private void SetRecoveryPosition_EP_Order(decimal squeezeOppositeBandPriceRecentlyUsed)
        {
            if (_mainPosition.Direction == Side.Buy)
            {
                Set_EN_Order_SHORT(Calc_EP_Price_ForRecovery_MAIN_is_LONG(squeezeOppositeBandPriceRecentlyUsed));
            }
            else if (_mainPosition.Direction == Side.Sell)
            {
                Set_EN_Order_LONG(Calc_EP_Price_ForRecovery_MAIN_is_SHORT(squeezeOppositeBandPriceRecentlyUsed));
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
            Set_EN_Order(entryPrice, Side.Buy);
        }

        private void Set_EN_Order_SHORT(decimal entryPrice)
        {
            Set_EN_Order(entryPrice, Side.Sell);
        }

        private void Set_EN_Order(decimal entryPrice, Side side)
        {
            decimal coinsVolume = MainPositionExists ? 
                CalcCoinsVolume_RecoveryPosition(side, entryPrice) : 
                CalcCoinsVolume_MainPosition(side, entryPrice);
            if (coinsVolume > 0)
            {
                if (side == Side.Buy)
                {
                    _bot.BuyAtStop(coinsVolume, entryPrice, entryPrice, StopActivateType.HigherOrEqual, 100000);
                }
                else
                {
                    _bot.SellAtStop(coinsVolume, entryPrice, entryPrice, StopActivateType.LowerOrEqyal, 100000);
                }
            }
        }

        private decimal Calc_TP_Price_MainPosition_FinishDealOnBreakEven()
        {
            decimal wantedCleanProfitMoney = 0 - _availableMoney;
            return _mainPosition.Direction == Side.Buy ? 
                Calc_TP_Price_LONG_TakeWantedCleanProfit_MONEY(_mainPosition, wantedCleanProfitMoney) : 
                Calc_TP_Price_SHORT_TakeWantedCleanProfit_MONEY(_mainPosition, wantedCleanProfitMoney);
        }

        private decimal Calc_TP_Price_LONG_TakeWantedCleanProfit_MONEY(Position p, decimal wantedCleanProfitMoney)
        {
            decimal feeInPercents = _bot.ComissionValue;
            return (p.OpenVolume * p.EntryPrice * (100 + feeInPercents) + 100 * wantedCleanProfitMoney) / (p.OpenVolume * (100 - feeInPercents));
        }

        private decimal Calc_TP_Price_SHORT_TakeWantedCleanProfit_MONEY(Position p, decimal wantedCleanProfitMoney)
        {
            decimal feeInPercents = _bot.ComissionValue;
            return (p.OpenVolume * p.EntryPrice * (100 - feeInPercents) - 100 * wantedCleanProfitMoney) / (p.OpenVolume * (100 + feeInPercents));
        }

        private decimal Calc_EP_Price_ForRecovery_MAIN_is_LONG(decimal squeezeUpBandPriceRecentlyUsed)
        {
            return squeezeUpBandPriceRecentlyUsed - _squeezeSize * StartRecoveryDistanceInSqueezes.ValueDecimal;
        }

        private decimal Calc_EP_Price_ForRecovery_MAIN_is_SHORT(decimal squeezeDownBandPriceRecentlyUsed)
        {
            return squeezeDownBandPriceRecentlyUsed + _squeezeSize * StartRecoveryDistanceInSqueezes.ValueDecimal;
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
            return (volLong * EP_Long * (100 + fee) - volShort * EP_Short * (100 - fee) - 100 * _availableMoney) / (volLong * (100 - fee) - volShort * (100 + fee));
        }

        private decimal Calc_BE_Price_MAIN_is_SHORT(decimal volLong, decimal volShort, decimal EP_Long, decimal EP_Short)
        {
            decimal fee = _bot.ComissionValue;
            return (volLong * EP_Long * (100 + fee) - volShort * EP_Short * (100 - fee) - 100 * _availableMoney) / (volLong * (100 - fee) - volShort * (100 + fee));
        }

        private decimal CalcCoinsVolume_MainPosition(Side side, decimal price)
        {
            decimal coinsVolume = 0;
            decimal mainPositionVolumeInMoney = CalcInitialMoneyVolume_MainPosition();
            if (mainPositionVolumeInMoney > 0)
            {
                coinsVolume = ConvertMoneyToCoins(side, mainPositionVolumeInMoney, price);
            }
            return coinsVolume;
        }

        private decimal CalcCoinsVolume_RecoveryPosition(Side side, decimal price)
        {
            decimal coinsVolume = 0;
            decimal recoveryPositionVolumeInMoney = CalcInitialMoneyVolume_RecoveryPosition();
            if (recoveryPositionVolumeInMoney > 0)
            {
                if (RecoveryVolumeMode.ValueString == REINVESTMENT)
                {
                    recoveryPositionVolumeInMoney += _availableMoney;
                }
                coinsVolume = ConvertMoneyToCoins(side, recoveryPositionVolumeInMoney, price);
            }
            return coinsVolume;
        }

        private decimal CalcInitialMoneyVolume_MainPosition()
        {
            decimal mainPositionVolumeInPercents, recoveryPositionVolumeInPercents, totalDealVolumeInPercents;
            CalcVolumePercentages(out mainPositionVolumeInPercents, out recoveryPositionVolumeInPercents, out totalDealVolumeInPercents);
            return _balanceOnStart * mainPositionVolumeInPercents / totalDealVolumeInPercents;
        }

        private decimal CalcInitialMoneyVolume_RecoveryPosition()
        {
            decimal mainPositionVolumeInPercents, recoveryPositionVolumeInPercents, totalDealVolumeInPercents;
            CalcVolumePercentages(out mainPositionVolumeInPercents, out recoveryPositionVolumeInPercents, out totalDealVolumeInPercents);
            return _balanceOnStart * recoveryPositionVolumeInPercents / totalDealVolumeInPercents;
        }

        private void CalcVolumePercentages(out decimal mainPositionVolumeInPercents, out decimal recoveryPositionVolumeInPercents, out decimal totalDealVolumeInPercents)
        {
            mainPositionVolumeInPercents = 100;
            recoveryPositionVolumeInPercents = RecoveryVolumeMultiplier.ValueDecimal * mainPositionVolumeInPercents;
            totalDealVolumeInPercents = mainPositionVolumeInPercents + recoveryPositionVolumeInPercents;
        }

        private decimal CalcPositionCleanProfitMoney(Position p)
        {
            return CalcPositionCleanProfitMoney(p, p.ClosePrice);
        }

        private decimal CalcPositionCleanProfitMoney(Position p, decimal closePrice)
        {
            decimal moneyIn = p.OpenVolume * p.EntryPrice;
            decimal moneyOut = p.OpenVolume * closePrice;
            decimal profitMoney = p.Direction == Side.Buy ? moneyOut - moneyIn : moneyIn - moneyOut;
            decimal feeIn = moneyIn * _bot.ComissionValue / 100;
            decimal feeOut = moneyOut * _bot.ComissionValue / 100;
            decimal feeMoney = feeIn + feeOut;
            return profitMoney - feeMoney;
        }

        private decimal ConvertMoneyToCoins(Side side, decimal money, decimal price)
        {
            decimal moneyNeededForFee = money / 100 * _bot.ComissionValue;
            decimal moneyLeftForCoins = money - moneyNeededForFee;
            decimal coinsVolumeDirty = moneyLeftForCoins / price;
            return Math.Round(coinsVolumeDirty, VolumeDecimals.ValueInt);
        }

        private decimal ConvertCoinsToMoney(decimal coins, decimal price)
        {
            decimal coinsMoney = coins * price;
            decimal feeMoney = coinsMoney * _bot.ComissionValue / 100;
            return coinsMoney + feeMoney;
        }

        private bool IsEnoughDataAndEnoughMoneyAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool robotEnabled = Regime.ValueString == "On";
            bool enoughCandlesForBollinger = candlesCount > BollingerLength.ValueInt;
            bool enoughCandlesForBollingerSqueeze = candlesCount > BollingerSqueezePeriod.ValueInt;
            _balanceOnStart = _balanceOnStart == 0 ? _bot.Portfolio.ValueCurrent : _balanceOnStart;
            bool enoughMoney = CalcInitialMoneyVolume_RecoveryPosition() >= MinVolumeUSDT.ValueDecimal;
            return robotEnabled && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze && enoughMoney;
        }
    }
}
