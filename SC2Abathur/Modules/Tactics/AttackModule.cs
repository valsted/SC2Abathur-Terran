using Abathur.Constants;
using Abathur.Core;
using Abathur.Core.Combat;
using Abathur.Model;
using Abathur.Modules;
using Abathur.Repositories;
using NydusNetwork.API.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static Abathur.Constants.BlizzardConstants;

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
        private int squadSize = 5;

        public IEnumerable<IColony> enemyPositions;

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
            intelManager.Handler.RegisterHandler(Case.UnitAddedSelf, OnUnitBuilt);
            intelManager.Handler.RegisterHandler(Case.UnitDestroyed, OnUnitDestroyed);
        }

        public void OnStep()
        {
            if (!intelManager.ProductionQueue.Any())
            {
                QueueSquad();
            }

            // Lets attack stuff!
            foreach (var squad in squads.Values)
            {
                AttackClosest(squad);
            }
        }

        private void AttackClosest(Squad squad)
        {
            var squadPos = GetSquadCenter(squad);

            // 1: Nearby Units
            var units = intelManager.UnitsEnemyVisible.ToList();
            if (units.Any())
            {
                var target = GetClosest(squadPos, units.Select(u => u.Point).ToList());
                combatManager.AttackMove(squad, target);
                return;
            }

            // 2: Nearby Structures
            var structures = intelManager.StructuresEnemyVisible.ToList();
            if (structures.Any())
            {
                var target = GetClosest(squadPos, structures.Select(u => u.Point).ToList());
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
        }

        public void OnAdded() => OnStart();
        public void OnRemoved() => OnRestart();

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
            if (unit.UnitType == BlizzardConstants.Unit.Marine)
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
            for (int i = 0; i < squadSize; i++)
            {
                productionManager.QueueUnit(BlizzardConstants.Unit.Marine);
            }
        }

        private void CloseSquad()
        {
            squads.Add(productionSquad.Name, productionSquad);
            squadIdx++;
            productionSquad = squadRepo.Create(squadIdx.ToString());
        }


        private Point2D GetClosest(Point2D source, List<Point2D> points)
        {
            var closest = points[0];
            var minDist = Distance(source, closest);
            foreach (var point in points)
            {
                var dist = Distance(source, point);
                if (dist < minDist)
                {
                    closest = point;
                    minDist = dist;
                }
            }
            return closest;
        }

        private double Distance(Point2D p1, Point2D p2)
        {
            return Math.Sqrt((p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y));
        }

        private Point2D GetSquadCenter(Squad squad)
        {
            // TODO : make something better
            return squad.Units.First().Point;
        }
    }
}
