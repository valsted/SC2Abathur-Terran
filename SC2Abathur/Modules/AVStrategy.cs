using Abathur;
using Abathur.Constants;
using Abathur.Core;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using SC2Abathur.Modules.Tactics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SC2Abathur.Modules {

    // Everything inheriting from IModule can be added in the Abathur setup file (use class name)
    // The Abathur Framework will handle instantiation and call events.

    public class AVStrategy : IModule 
    {
        IEnumerable<IColony> enemyStartLocations;

        // Framework repos
        IAbathur abathur;
        readonly IIntelManager intelManager;
        readonly ICombatManager combatManager;
        readonly IProductionManager productionManager;
        readonly ISquadRepository squadRepo;

        // Tactical modules
        List<IReplaceableModule> activeTactics;
        GroundModule attackModule;
        EconomyModule economyModule;
        AirModule airModule;
        // TODO: Add

        bool isBoostingSupply;

        public AVStrategy(IAbathur abathur, IIntelManager intelManager, ICombatManager combatManager, 
            IProductionManager productionManager, ISquadRepository squadRep,
            GroundModule attackModule, EconomyModule economyModule, AirModule airModule)
        {
            this.abathur = abathur;
            this.intelManager = intelManager;
            this.combatManager = combatManager;
            this.productionManager = productionManager;
            this.squadRepo = squadRep;

            // Tactics
            this.attackModule = attackModule;
            this.economyModule = economyModule;
            this.airModule = airModule;
        }

        #region Framework hooks

        // Called after connection is established to the StarCraft II Client, but before a game is entered.
        public void Initialize() { }

        // Called on the first frame in each game.
        public void OnStart()
        {
            activeTactics = new List<IReplaceableModule>();

            FindStartingLocations();
            attackModule.enemyPositions = enemyStartLocations;
            airModule.enemyPositions = enemyStartLocations;

            AddTactic(economyModule);
            economyModule.mode = EconomyMode.FillExisting;

            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);
        }

        // Called in every frame - except the first (use OnStart).
        // This method is called asynchronous if the framework IsParallelized is true in the setup file.
        public void OnStep() 
        {
            if (intelManager.GameLoop % 100 == 0)
            {
                Helpers.PrintProductionQueue(intelManager);
                activeTactics.ForEach(t => Console.WriteLine(t.GetType()));
            }


            if (intelManager.GameLoop == 600) // After 1 minute?
            {
                AddTactic(attackModule);
                economyModule.mode = EconomyMode.Expand;
            }

            if (intelManager.GameLoop == 4000)
            {
                RemoveTactic(attackModule);
                RemoveTactic(economyModule);
                AddTactic(airModule);
            }

            if (!isBoostingSupply && intelManager.ProductionQueue.Count() > 10 
                && intelManager.Common.FoodCap - intelManager.Common.FoodUsed < 5)
            {
                // We need to prioritize supply first
                isBoostingSupply = true;
                productionManager.ClearBuildOrder();
                productionManager.QueueUnit(BlizzardConstants.Unit.SupplyDepot);
                return;
            }
        }

        public void OnGameEnded() { }

        public void OnRestart() { }

        #endregion

        private void FindStartingLocations()
        {
            // Colonies marked with starting location are possible starting locations of the enemy, never yourself
            enemyStartLocations = intelManager.Colonies.Where(c => c.IsStartingLocation);
        }

        private void AddTactic(IReplaceableModule tactic)
        {
            activeTactics.Add(tactic);
            abathur.AddToGameloop(tactic);
        }

        private void RemoveTactic(IReplaceableModule tactic)
        {
            activeTactics.Remove(tactic);
            abathur.RemoveFromGameloop(tactic);
        }

        public void OnStructureBuilt(IUnit structure)
        {
            switch(structure.UnitType)
            {
                case BlizzardConstants.Unit.SupplyDepot:
                    if (!intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.SupplyDepot))
                        isBoostingSupply = false;
                    break;
                default:
                    break;
            }
        }
    }
}
