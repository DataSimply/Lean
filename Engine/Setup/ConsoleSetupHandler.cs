﻿/*
* QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals, V0.1
* Created by Jared Broad
*/

/**********************************************************
* USING NAMESPACES
**********************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.AlgorithmFactory;
using QuantConnect.Brokerages;
using QuantConnect.Brokerages.Backtesting;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Logging;
using QuantConnect.Packets;

namespace QuantConnect.Lean.Engine.Setup
{
    /// <summary>
    /// Console setup handler to initialize and setup the Lean Engine properties for a local backtest
    /// </summary>
    public class ConsoleSetupHandler : ISetupHandler
    {
        /******************************************************** 
        * PUBLIC PROPERTIES
        *********************************************************/
        /// <summary>
        /// Error which occured during setup may appear here.
        /// </summary>
        public List<string> Errors { get;  set; }

        /// <summary>
        /// Maximum runtime of the strategy. (Set to 10 years for local backtesting).
        /// </summary>
        public TimeSpan MaximumRuntime { get; private set; }

        /// <summary>
        /// Starting capital for the algorithm (Loaded from the algorithm code).
        /// </summary>
        public decimal StartingCapital { get; private set; }

        /// <summary>
        /// Start date for the backtest.
        /// </summary>
        public DateTime StartingDate { get; private set; }

        /// <summary>
        /// Maximum number of orders for this backtest.
        /// </summary>
        public int MaxOrders { get; private set; }

        /******************************************************** 
        * PUBLIC CONSTRUCTOR
        *********************************************************/
        /// <summary>
        /// Setup the algorithm data, cash, job start end date etc:
        /// </summary>
        public ConsoleSetupHandler()
        {
            MaxOrders = int.MaxValue;
            StartingCapital = 0;
            StartingDate = new DateTime(1998, 01, 01);
            MaximumRuntime = TimeSpan.FromDays(10 * 365);
            Errors = new List<string>();
        }

        /******************************************************** 
        * PUBLIC METHODS
        *********************************************************/
        /// <summary>
        /// Creates a new algorithm instance. Checks configuration for a specific type name, and if present will
        /// force it to find that one
        /// </summary>
        /// <param name="assemblyPath">Physical path of the algorithm dll.</param>
        /// <returns>Algorithm instance</returns>
        public IAlgorithm CreateAlgorithmInstance(string assemblyPath)
        {
            string error;
            IAlgorithm algorithm;
            var algorithmName = Config.Get("algorithm-type-name");

            // don't force load times to be fast here since we're running locally, this allows us to debug
            // and step through some code that may take us longer than the default 10 seconds
            var loader = new Loader(TimeSpan.FromHours(1), names => names.Single(name => MatchTypeName(name, algorithmName)));

            var complete = loader.TryCreateAlgorithmInstanceWithIsolator(assemblyPath, out algorithm, out error);
            if (!complete) throw new Exception(error + ": try re-building algorithm.");

            return algorithm;
        }

        /// <summary>
        /// Setup the algorithm cash, dates and portfolio as desired.
        /// </summary>
        /// <param name="algorithm">Existing algorithm instance</param>
        /// <param name="brokerage">New brokerage instance</param>
        /// <param name="baseJob">Backtesting job</param>
        /// <returns>Boolean true on successfully setting up the console.</returns>
        public bool Setup(IAlgorithm algorithm, out IBrokerage brokerage, AlgorithmNodePacket baseJob)
        {
            var initializeComplete = false;
            brokerage = new BacktestingBrokerage(algorithm);

            try
            {
                //Set common variables for console programs:

                if (baseJob.Type == PacketType.BacktestNode)
                {
                    var backtestJob = baseJob as BacktestNodePacket;
                    
                    //Set the limits on the algorithm assets (for local no limits)
                    algorithm.SetAssetLimits(999, 999, 999);
                    algorithm.SetMaximumOrders(int.MaxValue);

                    //Setup Base Algorithm:
                    algorithm.Initialize();
                    //Add currency data feeds that weren't explicity added in Initialize
                    algorithm.Portfolio.CashBook.EnsureCurrencyDataFeeds(algorithm.SubscriptionManager, algorithm.Securities);

                    //Construct the backtest job packet:
                    backtestJob.PeriodStart = algorithm.StartDate;
                    backtestJob.PeriodFinish = algorithm.EndDate;
                    backtestJob.BacktestId = "LOCALHOST";
                    backtestJob.UserId = 1001;
                    backtestJob.Type = PacketType.BacktestNode;

                    //Endpoints:
                    backtestJob.TransactionEndpoint = TransactionHandlerEndpoint.Backtesting;
                    backtestJob.ResultEndpoint = ResultHandlerEndpoint.Console;
                    backtestJob.DataEndpoint = DataFeedEndpoint.FileSystem;
                    backtestJob.RealTimeEndpoint = RealTimeEndpoint.Backtesting;
                    backtestJob.SetupEndpoint = SetupHandlerEndpoint.Console;

                    //Backtest Specific Parameters:
                    StartingDate = backtestJob.PeriodStart;
                    StartingCapital = algorithm.Portfolio.Cash;
                }
                else
                {
                    var liveJob = baseJob as LiveNodePacket;
                    
                    //Live Job Parameters:
                    liveJob.DeployId = "LOCALHOST";
                    liveJob.IssuedAt = DateTime.Now.Subtract(TimeSpan.FromSeconds(86399 - 60));     //For testing, first access token expires in 60 sec. refresh.
                    liveJob.LifeTime = TimeSpan.FromSeconds(86399);
                    liveJob.AccessToken = "123456";
                    liveJob.AccountId = "123456";
                    liveJob.RefreshToken = "";
                    liveJob.Type = PacketType.LiveNode;

                    //Endpoints:
                    liveJob.TransactionEndpoint = TransactionHandlerEndpoint.Backtesting;
                    liveJob.ResultEndpoint = ResultHandlerEndpoint.LiveTrading;
                    liveJob.DataEndpoint = DataFeedEndpoint.LiveTrading;
                    liveJob.RealTimeEndpoint = RealTimeEndpoint.LiveTrading;
                    liveJob.SetupEndpoint = SetupHandlerEndpoint.Console;

                    //Call in the paper trading setup:
                    var setup = new PaperTradingSetupHandler();
                    setup.Setup(algorithm, out brokerage, baseJob);

                    //Live Specific Parameters:
                    StartingDate = DateTime.Now;
                    StartingCapital = algorithm.Portfolio.Cash;
                }
            }
            catch (Exception err)
            {
                Log.Error("ConsoleSetupHandler().Setup(): " + err.Message);
                Errors.Add("Failed to initialize algorithm: Initialize(): " + err.Message);
            }

            if (Errors.Count == 0)
            {
                initializeComplete = true;
            }

            return initializeComplete;
        }

        /// <summary>
        /// Error handlers in event of a brokerage error.
        /// </summary>
        /// <param name="results">Result handler for sending results on error.</param>
        /// <param name="brokerage">Brokerage instance</param>
        /// <remarks>Not used for local setup.</remarks>
        /// <returns>Boolean true on successfully setting up local algorithm</returns>
        public bool SetupErrorHandler(IResultHandler results, IBrokerage brokerage)
        {
            return true;
        }

        /// <summary>
        /// Matches type names as namespace qualified or just the name
        /// If expectedTypeName is null or empty, this will always return true
        /// </summary>
        /// <param name="currentTypeFullName"></param>
        /// <param name="expectedTypeName"></param>
        /// <returns>True on matching the type name</returns>
        private static bool MatchTypeName(string currentTypeFullName, string expectedTypeName)
        {
            if (string.IsNullOrEmpty(expectedTypeName))
            {
                return true;
            }
            return currentTypeFullName == expectedTypeName
                || currentTypeFullName.Substring(currentTypeFullName.LastIndexOf('.') + 1) == expectedTypeName;
        }

    } // End Result Handler Thread:

} // End Namespace
