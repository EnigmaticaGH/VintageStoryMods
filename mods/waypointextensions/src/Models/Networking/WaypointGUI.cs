using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace WaypointManager.Models.Networking
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointGuiDataRequestMessage
    {
        public string Message;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointGuiMessage
    {
        public IList<Waypoint> Waypoints;
        public Vec3d WorldSpawnPos;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointGuiResponse
    {
        public IList<Waypoint> Response;
    }
}
