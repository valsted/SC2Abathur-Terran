using Abathur.Constants;
using Abathur.Core;
using Abathur.Model;
using Abathur.Modules;
using Microsoft.VisualBasic;
using SC2Abathur.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SC2Abathur.Modules.Tactics
{
    public class EconomyModule : IReplaceableModule
    {
        private readonly IIntelManager intelManager;
        private readonly IProductionManager productionManager;

        private static int COLONY_MAX_VESPENE_WORKERS = 6;

        public EconomyMode mode { get; set; }

        public EconomyModule(IIntelManager intelManager, IProductionManager productionManager)
        {
            this.intelManager = intelManager;
            this.productionManager = productionManager;
        }

        public void Initialize() { }

        public void OnStart() 
        {
            // Register handlers
            intelManager.Handler.RegisterHandler(Case.MineralDepleted, OnMineralDepleted);
            intelManager.Handler.RegisterHandler(Case.StructureDestroyed, OnStructureLost);
            // intelManager.Handler.RegisterHandler(Case.VespeneDepleted, OnVespeneDepleted);

            mode = EconomyMode.Expand;
        }

        public void OnStep()
        {
            if (intelManager.GameLoop % 10 != 0)
                return;  // We don't need to be that high-frequency..

            switch (mode)
            {
                case EconomyMode.Standby:
                    // TODO: call down MULEs
                    return;
                case EconomyMode.FillExisting:
                    GetOwnColonies().ForEach(FillColonyEconomy);
                    break;
                case EconomyMode.Expand:
                    var ownColonies = GetOwnColonies();
                    ownColonies.ForEach(FillColonyEconomy);
                    if (ownColonies.All(ColonyEconomyFilled)
                        && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.CommandCenter))
                    {
                        var expansionSpot = FindExpansionSpace();
                        productionManager.QueueUnit(BlizzardConstants.Unit.CommandCenter, desiredPosition: expansionSpot.Point);
                    }
                    break;
            }
            // TODO: check for idle workers (even after auto-harvest-gather)
            // and auto-cast repair, and place near colonies
        }

        public void OnScvBuilt(IUnit scv)
        {
            // TODO: autobalance among existing resource-centers
        }

        public void OnCommandCenterBuilt(IUnit commandCenter)
        {

        }

        public void OnRefineryBuilt(IUnit refinery)
        {
            if (refinery.UnitType == BlizzardConstants.Unit.Refinery)
            {
                // Find refinery's colony and increase desired workers
                var colony = Geometry.GetClosest(refinery, GetOwnColonies());
                ((IColony) colony).DesiredVespeneWorkers += 2;
            }
        }


        public void OnStructureLost(IUnit structure)
        {
            //throw new NotImplementedException("TODO... Handle lost economy structures");
        }

        public void OnMineralDepleted(IUnit mineralField)
        {
            //throw new NotImplementedException("TODO... handle expired minerals");
        }

        public void OnVespeneDepleted(IUnit vespeneGeyser)
        {
            // Maybe raise an issue on this one 
            //throw new NotImplementedException("TODO... handle expired vespene");
        }

        public void OnGameEnded() { }

        public void OnRestart()
        {
            // Deregister handlers
            intelManager.Handler.DeregisterHandler(OnStructureLost);
            intelManager.Handler.DeregisterHandler(OnMineralDepleted);
        }


        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();

        private void FillColonyEconomy(IColony colony)
        {
            var currentWorkers = colony.Workers.Count();
            var workersInQueue = intelManager.ProductionQueue.Where(u => u.UnitId == BlizzardConstants.Unit.SCV).Count();

            // Check minerals
            var optimalMineralWorkers = OptimalMineralWorkers(colony);
            var curMineralWorkers = currentWorkers - colony.DesiredVespeneWorkers;
            if ((curMineralWorkers + workersInQueue) < optimalMineralWorkers) 
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.SCV);
            }
            else
            {
                // Check vespene
                var vespeneCapacity = VespeneWorkerCapacity(colony);
                if (vespeneCapacity < COLONY_MAX_VESPENE_WORKERS
                    && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.Refinery))
                {
                    productionManager.QueueUnit(BlizzardConstants.Unit.Refinery,
                        desiredPosition: colony.Point, lowPriority: false);
                }
            }
        }

        private bool ColonyEconomyFilled(IColony colony)
        {
            return colony.Workers.Count() == (OptimalMineralWorkers(colony) + colony.DesiredVespeneWorkers);
        }

        private List<IColony> GetOwnColonies()
        {
            var ownColonies = new List<IColony>();
            var commandCenters = intelManager.StructuresSelf(BlizzardConstants.Unit.CommandCenter).ToList();
            foreach (var colony in intelManager.Colonies)
            {
                if (commandCenters.Any(cc => colony.Structures.Contains(cc))) {
                    ownColonies.Add(colony);
                }
            }
            return ownColonies;
        }

        private int VespeneWorkerCapacity(IColony colony)
            => colony.Structures.Where(s => s.UnitType == BlizzardConstants.Unit.Refinery).Count() * 3;

        private int OptimalMineralWorkers(IColony colony) 
            => colony.Minerals.Count() * 2;

        private IColony FindExpansionSpace()
        {
            var ownColonies = GetOwnColonies();
            var enemyColonies = GetEnemyColonyCenters();
            var candidates = intelManager.Colonies.Where(c => !c.IsStartingLocation)
                .Where(c => !ownColonies.Contains(c))
                .Where(c => !enemyColonies.Any(e => e.Point == c.Point)).ToList();

            var closest = Geometry.GetClosest(intelManager.PrimaryColony, candidates);
            return (IColony) closest;
        }


        // Returns detected colony center structures (command center, nexus, hatchery)
        private List<IUnit> GetEnemyColonyCenters()
            => intelManager.StructuresEnemy().Where(s => IsResourceCenter(s)).ToList();

        private bool IsResourceCenter(IUnit unit)
        {
            return unit.UnitType == BlizzardConstants.Unit.CommandCenter
                || unit.UnitType == BlizzardConstants.Unit.Hatchery
                || unit.UnitType == BlizzardConstants.Unit.Nexus;
        }
    }

    public enum EconomyMode
    {
        Standby,
        FillExisting,
        Expand
    }
}
