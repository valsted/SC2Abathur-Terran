using Abathur.Constants;
using Abathur.Core;
using Abathur.Core.Combat;
using Abathur.Extensions;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SC2Abathur.Modules.Tactics
{
	public class MechModule : IReplaceableModule
    {
        readonly string SQUAD_PREFIX = "vec";
        readonly int SQUAD_SIZE = 3;

        readonly IIntelManager intelManager;
        readonly IProductionManager productionManager;
        readonly ICombatManager combatManager;
        readonly ISquadRepository squadRepo;

        StateSnapshot snapshot;

        Random rng = new Random();

        Dictionary<string, Squad> squads;
        Dictionary<ulong, string> unitToSquad; // Inverted index
        Squad currentSquad;
        uint squadCounter = 1;
        uint unitCounter = 0;

        List<ProductionFacility> factories;

        public MechModule(StateSnapshot snapshot,
            IIntelManager intelManager, IProductionManager productionManager,
            ICombatManager combatManager, ISquadRepository squadRepo)
        {
            this.snapshot = snapshot;
            this.intelManager = intelManager;
            this.productionManager = productionManager;
            this.combatManager = combatManager;
            this.squadRepo = squadRepo;
        }

        public void Initialize() { }

        public void OnStart()
        {
            factories = new List<ProductionFacility>();
            intelManager.StructuresSelf(BlizzardConstants.Unit.Factory, BlizzardConstants.Unit.FactoryReactor)
                .ToList().ForEach(b => factories.Add(new ProductionFacility(b)));

            squads = new Dictionary<string, Squad>();
            unitToSquad = new Dictionary<ulong, string>();
            currentSquad = squadRepo.Create(NextSquadName());

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

            // More units!
            if (ProductionCapacityAvailable())
            {
                QueueUnit();
            }

            // More buildings!
            if (!intelManager.StructuresSelf(BlizzardConstants.Unit.Factory).Any()
                && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.Factory))
			{
                productionManager.QueueUnit(BlizzardConstants.Unit.Factory, spacing: 2);
			}
            else if (FactoriesReady() && !intelManager.ProductionQueue.Any(u => IsFactory(u.UnitId))
                && factories.Count < squads.Count)
            {
                QueueProductionFacility();
            }

            foreach (var (name, squad) in squads)
            {
                if (snapshot.Attacking)
                {
                    Attack(squad);
                }
                else
                {
                    Defend(squad);
                }
            }
        }

        private void Attack(Squad squad)
        {
            var squadPos = Helpers.GetAvgLocation(squad.Units);

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
            foreach (var enemyPos in snapshot.EnemyColonies)
            {
                combatManager.AttackMove(squad, enemyPos.Point, queue: !first);
                first = false;
            }
        }

        private void Defend(Squad squad)
        {
            var squadPos = Helpers.GetAvgLocation(squad.Units);
            var maxEnemyCount = snapshot.BaseThreats.Max(x => x.Value.Count);
            if (maxEnemyCount > 0)
            {
                var underAttack = snapshot.BaseThreats.Where(x => x.Value.Count == maxEnemyCount).First();

                // 2: Nearby Units
                var target = squadPos.GetClosest(underAttack.Value).Point;
                combatManager.AttackMove(squad, target);
                return;
            }

            // 3: Just move to one of our bases
            var defendColony = snapshot.OwnColonies[rng.Next(snapshot.OwnColonies.Count)];
            combatManager.AttackMove(squad, defendColony.Point);
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
                case BlizzardConstants.Unit.Factory:
                case BlizzardConstants.Unit.FactoryReactor:
                    var facility = new ProductionFacility(structure);
                    factories.Add(facility);
                    break;
                default:
                    break; // None of our business
            }
        }

        public void OnStructureDestroyed(IUnit structure)
        {
            switch (structure.UnitType)
            {
                case BlizzardConstants.Unit.Factory:
                case BlizzardConstants.Unit.FactoryReactor:
                    var facility = new ProductionFacility(structure);
                    factories.Remove(facility);
                    break;
                default:
                    break; // None of our business
            }
        }

        public void OnUnitDestroyed(IUnit unit)
        {
            if (unitToSquad.ContainsKey(unit.Tag))
            {
                var squad = squadRepo.Get(unitToSquad[unit.Tag]);
                squad.RemoveUnit(unit);
                if (squad.Units.Count == 0)
                {
                    squads.Remove(squad.Name);
                }

                unitToSquad.Remove(unit.Tag);
            }
        }

        public void OnUnitBuilt(IUnit unit)
        {
            if (IsMechUnit(unit.UnitType))
            {
                unitToSquad.Add(unit.Tag, currentSquad.Name);
                currentSquad.AddUnit(unit);
                if (currentSquad.Units.Count >= SQUAD_SIZE)
                {
                    CloseSquad();
                }
            }
        }

        private void CloseSquad()
        {
            squads.Add(currentSquad.Name, currentSquad);
            currentSquad = squadRepo.Create(NextSquadName());
        }

        private void QueueUnit()
        {
            if (intelManager.StructuresSelf(BlizzardConstants.Unit.FactoryTechLab).Any()
                && unitCounter % 3 == 0)
			{
                productionManager.QueueUnit(BlizzardConstants.Unit.HellionTank); // Hellbat?
            } 
            else
			{
                productionManager.QueueUnit(BlizzardConstants.Unit.Hellion);
			}
            unitCounter++;
        }

        private void QueueProductionFacility()
        {
            var factoryCount = factories.Where(b => b.Ready && b.Structure.UnitType == BlizzardConstants.Unit.Factory).Count();
            var reactorCount = factories.Where(b => b.Ready && b.Structure.UnitType == BlizzardConstants.Unit.FactoryReactor).Count();

            if (!intelManager.StructuresSelf(BlizzardConstants.Unit.FactoryTechLab).Any()
                && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.FactoryTechLab))
			{
                productionManager.QueueUnit(BlizzardConstants.Unit.FactoryTechLab);
            }
            else if (reactorCount < factoryCount)
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.FactoryReactor);
            }
            else
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.Factory, spacing: 2);
            }
        }

        private string NextSquadName() => $"{SQUAD_PREFIX}_{squadCounter++}";

        private bool ProductionCapacityAvailable()
        {
            var capacity = factories.Where(b => b.Ready).Count();
            return intelManager.ProductionQueue.Where(u => IsMechUnit(u.UnitId)).Count() < capacity;
        }

        private bool FactoriesReady() => factories.All(b => b.Ready);

        private bool IsFactory(uint unitTypeId)
            => unitTypeId == BlizzardConstants.Unit.Factory
            || unitTypeId == BlizzardConstants.Unit.FactoryTechLab
            || unitTypeId == BlizzardConstants.Unit.FactoryReactor;

        private bool IsMechUnit(uint unitTypeId)
            => unitTypeId == BlizzardConstants.Unit.Hellion
            || unitTypeId == BlizzardConstants.Unit.HellionTank;
    }
}
