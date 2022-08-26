using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace WaypointManager.ExtensionMethods
{
    public static class ExtensionMethods
    {   
        public static Waypoint NormalizePosition(this Waypoint waypoint, Vec3d middle)
        {
            waypoint.Position = waypoint.Position - middle;
            return waypoint;
        }

        public static Vec3d WithoutYComponent(this Vec3d position)
        {
            position.Y = 0;
            return position;
        }
    }
}
