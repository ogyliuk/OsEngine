using OsEngine.Charts.CandleChart.Elements;
using OsEngine.Charts.CandleChart.Indicators;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;

/***************** RESULTS: ****************
-== STATIC ==-
  Очень долгий REC (годы). OnlySort лучше потому что REC LONG объем выше (recovery процесс быстрее).
  
-== REINVESTMENT ==-
  Очень долгий REC (годы). OnlySort лучше потому что REC LONG объем выше (recovery процесс быстрее).

-== REINVESTMENT + SWAP ==-
  Оптимизация показывает что верная пропорция депо приблизительно [ MAIN : REC = 3 : 1 ]
  Восстановление до момента переворота может занимать ооочень много времени.  
  После переворота(ов) имеем очень широкую зону в которой можно зависнуть на долго. 

-== Оптимизация - лучшие результаты ==-
  ПЕРИОД : 999 дней
  ТФ : 5 min

  TakeProfit : Static + NO_SWAPS
  Mode: Reinvestment
  Критерий : Positions count
  Profit : 1472$ (147% / 4.41% per 1 month)
  MaxStuck : 457 days
  RecoveryVolumeMultiplier : 0.5
  StartRecoveryDistanceInSqueezes : 1.5
  MinWantedCleanProfitPercent : 0.3
  
  TakeProfit : Static + Swap
  Mode: Reinvestment
  Критерий : Total profit
  Profit : 3820$ (382% / 11.5% per 1 month)
  MaxStuck : 155 days
  RecoveryVolumeMultiplier : 0.4
  StartRecoveryDistanceInSqueezes : 1.8
  MinWantedCleanProfitPercent : 1.6
  MaxDistanceInPercentsToBreakevenPriceToTriggerSwap : 40

  TakeProfit : Trailing + Swap
  Mode: Reinvestment
  Критерий : Total profit
  Profit : 4200$ (420% / 12.6% per 1 month)
  MaxStuck : 296 days
  RecoveryVolumeMultiplier : 0.35
  StartRecoveryDistanceInSqueezes : 3.2
  MaxDistanceInPercentsToBreakevenPriceToTriggerSwap : 100
***************** RESULTS: ****************/
namespace OsEngine.Robots.Oleg.Good
{
    [Bot("MovingRecoveryZone")]
    public class MovingRecoveryZone : BotPanel
    {
        private static readonly String STATIC = "Static";
        private static readonly String REINVESTMENT = "Reinvestment";
        private static readonly String TRAILING = "Trailing";

        #region STATS
        private Situation _situation;
        private int _swapesCounter;
        private int _recoveriesCounter;
        #endregion

        private BotTabSimple _bot;
        private string _dealGuid;
        private decimal _lastUsedSqueezeUpBand;
        private decimal _lastUsedSqueezeDownBand;
        private decimal _balanceMoneyOnDealStart;
        private decimal _nonLockedMoneyAmount;
        private decimal _consumedProfitMoneyAmount;
        private Position _mainPosition;
        private Position _recoveryPosition;
        private List<Position> _dealPositions;

        private LineHorisontal _longEntryLine;
        private LineHorisontal _shortEntryLine;

        private BollingerWithSqueeze _bollingerWithSqueeze;

        private StrategyParameterInt BollingerLength;
        private StrategyParameterInt BollingerSqueezePeriod;
        private StrategyParameterDecimal BollingerDeviation;

        private StrategyParameterString Regime;
        private StrategyParameterString RecoveryVolumeMode;
        private StrategyParameterBool SwapsEnabled;
        private StrategyParameterDecimal RecoveryVolumeMultiplier;
        private StrategyParameterInt VolumeDecimals;
        private StrategyParameterDecimal MinVolumeUSDT;
        private StrategyParameterDecimal MinWantedCleanProfitPercent;
        private StrategyParameterDecimal StartRecoveryDistanceInSqueezes;
        private StrategyParameterString TakeProfitMode;
        private StrategyParameterInt MaxDistanceInPercentsToBreakevenPriceToTriggerSwap;

        private decimal MAX_PRICE { get { return 1000000m; } }
        private decimal MIN_PRICE { get { return _bot.Securiti.PriceStep; } }
        private bool MainPositionExists { get { return _mainPosition != null; } }
        private bool RecoveryPositionExists { get { return _recoveryPosition != null; } }
        private bool HasEntryOrdersSet { get { return _bot.PositionOpenerToStopsAll.Count > 0; } }
        private decimal LastUsedSqueezeSize
        {
            get
            {
                decimal squeezeSize = _lastUsedSqueezeUpBand - _lastUsedSqueezeDownBand;
                return squeezeSize > 0 ? squeezeSize : 0;
            }
        }
        private decimal AvailableAlreadyRecoveredCleanProfitMoney
        {
            get
            {
                return _dealPositions
                    .Where(p => p.State == PositionStateType.Done)
                    .Select(p => CalcPositionCleanProfitMoney_CLOSED_Position(p))
                    .Sum() - _consumedProfitMoneyAmount;
            }
        }

        public MovingRecoveryZone(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Simple);
            _bot = TabsSimple[0];
            _dealGuid = String.Empty;
            _situation = Situation.NONE;
            _swapesCounter = 0;
            _recoveriesCounter = 0;
            _dealPositions = new List<Position>();
            _longEntryLine = null;
            _shortEntryLine = null;

            Regime = CreateParameter("Regime", "Off", new[] { "Off", "On", "OnlyLong", "OnlyShort" }, "Base");
            VolumeDecimals = CreateParameter("Decimals in VOLUME", 0, 0, 4, 1, "Base");
            MinVolumeUSDT = CreateParameter("Min VOLUME USDT", 5m, 5m, 5m, 1m, "Base");
            RecoveryVolumeMode = CreateParameter("Recovery VOLUME MODE", REINVESTMENT, new[] { REINVESTMENT, STATIC }, "Base");
            SwapsEnabled = CreateParameter("Swaps enabled", false, "Base");
            RecoveryVolumeMultiplier = CreateParameter("Recovery VOLUME MULTIPLIER", 0.8m, 0.8m, 0.9m, 0.05m, "Base");
            StartRecoveryDistanceInSqueezes = CreateParameter("Start recovery DISTANCE in SQUEEZEs", 2m, 2m, 10, 0.1m, "Base");
            MinWantedCleanProfitPercent = CreateParameter("Min wanted CLEAN PROFIT %", 0.1m, 0.1m, 1, 0.1m, "Base");
            TakeProfitMode = CreateParameter("Take profit MODE", STATIC, new[] { STATIC, TRAILING }, "Base");
            MaxDistanceInPercentsToBreakevenPriceToTriggerSwap = CreateParameter("Max distance % to BE price to do swap", 5000, 50, 5000, 10, "Base");

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
            _bot.PositionNetVolumeChangeEvent += event_PositionVolumeChange_MANAGE_PARTIAL_MAIN_POS_CLOSE;

