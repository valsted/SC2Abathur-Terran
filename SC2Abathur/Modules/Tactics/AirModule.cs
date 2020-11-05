using Abathur.Constants;
using Abathur.Core;
using Abathur.Core.Combat;
using Abathur.Extensions;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using NydusNetwork.API.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SC2Abathur.Modules.Tactics
{
    public class AirModule : IReplaceableModule
    {
        public IEnumerable<IColony> enemyPositions;

        static readonly string SQUAD_NAME = "Fleet";
        static readonly int LIBERATOR_MINERALS = 150;
        static readonly int LIBERATOR_VESPENE = 150;

        readonly IIntelManager intelManager;
        readonly IProductionManager productionManager;
        readonly ICombatManager combatManager;
        readonly ISquadRepository squadRepo;

        Random rng = new Random();

        bool IsUpgrading;
        bool fleetDeployed = false;
        FleetState fleetstate;
        Squad fleet;
        List<IUnit> productionFacilities;
        HashSet<IUnit> loneUnits;
        

        public AirModule(IIntelManager intelManager, IProductionManager productionManager, 
            ICombatManager combatManager, ISquadRepository squadRepo)
        {
            this.intelManager = intelManager;
            this.productionManager = productionManager;
            this.combatManager = combatManager;
            this.squadRepo = squadRepo;
        }

        public void Initialize() { }

        public void OnGameEnded() { }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();

        public void OnStart()
        {
            productionFacilities = new List<IUnit>();
            loneUnits = new HashSet<IUnit>();
            fleet = squadRepo.Create(SQUAD_NAME);
            fleetstate = FleetState.Retaliate;

            // Register handlers
            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, OnUnitBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitDestroyed, OnUnitDestroyed);

            if (!intelManager.StructuresSelf(BlizzardConstants.Unit.Factory).Any())
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.Factory, spacing: 2);
            }
        }

        public void OnStep()
        {
            if (intelManager.GameLoop % 3 != 1)
                return;

            CheckBuiltStatus();

            if (ShouldUpgrade())
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.Starport);
                IsUpgrading = true;
                return;
            }

            switch (fleetstate)
            {
                case FleetState.Defend:
                    Defend();
                    break;
                case FleetState.Attack:
                    Attack();
                    break;
                case FleetState.Retaliate:
                    Retaliate();
                    break;
            }

            BuildFleet();
        }

        private void CheckBuiltStatus()
        {
            IsUpgrading = productionFacilities.All(u => u.BuildProgress > 99);
        }

        private void Attack()
        {
            // Ensure all in fleet
            var fleetPos = Helpers.GetAvgLocation(fleet.Units);
            foreach (var unit in loneUnits)
            {
                combatManager.Move(unit.Tag, fleetPos);
                fleet.AddUnit(unit);
            }

            var units = fleet.Units.ToList();
            if (fleetDeployed)
            {
                var nearbyUnits = intelManager.UnitsEnemyVisible.Where(u => fleetPos.Distance(u.Point) < 12).ToList();
                if (nearbyUnits.Count < 3)
                {
                    combatManager.UseTargetlessAbility(BlizzardConstants.Ability.LiberatorMorphtoAA, fleet);
                    fleetDeployed = false;
                }
            }
            else if (units.Any(u => u.Orders.Count() == 0))
            {
                // Have we arrived?
                if (enemyPositions.Any(p => p.Point.Distance(fleetPos) < 10))
                {
                    combatManager.UseTargetlessAbility(BlizzardConstants.Ability.LiberatorMorphtoAG, fleet);
                    fleetDeployed = true;
                }
                // Go to enemy colony
                else
                {
                    combatManager.AttackMove(fleet, enemyPositions.First().Point);
                }
            }
        }

        private void Defend()
        {
            throw new NotImplementedException();
        }

        private void BuildFleet()
        {
            // Do we have resources? 
            if (ProductionCapacityAvailable() 
                && intelManager.Common.Minerals > LIBERATOR_MINERALS && intelManager.Common.Vespene > LIBERATOR_VESPENE)
            {
                var rallyPoint = fleetstate == FleetState.Attack ? fleet.Units.First().Point : SampleDefensePoint();
                productionManager.QueueUnit(BlizzardConstants.Unit.Liberator, desiredPosition:rallyPoint, spacing: 1);
            }
        }

        private Point2D SampleDefensePoint()
        {
            var ownColonies = Helpers.GetOwnColonies(intelManager);
            return ownColonies[rng.Next(ownColonies.Count)].Point;
        }
            

        private void Retaliate()
        {
            if (fleet.Units.Any(u => u.Orders.Count() == 0))
            {
                combatManager.Move(fleet, intelManager.PrimaryColony.Point);
                // TODO: switch to defense soon after
            }
        }

        public void OnRestart()
        {
            // Deregister handlers
            intelManager.Handler.DeregisterHandler(OnUnitBuilt);
            intelManager.Handler.DeregisterHandler(OnUnitDestroyed);
            intelManager.Handler.DeregisterHandler(OnStructureBuilt);
        }

        public void OnStructureBuilt(IUnit structure)
        {
            if (structure.UnitType == BlizzardConstants.Unit.Starport)
            {
                productionFacilities.Add(structure);
            }
        }

        public void OnUnitBuilt(IUnit unit)
        {
            if (unit.UnitType == BlizzardConstants.Unit.Liberator)
            {
                if (fleetstate == FleetState.Attack)
                    fleet.AddUnit(unit);
                else
                    loneUnits.Add(unit);
            }
        }

        public void OnUnitDestroyed(IUnit unit)
        {
            if (unit.UnitType == BlizzardConstants.Unit.Liberator)
            {
                if (loneUnits.Contains(unit))
                    loneUnits.Remove(unit);

                if (fleetstate == FleetState.Attack)
                {
                    // Should we retreat?
                    var fleetPos = Helpers.GetAvgLocation(fleet.Units);
                    var nearbyAA = intelManager.UnitsEnemyVisible.Where(IsAAUnit)
                        .Where(u => fleetPos.Distance(u.Point) < 10)  // Max liberator attack range + buffer
                        .ToList();

                    if (fleet.Units.Count < nearbyAA.Count())
                    {
                        fleetstate = FleetState.Retaliate;
                        if (fleetDeployed)
                        {
                            combatManager.UseTargetlessAbility(BlizzardConstants.Ability.LiberatorMorphtoAA, fleet);
                        }
                        combatManager.Move(fleet, intelManager.PrimaryColony.Point, queue: true); // Back to base
                    }
                }
            }
        }

        private bool ShouldUpgrade()
        {
            if (IsUpgrading)
            {
                return false;
            }
            else if (productionFacilities.Count == 0)
            {
                return true;
            }
            else
            {
                return intelManager.Common.Minerals > 500 && intelManager.Common.Vespene > 200;
            }
        }

        private bool ProductionCapacityAvailable() =>
            intelManager.ProductionQueue.Where(u => u.UnitId == BlizzardConstants.Unit.Liberator).Count() < productionFacilities.Count;

        private bool IsAAUnit(IUnit unit) => AntiAirUnits.Contains(unit.UnitType);
        private static HashSet<uint> AntiAirUnits = new HashSet<uint>
        {
            // Zerg
            BlizzardConstants.Unit.Queen,
            BlizzardConstants.Unit.Hydralisk,
            BlizzardConstants.Unit.Mutalisk,
            BlizzardConstants.Unit.Corruptor,
            //BlizzardConstants.Unit.Infestor, // Only with upgrade
            //BlizzardConstants.Unit.Ravager,  // Only with upgrade

            // Protoss
            BlizzardConstants.Unit.Stalker,
            BlizzardConstants.Unit.Sentry,
            BlizzardConstants.Unit.Archon,
            BlizzardConstants.Unit.Phoenix,
            BlizzardConstants.Unit.VoidRay,
            BlizzardConstants.Unit.Carrier,
            BlizzardConstants.Unit.Mothership,
            // BlizzardConstants.Unit.HighTemplar, // Only with upgrade
            BlizzardConstants.Unit.Tempest,

            // Terran
            BlizzardConstants.Unit.Marine,
            BlizzardConstants.Unit.Ghost,
            BlizzardConstants.Unit.VikingFighter,
            BlizzardConstants.Unit.Thor,
            BlizzardConstants.Unit.Battlecruiser,
            // BlizzardConstants.Unit.Raven, // Only with upgrade
            BlizzardConstants.Unit.Liberator,
            BlizzardConstants.Unit.Cyclone,
        };

    }

    public enum FleetState
    {
        Defend,
        Attack,
        Retaliate
    }
}
