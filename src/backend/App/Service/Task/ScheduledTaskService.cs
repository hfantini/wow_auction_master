﻿/*
 *  + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
 *  
 *  == AUCTION MASTER ==
 *  
 *  Author: Henrique Fantini
 *  Contact: contact@henriquefantini.com
 * 
 *  - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
 *  
 *  Auction Master is a software with the objective to collect and process data 
 *  from World of Warcraft's auction house. The idea behind this is display data 
 *  and graphics about the market of each realm and analyse your movimentation.
 *  
 *  + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
 */

// == IMPORTS
// ==============================================================================

using AuctionMaster.App.Enumeration;
using AuctionMaster.App.Exception;
using AuctionMaster.App.Model;
using AuctionMaster.App.Service.Blizzard;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;

// == NAMESPACE
// ==============================================================================

namespace AuctionMaster.App.Service.Task
{
    // == CLASS
    // ==========================================================================

    public class ScheduledTaskService : IScheduledTaskService
    {
        // == DECLARATIONS
        // ======================================================================

        // == CONST
        private readonly IBlizzardRealmService _blizzardRealmService;
        private readonly IBlizzardAuctionHouseService _blizzardAuctionHouseService;

        // == VAR
        private Timer _timer;
        private IServiceScopeFactory _scopeFactory;
        private Dictionary<int, GenericScheduledTask> _scheduledTasks;

        // == CONSTRUCTOR(S)
        // ======================================================================

        public ScheduledTaskService(IServiceScopeFactory scopeFactory, IBlizzardRealmService blizzardRealmService, IBlizzardAuctionHouseService blizzardAuctionHouseService)
        {
            this._scopeFactory = scopeFactory;
            this._scheduledTasks = new Dictionary<int, GenericScheduledTask>();
            this._blizzardRealmService = blizzardRealmService;
            this._blizzardAuctionHouseService = blizzardAuctionHouseService;

            Console.WriteLine("SCHEDULED TASK SERVICE CREATED.");
        }

        // == METHOD(S)
        // ======================================================================

        public void initTaskService()
        {
            // DISABLE INTERVAL (IF EXISTS)

            if(this._timer != null)
            {
                this._timer.Enabled = false;
            }

            // SYNC TASKS

            this.syncScheduledTasks();

            if (this._timer == null)
            {
                // CREATE NEW INTERVAL

                this.onTimedEvent(null, null);

                this._timer = new Timer();
                this._timer.Elapsed += new ElapsedEventHandler(onTimedEvent);
                this._timer.Interval = 10000;
                this._timer.Enabled = true;
            }
            else
            {
                // ENABLE THE INTERVAL

                this.onTimedEvent(null, null);

                this._timer.Enabled = true;
            }

            Console.WriteLine("SCHEDULED TASK SERVICE INITIALIZED.");
        }

        /// <summary>
        /// Through the task information inside the database, updates the current scheduled task structures.
        /// </summary>
        public void syncScheduledTasks()
        {
            // TREATMENT OF THE CURRENT LIST OF TASKS

            if(this._scheduledTasks.Count > 0)
            {
                foreach(KeyValuePair<int, GenericScheduledTask> entry in this._scheduledTasks)
                {
                    entry.Value.stop();
                }

                this._scheduledTasks.Clear();
            }

            // DATABASE SEARCH FOR TASKS

            List<ScheduledTask> scheduledTaskList = null;

            using (var scope = this._scopeFactory.CreateScope()) 
            {
                DatabaseContext databaseContext = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

                scheduledTaskList = databaseContext.ScheduledTask
                                                        .Include(p => p.ScheduledTaskTypeNavigation)
                                                        .Include(p => p.SheduledTaskFrequencyNavigation)
                                                        .Include(p => p.ScheduledTaskInterval)
                                                        .ToList();
            }

            // REGISTERING TASKS

            foreach (ScheduledTask task in scheduledTaskList)
            {
                GenericScheduledTask taskInstance = null;

                switch(task.ScheduledTaskType)
                {
                    case 1:
                        taskInstance = new RealmScanTask(this._scopeFactory, task, this._blizzardRealmService);
                        break;

                    case 2:
                        taskInstance = new AuctionHouseScanTask(this._scopeFactory, task, this._blizzardAuctionHouseService);
                        break;
                }

                if (task != null)
                {
                    this._scheduledTasks.Add(task.Id, taskInstance);
                }
                else
                {
                    throw new AuctionMasterTaskException(ExceptionType.FATAL, $"The task ({task.Name}) has a invalid type for initialization.");
                }
            }
        }

        // == EVENT(S)
        // ======================================================================

        private void onTimedEvent(object source, ElapsedEventArgs e)
        {
            foreach (KeyValuePair<int, GenericScheduledTask> entry in this._scheduledTasks)
            {
                if (entry.Value.task.Enabled == 1)
                {
                    if (entry.Value.state == ScheduledTaskState.IDLE)
                    {
                        if(entry.Value.task.SheduledTaskFrequencyNavigation.Id == 1 && entry.Value.task.ScheduledTaskInterval != null)
                        {
                            // INTERVAL

                            if (entry.Value.task.LastExecution == null)
                            {
                                entry.Value.start();
                            }
                            else
                            {
                                if ((new DateTime() - entry.Value.task.LastExecution.Value).TotalSeconds > entry.Value.task.ScheduledTaskInterval.First().Interval )
                                {
                                    entry.Value.start();
                                }
                            }
                        }                        
                    }
                }
            }
        }

        // == GETTER(S) AND SETTER(S)
        // ======================================================================
    }
}
