using Abathur;
using Abathur.Constants;
using Abathur.Core;
using Abathur.Extensions;
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
        // Framework repos
        IAbathur abathur;
        readonly IIntelManager intelManager;
        readonly ICombatManager combatManager;
        readonly IProductionManager productionManager;
        readonly ISquadRepository squadRepo;
        readonly IRawManager rawManager;

        // Global AV modules state
        StateSnapshot snapshot;

        // Tactical modules
        List<IReplaceableModule> activeTactics;
        EconomyModule economyModule;

        bool startupSequenceDone = false;
        bool infantryUpgraded = false;

        public AVStrategy(IAbathur abathur, IIntelManager intelManager, ICombatManager combatManager, 
            IProductionManager productionManager, ISquadRepository squadRepo, IRawManager rawManager)
        {
            this.abathur = abathur;
            this.intelManager = intelManager;
            this.combatManager = combatManager;
            this.productionManager = productionManager;
            this.squadRepo = squadRepo;
            this.rawManager = rawManager;
        }

        #region Framework hooks

        public void Initialize() { }

        public void OnStart()
        {
            snapshot = new StateSnapshot();
            snapshot.UpdateState(intelManager);

            activeTactics = new List<IReplaceableModule>();

            economyModule = new EconomyModule(snapshot, intelManager, productionManager, combatManager, rawManager);
            abathur.AddToGameloop(economyModule);

            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);

            // Startup Queue
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.SupplyDepot, lowPriority: false, spacing: 1);
            productionManager.QueueUnit(BlizzardConstants.Unit.Refinery, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.Barracks, lowPriority: false, spacing: 3);
            productionManager.QueueUnit(BlizzardConstants.Unit.SupplyDepot, lowPriority: true, spacing: 1);
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.BarracksTechLab, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.Barracks, lowPriority: false, spacing: 3);
            productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: false);
            productionManager.QueueUnit(BlizzardConstants.Unit.BarracksReactor, lowPriority: false);
            productionManager.QueueTech(BlizzardConstants.Research.CombatShield, lowPriority: false);
        }

        public void OnStep() 
        {
            snapshot.UpdateState(intelManager);

            if (intelManager.GameLoop % 100 == 0)
            {
				Console.WriteLine("Attacking: " + snapshot.Attacking);
				Helpers.PrintProductionQueue(intelManager);
				activeTactics.ForEach(t => Console.WriteLine(t.GetType().Name));
				
            }
            
            // 0. Build basic econ and defense
            startupSequenceDone = CheckStartupDone();

            // 1. Assess threats? (10%)
            var ownUnitCount = intelManager.UnitsSelf().Count();
            var enemyUnitCount = intelManager.UnitsEnemy().Count();
            if (snapshot.BaseThreats.Sum(kv => kv.Value.Count) > (0.2 * ownUnitCount))
			{
                snapshot.Attacking = false;
                snapshot.EconomyMode = EconomyMode.Standby;
            }
            else if (ownUnitCount > enemyUnitCount)
			{
                snapshot.Attacking = true;
                snapshot.EconomyMode = EconomyMode.Expand;
            } 
            else
			{
                snapshot.Attacking = false;  // Build up as default
                snapshot.EconomyMode = EconomyMode.Expand;
			}

            // 2. Coordinate modules
            if (intelManager.StructuresSelf(BlizzardConstants.Unit.Barracks).Any()
                    && !TacticActive(typeof(InfantryModule)))
            {
                AddTactic(new InfantryModule(snapshot, intelManager, productionManager, combatManager, squadRepo));
            }

            if (startupSequenceDone) 
            {
                if (!infantryUpgraded)
                {
                    QueueInfantryUpgrades();
                    infantryUpgraded = true;
                }

                if (intelManager.GameLoop > 600 // 1 m 30 sec?
                    && !TacticActive(typeof(MechModule)))  
				{
                    AddTactic(new MechModule(snapshot, intelManager, productionManager, combatManager, squadRepo));
                }

                if (intelManager.GameLoop > 1800 // 3 m?
                    && !TacticActive(typeof(AirModule)))
                {
                    AddTactic(new AirModule(snapshot, intelManager, productionManager, combatManager, squadRepo));
                }
            }
        }

        private bool CheckStartupDone()
        {
            if (!startupSequenceDone)
            {
                // Call it a bit early, in case we get rushed
                var lab = intelManager.StructuresSelf(BlizzardConstants.Unit.BarracksTechLab).FirstOrDefault();
                return Helpers.BuildCompleted(lab);
            }
            return true;
        }

        public void OnGameEnded() { }

        public void OnRestart() { }

        #endregion

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
                    break;
                default:
                    break;
            }
        }

        private void QueueInfantryUpgrades()
        {
            if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.CombatShield))
                productionManager.QueueTech(BlizzardConstants.Research.CombatShield);

            if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.ConcussiveShells))
                productionManager.QueueTech(BlizzardConstants.Research.ConcussiveShells, lowPriority: true);
        }

        private bool TacticActive(Type tacticType) => activeTactics.Any(t => t.GetType() == tacticType);
    }

}
