using Abathur.Core;
using Abathur.Extensions;
using Abathur.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SC2Abathur.Modules
{
	// Global communication object
	public class StateSnapshot
	{
		// Pushing? or Defending/Retaliating?
		public bool Attacking { get; set; }

		public EconomyMode EconomyMode { get; set; }

		// Enemy units close to one of own colonies
		public Dictionary<IColony, List<IUnit>> BaseThreats { get; set; }

		// Colonies owned by player
		public List<IColony> OwnColonies { get; set; }

		// Colonies owned by enemy (seen so far)
		public List<IColony> EnemyColonies { get; set; }

		public uint RemainingSupply { get; set; }

		public StateSnapshot()
		{
			BaseThreats = new Dictionary<IColony, List<IUnit>>();
			OwnColonies = new List<IColony>();
			EnemyColonies = new List<IColony>();
			EconomyMode = EconomyMode.Standby;
		}

		public void UpdateState(IIntelManager intelManager)
		{
			OwnColonies = Helpers.GetOwnColonies(intelManager);
			EnemyColonies = Helpers.GetEnemyColonies(intelManager);

			BaseThreats = new Dictionary<IColony, List<IUnit>>();
			var enemyUnits = intelManager.UnitsEnemyVisible.ToList();
			foreach (var col in OwnColonies)
			{
				BaseThreats[col] = enemyUnits.Where(u => u.Point.Distance(col.Point) < 20).ToList();
			}

			RemainingSupply = intelManager.Common.FoodCap - intelManager.Common.FoodUsed;
		}
	}

	public enum EconomyMode
	{
		Standby,
		Expand
	}

	public class ProductionFacility
	{
		public IUnit Structure { get; set; }

		public bool Ready { get => Structure.BuildProgress > 0.99; }

		public ProductionFacility(IUnit structure)
		{
			Structure = structure;
		}

		public override bool Equals(object obj)
		{
			if (obj == null || GetType() != obj.GetType())
			{
				return false;
			}

			return Structure.Tag == ((ProductionFacility) obj).Structure.Tag;
		}

		public override int GetHashCode()
		{
			return Structure.GetHashCode();
		}
	}
}
