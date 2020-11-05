using Abathur.Constants;
using Abathur.Core;
using Abathur.Model;
using NydusNetwork.API.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SC2Abathur.Modules
{
    public class Helpers
    {
        static Dictionary<uint, string> UnitIdToName;

        public static bool IsTerranResourceCenter(IUnit unit)
            => unit.UnitType == BlizzardConstants.Unit.CommandCenter
               || unit.UnitType == BlizzardConstants.Unit.OrbitalCommand
               || unit.UnitType == BlizzardConstants.Unit.PlanetaryFortress;

        public static List<IColony> GetOwnColonies(IIntelManager intelManager)
        {
            var ownColonies = new List<IColony>();
            var commandCenters = intelManager.StructuresSelf().Where(IsTerranResourceCenter).ToList();
            foreach (var colony in intelManager.Colonies)
            {
                if (commandCenters.Any(cc => colony.Structures.Contains(cc)))
                {
                    ownColonies.Add(colony);
                }
            }
            return ownColonies;
        }

        public static Point2D GetAvgLocation(IEnumerable<IUnit> units)
        {
            var count = units.Count();
            if (count == 0)
                return new Point2D { X = float.NaN, Y = float.NaN };
            var p = units.Aggregate((0.0f, 0.0f), (sum, unit) => (sum.Item1 + unit.Point.X, sum.Item2 + unit.Point.Y));
            return new Point2D { X = p.Item1 / count, Y = p.Item2 / count };
        }

        public static void PrintProductionQueue(IIntelManager intelManager)
        {
            var unitIds = intelManager.ProductionQueue.Select(u => u.UnitId).ToList();

            BuildUnitIdToNameDict();

            Console.WriteLine("Queue: [");
            foreach (var id in unitIds)
            {
                if (UnitIdToName.ContainsKey(id)) Console.WriteLine($"   {UnitIdToName[id]}");
                else Console.WriteLine("   <non-unit>");
            }
            Console.WriteLine("]");
        }

        private static void BuildUnitIdToNameDict()
        {
            if (UnitIdToName == null)
            {
                UnitIdToName = new Dictionary<uint, string>();
                var dummyInstance = new BlizzardConstants.Unit();
                foreach (var field in typeof(BlizzardConstants.Unit).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var obj = field.GetValue(dummyInstance);
                    uint value = 0;
                    if (obj.GetType() == typeof(int))
                    {
                        value = (uint)(int)obj;
                    }
                    else if (obj.GetType() == typeof(uint))
                    {
                        value = (uint)obj;
                    } 
                    else
                    {
                        continue;
                    }
                    if (!UnitIdToName.ContainsKey(value))
                    {
                        UnitIdToName.Add(value, field.Name);
                    }
                }
            }
        }
    }
}
