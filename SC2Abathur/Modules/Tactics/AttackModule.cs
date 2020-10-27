using Abathur.Constants;
using Abathur.Core;
using Abathur.Core.Combat;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using NydusNetwork.API.Protocol;
using SC2Abathur.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace SC2Abathur.Modules
{
    public class AttackModule : IReplaceableModule
    {
        // Early game mode
        // TODO: toggle behaviour and unit types for later game stages

        private readonly IIntelManager intelManager;
        private readonly IProductionManager productionManager;
        private readonly ICombatManager combatManager;
        private readonly ISquadRepository squadRepo;

        private Dictionary<string, Squad> squads;
        private Squad productionSquad;
        private int squadIdx;
        private int squadSize = 8;

        public IEnumerable<IColony> enemyPositions;

        private bool expanding;

        public AttackModule(IIntelManager intelManager, IProductionManager productionManager,
            ICombatManager combatManager, ISquadRepository squadRepo)
        {
            this.intelManager = intelManager;
            this.productionManager = productionManager;
            this.combatManager = combatManager;
            this.squadRepo = squadRepo;
        }

        public void Initialize() { }

        public void OnStart()
        {
            squads = new Dictionary<string, Squad>();
            squadIdx = 1;
            productionSquad = squadRepo.Create(squadIdx.ToString());

            // Register handlers
            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, OnUnitBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitDestroyed, OnUnitDestroyed);
        }

        public void OnStep()
        {
            if (!intelManager.ProductionQueue.Any(u => IsInfantryUnit(u.UnitId)))
            {
                QueueSquad();
            }
            
            // If surplus of resources, increase production capacity
            if (!expanding && intelManager.Common.Minerals > 500)
            {
                ExpandInfantryProduction();
            }

            // Lets attack stuff!
            foreach (var squad in squads.Values)
            {
                AttackClosest(squad);
            }
        }

        private void ExpandInfantryProduction()
        {
            if (intelManager.ProductionQueue.Any(u => IsInfantryBuilding(u.UnitId)))
                return; // Already producing...

            productionManager.QueueUnit(BlizzardConstants.Unit.Barracks, lowPriority: false, spacing: 3);
            productionManager.QueueUnit(BlizzardConstants.Unit.BarracksTechLab);
            productionManager.QueueUnit(BlizzardConstants.Unit.Barracks, lowPriority: false, spacing: 3);
            productionManager.QueueUnit(BlizzardConstants.Unit.BarracksReactor);
        }

        private void AttackClosest(Squad squad)
        {
            var squadPos = GetSquadCenter(squad);

            // 1: Nearby Units
            var units = intelManager.UnitsEnemyVisible.ToList();
            if (units.Any())
            {
                var target = Geometry.GetClosest(squadPos, units.Select(u => u.Point).ToList());
                combatManager.AttackMove(squad, target);
                return;
            }

            // 2: Nearby Structures
            var structures = intelManager.StructuresEnemyVisible.ToList();
            if (structures.Any())
            {
                var target = Geometry.GetClosest(squadPos, structures.Select(u => u.Point).ToList());
                combatManager.AttackMove(squad, target);
                return;
            }

            // 3: Enemy starting position
            bool first = true;
            foreach (var enemyPos in enemyPositions)
            {
                combatManager.AttackMove(squad, enemyPos.Point, queue: !first);
                first = false;
            }
        }

        public void OnGameEnded() { }

        public void OnRestart() 
        {
            // Deregister handlers
            intelManager.Handler.DeregisterHandler(OnUnitBuilt);
            intelManager.Handler.DeregisterHandler(OnUnitDestroyed);
            intelManager.Handler.DeregisterHandler(OnStructureBuilt);
        }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();

        public void OnStructureBuilt(IUnit structure)
        {
            expanding = intelManager.ProductionQueue.Any(u => IsInfantryBuilding(u.UnitId));
            if (structure.UnitType == BlizzardConstants.Unit.BarracksTechLab)
            {
                if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.CombatShield))
                    productionManager.QueueTech(BlizzardConstants.Research.CombatShield);

                if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.Stimpack))
                    productionManager.QueueTech(BlizzardConstants.Research.Stimpack);

                if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.ConcussiveShells))
                    productionManager.QueueTech(BlizzardConstants.Research.ConcussiveShells);
            }
        }

        public void OnUnitDestroyed(IUnit unit)
        {
            // Check if a squad has been eradicated
            var lostSquads = new List<string>();
            foreach (var squad in squads.Values)
            {
                if (squad.Units.Count == 0)
                    lostSquads.Add(squad.Name);
            }

            foreach (var squadName in lostSquads)
                squads.Remove(squadName);
        }

        public void OnUnitBuilt(IUnit unit)
        {
            if (IsInfantryUnit(unit.UnitType))
            {
                productionSquad.AddUnit(unit);
                if (productionSquad.Units.Count >= squadSize)
                {
                    CloseSquad();
                }
            }
        }

        private void QueueSquad()
        {
            if (!intelManager.StructuresSelf(BlizzardConstants.Unit.Barracks).Any()
             && !intelManager.ProductionQueue.Any(u => IsInfantryBuilding(u.UnitId)))
            {
                ExpandInfantryProduction();
                return;
            }

            for (int i = 0; i < squadSize; i++)
            {
                if (i % 4 == 0)
                    productionManager.QueueUnit(BlizzardConstants.Unit.Marauder);
                else
                    productionManager.QueueUnit(BlizzardConstants.Unit.Marine);
            }
        }

        private void CloseSquad()
        {
            squads.Add(productionSquad.Name, productionSquad);
            squadIdx++;
            productionSquad = squadRepo.Create(squadIdx.ToString());
        }

        private Point2D GetSquadCenter(Squad squad)
        {
            // TODO : make something better
            return squad.Units.First().Point;
        }

        private bool IsInfantryBuilding(uint unitTypeId)
            => unitTypeId == BlizzardConstants.Unit.Barracks
            || unitTypeId == BlizzardConstants.Unit.BarracksTechLab
            || unitTypeId == BlizzardConstants.Unit.BarracksReactor;

        private bool IsInfantryUnit(uint unitTypeId)
            => unitTypeId == BlizzardConstants.Unit.Marauder
            || unitTypeId == BlizzardConstants.Unit.Marine;
    }
}
