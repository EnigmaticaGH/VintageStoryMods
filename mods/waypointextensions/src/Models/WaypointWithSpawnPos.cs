using Newtonsoft.Json;
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
        [JsonProperty("waypoints")]
        public IList<Waypoint> Waypoints;
        [JsonProperty("worldSpawnPos")]
        public Vec3d WorldSpawnPos;
    }
}
