﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using OsEngine.Entity;
using OsEngine.Logging;
using OsEngine.Market;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.OsTrader.Panels.Attributes;

namespace OsEngine.Robots.FoundBots
{
    [Bot("MasterBotClassic")]
    public class MasterBotClassic : BotPanel
    {

        #region общий портфель

        public static decimal StaticPortfolioValue
        {
            get { return _staticPortfolioValue; }
            set
            {
                _staticPortfolioValue = value;
                if (StaticPortfolioChangedEvent != null)
                {
                    StaticPortfolioChangedEvent();
                }
            }
        }
        private static decimal _staticPortfolioValue;

        private static bool _staticPortfolioLoaded;

        private static void LoadStaticPortfolio()
        {
            if (_staticPortfolioLoaded)
            {
                return;
            }
            _staticPortfolioLoaded = true;


            if (!File.Exists(@"Engine\AdminCapacityStaticPartSave.txt"))
            {
                return;
            }
            try
            {
                using (StreamReader reader = new StreamReader(@"Engine\AdminCapacityStaticPartSave.txt"))
                {
                    StaticPortfolioValue = reader.ReadLine().ToDecimal();

                    reader.Close();
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
            }

        }

        private static void SaveStaticPortfolio()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(@"Engine\AdminCapacityStaticPartSave.txt", false)
                )
                {
                    writer.WriteLine(StaticPortfolioValue);
                    writer.Close();
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.ToString());
            }
        }

        public static event Action StaticPortfolioChangedEvent;

        #endregion

        #region сервисный код, настройки

        public MasterBotClassic(string name, StartProgram startProgram) : base(name, startProgram)
        {
            LoadStaticPortfolio();

            Regime = CreateParameter("Regime", "Off", new[]
            {
                "Off", "On"
            });

            TimeStart = CreateParameterTimeOfDay("Начало торгов", 0, 0, 0, 0);
            TimeEnd = CreateParameterTimeOfDay("Конец торгов", 24, 0, 0, 0);

            List<string> strategies = BotFactory.GetScriptsNamesStrategy();
            CurrentStrategy = CreateParameter("Встроенная стратегия", "None", strategies.ToArray());

            VolumeOnPosition = CreateParameter("Объём входа", 10, 1.0m, 50, 4);

            VolumeRegime = CreateParameter("Тип объёма", "Валюта контракта", new[]
{
                "Кол-во контрактов", "Валюта контракта", "% от Общего объёма портфеля"
            });

            AllPortfolioValue = CreateParameter("Общий объём портфеля", 5000m, 1, 50000, 100);

            if (StaticPortfolioValue != 0)
            {
                AllPortfolioValue.ValueDecimal = StaticPortfolioValue;
            }
            else if (StaticPortfolioValue == 0)
            {
                StaticPortfolioValue = AllPortfolioValue.ValueDecimal;
            }

            AllPortfolioValue.ValueChange += () =>
            {
                if (StaticPortfolioValue == AllPortfolioValue.ValueDecimal)
                {
                    return;
                }
                StaticPortfolioValue = AllPortfolioValue.ValueDecimal;
                SaveStaticPortfolio();
            };

            StaticPortfolioChangedEvent += () =>
            {
                if (AllPortfolioValue.ValueDecimal == StaticPortfolioValue)
                {
                    return;
                }
                AllPortfolioValue.ValueDecimal = StaticPortfolioValue;
            };

            VolumeDecimals = CreateParameter("Знаков в объёме после запятой", 2, 1, 50, 4);

            MaxPositionDuplicateCount = CreateParameter("Макс. кол-во позиций для дублирования", 1, 1, 15, 1);

            ShowSlaveChartDialog = CreateParameterButton("Окно встроенной стратегии");
            ShowSlaveChartDialog.UserClickOnButtonEvent += ShowSlaveChartDialog_UserClickOnButtonEvent;

            ShowSlaveParameters = CreateParameterButton("Параметры встроенной стратегии");
            ShowSlaveParameters.UserClickOnButtonEvent += ShowSlaveParameters_UserClickOnButtonEvent;

            SlippageInter = CreateParameter("Проскальзывание вход %", 0.05m, 1, 50, 4);
            SlippageExit = CreateParameter("Проскальзывание выход %", 0.15m, 1, 50, 4);
            SlippageSecondInter = CreateParameter("Проскальзывание вход 2 %", 0.15m, 1, 50, 4);
            SlippageSecondExit = CreateParameter("Проскальзывание выход 2 %", 0.25m, 1, 50, 4);
            OpenOrderLifeTime = CreateParameter("Время жизни ордера", 60, 1, 50, 4);
            MaxOrderExecutionDeviation = CreateParameter("Макс отклонение для повторного исполнения %", 0.05m, 1m, 50, 4);
            InterTime = CreateParameter("Время на попытки повторного входа сек", 600, 1, 5000, 4);
            ShiftToStopOpdersValue = CreateParameter("Отступ для входов и стопов %", 0.01m, 1, 50, 4);

            CreateSlave();

            ParametrsChangeByUser += ParametrsChange;

            Thread worker = new Thread(Worker);
            worker.Start();

            Thread support = new Thread(PositionSupportThread);
            support.IsBackground = true;
            support.Start();

            ClearDataDirectory();

            DeleteEvent += MasterBotClassic_DeleteEvent;
        }

        private void MasterBotClassic_DeleteEvent()
        {
            ClearSavePoses("firstOpen");
            ClearSavePoses("secondOpen");
            ClearSavePoses("firstClose");
            ClearSavePoses("secondClose");

            if (_slave != null)
            {
                _slave.Delete();
            }
            if (_botLikeTester != null)
            {
                _botLikeTester.Delete();
            }
            _isDeteted = true;
        }

        private void ShowSlaveParameters_UserClickOnButtonEvent()
        {
            if (_slave == null)
            {
                MessageBox.Show("Slave Bot is not created");
                return;
            }
            _slave.ShowParametrDialog();
        }

        private void ParametrsChange()
        {
            CreateSlave();
        }

        public override string GetNameStrategyType()
        {
            return "MasterBotClassic";
        }

        public override void ShowIndividualSettingsDialog()
        {

        }

        public StrategyParameterString Regime;

        public StrategyParameterString CurrentStrategy;

        public StrategyParameterDecimal VolumeOnPosition;

        public StrategyParameterString VolumeRegime;

        public StrategyParameterInt VolumeDecimals;

        public StrategyParameterInt MaxPositionDuplicateCount;

        public StrategyParameterButton ShowSlaveParameters;

        public StrategyParameterButton ShowSlaveChartDialog;

        // сопровождение позиции

        public StrategyParameterTimeOfDay TimeStart;

        public StrategyParameterTimeOfDay TimeEnd;

        public StrategyParameterDecimal SlippageInter;

        public StrategyParameterDecimal SlippageExit;

        public StrategyParameterDecimal SlippageSecondInter;

        public StrategyParameterDecimal SlippageSecondExit;

        public StrategyParameterInt InterTime;

        public StrategyParameterDecimal MaxOrderExecutionDeviation;

        public StrategyParameterInt OpenOrderLifeTime;

        public StrategyParameterDecimal AllPortfolioValue;

        public StrategyParameterDecimal ShiftToStopOpdersValue;

        #endregion

        #region сабСтратегия

        private void CreateSlave()
        {
            if (_slave != null)
            {
                string strategyName = _slave.GetType().Name;
                if (strategyName == CurrentStrategy.ValueString)
                {
                    return;
                }
            }

            if (CurrentStrategy.ValueString == "None")
            {
                return;
            }

            string name = CurrentStrategy.ValueString;

            _slave = BotFactory.GetStrategyForName(
                CurrentStrategy.ValueString, "Slave" + NameStrategyUniq,
                StartProgram.IsOsTrader, true);

            RebuildTabs();
        }

        private void RebuildTabs()
        {
            ClearTabs();

            for (int i = 0; _slave.TabsSimple != null && i < _slave.TabsSimple.Count; i++)
            {
                TabCreate(BotTabType.Simple);
                TabsSimple[TabsSimple.Count - 1].CandleFinishedEvent += AdminCapacityCreator_CandleFinishedEvent;
                TabsSimple[TabsSimple.Count - 1].NewTickEvent += AdminCapacityCreator_NewTickEvent;
                TabsSimple[TabsSimple.Count - 1].PositionBuyAtStopActivateEvent += AdminCapacityCreator_PositionBuyAtStopActivateEvent;
                TabsSimple[TabsSimple.Count - 1].PositionSellAtStopActivateEvent += AdminCapacityCreator_PositionSellAtStopActivateEvent;
                TabsSimple[TabsSimple.Count - 1].PositionStopActivateEvent += AdminCapacityCreator_PositionStopActivateEvent;
                TabsSimple[TabsSimple.Count - 1].PositionProfitActivateEvent += AdminCapacityCreator_PositionProfitActivateEvent;

                TabsSimple[TabsSimple.Count - 1].ManualPositionSupport.DoubleExitIsOn = false;
                TabsSimple[TabsSimple.Count - 1].ManualPositionSupport.SecondToCloseIsOn = false;
                TabsSimple[TabsSimple.Count - 1].ManualPositionSupport.SecondToOpenIsOn = false;
            }

            for (int i = 0; _slave.TabsIndex != null && i < _slave.TabsIndex.Count; i++)
            {
                TabCreate(BotTabType.Index);
            }

            for (int i = 0; _slave.TabsCluster != null && i < _slave.TabsCluster.Count; i++)
            {
                TabCreate(BotTabType.Cluster);
            }
        }

        private BotPanel _slave;

        private void ShowSlaveChartDialog_UserClickOnButtonEvent()
        {
            if (CurrentStrategy.ValueString == "None")
            {
                return;
            }

            if (TabsSimple == null ||
                TabsSimple.Count == 0
                || TabsSimple[0].IsConnected == false)
            {
                return;
            }

            DateTime timeStartWaiting = DateTime.Now;

            _botLikeTester = TestRobot(CurrentStrategy.ValueString, TabsSimple, _slave.Parameters, StartProgram.IsTester);

            TabsSimple[0].SetNewLogMessage("Test bot Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

            if (_botLikeTester != null)
            {
                CloseParameterDialog();
                _slave.CloseParameterDialog();
                _botLikeTester.ShowChartDialog();
                _botLikeTester.ChartClosedEvent += Bot_ChartClosedEvent;
            }
        }

        private void Bot_ChartClosedEvent(string obj)
        {
            if (_botLikeTester != null)
            {
                _botLikeTester.ChartClosedEvent -= Bot_ChartClosedEvent;
                _botLikeTester.Clear();
                _botLikeTester.Delete();
                _botLikeTester = null;
            }

            ClearLastSession();
        }

        #endregion

        #region работа встроенного оптимизатора

        private OptimizerDataStorage _storageLast;

        private OptimizerServer _serverLast;

        private bool _lastTestIsOver = true;

        private string _botStarterLocker = "botsLocker";

        private BotPanel TestRobot(string strategyName, List<BotTabSimple> tabs, List<IIStrategyParameter> parametrs, StartProgram startProgram)
        {
            try
            {
                lock (_botStarterLocker)
                {
                    if (_lastTestIsOver == false)
                    {
                        return null;
                    }
                    _lastTestIsOver = false;
                }

                DateTime timeStartStorageCreate = DateTime.Now;

                _storageLast = CreateNewStorage(tabs);

                //TabsSimple[0].SetNewLogMessage("StorageCreate " + (DateTime.Now - timeStartStorageCreate).ToString(), LogMessageType.System);

                DateTime timeStartWaiting = DateTime.Now;

                while (_storageLast.Securities == null)
                {
                    Thread.Sleep(10);

                    if (timeStartWaiting.AddSeconds(5) < DateTime.Now)
                    {
                        _lastTestIsOver = true;
                        return null;
                    }
                }

                //TabsSimple[0].SetNewLogMessage("WaitingSecurities" + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

                timeStartWaiting = DateTime.Now;

                _serverLast = CreateNewServer(_storageLast, _storageLast.TimeStart, _storageLast.TimeEnd.AddHours(2));


                //TabsSimple[0].SetNewLogMessage("Server creation Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

                timeStartWaiting = DateTime.Now;

                BotPanel bot = CreateNewBot(strategyName, parametrs, _serverLast, startProgram);
                while (bot.IsConnected == false)
                {
                    Thread.Sleep(5);

                    if (timeStartWaiting.AddSeconds(20) < DateTime.Now)
                    {
                        _lastTestIsOver = true;
                        return null;
                    }
                }

                if (bot._chartUi != null)
                {
                    bot.StopPaint();
                }

                //TabsSimple[0].SetNewLogMessage("Bot creation Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);

                _serverLast.TestingEndEvent += Server_TestingEndEvent;
                _serverLast.TestingStart();

                timeStartWaiting = DateTime.Now;
                while (_lastTestIsOver == false)
                {
                    Thread.Sleep(5);
                    if (timeStartWaiting.AddSeconds(20) < DateTime.Now)
                    {
                        _serverLast.TestingEndEvent -= Server_TestingEndEvent;
                        return null;
                    }
                }
                _serverLast.TestingEndEvent -= Server_TestingEndEvent;

                //TabsSimple[0].SetNewLogMessage("Testing Time " + (DateTime.Now - timeStartWaiting).ToString(), LogMessageType.System);
                return bot;
            }
            catch (Exception e)
            {
                _lastTestIsOver = true;
                TabsSimple[0].SetNewLogMessage(e.ToString(), LogMessageType.Error);
                return null;
            }
        }

        private void Server_TestingEndEvent(int obj)
        {
            _lastTestIsOver = true;
        }

        private string _pathToBotData;

        private static bool _tempDirAlreadyClear = false;

        private static void ClearDataDirectory()
        {
            string folder = "Data\\";

            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }
            folder += "AdminCapacityTemp\\";
            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }

            if (_tempDirAlreadyClear == false)
            {
                _tempDirAlreadyClear = true;

                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {

                }

                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch
                {

                }
            }

        }

        private string GetDataPath()
        {
            if (_pathToBotData != null)
            {
                return _pathToBotData;
            }

            string folder = "Data\\";
            folder += "AdminCapacityTemp\\";
            folder += NumberGen.GetNumberDeal(TabsSimple[0].StartProgram) + "\\";

            if (Directory.Exists(folder) == false)
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }

            _pathToBotData = folder;

            return folder;
        }

        private OptimizerDataStorage CreateNewStorage(List<BotTabSimple> tabs)
        {
            string folder = GetDataPath();
            SaveCandlesInFolder(tabs, folder);

            OptimizerDataStorage Storage = new OptimizerDataStorage(NameStrategyUniq);
            Storage.SourceDataType = TesterSourceDataType.Folder;
            Storage.PathToFolder = folder;
            Storage.TimeEnd = tabs[0].CandlesAll[tabs[0].CandlesAll.Count - 1].TimeStart;
            Storage.TimeStart = tabs[0].CandlesFinishedOnly[0].TimeStart;
            Storage.TimeNow = tabs[0].CandlesFinishedOnly[0].TimeStart;

            Storage.ReloadSecurities(true);

            _storageLast = Storage;

            return Storage;
        }

        private void SaveCandlesInFolder(List<BotTabSimple> tabs, string folder)
        {
            try
            {
                while (Directory.GetFiles(folder) != null &&
                    Directory.GetFiles(folder).Length > 0)
                {
                    string file = Directory.GetFiles(folder)[0];
                    File.Delete(file);
                }

                for (int i = 0; i < tabs.Count; i++)
                {
                    List<Candle> candles = tabs[i].CandlesAll;
                    Candle last = candles[candles.Count - 1];
                    StreamWriter writer = new StreamWriter(folder + tabs[i].Securiti.Name, false);

                    for (int i2 = 0; i2 < candles.Count; i2++)
                    {
                        writer.WriteLine(candles[i2].StringToSave);
                    }
                    writer.Close();
                }
            }
            catch (Exception e)
            {

            }

        }

        private OptimizerServer CreateNewServer(OptimizerDataStorage storage, DateTime timeCandleStart,
            DateTime timeCandleEnd)
        {
            // 1. Create a new server for optimization. And one thread respectively
            // 1. создаём новый сервер для оптимизации. И один поток соответственно
            OptimizerServer server = ServerMaster.CreateNextOptimizerServer(storage,
                NumberGen.GetNumberDeal(StartProgram), 100000);

            server.TypeTesterData = storage.TypeTesterData;

            Security secToStart = storage.Securities[0];

            if (secToStart == null)
            {

            }

            server.GetDataToSecurity(secToStart, TabsSimple[0].Connector.TimeFrame, timeCandleStart,
                timeCandleEnd);


            if (server.Securities.Count == 0)
            {

            }

            return server;
        }


        private BotPanel _botLikeTester;

        private BotPanel _botLikeOptimizer;

        private BotPanel CreateNewBot(string botName,
        List<IIStrategyParameter> parametrs,
        OptimizerServer server, StartProgram regime)
        {
            BotPanel botToTest = null;

            if (regime == StartProgram.IsOsOptimizer)
            {
                botToTest = _botLikeOptimizer;
            }
            else if (regime == StartProgram.IsTester)
            {
                botToTest = _botLikeTester;
            }

            if (botToTest == null)
            {
                botToTest = BotFactory.GetStrategyForName(CurrentStrategy.ValueString, botName + NumberGen.GetNumberDeal(StartProgram), regime, true);

                if (regime == StartProgram.IsOsOptimizer)
                {
                    _botLikeOptimizer = botToTest;
                }
                else if (regime == StartProgram.IsTester)
                {
                    _botLikeTester = botToTest;
                }

            }
            else
            {
                botToTest.TabsSimple[0].Connector.ServerUid = server.NumberServer;
                botToTest.TabsSimple[0].Connector.ReconnectHard();
                Thread.Sleep(100);
            }

            for (int i = 0; i < parametrs.Count; i++)
            {
                IIStrategyParameter par = parametrs.Find(p => p.Name == parametrs[i].Name);

                if (par == null)
                {
                    par = parametrs[i];
                }

                if (botToTest.Parameters[i].Type == StrategyParameterType.Bool)
                {
                    ((StrategyParameterBool)botToTest.Parameters[i]).ValueBool = ((StrategyParameterBool)par).ValueBool;
                }
                else if (botToTest.Parameters[i].Type == StrategyParameterType.String)
                {
                    ((StrategyParameterString)botToTest.Parameters[i]).ValueString = ((StrategyParameterString)par).ValueString;
                }
                else if (botToTest.Parameters[i].Type == StrategyParameterType.Int)
                {
                    ((StrategyParameterInt)botToTest.Parameters[i]).ValueInt = ((StrategyParameterInt)par).ValueInt;
                }
                else if (botToTest.Parameters[i].Type == StrategyParameterType.Decimal)
                {
                    ((StrategyParameterDecimal)botToTest.Parameters[i]).ValueDecimal = ((StrategyParameterDecimal)par).ValueDecimal;
                }
            }

            // custom tabs
            // настраиваем вкладки
            for (int i = 0; i < TabsSimple.Count; i++)
            {
                botToTest.TabsSimple[i].Connector.ServerType = ServerType.Optimizer;
                botToTest.TabsSimple[i].Connector.PortfolioName = server.Portfolios[0].Number;
                botToTest.TabsSimple[i].Connector.SecurityName = TabsSimple[i].Connector.SecurityName;
                botToTest.TabsSimple[i].Connector.SecurityClass = TabsSimple[i].Connector.SecurityClass;
                botToTest.TabsSimple[i].Connector.TimeFrame = TabsSimple[i].Connector.TimeFrame;
                botToTest.TabsSimple[i].Connector.ServerUid = server.NumberServer;

                if (server.TypeTesterData == TesterDataType.Candle)
                {
                    botToTest.TabsSimple[i].Connector.CandleMarketDataType = CandleMarketDataType.Tick;
                }
                else if (server.TypeTesterData == TesterDataType.MarketDepthAllCandleState ||
                         server.TypeTesterData == TesterDataType.MarketDepthOnlyReadyCandle)
                {
                    botToTest.TabsSimple[i].Connector.CandleMarketDataType =
                        CandleMarketDataType.MarketDepth;
                }
            }

            return botToTest;
        }

        #endregion

        #region Вход в торговую логику

        private void AdminCapacityCreator_CandleFinishedEvent(List<Candle> candles)
        {
            //


            if (StartProgram == StartProgram.IsOsTrader)
            {
                return;
            }

            //TabsSimple[0].SetNewLogMessage("Candle finished event. last:  " + candles[candles.Count - 1].TimeStart, LogMessageType.System);
            if (Regime.ValueString == "Off")
            {
                return;
            }

            if (CurrentStrategy.ValueString == "None")
            {
                return;
            }

            if (candles.Count < 20)
            {
                return;
            }

            DateTime time = DateTime.Now;
            Logic();
            TimeSpan timeTest = DateTime.Now - time;
            TabsSimple[0].SetNewLogMessage("Logic time: " + timeTest.TotalSeconds.ToString(), LogMessageType.System);

            TimeSpan timeClearing = DateTime.Now - time;
            TabsSimple[0].SetNewLogMessage("Clearing time: " + timeClearing.TotalSeconds.ToString(), LogMessageType.System);
            ClearLastSession();

        }

        private bool _neadToTestBot;

        private bool _isDeteted;

        private void Worker()
        {
            if (StartProgram != StartProgram.IsOsTrader)
            {
                return;
            }
            while (true)
            {
                Thread.Sleep(50);

                if (_isDeteted == true)
                {
                    return;
                }

                if (_neadToTestBot == false)
                {
                    CheckCandlesCount();
                    continue;
                }

                _neadToTestBot = false;
                DateTime time = DateTime.Now;
                Logic();
                TimeSpan timeTest = DateTime.Now - time;
                TabsSimple[0].SetNewLogMessage("Logic time: " + timeTest.TotalSeconds.ToString(), LogMessageType.System);
                ClearLastSession();
            }
        }

        private void CheckCandlesCount()
        {
            if (TabsSimple == null
                || TabsSimple.Count == 0)
            {
                return;
            }

            List<Candle> allCandles = TabsSimple[0].CandlesAll;

            if (allCandles == null ||
                allCandles.Count == 0)
            {
                return;
            }

            Candle last = allCandles[allCandles.Count - 1];

            if (last == null)
            {
                return;
            }

            if (_timeLastCandle == DateTime.MinValue)
            {
                _timeLastCandle = last.TimeStart;
                return;
            }

            if (_timeLastCandle != last.TimeStart)
            {
                _timeLastCandle = last.TimeStart;
                _neadToTestBot = true;
            }
        }

        private DateTime _timeLastCandle = DateTime.MinValue;

        private void AdminCapacityCreator_NewTickEvent(Trade trade)
        {
            if (StartProgram == StartProgram.IsOsTrader)
            {
                return;
            }

            try
            {
                SupportLogic(trade.Time);
            }
            catch (Exception error)
            {
                TabsSimple[0].SetNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void ClearLastSession()
        {
            if (_serverLast != null)
            {
                ServerMaster.RemoveOptimizerServer(_serverLast);
                _serverLast = null;
            }

            if (_storageLast != null)
            {
                _storageLast.ClearDelete();
                _storageLast = null;
            }

            if (_botLikeOptimizer != null)
            {
                _botLikeOptimizer.Clear();
                _botLikeOptimizer.Delete();
                _botLikeOptimizer = null;
            }
        }

        private void Logic()
        {

            if (Regime.ValueString == "Off")
            {
                return;
            }

            if (TabsSimple[0].CandlesAll.Count < 50)
            {
                return;
            }

            BotPanel bot = TestRobot(CurrentStrategy.ValueString, TabsSimple, _slave.Parameters, StartProgram.IsOsOptimizer);

            if (bot == null)
            {
                return;
            }

            List<Position> slavePoses = null;

            for (int i = 0; i < bot.TabsSimple.Count; i++)
            {
                slavePoses = bot.TabsSimple[i].PositionsOpenAll;
                List<PositionOpenerToStop> slaveStops = bot.TabsSimple[i].PositionOpenerToStopsAll;

                CopyPositions(slavePoses, TabsSimple[i], bot.TabsSimple[i].PositionsAll);
                CopyStopOpenier(slaveStops, TabsSimple[i]);
            }

            if (slavePoses.Count != 0)
            {
                TabsSimple[0].SetNewLogMessage("Slave Pos" + slavePoses[0].TimeOpen + " stop : "
                    + slavePoses[0].StopOrderIsActiv.ToString() + " Price : "
                    + slavePoses[0].StopOrderRedLine, LogMessageType.System);
            }
        }

        #endregion

        #region Методы копирование позиций по завершению свечи

        private void CopyPositions(List<Position> slavePosActual, BotTabSimple tab, List<Position> slavePosAll)
        {
            List<Position> myPoses = tab.PositionsOpenAll;

            //SaveCompareDataInPositions(slavePosAll, tab.PositionsAll, tab.TimeServerCurrent);

            // тупо копируем т.к. у нас нет позиций

            if (slavePosActual.Count != 0 && myPoses.Count == 0)
            {
                for (int i = 0; i < slavePosActual.Count; i++)
                {
                    OpenPose(slavePosActual[i], tab);
                }
                return;
            }

            // проверяем чтобы у нас была позиция в нужную сторону

            if (slavePosActual.Count != 0 && myPoses.Count != 0)
            {
                CheckGoodWayPoses(slavePosActual, myPoses, tab);
            }

            // проверяем чтобы позиции были в одну сторону у обоих вкладок

            if (slavePosActual.Count != 0 && myPoses.Count != 0)
            {
                CheckWrongWayPoses(slavePosActual, myPoses, tab);
            }

            // проверяем стопы для позиций

            if (slavePosActual.Count != 0 && myPoses.Count != 0)
            {
                CheckStops(slavePosActual, myPoses, tab);
            }

            // закрываем у себя позиции, т.к. у раба нет, а у нас есть

            for (int i = 0; i < slavePosActual.Count; i++)
            {
                if (slavePosActual[i].State == PositionStateType.Closing)
                {
                    slavePosActual.RemoveAt(i);
                    i--;
                }
            }


            if (slavePosActual.Count == 0 && myPoses.Count != 0)
            {
                for (int i = 0; i < myPoses.Count; i++)
                {
                    CloseBadPoses(myPoses[i], tab);
                }
                return;
            }

            // открываем дополнительные позиции

            if (slavePosActual.Count != 0
                && myPoses.Count < slavePosActual.Count
                && myPoses.Count < MaxPositionDuplicateCount.ValueInt)
            {
                CheckDopPoses(slavePosActual, myPoses, tab);
            }
        }

        private void CopyStopOpenier(List<PositionOpenerToStop> stops, BotTabSimple tab)
        {
            tab.BuyAtStopCancel();
            tab.SellAtStopCancel();

            if (stops == null || stops.Count == 0)
            {
                return;
            }

            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i].Side == Side.Buy)
                {
                    decimal redLine = stops[i].PriceRedLine - stops[i].PriceRedLine * ShiftToStopOpdersValue.ValueDecimal / 100;

                    tab.BuyAtStop(
                        GetBuyVolume(),
                        redLine + (SlippageInter.ValueDecimal * redLine / 100),
                        redLine,
                        stops[i].ActivateType, stops[i].TimeCreate.ToString());
                    tab.SetNewLogMessage(
                        "Buy at stop, активация: " + redLine + " Ордер: "
                        + (redLine + (SlippageInter.ValueDecimal * redLine / 100)),
                        LogMessageType.System);
                }
                if (stops[i].Side == Side.Sell)
                {
                    decimal redLine = stops[i].PriceRedLine + stops[i].PriceRedLine * ShiftToStopOpdersValue.ValueDecimal / 100;

                    tab.SellAtStop(
                        GetSellVolume(),
                        redLine - (SlippageInter.ValueDecimal * redLine / 100),
                        redLine,
                        stops[i].ActivateType, stops[i].TimeCreate.ToString());
                    tab.SetNewLogMessage(
                        "Sell at stop, активация: " + redLine + " Ордер: "
                        + (redLine - (SlippageInter.ValueDecimal * redLine / 100)),
                        LogMessageType.System);
                }
            }
        }

        private bool HaveThisPos(List<Position> poses, Side side)
        {
            for (int i = 0; i < poses.Count; i++)
            {
                if (poses[i].Direction == side)
                {
                    return true;
                }
            }

            return false;
        }

        private void CheckStops(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            decimal stopActivateBuy = 0;
            decimal stopOrderBuy = 0;

            decimal stopActivateSell = 0;
            decimal stopOrderSell = 0;

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (slavePoses[i].Direction == Side.Buy &&
                    slavePoses[i].StopOrderPrice != 0)
                {
                    stopActivateBuy = slavePoses[i].StopOrderRedLine;
                    stopOrderBuy = slavePoses[i].StopOrderPrice;

                    stopOrderBuy = stopOrderBuy + stopOrderBuy * ShiftToStopOpdersValue.ValueDecimal / 100;
                    stopActivateBuy = stopActivateBuy + stopActivateBuy * ShiftToStopOpdersValue.ValueDecimal / 100;
                }
                if (slavePoses[i].Direction == Side.Sell &&
                    slavePoses[i].StopOrderPrice != 0)
                {
                    stopActivateSell = slavePoses[i].StopOrderRedLine;
                    stopOrderSell = slavePoses[i].StopOrderPrice;

                    stopOrderSell = stopOrderSell - stopOrderSell * ShiftToStopOpdersValue.ValueDecimal / 100;
                    stopActivateSell = stopActivateSell - stopActivateSell * ShiftToStopOpdersValue.ValueDecimal / 100;
                }
            }

            if (stopOrderBuy == 0 &&
                stopOrderSell == 0)
            {
                return;
            }


            for (int i = 0; i < myPoses.Count; i++)
            {
                if (myPoses[i].CloseActiv ||
                    myPoses[i].State != PositionStateType.Open)
                {
                    continue;
                }
                if (myPoses[i].Direction == Side.Buy && stopOrderBuy != 0)
                {
                    TabsSimple[0].CloseAtStop(myPoses[i], stopActivateBuy,
                        stopOrderBuy - (SlippageExit.ValueDecimal * stopOrderBuy / 100));

                }
                if (myPoses[i].Direction == Side.Sell && stopOrderSell != 0)
                {
                    TabsSimple[0].CloseAtStop(myPoses[i], stopActivateSell,
                        stopOrderSell + (SlippageExit.ValueDecimal * stopOrderBuy / 100));
                }
            }
        }

        private void CheckGoodWayPoses(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            bool haveBuySlave = HaveThisPos(slavePoses, Side.Buy);
            bool haveSellSlave = HaveThisPos(slavePoses, Side.Sell);
            bool haveBuyMy = HaveThisPos(myPoses, Side.Buy);
            bool haveSellMy = HaveThisPos(myPoses, Side.Sell);

            if (haveBuySlave && haveBuyMy == false)
            {
                Position pos = slavePoses.Find(p => p.Direction == Side.Buy);

                if (pos != null &&
                    CanReOpen(pos, tab))
                {
                    for (int i = 0; i < _numsPositionAlreadyUsed.Count; i++)
                    {
                        if (_numsPositionAlreadyUsed[i] == pos.TimeOpen)
                        {
                            return;
                        }
                    }
                    _numsPositionAlreadyUsed.Add(pos.TimeOpen);

                    Position myPos = tab.BuyAtLimit(GetBuyVolume(),
                        pos.EntryPrice + (SlippageInter.ValueDecimal * pos.EntryPrice / 100), pos.TimeOpen.ToString());
                    _positionsToSupportOpenFirstTime.Add(myPos);
                    TabsSimple[0].SetNewLogMessage("В позиции на сопровождение новый лонг" + pos.Number, LogMessageType.System);
                }
            }
            if (haveSellSlave && haveSellMy == false)
            {
                Position pos = slavePoses.Find(p => p.Direction == Side.Sell);

                if (pos != null
                    && CanReOpen(pos, tab))
                {
                    for (int i = 0; i < _numsPositionAlreadyUsed.Count; i++)
                    {
                        if (_numsPositionAlreadyUsed[i] == pos.TimeOpen)
                        {
                            return;
                        }
                    }
                    _numsPositionAlreadyUsed.Add(pos.TimeOpen);

                    Position myPos = tab.SellAtLimit(GetSellVolume(),
                        pos.EntryPrice - (SlippageInter.ValueDecimal * pos.EntryPrice / 100), pos.TimeOpen.ToString());
                    _positionsToSupportOpenFirstTime.Add(myPos);
                    TabsSimple[0].SetNewLogMessage("В позиции на сопровождение новый шорт" + pos.Number, LogMessageType.System);
                }
            }
        }

        /// <summary>
        /// если уже раб развеврнулся, то мы прям по рынку кроемся.
        /// Пофик на всё
        /// </summary>
        private void CheckWrongWayPoses(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            bool haveBuy = false;
            bool haveSell = false;

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (slavePoses[i].Direction == Side.Sell)
                {
                    haveSell = true;
                }
                if (slavePoses[i].Direction == Side.Buy)
                {
                    haveBuy = true;
                }
            }

            if (haveSell && haveBuy)
            {
                return;
            }

            if (haveBuy)
            {
                for (int i = 0; i < myPoses.Count; i++)
                {
                    if (myPoses[i].Direction == Side.Sell
                        && myPoses[i].State == PositionStateType.Open
                        && myPoses[i].CloseActiv == false
                        && HaveThisPosInArrays(myPoses[i]) == false)
                    {
                        tab.CloseAtMarket(myPoses[i], myPoses[i].OpenVolume);
                    }
                }
            }
            if (haveSell)
            {
                for (int i = 0; i < myPoses.Count; i++)
                {
                    if (myPoses[i].Direction == Side.Buy
                        && myPoses[i].State == PositionStateType.Open
                        && myPoses[i].CloseActiv == false
                        && HaveThisPosInArrays(myPoses[i]) == false)
                    {
                        tab.CloseAtMarket(myPoses[i], myPoses[i].OpenVolume);
                    }
                }
            }
        }

        /// <summary>
        /// если у раба нет позиций - а у нас есть
        /// </summary>
        private void CloseBadPoses(Position pos, BotTabSimple tab)
        {
            if (pos.CloseActiv == true)
            {
                tab.CloseAllOrderToPosition(pos);
                Thread.Sleep(500);
            }

            if (pos.State == PositionStateType.Done)
            {
                return;
            }

            decimal price = 0;

            if (pos.Direction == Side.Buy)
            {
                price = tab.PriceBestAsk + tab.Securiti.PriceStep * 30;
            }
            else if (pos.Direction == Side.Sell)
            {
                price = tab.PriceBestBid - tab.Securiti.PriceStep * 30;
            }

            tab.CloseAtMarket(pos, pos.OpenVolume);

            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == pos.Number) == null)
            {
                _positionsToSupportCloseFirstTime.Add(pos);
            }
        }

        /// <summary>
        /// открываем дополнительные позиции
        /// </summary>
        private void CheckDopPoses(List<Position> slavePoses, List<Position> myPoses, BotTabSimple tab)
        {
            bool haveBuy = false;
            bool haveSell = false;

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (slavePoses[i].Direction == Side.Sell)
                {
                    haveSell = true;
                }
                if (slavePoses[i].Direction == Side.Buy)
                {
                    haveBuy = true;
                }
            }

            if (haveSell && haveBuy)
            {
                return;
            }

            if (slavePoses.Count == 0 ||
                myPoses.Count == 0)
            {
                return;
            }

            if (slavePoses[0].Direction != myPoses[0].Direction)
            {
                return;
            }

            if (myPoses.Count >= slavePoses.Count)
            {
                return;
            }

            for (int i = 0; i < slavePoses.Count; i++)
            {
                if (tab.PositionsOpenAll.Count >= MaxPositionDuplicateCount.ValueInt)
                {
                    return;
                }
                OpenPose(slavePoses[i], tab);
            }

        }

        private void OpenPose(Position pos, BotTabSimple tab)
        {
            for (int i = 0; i < _numsPositionAlreadyUsed.Count; i++)
            {
                if (_numsPositionAlreadyUsed[i] == pos.TimeOpen)
                {
                    return;
                }
            }

            _numsPositionAlreadyUsed.Add(pos.TimeOpen);

            if (pos.State == PositionStateType.Open ||
                pos.State == PositionStateType.Opening)
            {
                if (pos.Direction == Side.Buy)
                {
                    decimal price = pos.EntryPrice;

                    if (pos.EntryPrice == 0)
                    {
                        price = pos.OpenOrders[0].Price;
                    }

                    decimal securityPrice = tab.PriceBestAsk;

                    if (price > securityPrice)
                    {
                        decimal lenFromSecPrice = Math.Abs(price - securityPrice) / (price / 100);

                        if (lenFromSecPrice > 2)
                        {
                            price = securityPrice;
                        }
                    }

                    price += (SlippageInter.ValueDecimal * pos.EntryPrice / 100);
                    Position posMy = tab.BuyAtLimit(GetBuyVolume(), price, pos.TimeOpen.ToString());

                    if (posMy != null)
                    {
                        _positionsToSupportOpenFirstTime.Add(posMy);
                    }
                }
                if (pos.Direction == Side.Sell)
                {
                    decimal price = pos.EntryPrice;

                    if (pos.EntryPrice == 0)
                    {
                        price = pos.OpenOrders[0].Price;
                    }

                    decimal securityPrice = tab.PriceBestBid;

                    if (price < securityPrice)
                    {
                        decimal lenFromSecPrice = Math.Abs(securityPrice - price) / (price / 100);

                        if (lenFromSecPrice > 2)
                        {
                            price = securityPrice;
                        }
                    }

                    price -= (SlippageInter.ValueDecimal * pos.EntryPrice / 100);
                    Position posMy = tab.SellAtLimit(GetSellVolume(), price, pos.TimeOpen.ToString());
                    if (posMy != null)
                    {
                        _positionsToSupportOpenFirstTime.Add(posMy);
                    }
                }
            }
        }

        List<DateTime> _numsPositionAlreadyUsed = new List<DateTime>();

        private decimal GetBuyVolume()
        {
            return GetVolume(Side.Buy);
        }

        private decimal GetSellVolume()
        {
            return GetVolume(Side.Sell);
        }

        private decimal GetVolume(Side side)
        {
            decimal volume = VolumeOnPosition.ValueDecimal;
            // "Кол-во контрактов", "Валюта контракта", "% от Общего объёма портфеля"

            VolumeRegimeType volumeRegimeType = GetVolumeRegimeType();
            if (volumeRegimeType == VolumeRegimeType.CONTRACT_CURRENCY || volumeRegimeType == VolumeRegimeType.PORTFOLIO_PERCENT)
            {
                decimal price = side == Side.Buy ? TabsSimple[0].PriceBestAsk : TabsSimple[0].PriceBestBid;
                decimal slippage = price * SlippageInter.ValueDecimal / 100;
                price = side == Side.Buy ? price + slippage : price - slippage;

                if (volumeRegimeType == VolumeRegimeType.CONTRACT_CURRENCY)
                {
                    volume = Math.Round(volume / price, VolumeDecimals.ValueInt);
                }
                else if (volumeRegimeType == VolumeRegimeType.PORTFOLIO_PERCENT)
                {
                    decimal portfolioPercent = VolumeOnPosition.ValueDecimal;
                    volume = Math.Round(StaticPortfolioValue / 100 * portfolioPercent / price, VolumeDecimals.ValueInt);
                }
            }

            return volume;
        }

        private VolumeRegimeType GetVolumeRegimeType()
        {
            VolumeRegimeType volumeRegimeType = VolumeRegimeType.CONTRACTS_NUMBER;
            if (VolumeRegime.ValueString == "Валюта контракта")
            {
                volumeRegimeType = VolumeRegimeType.CONTRACT_CURRENCY;
            }
            else if (VolumeRegime.ValueString == "% от Общего объёма портфеля")
            {
                volumeRegimeType = VolumeRegimeType.PORTFOLIO_PERCENT;
            }
            return volumeRegimeType;
        }

        #endregion

        #region Дополнительное сопровождение позиции

        /*
                Логика отвечающая за сопровождение позиции.
                1.	Проскальзывание на вход – обычное проскальзывание

                2.	Проскальзывание на выход – обычное проскальзывание

                3.	Отклонение вход – т.к в этом боте мы работаем стопами и знаем, когда нужно войти или выйти из позции.
                Этот параметр, дает возможность чуть ранее войти в позицию.Условно по стопу бота мы должны войти по 100, 
                ставим тут 10 и входим по 90.

                4.	Отклонение выход – аналогично но на выход.

                5.	Секунд на вход – данный параметр, связан с отклонением исполнения и с зашитым параметром перевыставления ордеров.
                    Логика тут следующая. – Получили сигнал от бота, выставили ордер, он ушел в таблицу заявок квика
                    с указанным проскальзыванием.Если не исполнился в течение 3х секунд – сняли.
                    Посмотрели на параметр секунд на вход, если времени с момента первой точки входа(по ТС) прошло меньше чем указано, 
                    то смотрим параметр Отклонение исполнения , если цена ушла меньше от первоначальной точки входа чем указано 
                    в этом параметре, то используем Проскальзывание 2 на вход на выход чтобы войти в позицию.

                6.	Отклонение исполнения – указывается в пунктах.

                7.	Зашитый параметр переставления ордера – ставим заявку на 3 секунды, если не исполнилась снимаем. 
                И далее начинаем работать логика с Секундами на вход и Отклонением на вход.
        */

        private void AdminCapacityCreator_PositionProfitActivateEvent(Position position)
        {
            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == position.Number) == null)
            {
                _positionsToSupportCloseFirstTime.Add(position);
                TabsSimple[0].SetNewLogMessage(
                    "Позиция добавлена в сопровождение закрытия 1. Т.к. закрывается по профиту " + position.Number,
                    LogMessageType.System);
            }
        }

        private void AdminCapacityCreator_PositionStopActivateEvent(Position position)
        {
            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == position.Number) == null)
            {
                _positionsToSupportCloseFirstTime.Add(position);
                TabsSimple[0].SetNewLogMessage(
                    "Позиция добавлена в сопровождение закрытия 1. Т.к. закрывается по стопу " + position.Number,
                    LogMessageType.System);
            }
        }

        private void AdminCapacityCreator_PositionSellAtStopActivateEvent(Position position)
        {
            _positionsToSupportOpenFirstTime.Add(position);

            TabsSimple[0].SetNewLogMessage(
                "Позиция добавлена в сопровождение открытия 1. SellAtStop " + position.Number,
                LogMessageType.System);
        }

        private void AdminCapacityCreator_PositionBuyAtStopActivateEvent(Position position)
        {
            _positionsToSupportOpenFirstTime.Add(position);
            TabsSimple[0].SetNewLogMessage(
                "Позиция добавлена в сопровождение открытия 1. BuyAtStop " + position.Number,
                LogMessageType.System);
        }

        private List<Position> _positionsToSupportOpenFirstTime = new List<Position>();
        private List<Position> _positionsToSupportOpenSecondTime = new List<Position>();

        private List<Position> _positionsToSupportCloseFirstTime = new List<Position>();
        private List<Position> _positionsToSupportCloseSecondTime = new List<Position>();

        private bool HaveThisPosInArrays(Position position)
        {

            if (_positionsToSupportOpenFirstTime.Find(p => p.Number == position.Number) != null)
            {
                if (position.State == PositionStateType.Open)
                {
                    for (int i = 0; i < _positionsToSupportOpenFirstTime.Count; i++)
                    {
                        if (_positionsToSupportOpenFirstTime[i].Number == position.Number)
                        {
                            _positionsToSupportOpenFirstTime.RemoveAt(i);
                            return false;
                        }
                    }
                }
                return true;
            }
            if (_positionsToSupportOpenSecondTime.Find(p => p.Number == position.Number) != null)
            {
                if (position.State == PositionStateType.Open)
                {
                    for (int i = 0; i < _positionsToSupportOpenSecondTime.Count; i++)
                    {
                        if (_positionsToSupportOpenSecondTime[i].Number == position.Number)
                        {
                            _positionsToSupportOpenSecondTime.RemoveAt(i);
                            return false;
                        }
                    }
                }
                return true;
            }
            if (_positionsToSupportCloseFirstTime.Find(p => p.Number == position.Number) != null)
            {
                return true;
            }
            if (_positionsToSupportCloseSecondTime.Find(p => p.Number == position.Number) != null)
            {
                return true;
            }


            return false;
        }

        private void PositionSupportThread()
        {
            if (StartProgram != StartProgram.IsOsTrader)
            {
                return;
            }

            while (true)
            {
                SupportLogic(DateTime.Now);
                Thread.Sleep(300);
            }
        }

        private DateTime _lastSupportCheckTime;

        private void SupportLogic(DateTime lastTime)
        {
            if (_lastSupportCheckTime.AddSeconds(1) > lastTime)
            {
                return;
            }

            _lastSupportCheckTime = lastTime;

            if (MainWindow.ProccesIsWorked == false)
            {
                return;
            }

            try
            {
                if (IsConnected == false)
                {
                    return;
                }

                if (TabsSimple == null || TabsSimple.Count == 0)
                {
                    return;
                }

                BotTabSimple tab = TabsSimple[0];

                // удаляем из массивов позиции которые были завершены
                // очищаем второй уровень позиций которые дважны не открылись

                TryRemovePosAsDone(_positionsToSupportOpenFirstTime);
                TryRemovePosAsDone(_positionsToSupportOpenSecondTime);
                TryRemovePosAsDone(_positionsToSupportCloseFirstTime);
                TryRemovePosAsDone(_positionsToSupportCloseSecondTime);

                // удаляем из поддержки открывающие позиции если они открылись

                TryRemoveOpeningPositionAsOpen(_positionsToSupportOpenFirstTime);
                TryRemoveOpeningPositionAsOpen(_positionsToSupportOpenSecondTime);

                // открытия сначала отзываем ордера которые отжили своё

                CanselationOrder(_positionsToSupportOpenFirstTime, tab);
                CanselationOrder(_positionsToSupportOpenSecondTime, tab);

                CanselationOrder(_positionsToSupportCloseFirstTime, tab);
                CanselationOrder(_positionsToSupportCloseSecondTime, tab);

                // открытия переоткрываем ордера с первого уровня с новыми проскальзываниями переносим позиции из одного массива в другой

                TryReOpenPositionAndMoveInNextLevel(
                    _positionsToSupportOpenFirstTime, _positionsToSupportOpenSecondTime, tab);

                // Теперь отзываем ордера второго уровня поддержки

                TryRemovePosAsOpeningFail(_positionsToSupportOpenSecondTime);

                // закрытия. Перезакрываем ордера первого уровня

                TryReClosePositionAndMoveInNextLevel(_positionsToSupportCloseFirstTime,
                    _positionsToSupportCloseSecondTime, tab);

                TryRemovePosAsClosingFail(_positionsToSupportCloseSecondTime);

                // теперь закрытия
                // отзываем через N времени из первого массива
                // перезакрываем с проскальзыванием 2. Переносим в массив 2.
                // отзываем через N времени из второго, в третий
                // в третьем закрываем тогда когда у робота нет совсем позиций или противоположная

            }
            catch (Exception error)
            {
                TabsSimple[0].SetNewLogMessage(error.ToString(), LogMessageType.Error);
            }
        }

        private void TryRemovePosAsDone(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.Done)
                {
                    TabsSimple[0].SetNewLogMessage(
                        "Позиция удалена из доп сопровождения, т.к. Done " + positions[i].Number,
                        LogMessageType.System);
                    positions.RemoveAt(i);
                    i--;
                }
            }
        }

        private void TryRemovePosAsOpeningFail(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.OpeningFail &&
                    positions[i].OpenActiv == false)
                {
                    TabsSimple[0].SetNewLogMessage(
                        "Позиция удалена из доп сопровождения, т.к. OpeningFail " + positions[i].Number,
                        LogMessageType.System);
                    positions.RemoveAt(i);
                    i--;
                }
            }
        }

        private void TryRemovePosAsClosingFail(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.ClosingFail &&
                    positions[i].CloseActiv == false)
                {
                    TabsSimple[0].SetNewLogMessage(
                        "Позиция удалена из доп сопровождения, т.к. ClosingFail 2 раза " + positions[i].Number,
                        LogMessageType.System);
                    positions.RemoveAt(i);
                    i--;
                }
            }
        }

        private void CanselationOrder(List<Position> positions, BotTabSimple tab)
        {
            if (positions.Count == 0)
            {
                return;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                Order ord = GetOpenOrder(positions[i]);

                if (ord == null)
                {
                    continue;
                }

                if (_cancelledOrders.Find(o => o.NumberUser == ord.NumberUser) != null)
                {
                    continue;
                }

                if (ord.TimeCreate != DateTime.MinValue &&
                    ord.TimeCreate.AddSeconds(OpenOrderLifeTime.ValueInt) < tab.TimeServerCurrent)
                {
                    tab.CloseAllOrderToPosition(positions[i]);
                    _cancelledOrders.Add(ord);
                }
            }
        }

        private List<Order> _cancelledOrders = new List<Order>();

        private Order GetOpenOrder(Position pos)
        {
            if (pos.OpenActiv)
            {
                return pos.OpenOrders.Find(o => o.State == OrderStateType.Activ);
            }
            if (pos.CloseActiv)
            {
                return pos.CloseOrders.Find(o => o.State == OrderStateType.Activ);
            }

            return null;
        }

        private void TryRemoveOpeningPositionAsOpen(List<Position> positions)
        {
            for (int i = 0; i < positions.Count; i++)
            {
                if (positions[i].State == PositionStateType.Open)
                {
                    Position pos = positions[i];
                    positions.RemoveAt(i);
                    TabsSimple[0].SetNewLogMessage(
                     "Позиция удалена из доп сопровождения, т.к. Open" + pos.Number,
                  LogMessageType.System);
                    i--;
                }
            }
        }

        private void TryReOpenPositionAndMoveInNextLevel(
            List<Position> positionsFirst,
            List<Position> positionsSecond,
            BotTabSimple tab)
        {
            for (int i = 0; i < positionsFirst.Count; i++)
            {

                if (positionsFirst[i].State == PositionStateType.OpeningFail &&
                    positionsFirst[i].OpenActiv == false)
                {
                    Position pos = positionsFirst[i];

                    if (CanReOpen(pos, tab))
                    {
                        TabsSimple[0].SetNewLogMessage(
                            "Позиция перенесена из доп сопровождения 1 в 2, т.к. можно пробовать переоткрывать " + pos.Number,
                            LogMessageType.System);
                        positionsSecond.Add(pos);
                        if (pos.Direction == Side.Buy)
                        {
                            tab.BuyAtLimitToPosition(
                                pos,
                                tab.PriceBestAsk + (SlippageSecondInter.ValueDecimal * tab.PriceBestAsk / 100),
                                pos.OpenOrders[0].Volume);
                        }
                        if (pos.Direction == Side.Sell)
                        {
                            tab.SellAtLimitToPosition(
                                pos,
                                tab.PriceBestBid - (SlippageSecondInter.ValueDecimal * tab.PriceBestBid / 100),
                                pos.OpenOrders[0].Volume);
                        }
                    }
                    else
                    {
                        DateTime firstTime = pos.OpenOrders[0].TimeCreate;

                        if (firstTime.AddSeconds(InterTime.ValueInt) < tab.TimeServerCurrent)
                        {
                            positionsFirst.RemoveAt(i);
                            i--;

                            TabsSimple[0].SetNewLogMessage(
                           "Позиция НЕ перенесена из доп сопровождения 1 в 2, т.к. время на переоткрытие закончилось и мы не приблизились к цене открытия" + pos.Number,
                            LogMessageType.System);
                        }
                    }
                }
            }
        }

        private bool CanReOpen(Position pos, BotTabSimple tab)
        {
            /*
             Получили сигнал от бота, выставили ордер, он ушел в таблицу заявок квика
             с указанным проскальзыванием.Если не исполнился в течение 3х секунд – сняли.
             Посмотрели на параметр секунд на вход, если времени с момента первой точки входа(по ТС) прошло меньше чем указано,
             то смотрим параметр Отклонение исполнения, если цена ушла меньше от первоначальной точки входа чем указано
             в этом параметре, то используем Проскальзывание 2 на вход на выход чтобы войти в позицию.
            */

            if (pos.State == PositionStateType.Opening)
            {
                return true;
            }

            DateTime firstTime = pos.OpenOrders[0].TimeCreate;

            if (firstTime.AddSeconds(InterTime.ValueInt) < tab.TimeServerCurrent)
            {
                return false;
            }

            decimal price = pos.OpenOrders[0].Price;

            if (StartProgram == StartProgram.IsOsTrader)
            {
                if (pos.Direction == Side.Buy &&
                    (Math.Abs(price - tab.PriceBestAsk) / price * 100)
                    > MaxOrderExecutionDeviation.ValueDecimal)

                {
                    return false;
                }

                if (pos.Direction == Side.Sell &&
                    (Math.Abs(price - tab.PriceBestBid) / price * 100)
                    > MaxOrderExecutionDeviation.ValueDecimal)
                {
                    return false;
                }
            }

            return true;
        }

        private void TryReClosePositionAndMoveInNextLevel(
            List<Position> positionsFirst,
            List<Position> positionsSecond,
            BotTabSimple tab)
        {
            for (int i = 0; i < positionsFirst.Count; i++)
            {
                if (positionsFirst[i].State == PositionStateType.ClosingFail &&
                    positionsFirst[i].CloseActiv == false)
                {
                    Position pos = positionsFirst[i];
                    positionsFirst.RemoveAt(i);
                    i--;
                    positionsSecond.Add(pos);

                    TabsSimple[0].SetNewLogMessage(
                        "Позиция перенесена из доп сопровождения закрытия 1 в 2, т.к. ClosingFail. Пробуем перезакрывать " + pos.Number,
                        LogMessageType.System);

                    if (pos.Direction == Side.Buy)
                    {
                        tab.CloseAtLimit(
                            pos,
                            tab.PriceBestBid - (SlippageSecondExit.ValueDecimal * tab.PriceBestBid / 100),
                            pos.OpenVolume);
                    }
                    if (pos.Direction == Side.Sell)
                    {
                        tab.CloseAtLimit(
                            pos,
                            tab.PriceBestAsk + (SlippageSecondExit.ValueDecimal * tab.PriceBestAsk / 100),
                            pos.OpenVolume);
                    }
                }
            }
        }

        private void ClearSavePoses(string name)
        {
            if (!File.Exists(@"Engine\" + NameStrategyUniq + name + @"SupportPoses.txt"))
            {
                return;
            }


            try
            {
                File.Delete(@"Engine\" + NameStrategyUniq + name + @"SupportPoses.txt");
            }
            catch (Exception)
            {
                return;
            }
        }

        #endregion
    }

    public class PositionReport
    {
        // данные общие

        public decimal DifferenceValueAbsolute
        {
            get
            {
                if (ProfitSlave == 0 &&
                    ProfitReal == 0)
                {
                    return 0;
                }
                return -(ProfitSlave - ProfitReal);
            }
        }

        public decimal DifferenceValuePercent
        {
            get
            {
                if (ProfitPercentSlave == 0 &&
                    ProfitPercentReal == 0)
                {
                    return 0;
                }
                return (ProfitPercentSlave - ProfitPercentReal);
            }
        }

        public Side Side;

        public string GetSaveString()
        {
            string saveStr = "";

            saveStr += Side.ToString() + ";";

            saveStr += PosNumSlave.ToString() + ";";
            saveStr += TimeOpenSlave.ToString() + ";";
            saveStr += EnterPriceSlave.ToString() + ";";
            saveStr += ExitPriceSlave.ToString() + ";";

            saveStr += PosNumReal.ToString() + ";";
            saveStr += TimeOpenReal.ToString() + ";";
            saveStr += EnterPriceReal.ToString() + ";";
            saveStr += ExitPriceReal.ToString() + ";";

            saveStr += TimeExitSlave.ToString() + ";";
            saveStr += TimeExitReal.ToString() + ";";

            return saveStr;
        }

        public void LoadFromString(string str)
        {
            string[] save = str.Split(';');

            Enum.TryParse(save[0], out Side);

            PosNumSlave = Convert.ToInt32(save[1]);
            TimeOpenSlave = Convert.ToDateTime(save[2]);
            EnterPriceSlave = save[3].ToDecimal();
            ExitPriceSlave = save[4].ToDecimal();

            PosNumReal = Convert.ToInt32(save[5]);
            TimeOpenReal = Convert.ToDateTime(save[6]);
            EnterPriceReal = save[7].ToDecimal();
            ExitPriceReal = save[8].ToDecimal();

            TimeExitSlave = Convert.ToDateTime(save[9]);
            TimeExitReal = Convert.ToDateTime(save[10]);
        }

        // данные по рабу

        public int PosNumSlave;

        public DateTime TimeOpenSlave;

        public DateTime TimeExitSlave;

        public decimal EnterPriceSlave;

        public decimal ExitPriceSlave;

        public decimal ProfitSlave
        {
            get
            {
                if (Side == Side.Buy)
                {
                    return ExitPriceSlave - EnterPriceSlave;
                }
                else
                {
                    return EnterPriceSlave - ExitPriceSlave;
                }
            }
        }

        public decimal ProfitPercentSlave
        {
            get
            {
                if (EnterPriceSlave == 0)
                {
                    return 0;
                }

                decimal absolute = ProfitSlave;

                return ProfitSlave / EnterPriceSlave * 100;
            }
        }

        // данные по реальной позиции

        public int PosNumReal;

        public DateTime TimeOpenReal;

        public DateTime TimeExitReal;

        public decimal EnterPriceReal;

        public decimal ExitPriceReal;

        public decimal ProfitReal
        {
            get
            {
                if (Side == Side.Buy)
                {
                    return ExitPriceReal - EnterPriceReal;
                }
                else
                {
                    return EnterPriceReal - ExitPriceReal;
                }
            }
        }

        public decimal ProfitPercentReal
        {
            get
            {
                if (EnterPriceReal == 0)
                {
                    return 0;
                }

                decimal absolute = ProfitReal;

                return ProfitReal / EnterPriceReal * 100;
            }
        }

    }

    internal enum VolumeRegimeType
    {
        CONTRACTS_NUMBER,
        CONTRACT_CURRENCY,
        PORTFOLIO_PERCENT
    }
}