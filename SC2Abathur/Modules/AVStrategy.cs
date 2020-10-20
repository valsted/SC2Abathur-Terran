using Abathur;
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
        //private List
        private Strategy strategy;
        private IEnumerable<IColony> enemyStartLocations;

        // Framework repos
        // Need access to replace modules.
        private IAbathur abathur;
        private readonly IIntelManager intelManager;
        private readonly ICombatManager combatManager;
        private readonly IProductionManager productionManager;
        private readonly ISquadRepository squadRepo;

        // Tactical modules
        private List<IReplaceableModule> activeTactics;
        private OpenerModule openerModule;
        private AttackModule attackModule;
        // TODO: Add

        public AVStrategy(IAbathur abathur, IIntelManager intelManager, ICombatManager combatManager, 
            IProductionManager productionManager, ISquadRepository squadRep,
            OpenerModule openerModule, AttackModule attackModule)
        {
            this.abathur = abathur;
            this.intelManager = intelManager;
            this.combatManager = combatManager;
            this.productionManager = productionManager;
            this.squadRepo = squadRep;

            // Tactics
            this.openerModule = openerModule;
            this.attackModule = attackModule;
        }

        #region Framework hooks

        // Called after connection is established to the StarCraft II Client, but before a game is entered.
        public void Initialize() {
            activeTactics = new List<IReplaceableModule>();
        }

        // Called on the first frame in each game.
        public void OnStart()
        {
            FindStartingLocations();
            attackModule.enemyPositions = enemyStartLocations;

            AddTactic(openerModule);
            strategy = Strategy.Opener;
        }


        // Called in every frame - except the first (use OnStart).
        // This method is called asynchronous if the framework IsParallelized is true in the setup file.
        public void OnStep() 
        {
            switch (strategy)
            {
                case Strategy.Opener:
                    if (openerModule.Completed)
                    {
                        RemoveTactic(openerModule);
                        AddTactic(attackModule);
                        strategy = Strategy.Aggression;
                    }
                    break;
                case Strategy.Aggression:
                    // We just stay here...
                    break;
            }
        }

        // Called when game has ended but before leaving the match.
        public void OnGameEnded() {}

        // Called before starting when starting a new game (but not the first) - can be called mid-game if a module request a restart
        public void OnRestart() 
        {
            // _abathur.RemoveFromGameloop(_terranModule);
            strategy = Strategy.Opener;
        }

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
    }

    public enum Strategy
    {
        Opener,
        Aggression
    }
}
