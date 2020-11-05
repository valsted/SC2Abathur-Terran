using Abathur.Constants;
using Abathur.Core;
using Abathur.Core.Combat;
using Abathur.Extensions;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using NydusNetwork.API.Protocol;
using System.Collections.Generic;
using System.Linq;

namespace SC2Abathur.Modules
{
    public class GroundModule : IReplaceableModule
    {
        readonly IIntelManager intelManager;
        readonly IProductionManager productionManager;
        readonly ICombatManager combatManager;
        readonly ISquadRepository squadRepo;

        public IEnumerable<IColony> enemyPositions;

        Dictionary<string, Squad> squads;
        Squad productionSquad;
        int squadIdx;
        int squadSize = 4;

        bool isUpgrading;
        List<IUnit> barracks;
        List<IUnit> barrackReactors;
        List<IUnit> barrackLabs;

        public GroundModule(IIntelManager intelManager, IProductionManager productionManager,
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
            barracks = new List<IUnit>();
            barrackReactors = new List<IUnit>();
            barrackLabs = new List<IUnit>();

            squads = new Dictionary<string, Squad>();
            squadIdx = 1;
            productionSquad = squadRepo.Create(squadIdx.ToString());

            // Register handlers
            intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, OnUnitBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitDestroyed, OnUnitDestroyed);
            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);
            intelManager.Handler.RegisterHandler(Case.StructureDestroyed, OnStructureDestroyed);
        }

        public void OnStep()
        {
            if (intelManager.GameLoop % 3 != 0)
                return;

            CheckBuiltStatus();

            // More units!
            if (intelManager.ProductionQueue.Where(u => IsInfantryUnit(u.UnitId)).Count() < squadSize >> 2)
            {
                QueueSquad();
            }
            
            // More buildings!
            if (!intelManager.ProductionQueue.Any(u => IsInfantryBuilding(u.UnitId)))
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
            if (isUpgrading || intelManager.Common.Minerals < 250)
            {
                // Either cannot afford, or have already begun expanding
                return; 
            }
            else
            {
                if (barrackLabs.Count < barrackReactors.Count - 2)
                {
                    productionManager.QueueUnit(BlizzardConstants.Unit.BarracksTechLab);
                }
                else if (barrackReactors.Count < (barracks.Count - barrackLabs.Count))
                {
                    productionManager.QueueUnit(BlizzardConstants.Unit.BarracksReactor);
                } 
                else
                {
                    productionManager.QueueUnit(BlizzardConstants.Unit.Barracks, spacing: 2, lowPriority: false);
                }
            }
        }

        private void CheckBuiltStatus()
        {
            isUpgrading = barracks.Concat(barrackLabs).Concat(barrackReactors)
                .All(u => u.BuildProgress > 99);
        }

        private void AttackClosest(Squad squad)
        {
            var squadPos = GetSquadCenter(squad);

            // 1: Nearby Units
            var units = intelManager.UnitsEnemyVisible.ToList();
            if (units.Any())
            {
                var target = squadPos.GetClosest(units).Point;
                combatManager.AttackMove(squad, target);
                return;
            }

            // 2: Nearby Structures
            var structures = intelManager.StructuresEnemyVisible.ToList();
            if (structures.Any())
            {
                var target = squadPos.GetClosest(structures).Point;
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
            intelManager.Handler.DeregisterHandler(OnStructureDestroyed);
        }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();

        public void OnStructureBuilt(IUnit structure)
        {
            switch (structure.UnitType)
            {
                case BlizzardConstants.Unit.Barracks:
                    barracks.Add(structure);
                    squadSize += 2;
                    break;
                case BlizzardConstants.Unit.BarracksReactor:
                    barrackReactors.Add(structure);
                    squadSize += 2;
                    break;
                case BlizzardConstants.Unit.BarracksTechLab:
                    barrackLabs.Add(structure);
                    QueueInfantryUpgrades();
                    break;
                default:
                    break; // None of our business
            }
        }

        private void QueueInfantryUpgrades()
        {
            if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.CombatShield))
                productionManager.QueueTech(BlizzardConstants.Research.CombatShield);

            if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.ConcussiveShells))
                productionManager.QueueTech(BlizzardConstants.Research.ConcussiveShells, lowPriority: true);

            // TODO: toggle when we know how to use!
            //if (!intelManager.UpgradesSelf.Any(u => u.UpgradeId == BlizzardConstants.Research.Stimpack))
            //    productionManager.QueueTech(BlizzardConstants.Research.Stimpack, lowPriority: true);
        }

        public void OnStructureDestroyed(IUnit structure)
        {
            switch(structure.UnitType)
            {
                case BlizzardConstants.Unit.Barracks:
                    barracks.Remove(structure);
                    squadSize -= 2;
                    break;
                case BlizzardConstants.Unit.BarracksReactor:
                    barrackReactors.Remove(structure);
                    squadSize -= 2;
                    break;
                case BlizzardConstants.Unit.BarracksTechLab:
                    barrackLabs.Remove(structure);
                    break;
                default:
                    break; // None of our business
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
                // Impossible with no infantry buildings..
                return;
            }

            // Only produce marauders if lab && excess vespene
            var makeMarauders = barrackLabs.Count > 0
                && intelManager.Common.Vespene > 100;

            for (int i = 0; i < squadSize; i++)
            {
                if (makeMarauders && i % 4 == 0)
                    productionManager.QueueUnit(BlizzardConstants.Unit.Marauder, lowPriority: true);
                else
                    productionManager.QueueUnit(BlizzardConstants.Unit.Marine, lowPriority: true);
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
