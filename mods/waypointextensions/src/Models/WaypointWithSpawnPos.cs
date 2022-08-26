using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace WaypointManager.Models
{
    public class WaypointsWithSpawnPos
    {
        public IList<Waypoint> waypoints;
        public Vec3d worldSpawnPos;
    }
}