            this.ParametrsChangeByUser += event_ParametersChangedByUser;
            event_ParametersChangedByUser();
            OlegUtils.LogSeparationLine();
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
                ThrowException("Volume multiplier value is INVALID! It must be > 0 and < 1");
            }
        }

        private void event_CandleClosed_SQUEEZE_FOUND(List<Candle> candles)
        {
            RefreshEntryLinesOnChart(candles);

            if (IsEnoughDataAndEnabledToTrade())
            {
                CancelNewDealEntryOrderIfSqueezeLost(candles.Last());
                HandleNewSqueeze();
            }
        }

        private void event_PositionOpened_SET_ORDERS(Position p)
        {
            if (p != null && !_dealPositions.Contains(p) && p.State == PositionStateType.Open)
            {
                _dealPositions.Add(p);

                Cancel_EP_Orders();
                RecognizeAndSetupPosition(p);
                LockMoneySpentForPosition(p);
                bool swapped = SwapPositionsIfNeeded();

                if (MainPositionExists)
                {
                    if (RecoveryPositionExists)
                    {
                        _situation = Situation.TWO_POSES_ON_BE;
                        Cancel_TP_and_SL_Orders();
                        SetBothPositionsTo_BE_PriceExit();
                        if (swapped)
                        {
                            // Unreachable BE price should never happen after swap
                            // So, we don't expect this method to run
                            HandleUnreachable_BE_PriceAfterSwap();
                        }
                    }
                    else
                    {
                        _situation = Situation.FIRST_TRY;
                        SetMainPosition_TP_Order_InitialIfNeeded();
                        SetRecoveryPosition_EP_Order(_lastUsedSqueezeUpBand, _lastUsedSqueezeDownBand);
                    }
                }
            }
        }

        // REAL TRADING COMMENT : it can be the case when both positions are triggered to close by BE price
        // the first one will be a position with loss which is greater that initial entry money
        // that may lead to negative balance on accoutn which is not possibel and such closing may be
        // rejected/failed by exchange. So, we need to add control here to always close opposite position
        // first to guarantee some money on balance for second position successful close as well.
        private void event_PositionClosed_CONTINUE_OR_FINISH_DEAL(Position p)
        {
            if (p != null && p.State == PositionStateType.Done)
            {
                UnlockMoneyReleasedBy_FULLY_CLOSED_Position(p);

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
                    _recoveriesCounter++;
                    if (MainPositionExists)
                    {
                        bool bothPosesStayOnSingle_BE_ExitPoint = p.StopOrderRedLine == _mainPosition.ProfitOrderRedLine && !IsPriceUreachable(p.StopOrderRedLine);
                        bool closingOnlyRecoveryPositionNow = !PositionClosedBySL(p) || !bothPosesStayOnSingle_BE_ExitPoint;
                        if (closingOnlyRecoveryPositionNow)
                        {
                            _situation = Situation.MAIN_POS_ON_BE;
                            decimal cleanProfit = CalcPositionCleanProfitMoney_CLOSED_Position(p);
                            bool closedPartiallyMainPosition = ClosePartially_MainPosition_Or_ContinueRecovering(cleanProfit);
                            if (!closedPartiallyMainPosition)
                            {
                                SetMainPosition_TP_Order_ToFullLossRecovery();
                            }
                        }
                    }
                    else
                    {
                        FinishDeal();
                    }
                    _recoveryPosition = null;
                }
            }
        }

        private void event_PositionVolumeChange_MANAGE_PARTIAL_MAIN_POS_CLOSE(Position p, decimal volumeChangeCoins, decimal volumeChangePrice, DateTime volumeChangeTime)
        {
            bool volumeReduced = volumeChangeCoins < 0;
            if (IsMainPosition(p) && p.State != PositionStateType.Done && volumeReduced)
            {
                decimal absoluteVolumeChangeCoins = Math.Abs(volumeChangeCoins);

                OlegUtils.Log("MAIN POS volume has CHANGED! It has been REDUCED on {0} coins. " +
                    "NewVolume = {1}; EntryVolume = {2}", absoluteVolumeChangeCoins, p.OpenVolume, p.MaxVolume);

                UnlockMoneyReleasedBy_PARTIALLY_CLOSED_Position(p, absoluteVolumeChangeCoins, volumeChangePrice, volumeChangeTime);
                Cancel_TP_and_SL_Orders();
                SetMainPosition_TP_Order_ToFullLossRecovery();
                Cancel_EP_Orders();
                SetRecoveryPosition_EP_Order(_lastUsedSqueezeUpBand, _lastUsedSqueezeDownBand);
            }
        }

        private bool ClosePartially_MainPosition_Or_ContinueRecovering(decimal recoveredProfitMoney)
        {
            bool closedPartiallyMainPosition = false;
            if (recoveredProfitMoney > 0)
            {
                decimal mainPositionCoinsToClose;
                if (CanCloseSomeVolumeOnMainPosition(recoveredProfitMoney, out mainPositionCoinsToClose))
                {
                    _bot.CloseAtMarket(_mainPosition, mainPositionCoinsToClose);
                    closedPartiallyMainPosition = true;
                }
                else
                {
                    SetRecoveryPosition_EP_Order(_lastUsedSqueezeUpBand, _lastUsedSqueezeDownBand);
                }
            }
            return closedPartiallyMainPosition;
        }

        private bool CanCloseSomeVolumeOnMainPosition(decimal volumeToCloseInMoney, out decimal coinsToClose)
        {
            decimal minTradableMoney = MinVolumeUSDT.ValueDecimal;
            if (volumeToCloseInMoney >= minTradableMoney && SwapsEnabled.ValueBool)
            {
                decimal currentPrice = GetCurrentPrice();
                decimal volumeToCloseInCoins = CalcCoinsVolume_ToTakeWantedProfit(_mainPosition.Direction, _mainPosition.EntryPrice, currentPrice, -volumeToCloseInMoney);
                if (volumeToCloseInCoins > 0)
                {
                    bool shouldCloseMainPositionFully = volumeToCloseInCoins >= _mainPosition.OpenVolume;
                    if (shouldCloseMainPositionFully)
                    {
                        coinsToClose = _mainPosition.OpenVolume;
                        decimal volumeMoneyToBeUsedForClosure = ConvertCoinsToMoney(coinsToClose, currentPrice, PositionAction.EXIT);
                        if (volumeMoneyToBeUsedForClosure >= minTradableMoney)
                        {
                            OlegUtils.Log("Recovered profit = {0}$ is big enough to close MAIN position fully!", TruncateMoney(volumeToCloseInMoney));
                            return true;
                        }
                        else
                        {
                            OlegUtils.Log("Can't fully close MAIN position because its' volume {0} coins => " + 
                                "in money = {1}$ < {2}$ is too small. Continue recovering", coinsToClose, 
                                TruncateMoney(volumeMoneyToBeUsedForClosure), minTradableMoney);
                        }
                    }
                    else
                    {
                        coinsToClose = volumeToCloseInCoins;
                        decimal coinsToKeep = _mainPosition.OpenVolume - coinsToClose;
                        decimal volumeMoneyToKeep = ConvertCoinsToMoney(coinsToKeep, currentPrice, PositionAction.ENTRY);
                        bool leftVolumeIsTradable = volumeMoneyToKeep >= minTradableMoney;
                        if (leftVolumeIsTradable)
                        {
                            OlegUtils.Log("Going to reduce MAIN position volume on {0} coins ~ {1}$", coinsToClose, TruncateMoney(volumeToCloseInMoney));
                            return true;
                        }
                        else
                        {
                            OlegUtils.Log("Can't reduce MAIN position volume on {0} coins, because left volume in money = {1}$ < {2}$ is too small. " +
                                "Continue recovering", coinsToClose, TruncateMoney(volumeMoneyToKeep), minTradableMoney);
                        }
                    }
                }
                else
                {
                    OlegUtils.Log("Can't reduce MAIN position volume on {0} coins using money = {1}$ when " + 
                        "MIN tradable money = {2}$. Continue recovering", volumeToCloseInCoins, 
                        TruncateMoney(volumeToCloseInMoney), minTradableMoney);
                }
            }
            coinsToClose = 0;
            return false;
        }

        private void HandleUnreachable_BE_PriceAfterSwap()
        {
            // After swap distance between both poses can be very big and in case of move
            // into recovery direction we won't be doing any recoveries for a long time.
            // That's why we decide to exit rec pos by TP with LOSS = GAINED_PROFIT to have
            // ability enter new rec pos closer to a main pos and keep recovering again.
            decimal BE_price = Calc_BE_Price();
            if (IsPriceUreachable(BE_price))
            {
                // This order will always be executed before any possible move of REC SL
                // to new profitable squeeze beceause this TP is always stay closer to current price
                // Potential profitable squeeze is far from this point because we ignore all 
                // new squeezes which are non profitable against of REC pos EP, but this TP
                // order is not profitable, that's why it will be triggered first
                if (AvailableAlreadyRecoveredCleanProfitMoney > 0)
                {
                    SetRecoveryPosition_TP_Order_FinishWithLossEqualToExistingRecoveredProfit();
                }
                else
                {
                    // Do nothing because if we came into swap with no recovered profit,
                    // that means that we did swap immediately after previous swap. In such
                    // case recovery zone is small and there is no sense to exit recovery
                    // position at the point of entry (with 0 profit). Better to let price
                    // go and probably reach some squeeze behind the recovery position EP to
                    // exit and recover some profit
                }
            }
        }

        private void InitNewDealParameters()
        {
            _balanceMoneyOnDealStart = _bot.Portfolio.ValueCurrent;
            _nonLockedMoneyAmount = _bot.Portfolio.ValueCurrent;
            _consumedProfitMoneyAmount = 0;
        }

        private void SetNewDealEntryOrders(decimal squeezeUpBand, decimal squeezeDownBand)
        {
            if (Regime.ValueString == "On" || Regime.ValueString == "OnlyLong")
            {
                Set_EN_Order_LONG(squeezeUpBand);
            }
            if (Regime.ValueString == "On" || Regime.ValueString == "OnlyShort")
            {
                Set_EN_Order_SHORT(squeezeDownBand);
            }
        }

        private void CancelNewDealEntryOrderIfSqueezeLost(Candle closedCandle)
        {
            bool aboutToEnterNewDeal = !MainPositionExists && !RecoveryPositionExists && HasEntryOrdersSet;
            if (aboutToEnterNewDeal)
            {
                bool longOnlyMode = Regime.ValueString == "OnlyLong";
                bool shortOnlyMode = Regime.ValueString == "OnlyShort";
                bool priceWentOutFromSqueezeToShort = closedCandle.Low < _lastUsedSqueezeDownBand;
                bool priceWentOutFromSqueezeToLong = closedCandle.High > _lastUsedSqueezeUpBand;
                if ((longOnlyMode && priceWentOutFromSqueezeToShort) || (shortOnlyMode && priceWentOutFromSqueezeToLong))
                {
                    Cancel_EP_Orders();
                }
            }
        }

        private void HandleNewSqueeze()
        {
            bool lastCandleHasSqueeze = _bollingerWithSqueeze.ValuesSqueezeFlag.Last() > 0;
            if (lastCandleHasSqueeze)
            {
                bool squeezeWasUseful = false;
                decimal squeezeUpBand = _bollingerWithSqueeze.ValuesUp.Last();
                decimal squeezeDownBand = _bollingerWithSqueeze.ValuesDown.Last();

                bool noDeal = !MainPositionExists && !RecoveryPositionExists && !HasEntryOrdersSet;
                if (noDeal && HasEnoughMoneyToTrade())
                {
                    squeezeWasUseful = true;
                    InitNewDealParameters();
                    SetNewDealEntryOrders(squeezeUpBand, squeezeDownBand);
                }

                bool noRecoveryYet = MainPositionExists && !RecoveryPositionExists && GetLastClosedRecoveryPosition() == null;
                if (noRecoveryYet)
                {
                    MoveOrSetMainTrailing_SL_WhenMoreProfitableSqueezeFound(squeezeUpBand, squeezeDownBand);
                }

                bool recoveringDeal = MainPositionExists && RecoveryPositionExists;
                if (recoveringDeal)
                {
                    squeezeWasUseful = MoveRecoveryTrailing_SL_WhenMoreProfitableSqueezeFound(squeezeUpBand, squeezeDownBand);
                }

                bool alreadyClosedSomeRecoveries = GetLastClosedRecoveryPosition() != null;
                bool mainDealAfterClosedRecovery = MainPositionExists && !RecoveryPositionExists && alreadyClosedSomeRecoveries;
                if (mainDealAfterClosedRecovery)
                {
                    squeezeWasUseful = MoveRecoveryTrailing_EP_WhenMoreBeneficialSqueezeFound(squeezeUpBand, squeezeDownBand);
                }

                if (squeezeWasUseful)
                {
                    _lastUsedSqueezeUpBand = squeezeUpBand;
                    _lastUsedSqueezeDownBand = squeezeDownBand;
                }
            }
        }

        private void FinishDeal()
        {
            int days = (_dealPositions.Last().TimeClose - _dealPositions.First().TimeOpen).Days;
            OlegUtils.Log("DAYS = {0}", days == 0 ? 1 : days);

            // BREAK-POINT CONDITION : _dealPositions[0].Direction == Side.Sell && _situation == Situation.MAIN_POS_ON_BE && _recoveriesCounter == 1 && _swapesCounter == 0
            if (HasEntryOrdersSet)
            {
                Cancel_EP_Orders();
            }
            _dealGuid = String.Empty;
            _situation = Situation.NONE;
            _swapesCounter = 0;
            _recoveriesCounter = 0;
            _dealPositions = new List<Position>();
        }

        private bool HasEnoughMoneyToTrade()
        {
            return _bot.Portfolio.ValueCurrent >= MinVolumeUSDT.ValueDecimal;
        }

        private void LockMoneySpentForPosition(Position p)
        {
            decimal nonLockedMoneyAmountBefore = _nonLockedMoneyAmount;
            decimal moneyIn = ConvertCoinsToMoney(p.OpenVolume, p.EntryPrice, PositionAction.ENTRY);
            _nonLockedMoneyAmount -= moneyIn;
            OlegUtils.Log("HOLD {0} {1}: Deal={2}; {3} - {4}[{5}] = {6}; EntryPrice = {7}, PosID = {8}, Date = {9}",
                IsMainPosition(p) ? "MAIN" : "REC", p.Direction == Side.Buy ? "(long)" : "(short)", 
                _dealGuid.Substring(0, 8), TruncateMoney(nonLockedMoneyAmountBefore), TruncateMoney(moneyIn),
                p.OpenVolume, TruncateMoney(_nonLockedMoneyAmount), p.EntryPrice, p.Number, p.TimeOpen);
        }

        private void UnlockMoneyReleasedBy_FULLY_CLOSED_Position(Position p)
        {
            UnlockMoneyReleasedBy_CLOSED_Position(
                position: p, 
                closedVolumeCoins: GetLastCloseOrder_VOLUME(p),
                closePrice: GetLastCloseOrder_PRICE(p),
                closeTime: p.TimeClose,
                partialClose: false
            );
        }

        private void UnlockMoneyReleasedBy_PARTIALLY_CLOSED_Position(Position p, decimal closedVolumeCoins, decimal closePrice, DateTime closeTime)
        {
            UnlockMoneyReleasedBy_CLOSED_Position(
                position: p,
                closedVolumeCoins: closedVolumeCoins,
                closePrice: closePrice,
                closeTime: closeTime,
                partialClose: true
            );
        }

        private void UnlockMoneyReleasedBy_CLOSED_Position(Position position, decimal closedVolumeCoins, decimal closePrice, DateTime closeTime, bool partialClose)
        {
            UnlockMoneyReleasedBy_CLOSED_Position(
                positionNumber: position.Number,
                positionDirection: position.Direction,
                volumeCoins: closedVolumeCoins,
                entryPrice: position.EntryPrice,
                closePrice: closePrice,
                closeTime: closeTime,
                partialClose: partialClose
            );
        }

        private void UnlockMoneyReleasedBy_CLOSED_Position(int positionNumber, Side positionDirection, decimal volumeCoins, decimal entryPrice, decimal closePrice, DateTime closeTime, bool partialClose)
        {
            bool mainPosition = _mainPosition != null && _mainPosition.Number == positionNumber;
            decimal nonLockedMoneyAmountBefore = _nonLockedMoneyAmount;
            decimal moneyIn = ConvertCoinsToMoney(volumeCoins, entryPrice, PositionAction.ENTRY);
            decimal cleanProfitMoney = CalcPositionCleanProfitMoney(positionDirection, volumeCoins, entryPrice, closePrice);
            decimal moneyOut = moneyIn + cleanProfitMoney;
            _nonLockedMoneyAmount += moneyOut;
            if (partialClose)
            {
                _consumedProfitMoneyAmount += Math.Abs(cleanProfitMoney);
            }

            OlegUtils.Log("FREE {0}{1} : Deal={2}; {3} + {4} = {5}; ClosePrice = {6}, Profit = {7}, " + 
                "PosID = {8}, Date = {9}", partialClose ? "(part)" : String.Empty, mainPosition ? "MAIN" : "REC", 
                _dealGuid.Substring(0, 8), TruncateMoney(nonLockedMoneyAmountBefore), TruncateMoney(moneyOut), 
                TruncateMoney(_nonLockedMoneyAmount), TruncatePrice(closePrice), TruncateMoney(cleanProfitMoney), 
                positionNumber, closeTime);
        }

        private void MoveOrSetMainTrailing_SL_WhenMoreProfitableSqueezeFound(decimal newSqueezeUpBand, decimal newSqueezeDownBand)
        {
            if (TakeProfitMode.ValueString == TRAILING)
            {
                bool mainIsLong = _mainPosition.Direction == Side.Buy;
                decimal curTrailing_SL_Price = _mainPosition.StopOrderRedLine;
                decimal newTrailing_SL_Price = mainIsLong ? newSqueezeDownBand : newSqueezeUpBand;

                bool new_SL_PriceIsFarEnough = CalcPositionCleanProfitMoney_OPEN_Position(_mainPosition, newTrailing_SL_Price) > CalcMainPositionMinWantedProfitInMoney();
                if (new_SL_PriceIsFarEnough)
                {
                    bool canShiftExistingTrailing_SL_ToBetterPlace = false;
                    bool alreadyHaveTrailing_SL_Set = _mainPosition.StopOrderIsActiv;
                    if (alreadyHaveTrailing_SL_Set)
                    {
                        canShiftExistingTrailing_SL_ToBetterPlace = mainIsLong ?
                            newTrailing_SL_Price > curTrailing_SL_Price :
                            newTrailing_SL_Price < curTrailing_SL_Price;
                    }

                    bool needToSetNewTrailing_SL_Price = !alreadyHaveTrailing_SL_Set || canShiftExistingTrailing_SL_ToBetterPlace;
                    if (needToSetNewTrailing_SL_Price)
                    {
                        Set_SL_Order(_mainPosition, newTrailing_SL_Price);
                    }
                }
            }
        }

        private bool MoveRecoveryTrailing_SL_WhenMoreProfitableSqueezeFound(decimal newSqueezeUpBand, decimal newSqueezeDownBand)
        {
            bool recoveryIsLong = _recoveryPosition.Direction == Side.Buy;
            decimal cur_TP_Price = _recoveryPosition.ProfitOrderRedLine;
            decimal cur_SL_Price = _recoveryPosition.StopOrderRedLine;
            decimal new_SL_Price = recoveryIsLong ? newSqueezeDownBand : newSqueezeUpBand;
            bool canShiftToBetterPlace = recoveryIsLong ? new_SL_Price > cur_SL_Price : new_SL_Price < cur_SL_Price;
            if (canShiftToBetterPlace)
            {
                bool new_SL_InConflictWithExisting_TP = 
                    _recoveryPosition.ProfitOrderIsActiv && 
                    (recoveryIsLong ? new_SL_Price > cur_TP_Price : new_SL_Price < cur_TP_Price);
                if (new_SL_InConflictWithExisting_TP)
                {
                    ThrowException("Can't set for RECOVERY position SL = {0} when have TP = {1}", new_SL_Price, cur_TP_Price);
                }

                decimal potentialRecoveryCleanProfitMoney = CalcPositionCleanProfitMoney_OPEN_Position(_recoveryPosition, new_SL_Price);
                if (potentialRecoveryCleanProfitMoney > MinVolumeUSDT.ValueDecimal)
                {
                    Set_SL_Order(_recoveryPosition, new_SL_Price);
                    _situation = Situation.TWO_POSES_REC_ON_SHIFTED_EXIT;
                    return true;
                }
            }
            return false;
        }

        private bool MoveRecoveryTrailing_EP_WhenMoreBeneficialSqueezeFound(decimal newSqueezeUpBand, decimal newSqueezeDownBand)
        {
            bool recoveryIsLong = _mainPosition.Direction == Side.Sell;
            decimal cur_EP_Price = _bot.PositionOpenerToStopsAll
                .Where(p => p.Side == (recoveryIsLong ? Side.Buy : Side.Sell))
                .Select(p => p.PriceRedLine)
                .FirstOrDefault();

            bool noRecoveryEntryOrderSet = cur_EP_Price == 0;
            cur_EP_Price = noRecoveryEntryOrderSet ? (recoveryIsLong ? MAX_PRICE : MIN_PRICE) : cur_EP_Price;

            decimal new_EP_Price = Calc_EP_Price_RecoveryPosition(recoveryIsLong, newSqueezeUpBand, newSqueezeDownBand);
            bool canShiftToBetterPlace = recoveryIsLong ? new_EP_Price < cur_EP_Price : new_EP_Price > cur_EP_Price;
            if (canShiftToBetterPlace)
            {
                bool new_EP_reachedMainPosition_EP_Level = recoveryIsLong ? new_EP_Price < _mainPosition.EntryPrice : new_EP_Price > _mainPosition.EntryPrice;
                if (!new_EP_reachedMainPosition_EP_Level)
                {
                    Cancel_EP_Orders();
                    SetRecoveryPosition_EP_Order(newSqueezeUpBand, newSqueezeDownBand);
                    _situation = Situation.MAIN_POS_ON_BE_AND_REC_ON_SHIFTED_ENTRY;
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
                OlegUtils.Log("New Deal time OPEN = {0}", p.TimeOpen);
            }
            else
            {
                _recoveryPosition = p;
            }
            p.DealGuid = _dealGuid;
        }

        private bool SwapPositionsIfNeeded()
        {
            bool swapped = false;
            if (SwapsEnabled.ValueBool && MainPositionExists && RecoveryPositionExists)
            {
                decimal BE_PriceIfSwapped = Calc_BE_PriceIfSwapped();
                bool BE_Price_CloseEnoughToCurrentPrice = Is_BE_Price_CloseEnoughToCurrentPriceForSwapping(BE_PriceIfSwapped);
                bool swapRequired = !IsPriceUreachable(BE_PriceIfSwapped) && BE_Price_CloseEnoughToCurrentPrice;
                if (swapRequired)
                {
                    Position bufferPosition = _mainPosition;
                    _mainPosition = _recoveryPosition;
                    _recoveryPosition = bufferPosition;
                    _swapesCounter++;
                    OlegUtils.Log("Swap! Main is {0} now, PosID = {1}!", 
                        _mainPosition.Direction == Side.Buy ? "LONG" : "SHORT", _mainPosition.Number);
                    swapped = true;
                }
            }
            return swapped;
        }

        private bool Is_BE_Price_CloseEnoughToCurrentPriceForSwapping(decimal BE_Price)
        {
            decimal currentPrice = GetCurrentPrice();
            decimal upPrice = currentPrice > BE_Price ? currentPrice : BE_Price;
            decimal downPrice = BE_Price < currentPrice ? BE_Price : currentPrice;
            decimal distanceTo_BE_PriceInPercents = downPrice > 0 ? (upPrice * 100 / downPrice) - 100 : 1000000;
            bool closeEnoughForSwap = distanceTo_BE_PriceInPercents <= MaxDistanceInPercentsToBreakevenPriceToTriggerSwap.ValueInt;
            if (!IsPriceUreachable(BE_Price) && !closeEnoughForSwap)
            {
                OlegUtils.Log("Can't do swap because BE price = {0} on the {1}% distance (MAX = {2}%) " + 
                    "to current price = {3}", TruncatePrice(BE_Price), TruncateDecimal(distanceTo_BE_PriceInPercents, 2), 
                    MaxDistanceInPercentsToBreakevenPriceToTriggerSwap.ValueInt, TruncatePrice(currentPrice));
            }
            return closeEnoughForSwap;
        }

        private void Cancel_EP_Orders()
        {
            _bot.BuyAtStopCancel();
            _bot.SellAtStopCancel();
            DeleteEntryLinesFromChart();
        }

        private void Cancel_TP_and_SL_Orders()
        {
            if (_mainPosition != null)
            {
                _mainPosition.StopOrderIsActiv = false;
                _mainPosition.ProfitOrderIsActiv = false;
            }
            if (_recoveryPosition != null)
            {
                _recoveryPosition.StopOrderIsActiv = false;
                _recoveryPosition.ProfitOrderIsActiv = false;
            }
        }

        private LineHorisontal CreateEntryLineOnChart_LONG(decimal entryPrice)
        {
            return CreateLineOnChart("LONG entry line", "LONG entry", entryPrice);
        }

        private LineHorisontal CreateEntryLineOnChart_SHORT(decimal entryPrice)
        {
            return CreateLineOnChart("SHORT entry line", "SHORT entry", entryPrice);
        }

        private LineHorisontal CreateLineOnChart(string lineName, string lineLabel, decimal linePriceLevel)
        {
            LineHorisontal line = new LineHorisontal(lineName, "Prime", false);
            line.Value = linePriceLevel;
            line.TimeStart = _bot.CandlesAll[0].TimeStart;
            line.TimeEnd = _bot.CandlesAll[_bot.CandlesAll.Count - 1].TimeStart;
            line.Color = Color.White;
            line.Label = lineLabel;
            _bot.SetChartElement(line);
            return line;
        }

        private void DeleteEntryLinesFromChart()
        {
            if (_longEntryLine != null)
            {
                _bot.DeleteChartElement(_longEntryLine);
                _longEntryLine = null;
            }
            if (_shortEntryLine != null)
            {
                _bot.DeleteChartElement(_shortEntryLine);
                _shortEntryLine = null;
            }
        }

        private void RefreshEntryLinesOnChart(List<Candle> candles)
        {
            if (_longEntryLine != null)
            {
                _longEntryLine.TimeEnd = candles[candles.Count - 1].TimeStart;
                _longEntryLine.Refresh();
            }
            if (_shortEntryLine != null)
            {
                _shortEntryLine.TimeEnd = candles[candles.Count - 1].TimeStart;
                _shortEntryLine.Refresh();
            }
        }

        private void SetMainPosition_TP_Order_InitialIfNeeded()
        {
            if (TakeProfitMode.ValueString == STATIC)
            {
                decimal minWantedCleanProfitMoney = CalcMainPositionMinWantedProfitInMoney();
                decimal TP_price = _mainPosition.Direction == Side.Buy ?
                    Calc_EXIT_Price_LONG_TakeWantedCleanProfit_MONEY(_mainPosition, minWantedCleanProfitMoney) :
                    Calc_EXIT_Price_SHORT_TakeWantedCleanProfit_MONEY(_mainPosition, minWantedCleanProfitMoney);
                Set_TP_Order(_mainPosition, TP_price);
            }
        }

        private void SetMainPosition_TP_Order_ToFullLossRecovery()
        {
            decimal fullLossRecovery_TP_Price = Calc_EXIT_Price_FinishWithLossEqualToExistingRecoveredProfit(_mainPosition);
            OlegUtils.Log("NO LOSS main TP = {0}, CurPrice = {1}, PosID = {2}", 
                TruncatePrice(fullLossRecovery_TP_Price), TruncatePrice(GetCurrentPrice()), _mainPosition.Number);
            Set_TP_Order(_mainPosition, fullLossRecovery_TP_Price);
        }

        private void SetRecoveryPosition_TP_Order_FinishWithLossEqualToExistingRecoveredProfit()
        {
            decimal lossEqualToExistingRecoveredProfit_TP_Price = Calc_EXIT_Price_FinishWithLossEqualToExistingRecoveredProfit(_recoveryPosition);
            OlegUtils.Log("CONSUME EXISTING PROFIT recovery TP = {0}, ExistingProfit = {1}, PosID = {2}", 
                TruncatePrice(lossEqualToExistingRecoveredProfit_TP_Price), TruncateMoney(AvailableAlreadyRecoveredCleanProfitMoney), _recoveryPosition.Number);
            Set_TP_Order(_recoveryPosition, lossEqualToExistingRecoveredProfit_TP_Price);
        }

        private void SetRecoveryPosition_EP_Order(decimal squeezeUpBand, decimal squeezeDownBand)
        {
            bool recoveryIsLong = _mainPosition.Direction == Side.Sell;
            decimal recoveryPosition_EP_Price = Calc_EP_Price_RecoveryPosition(recoveryIsLong, squeezeUpBand, squeezeDownBand);
            if (recoveryIsLong)
            {
                Set_EN_Order_LONG(recoveryPosition_EP_Price);
            }
            else
            {
                Set_EN_Order_SHORT(recoveryPosition_EP_Price);
            }
        }

        private void SetBothPositionsTo_BE_PriceExit()
        {
            // PROBLEM: When we move to a point where BE price is unreachable
            // it means that if price will strongly move into main pos direction,
            // then we will stuck as no recoveries will happen
            // 
            // SOLUTION:
            // a) we can let rec pos to exit by SL at the point where it will provide loss = gained_profit
            // That will allow to keep moving into main pos direction to finish or start recovering
            // again closer to the price
            // b) we can let rec pos to exit at the main pos EP level. It will produce a loss,
            // but should allow to never stuck
            // 
            // DECISION: now we will never have available gained_profit to try (a) option because we decided
            // to consume it in order to decrease main pos volume each time when we recovered some profit
            // Thus, we go with implementation of option (b).

            decimal BE_price = Calc_BE_Price(loggingEnabled: true);
            
            decimal recovery_SL_Price = BE_price;
            if (IsPriceUreachable(BE_price))
            {
                recovery_SL_Price = _mainPosition.EntryPrice;
                OlegUtils.Log("In order to handle UNREACHABLE BE price, setting rec pos SL to main pos EP = {0}", TruncatePrice(recovery_SL_Price));
            }

            Set_SL_Order(_recoveryPosition, recovery_SL_Price);
            Set_TP_Order(_mainPosition, BE_price);
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
            decimal coinsVolume = MainPositionExists ? CalcCoinsVolume_RecoveryPosition(entryPrice) : CalcCoinsVolume_MainPosition(entryPrice);
            if (coinsVolume > 0)
            {
                if (side == Side.Buy)
                {
                    _bot.BuyAtStop(coinsVolume, entryPrice, entryPrice, StopActivateType.HigherOrEqual, 100000);
                    _longEntryLine = CreateEntryLineOnChart_LONG(entryPrice);
                }
                else
                {
                    _bot.SellAtStop(coinsVolume, entryPrice, entryPrice, StopActivateType.LowerOrEqyal, 100000);
                    _shortEntryLine = CreateEntryLineOnChart_SHORT(entryPrice);
                }
            }
        }

        private decimal Calc_EP_Price_RecoveryPosition(bool recoveryIsLong, decimal squeezeUpBand, decimal squeezeDownBand)
        {
            decimal distance = StartRecoveryDistanceInSqueezes.ValueDecimal;
            decimal EP_Price = recoveryIsLong ?
                squeezeDownBand + LastUsedSqueezeSize * distance :
                squeezeUpBand - LastUsedSqueezeSize * distance;
            return NormalizePrice(EP_Price);
        }

        private decimal Calc_EXIT_Price_FinishWithLossEqualToExistingRecoveredProfit(Position p)
        {
            decimal wantedCleanProfitMoney = AvailableAlreadyRecoveredCleanProfitMoney <= 0 ? 
                0 : -AvailableAlreadyRecoveredCleanProfitMoney;
            return p.Direction == Side.Buy ? 
                Calc_EXIT_Price_LONG_TakeWantedCleanProfit_MONEY(p, wantedCleanProfitMoney) : 
                Calc_EXIT_Price_SHORT_TakeWantedCleanProfit_MONEY(p, wantedCleanProfitMoney);
        }

        private decimal Calc_EXIT_Price_LONG_TakeWantedCleanProfit_MONEY(Position p, decimal wantedCleanProfitMoney)
        {
            // Profit will be a bit SMALLER than expected because of price rouding to SMALLER size
            decimal feeInPercents = _bot.ComissionValue;
            decimal exitPrice = (p.OpenVolume * p.EntryPrice * (100 + feeInPercents) + 100 * wantedCleanProfitMoney) / (p.OpenVolume * (100 - feeInPercents));
            return NormalizePrice(exitPrice);
        }

        private decimal Calc_EXIT_Price_SHORT_TakeWantedCleanProfit_MONEY(Position p, decimal wantedCleanProfitMoney)
        {
            // Profit will be a bit BIGGER than expected because of price rouding to SMALLER size
            decimal feeInPercents = _bot.ComissionValue;
            decimal exitPrice = (p.OpenVolume * p.EntryPrice * (100 - feeInPercents) - 100 * wantedCleanProfitMoney) / (p.OpenVolume * (100 + feeInPercents));
            return NormalizePrice(exitPrice);
        }

        private decimal Calc_BE_Price(bool loggingEnabled = false)
        {
            return Calc_BE_Price(_mainPosition.Direction, _mainPosition.OpenVolume, _recoveryPosition.OpenVolume, _mainPosition.EntryPrice, _recoveryPosition.EntryPrice, loggingEnabled);
        }

        private decimal Calc_BE_PriceIfSwapped(bool loggingEnabled = false)
        {
            return Calc_BE_Price(_recoveryPosition.Direction, _recoveryPosition.OpenVolume, _mainPosition.OpenVolume, _recoveryPosition.EntryPrice, _mainPosition.EntryPrice, loggingEnabled);
        }

        private decimal Calc_BE_Price(Side mainDirection, decimal mainVolume, decimal recoveryVolume, decimal main_EP, decimal recovery_EP, bool loggingEnabled = false)
        {
            // ************************************** NOTE: ******************************************
            // * 1) When recovery COINS volume becomes bigger than main COINS volume, then BE price  *
            // * can be negative which means that BE price doesn't exist for this situation.         *
            // * EXAMPLE: (deal start = 26.09.2022 11:05:00 | 5mADA2022to2023flat)                   *
            // * 2) Also we can't apply BE price when it is already reached by current price.        *
            // * 3) We can't apply BE price as well, when it is further than liquidation price for   *
            // one or both positions. In case of Binance Futures Cross Margin it is allowed to have  *
            // a huge liquidation price when in hedge situation. In this case liquidation price is   *
            // going to be calculated based on both positions PnL and free USDT assets. It is kind   *
            // of allowing to lose more than it was invested into position as long as you have       *
            // another position which in opposite direction and which allows to cover your loses.    *
            // In this case you will never finish deal with negative balance which is not acceptable *
            // for Exchange. You will never be able to have the same huge liquidation price with one *
            // position only. Exchange will allow you just lose everything you invested but not      *
            // more. So, your liquidation price will never be that big as in hedge situation.        *
            // *                                                                                     *
            // * It is OK, it just means that we can only finish deal by exiting recoveryPos         *
            // * first and mainPos after. There is no way to finish deal by this BE price from       *
            // * mentioned cases above.                                                              *
            // * In these cases we just set main_TP and recovery_SL at unreachably far values and    *
            // wait for recovery position to recover some money again.                               *
            // ***************************************************************************************

            decimal BE_price = mainDirection == Side.Sell ?
                Calc_BE_Price_ByFormula(recoveryVolume, mainVolume, recovery_EP, main_EP, AvailableAlreadyRecoveredCleanProfitMoney) :
                Calc_BE_Price_ByFormula(mainVolume, recoveryVolume, main_EP, recovery_EP, AvailableAlreadyRecoveredCleanProfitMoney);

            if (loggingEnabled)
            {
                OlegUtils.Log("BE = {0}; CurPrice = {1}; recCleanProfitMoney = {2}", 
                    TruncatePrice(BE_price), TruncatePrice(GetCurrentPrice()), TruncateMoney(AvailableAlreadyRecoveredCleanProfitMoney));
            }

            bool BE_PriceNegative = BE_price < 0;
            bool BE_PriceAlreadyReached = mainDirection == Side.Buy ?
                GetCurrentPrice() >= BE_price : GetCurrentPrice() <= BE_price;

            if (BE_PriceNegative || BE_PriceAlreadyReached)
            {
                decimal unreachablePrice = mainDirection == Side.Buy ? MAX_PRICE : MIN_PRICE;
                if (loggingEnabled)
                {
                    OlegUtils.Log("Unable to set BE price = {0}; CUR price = {1}; MAIN pos is {2}! " +
                        "Will use {3} value instead.", TruncatePrice(BE_price), GetCurrentPrice(),
                        mainDirection == Side.Buy ? "LONG" : "SHORT",
                        unreachablePrice.ToStringWithNoEndZero());
                }
                BE_price = unreachablePrice;
            }

            return BE_price;
        }

        private decimal Calc_BE_Price_ByFormula(decimal volLong, decimal volShort, decimal EP_Long, decimal EP_Short, decimal availableAlreadyRecoveredCleanProfitMoney)
        {
            decimal fee = _bot.ComissionValue;
            decimal BE_Price = (volLong * EP_Long * (100 + fee) - volShort * EP_Short * (100 - fee) - 100 * availableAlreadyRecoveredCleanProfitMoney) / (volLong * (100 - fee) - volShort * (100 + fee));
            return NormalizePrice(BE_Price);
        }

        private decimal CalcCoinsVolume_RecoveryPosition_ByPriceBE_MAIN_is_LONG(decimal volLong, decimal BE_Price, decimal EP_Long, decimal EP_Short, decimal availableMoney)
        {
            decimal fee = _bot.ComissionValue;
            return (volLong * EP_Long * (100 + fee) - BE_Price * volLong * (100 - fee) - 100 * availableMoney) / (EP_Short * (100 - fee) - BE_Price * (100 + fee));
        }

        private decimal CalcCoinsVolume_RecoveryPosition_ByPriceBE_MAIN_is_SHORT(decimal volShort, decimal BE_Price, decimal EP_Long, decimal EP_Short, decimal availableMoney)
        {
            decimal fee = _bot.ComissionValue;
            return (BE_Price * volShort * (100 + fee) - volShort * EP_Short * (100 - fee) - 100 * availableMoney) / (BE_Price * (100 - fee) - EP_Long * (100 + fee));
        }

        private decimal CalcCoinsVolume_MainPosition(decimal entryPrice)
        {
            decimal coinsVolume = 0;
            decimal mainPositionVolumeInMoney = CalcInitialMoneyVolume_MainPosition();
            if (mainPositionVolumeInMoney > 0)
            {
                coinsVolume = ConvertMoneyToCoins(mainPositionVolumeInMoney, entryPrice);
            }
            return coinsVolume;
        }

        private decimal CalcCoinsVolume_RecoveryPosition(decimal entryPrice)
        {
            decimal availableMoneyForRecovery = CalcInitialMoneyVolume_RecoveryPosition();

            bool reinvestmentEnabled = RecoveryVolumeMode.ValueString == REINVESTMENT;
            if (reinvestmentEnabled)
            {
                availableMoneyForRecovery = _nonLockedMoneyAmount;
            }

            decimal coinsVolume = ConvertMoneyToCoins(availableMoneyForRecovery, entryPrice);

            if (!SwapsEnabled.ValueBool)
            {
                decimal volumeStep = GetVolumeStep();
                decimal BE_Price = Calc_BE_Price(_mainPosition.Direction, _mainPosition.OpenVolume, coinsVolume, _mainPosition.EntryPrice, entryPrice);
                decimal moneyNeeded = ConvertCoinsToMoney(coinsVolume, BE_Price, PositionAction.ENTRY);
                bool enoughMoney = availableMoneyForRecovery > moneyNeeded;

                while (coinsVolume > 0 && (IsPriceUreachable(BE_Price) || !enoughMoney))
                {
                    coinsVolume -= volumeStep;
                    BE_Price = Calc_BE_Price(_mainPosition.Direction, _mainPosition.OpenVolume, coinsVolume, _mainPosition.EntryPrice, entryPrice);
                    moneyNeeded = ConvertCoinsToMoney(coinsVolume, BE_Price, PositionAction.ENTRY);
                    enoughMoney = availableMoneyForRecovery > moneyNeeded;
                }
            }

            if (coinsVolume <= 0)
            {
                ThrowException("Entry volume can't be <= 0!");
            }

            return coinsVolume;
        }

        public decimal CalcCoinsVolume_ToTakeWantedProfit(Side positionDirection, decimal entryPrice, decimal takeProfitPrice, decimal wantedCleanProfit)
        {
            decimal coinsVolumeDirty = positionDirection == Side.Buy ?
                CalcCoinsVolume_ToTakeWantedProfit_LONG(entryPrice, takeProfitPrice, wantedCleanProfit) :
                CalcCoinsVolume_ToTakeWantedProfit_SHORT(entryPrice, takeProfitPrice, wantedCleanProfit);
            return TruncateDecimal(coinsVolumeDirty, VolumeDecimals.ValueInt);
        }

        private decimal CalcCoinsVolume_ToTakeWantedProfit_LONG(decimal entryPrice, decimal takeProfitPrice, decimal wantedCleanProfit)
        {
            decimal fee = _bot.ComissionValue;
            return (100 * wantedCleanProfit) / (100 * (takeProfitPrice - entryPrice) - fee * (entryPrice + takeProfitPrice));
        }

        private decimal CalcCoinsVolume_ToTakeWantedProfit_SHORT(decimal entryPrice, decimal takeProfitPrice, decimal wantedCleanProfit)
        {
            decimal fee = _bot.ComissionValue;
            return (100 * wantedCleanProfit) / (100 * (entryPrice - takeProfitPrice) - fee * (entryPrice + takeProfitPrice));
        }

        private decimal CalcInitialMoneyVolume_MainPosition()
        {
            decimal mainPositionVolumeInPercents, recoveryPositionVolumeInPercents, totalDealVolumeInPercents;
            CalcVolumePercentages(out mainPositionVolumeInPercents, out recoveryPositionVolumeInPercents, out totalDealVolumeInPercents);
            return _balanceMoneyOnDealStart * mainPositionVolumeInPercents / totalDealVolumeInPercents;
        }

        private decimal CalcInitialMoneyVolume_RecoveryPosition()
        {
            decimal mainPositionVolumeInPercents, recoveryPositionVolumeInPercents, totalDealVolumeInPercents;
            CalcVolumePercentages(out mainPositionVolumeInPercents, out recoveryPositionVolumeInPercents, out totalDealVolumeInPercents);
            return _balanceMoneyOnDealStart * recoveryPositionVolumeInPercents / totalDealVolumeInPercents;
        }

        private void CalcVolumePercentages(out decimal mainPositionVolumeInPercents, out decimal recoveryPositionVolumeInPercents, out decimal totalDealVolumeInPercents)
        {
            mainPositionVolumeInPercents = 100;
            recoveryPositionVolumeInPercents = RecoveryVolumeMultiplier.ValueDecimal * mainPositionVolumeInPercents;
            totalDealVolumeInPercents = mainPositionVolumeInPercents + recoveryPositionVolumeInPercents;
        }

        private decimal CalcMainPositionMinWantedProfitInMoney()
        {
            decimal moneyIn = ConvertCoinsToMoney(_mainPosition.OpenVolume, _mainPosition.EntryPrice, PositionAction.ENTRY);
            return moneyIn * MinWantedCleanProfitPercent.ValueDecimal / 100;
        }

        private decimal CalcPositionCleanProfitMoney_CLOSED_Position(Position p)
        {
            return CalcPositionCleanProfitMoney(p.Direction, GetLastCloseOrder_VOLUME(p), p.EntryPrice, GetLastCloseOrder_PRICE(p));
        }

        private decimal CalcPositionCleanProfitMoney_OPEN_Position(Position p, decimal closePrice)
        {
            return CalcPositionCleanProfitMoney(p.Direction, p.OpenVolume, p.EntryPrice, closePrice);
        }

        private decimal CalcPositionCleanProfitMoney(Side positionDirection, decimal volumeCoins, decimal entryPrice, decimal closePrice)
        {
            decimal moneyIn = volumeCoins * entryPrice;
            decimal moneyOut = volumeCoins * closePrice;
            decimal profitMoney = positionDirection == Side.Buy ? moneyOut - moneyIn : moneyIn - moneyOut;
            decimal feeIn = moneyIn * _bot.ComissionValue / 100;
            decimal feeOut = moneyOut * _bot.ComissionValue / 100;
            decimal feeMoney = feeIn + feeOut;
            return profitMoney - feeMoney;
        }

        private decimal ConvertMoneyToCoins(decimal money, decimal price)
        {
            decimal moneyNeededForFee = money / 100 * _bot.ComissionValue;
            decimal moneyLeftForCoins = money - moneyNeededForFee;
            decimal coinsVolumeDirty = moneyLeftForCoins / price;
            return TruncateDecimal(coinsVolumeDirty, VolumeDecimals.ValueInt);
        }

        private decimal ConvertCoinsToMoney(decimal coins, decimal price, PositionAction action)
        {
            decimal moneySpentForCoins = coins * price;
            decimal moneySpentForFee = moneySpentForCoins * _bot.ComissionValue / 100;
            return action == PositionAction.ENTRY ? 
                moneySpentForCoins + moneySpentForFee : 
                moneySpentForCoins - moneySpentForFee;
        }

        private Position GetLastClosedRecoveryPosition()
        {
            for (int i = _dealPositions.Count - 1; i >= 0; i--)
            {
                if (_dealPositions[i].State == PositionStateType.Done)
                {
                    return _dealPositions[i];
                }
            }
            return null;
        }

        private decimal TruncateMoney(decimal money)
        {
            return TruncateDecimal(money, 2);
        }

        private decimal TruncatePrice(decimal price)
        {
            decimal priceFromOrderBook = TabsSimple != null && TabsSimple.Count > 0 ? TabsSimple[0].PriceBestAsk : 0;
            if (priceFromOrderBook > 0)
            {
                int priceDecimalDigitsNumber = GetDecimalDigitsNumber(priceFromOrderBook);
                price = TruncateDecimal(price, priceDecimalDigitsNumber);
            }
            return price;
        }

        private int GetDecimalDigitsNumber(decimal someNumber)
        {
            string numberAsText = someNumber.ToString().TrimEnd('0');
            int decimalPointIndex = numberAsText.IndexOf(Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            string integerPartAndPoint = numberAsText.Substring(0, decimalPointIndex + 1);
            string decimalPart = numberAsText.Replace(integerPartAndPoint, String.Empty);
            return decimalPart.Length;
        }

        private decimal TruncateDecimal(decimal decimalNumber, int decimalDigitsCount)
        {
            if (decimalDigitsCount < 0)
            {
                ThrowException("DecimalDigitsCount cannot be less than zero");
            }
            string decimalNumberAsText = decimalNumber.ToString();
            int decimalPointIndex = decimalNumberAsText.IndexOf(Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            if (decimalPointIndex < 0)
            {
                return decimalNumber;
            }
            int newLength = decimalPointIndex + decimalDigitsCount + 1;
            while (newLength > decimalNumberAsText.Length)
            {
                newLength--;
            }
            return Convert.ToDecimal(decimalNumberAsText.Substring(0, newLength).TrimEnd('0'));
        }

        private bool IsEnoughDataAndEnabledToTrade()
        {
            int candlesCount = _bot.CandlesAll != null ? _bot.CandlesAll.Count : 0;
            bool robotEnabled = Regime.ValueString == "On" || Regime.ValueString == "OnlyLong" || Regime.ValueString == "OnlyShort";
            bool enoughCandlesForBollinger = candlesCount > BollingerLength.ValueInt;
            bool enoughCandlesForBollingerSqueeze = candlesCount > BollingerSqueezePeriod.ValueInt;
            _balanceMoneyOnDealStart = _balanceMoneyOnDealStart == 0 ? _bot.Portfolio.ValueCurrent : _balanceMoneyOnDealStart;
            return robotEnabled && enoughCandlesForBollinger && enoughCandlesForBollingerSqueeze;
        }

        private decimal GetCurrentPrice()
        {
            return _bot != null && _bot.CandlesAll != null && _bot.CandlesAll.Count > 0 ? 
                _bot.CandlesAll.Last().Close : 0;
        }

        private decimal GetLastCloseOrder_PRICE(Position p)
        {
            return p.CloseOrders.Last().PriceReal;
        }

        private decimal GetLastCloseOrder_VOLUME(Position p)
        {
            return p.CloseOrders.Last().VolumeExecute;
        }

        private bool PositionClosedBySL(Position p)
        {
            decimal lastCloseOrderPrice = GetLastCloseOrder_PRICE(p);
            decimal distanceTP = Math.Abs(p.ProfitOrderRedLine - lastCloseOrderPrice);
            decimal distanceSL = Math.Abs(p.StopOrderRedLine - lastCloseOrderPrice);
            return distanceSL < distanceTP;
        }

        private bool IsPriceUreachable(decimal price)
        {
            return price == MIN_PRICE || price == MAX_PRICE;
        }

        private decimal GetVolumeStep()
        {
            if (VolumeDecimals.ValueInt != 0)
            {
                string decimalZeros = String.Empty;
                for (int i = 1; i < VolumeDecimals.ValueInt; i++)
                {
                    decimalZeros += "0";
                }
                string volumeStepString = String.Format("0{0}{1}1", Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator, decimalZeros);
                return Decimal.Parse(volumeStepString);
            }
            return 1m;
        }

        private decimal NormalizePrice(decimal price)
        {
            return price - price % _bot.Securiti.PriceStep;
        }

        private void ThrowException(string messageTemplate, params object[] messageArgs)
        {
            string message = "ERROR : " + String.Format(messageTemplate, messageArgs);
            OlegUtils.Log(message);
            throw new Exception(message);
        }
    }

    enum Situation
    {
        NONE,
        FIRST_TRY,
        MAIN_POS_ON_BE,
        TWO_POSES_ON_BE,
        TWO_POSES_REC_ON_SHIFTED_EXIT,
        MAIN_POS_ON_BE_AND_REC_ON_SHIFTED_ENTRY
    }

    enum PositionAction
    {
        ENTRY,
        EXIT
    }
}
