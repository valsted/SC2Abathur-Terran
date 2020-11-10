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
        static readonly int LIBERATOR_MINERALS = 150;
        static readonly int LIBERATOR_VESPENE = 150;
        static readonly int STARPORT_MINERALS = 150;
        static readonly int STARPORT_VESPENE = 100;

        readonly string SQUAD_NAME = "Fleet";

        readonly IIntelManager intelManager;
        readonly IProductionManager productionManager;
        readonly ICombatManager combatManager;
        readonly ISquadRepository squadRepo;

        StateSnapshot snapshot;

        Random rng = new Random();

        bool fleetDeployed = false;
        Squad fleet;
        List<ProductionFacility> starports;
        HashSet<IUnit> loneUnits;
        

        public AirModule(StateSnapshot snapshot, 
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

        public void OnGameEnded() { }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();

        public void OnStart()
        {
            starports = new List<ProductionFacility>();
            loneUnits = new HashSet<IUnit>();
            fleet = squadRepo.Create(SQUAD_NAME);

            // Register handlers
            intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);
            intelManager.Handler.RegisterHandler(Case.StructureDestroyed, OnStructureDestroyed);
            intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, OnUnitBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitDestroyed, OnUnitDestroyed);

            if (!intelManager.StructuresSelf(BlizzardConstants.Unit.Starport).Any())
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.Starport, lowPriority: true,
                    desiredPosition: snapshot.LeastExpandedColony.Point, spacing: 2);
            }
        }

        public void OnStep()
        {
            if (intelManager.GameLoop % 3 != 1)
                return;

            if (ShouldUpgrade())
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.Starport, lowPriority: true,
                    desiredPosition: snapshot.LeastExpandedColony.Point, spacing: 2);
            }

            if (snapshot.Attacking)
            {
                Attack();
            }
            else
            {
                Defend();
            }

            BuildFleet();
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
                if (snapshot.EnemyColonies.Any(p => p.Point.Distance(fleetPos) < 10))
                {
                    combatManager.UseTargetlessAbility(BlizzardConstants.Ability.LiberatorMorphtoAG, fleet);
                    fleetDeployed = true;
                }
                // Go to enemy colony
                else
                {
                    combatManager.AttackMove(fleet, snapshot.EnemyColonies.First().Point);
                }
            }
        }

        private void Defend()
        {
            // TODO: implement
            Attack();
        }

        private void BuildFleet()
        {
            // Do we have resources? 
            if (ProductionCapacityAvailable() 
                && intelManager.Common.Minerals > LIBERATOR_MINERALS && intelManager.Common.Vespene > LIBERATOR_VESPENE)
            {
                var rallyPoint = snapshot.Attacking ? fleet.Units.First().Point : SampleDefensePoint();
                productionManager.QueueUnit(BlizzardConstants.Unit.Liberator, desiredPosition: rallyPoint);
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
            intelManager.Handler.DeregisterHandler(OnStructureDestroyed);
        }

        public void OnStructureBuilt(IUnit structure)
        {
            switch (structure.UnitType)
            {
                case BlizzardConstants.Unit.Starport:
                case BlizzardConstants.Unit.StarportReactor:
                    starports.Add(new ProductionFacility(structure));
                    break;
                default:
                    break; // None of our business
            }
        }

        public void OnStructureDestroyed(IUnit structure)
        {
            switch (structure.UnitType)
            {
                case BlizzardConstants.Unit.Barracks:
                case BlizzardConstants.Unit.BarracksReactor:
                    var lost = starports.Where(b => b.Structure.Tag == structure.Tag).FirstOrDefault();
                    starports.Remove(lost);
                    break;
                default:
                    break; // None of our business
            }
        }

        public void OnUnitBuilt(IUnit unit)
        {
            if (unit.UnitType == BlizzardConstants.Unit.Liberator)
            {
                fleet.AddUnit(unit);
            }
        }

        public void OnUnitDestroyed(IUnit unit)
        {
            if (unit.UnitType == BlizzardConstants.Unit.Liberator)
            {
                // We don't care yet

                //if (fleetstate == FleetState.Attack)
                //{
                //    // Should we retreat?
                //    var fleetPos = Helpers.GetAvgLocation(fleet.Units);
                //    var nearbyAA = intelManager.UnitsEnemyVisible.Where(IsAAUnit)
                //        .Where(u => fleetPos.Distance(u.Point) < 10)  // Max liberator attack range + buffer
                //        .ToList();

                //    if (fleet.Units.Count < nearbyAA.Count())
                //    {
                //        fleetstate = FleetState.Retaliate;
                //        if (fleetDeployed)
                //        {
                //            combatManager.UseTargetlessAbility(BlizzardConstants.Ability.LiberatorMorphtoAA, fleet);
                //        }
                //        combatManager.Move(fleet, intelManager.PrimaryColony.Point, queue: true); // Back to base
                //    }
                //}
            }
        }

        private bool ShouldUpgrade() => 
            StarportsReady() && CanAfford() && fleet.Units.Count() > starports.Count * 2
            && !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.Starport);

		private bool StarportsReady() => starports.All(b => b.Ready);

		private bool CanAfford() => 
            intelManager.Common.Minerals > STARPORT_MINERALS * 3 && intelManager.Common.Vespene > STARPORT_VESPENE * 2;

        private bool ProductionCapacityAvailable() =>
            intelManager.ProductionQueue.Where(u => u.UnitId == BlizzardConstants.Unit.Liberator).Count() < starports.Count;

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
}
