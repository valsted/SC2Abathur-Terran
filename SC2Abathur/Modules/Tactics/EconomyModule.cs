using Abathur.Constants;
using Abathur.Core;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Modules.Services;
using Microsoft.VisualBasic;
using SC2Abathur.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SC2Abathur.Modules.Tactics
{
    public class EconomyModule : IReplaceableModule
    {
        private readonly IIntelManager intelManager;
        private readonly IProductionManager productionManager;
        private readonly ICombatManager combatManager;

        private Random rng = new Random();
        private static int COLONY_MAX_VESPENE_WORKERS = 6;
        private static double ECONOMY_THRIVING_THRESHOLD = 0.8;

        public EconomyMode mode { get; set; }

        public EconomyModule(IIntelManager intelManager, IProductionManager productionManager,
            ICombatManager combatManager)
        {
            this.intelManager = intelManager;
            this.productionManager = productionManager;
            this.combatManager = combatManager;
        }

        public void Initialize() { }

        public void OnStart() 
        {
            // Start focus on minerals
            GetOwnColonies().ForEach(c => c.DesiredVespeneWorkers = 0);

            // Built handlers
            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);

            // Destruction handlers
            intelManager.Handler.RegisterHandler(Case.MineralDepleted, OnMineralDepleted);
            intelManager.Handler.RegisterHandler(Case.StructureDestroyed, OnStructureLost);
            
            // intelManager.Handler.RegisterHandler(Case.VespeneDepleted, OnVespeneDepleted);

            mode = EconomyMode.Expand;
        }

        public void OnStep()
        {
            if (intelManager.GameLoop % 5 != 0)
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
                    if (ownColonies.All(c => GetEconomyState(c) == EconomyState.Thriving)
                        && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.CommandCenter))
                    {
                        var expansionSpot = FindExpansionSpace();
                        productionManager.QueueUnit(BlizzardConstants.Unit.CommandCenter, desiredPosition: expansionSpot.Point);  // Spacing 3 for mineral patch distance
                    }
                    break;
            }

            BalanceWorkers();

            // TODO: check for idle workers (even after auto-harvest-gather)
            // and auto-cast repair, and place near colonies
        }

        public void OnStructureBuilt(IUnit structure)
        {
            if (structure.UnitType == BlizzardConstants.Unit.Refinery)
                OnRefineryBuilt(structure);
            else if (IsTerranResourceCenter(structure))
                OnCommandCenterBuilt(structure);
        }

        private void OnCommandCenterBuilt(IUnit commandCenter)
        {
            // Find new center, and divert workers from existing (to balance auto-harvest gather)
            var ownColonies = GetOwnColonies();
            var newColony = (IColony)Geometry.GetClosest(commandCenter, ownColonies);
            newColony.DesiredVespeneWorkers = 0;
        }

        private void OnRefineryBuilt(IUnit refinery)
        {
            // Find refinery's colony and increase desired workers
            var colony = Geometry.GetClosest(refinery, GetOwnColonies());
            ((IColony) colony).DesiredVespeneWorkers += 1;
        }


        public void OnStructureLost(IUnit structure)
        {
            // throw new NotImplementedException("TODO... Handle lost economy structures");
        }

        public void OnMineralDepleted(IUnit mineralField)
        {
            //throw new NotImplementedException("TODO... handle expired minerals");
        }

        public void OnVespeneDepleted(IUnit vespeneGeyser)
        {
            // Maybe raise an issue on this one 
            // throw new NotImplementedException("TODO... handle expired vespene");
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

        private void BalanceWorkers()
        {
            var ownColonies = GetOwnColonies();
            var surplusColonies = ownColonies.Where(c => GetEconomyState(c) == EconomyState.Surplus);
            var missingColonies = new Stack<IColony>();
            ownColonies.Where(c => GetEconomyState(c) == EconomyState.Missing).ToList()
                .ForEach(c => missingColonies.Push(c));

            foreach (var colony in surplusColonies)
            {
                if (missingColonies.Count() == 0)
                    return;  // We need more cc instead..

                var surplusWorkers = colony.Workers.Take(colony.Workers.Count() - OptimalMineralWorkers(colony)).ToList();
                var targetColony = missingColonies.Pop();
                surplusWorkers.ForEach(w => TransferWorker(w, colony, targetColony));
            }
        }

        private void TransferWorker(IUnit w, IColony fromColony, IColony toColony)
        {
            combatManager.Move(w.Tag, GetRandomMineralPatch(toColony).Point);
            fromColony.Workers.Remove(w);
            toColony.Workers.Add(w);
        }

        private void FillColonyEconomy(IColony colony)
        {
            var currentWorkers = colony.Workers.Count();
            var workersInQueue = intelManager.ProductionQueue.Where(u => u.UnitId == BlizzardConstants.Unit.SCV).Count();

            // Check minerals
            var optimalMineralWorkers = OptimalMineralWorkers(colony);
            var curMineralWorkers = currentWorkers - colony.DesiredVespeneWorkers;
            if (curMineralWorkers < optimalMineralWorkers) 
            {
                if (workersInQueue < (optimalMineralWorkers - curMineralWorkers))
                {
                    productionManager.QueueUnit(BlizzardConstants.Unit.SCV);
                }
            }
            else
            {
                // Check vespene
                var vespeneCapacity = VespeneWorkerCapacity(colony);
                if (colony.DesiredVespeneWorkers < vespeneCapacity)
                {
                    colony.DesiredVespeneWorkers += 1;   // Make production manager divert 
                }
                else if (vespeneCapacity < COLONY_MAX_VESPENE_WORKERS 
                    && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.Refinery))
                {
                    productionManager.QueueUnit(BlizzardConstants.Unit.Refinery,
                        desiredPosition: colony.Point, lowPriority: false);
                }
            }
        }

        private EconomyState GetEconomyState(IColony colony)
        {
            var target = OptimalMineralWorkers(colony) + colony.DesiredVespeneWorkers;
            var current = colony.Workers.Count();
            if (current < target)
                if (current > (ECONOMY_THRIVING_THRESHOLD * target))
                    return EconomyState.Thriving;
                else
                    return EconomyState.Missing;
            else if (current == target)
                return EconomyState.Saturated;
            else
                return EconomyState.Surplus;
        }

        private List<IColony> GetOwnColonies()
        {
            var ownColonies = new List<IColony>();
            var commandCenters = intelManager.StructuresSelf().Where(IsTerranResourceCenter).ToList();
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
            var candidates = intelManager.Colonies.Where(c => !c.IsStartingLocation).Where(c => c.Structures.Count() == 0);
            var closest = Geometry.GetClosest(intelManager.PrimaryColony, candidates);
            return (IColony) closest;
        }

        private IUnit GetRandomMineralPatch(IColony colony)
        {
            var patches = colony.Minerals.ToList();
            return patches[rng.Next(patches.Count)];
        }

        private bool IsTerranResourceCenter(IUnit unit)
            => unit.UnitType == BlizzardConstants.Unit.CommandCenter
               || unit.UnitType == BlizzardConstants.Unit.OrbitalCommand
               || unit.UnitType == BlizzardConstants.Unit.PlanetaryFortress;
    }

    public enum EconomyMode
    {
        Standby,
        FillExisting,
        Expand
    }

    public enum EconomyState
    {
        Missing,
        Thriving, // Close to full
        Saturated, // We want to be here for all colonies optimally
        Surplus
    }
}
