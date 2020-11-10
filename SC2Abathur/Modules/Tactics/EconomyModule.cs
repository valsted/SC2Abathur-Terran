using Abathur.Constants;
using Abathur.Core;
using Abathur.Extensions;
using Abathur.Model;
using Abathur.Modules;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SC2Abathur.Modules.Tactics
{
	public class EconomyModule : IReplaceableModule
	{
		static int COLONY_MAX_VESPENE_WORKERS = 6;

		readonly IIntelManager intelManager;
		readonly IProductionManager productionManager;
		readonly ICombatManager combatManager;
		readonly IRawManager rawManager;

		StateSnapshot state;

		Random rng = new Random();

		public EconomyModule(StateSnapshot snapshot,
			IIntelManager intelManager, IProductionManager productionManager,
			ICombatManager combatManager, IRawManager rawManager)
		{
			this.state = snapshot;
			this.intelManager = intelManager;
			this.productionManager = productionManager;
			this.combatManager = combatManager;
			this.rawManager = rawManager;
		}

		public void Initialize() { }

		public void OnStart()
		{
			// Start focus on minerals
			state.OwnColonies.ForEach(c => c.DesiredVespeneWorkers = 0);

			// Register handlers
			intelManager.Handler.RegisterHandler(Case.StructureAddedSelf, OnStructureBuilt);
			intelManager.Handler.RegisterHandler(Case.StructureDestroyed, OnStructureLost);
			intelManager.Handler.RegisterHandler(Case.MineralDepleted, OnMineralDepleted);
			//intelManager.Handler.RegisterHandler(Case.VespeneDepleted, OnVespeneDepleted);
		}

		public void OnStep()
		{
			if (intelManager.GameLoop % 100 == 0)
			{
				state.OwnColonies.ForEach(c =>
					Console.WriteLine($"Col: {c.Id}, workers: {c.Workers.Count} / {OptimalMineralWorkers(c)}"));
				Console.WriteLine($"Total workers {intelManager.WorkersSelf().Count()}");
			}

			if (intelManager.GameLoop % 5 == 0) // We don't need to be that high-frequency.. 
			{
				switch (state.EconomyMode)
				{
					case EconomyMode.Standby:
						// TODO: call down MULEs
						break;

					case EconomyMode.Expand:
						state.OwnColonies.ForEach(FillColonyWorkers);

						if (intelManager.GameLoop > 600
							&& state.OwnColonies.All(c => GetEconomyState(c) == EconomyState.Saturated || GetEconomyState(c) == EconomyState.Surplus)
							&& !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.CommandCenter))
						{
							var expansionSpot = FindExpansionSpace();
							productionManager.QueueUnit(BlizzardConstants.Unit.CommandCenter, desiredPosition: expansionSpot.Point);
						}
						break;
				}

				BalanceWorkers();
			}
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

		public void OnStructureBuilt(IUnit structure)
		{
			if (structure.UnitType == BlizzardConstants.Unit.Refinery)
			{
				// Find refinery's colony and increase desired workers
				var colony = structure.GetClosest(state.OwnColonies);
				colony.DesiredVespeneWorkers += 1;
				return;
			}
			else if (GameConstants.IsHeadquarter(structure.UnitType))
			{
				var newColony = structure.GetClosest(state.OwnColonies);
				newColony.DesiredVespeneWorkers = 0;
			}
		}

		public void OnStructureLost(IUnit structure)
		{
			if (GameConstants.IsHeadquarter(structure.UnitType))
			{
				var lostColony = structure.GetClosest(state.OwnColonies);
				var newColony = state.OwnColonies.Where(c => c.Id != lostColony.Id).FirstOrDefault();
				lostColony.Workers.ToList().ForEach(w => TransferWorker(w, lostColony, newColony));
			}
		}

		public void OnMineralDepleted(IUnit mineralField)
		{
			var colony = mineralField.GetClosest(state.OwnColonies);

			// Divert to colony with fewest workers
			var otherColonies = state.OwnColonies.Where(c => c.Id != colony.Id);
			var minWorkers = otherColonies.Min(c => c.Workers.Count());
			var newColony = otherColonies.Where(c => c.Workers.Count() == minWorkers).FirstOrDefault();
			colony.Workers.ToList().ForEach(w => TransferWorker(w, colony, newColony));
		}

		public void OnVespeneDepleted(IUnit vespeneGeyser)
		{
			// Maybe raise an issue on this one 
			// throw new NotImplementedException("TODO... handle expired vespene");
		}

		
		private void BalanceWorkers()
		{
			var surplusColonies = state.OwnColonies.Where(c => GetEconomyState(c) == EconomyState.Surplus);
			var missingColonies = new Stack<IColony>();
			state.OwnColonies.Where(c => GetEconomyState(c) == EconomyState.Missing).ToList()
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

		private void TransferWorker(IUnit worker, IColony fromColony, IColony toColony)
		{
			fromColony.Workers.Remove(worker);
			toColony.Workers.Add(worker);
			worker.Orders.Clear();
			combatManager.Move(worker.Tag, toColony.Point);
		}

		private void FillColonyWorkers(IColony colony)
		{
			var currentWorkers = colony.Workers.Count();
			var workersInQueue = intelManager.ProductionQueue.Where(u => u.UnitId == BlizzardConstants.Unit.SCV).Count();

			// Check minerals
			var optimalMineralWorkers = OptimalMineralWorkers(colony);
			var curMineralWorkers = currentWorkers - colony.DesiredVespeneWorkers;
			if (curMineralWorkers < optimalMineralWorkers && workersInQueue < state.OwnColonies.Count)
			{
				productionManager.QueueUnit(BlizzardConstants.Unit.SCV, lowPriority: true, desiredPosition: colony.Point);
			}
			else
			{
				// Check vespene
				var vespeneCapacity = VespeneWorkerCapacity(colony);
				if (intelManager.Common.Vespene > (intelManager.Common.Minerals * 0.75) && colony.DesiredVespeneWorkers > 0)
				{
					colony.DesiredVespeneWorkers -= 1;   // Surplus Vespene..
				}
				else if (colony.DesiredVespeneWorkers < vespeneCapacity)
				{
					colony.DesiredVespeneWorkers += 1;   // Allow increase production
				}
				else if (vespeneCapacity < COLONY_MAX_VESPENE_WORKERS
					&& !intelManager.ProductionQueue.Any(u => u.UnitId == BlizzardConstants.Unit.Refinery))
				{
					productionManager.QueueUnit(BlizzardConstants.Unit.Refinery,
						desiredPosition: GetRandomVespeneGeyser(colony).Point,
						lowPriority: true);
				}
			}
		}

		private EconomyState GetEconomyState(IColony colony)
		{
			// TODO: something smarter. Currently consideres only minerals
			var target = OptimalMineralWorkers(colony) + colony.DesiredVespeneWorkers;
			var current = colony.Workers.Count();
			if (current < target)
				return EconomyState.Missing;
			else if (current == target)
				return EconomyState.Saturated;
			else
				return EconomyState.Surplus;
		}

		private int VespeneWorkerCapacity(IColony colony)
			=> colony.Structures.Where(s => s.UnitType == BlizzardConstants.Unit.Refinery).Count() * 3;

		private int OptimalMineralWorkers(IColony colony)
			=> colony.Minerals.Count() * 2;

		private IColony FindExpansionSpace()
		{
			var candidates = intelManager.Colonies.Where(c => !c.IsStartingLocation).Where(c => c.Structures.Count() == 0);
			var closest = intelManager.PrimaryColony.GetClosest(candidates);
			return closest;
		}

		private IUnit GetRandomMineralPatch(IColony colony)
		{
			var patches = colony.Minerals.ToList();
			return patches[rng.Next(patches.Count)];
		}

		private IUnit GetRandomVespeneGeyser(IColony colony)
		{
			var geysers = colony.Vespene.ToList();
			return geysers[rng.Next(geysers.Count)];
		}
	}

	public enum EconomyState
	{
		Missing,
		Saturated, // We want to be here for all colonies optimally
		Surplus
	}
}
