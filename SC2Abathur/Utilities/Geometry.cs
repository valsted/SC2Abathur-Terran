using Abathur.Model;
using NydusNetwork.API.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SC2Abathur.Utilities
{
    public class Geometry
    {
        public static Point2D GetClosest(Point2D source, IEnumerable<Point2D> points)
        {
            // TODO: super primitive, could probably be a lot faster
            var closest = points.First();
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

        public static IPosition GetClosest(IPosition source, IEnumerable<IPosition> points)
        {
            // TODO: super primitive, could probably be a lot faster
            var closest = points.First();
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

        // Euclidean distance
        public static double Distance(IPosition pos1, IPosition pos2) => Distance(pos1.Point, pos2.Point);

        // Euclidean distance
        public static double Distance(Point2D p1, Point2D p2)
            => Math.Sqrt((p1.X - p2.X) * (p1.X - p2.X) + (p1.Y - p2.Y) * (p1.Y - p2.Y));
    }
}
