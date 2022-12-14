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
    public class WaypointImportMessage
    {
        public IList<Waypoint> Message;
        public Vec3d WorldSpawnPos;
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public class WaypointImportResponse
    {
        public string Response;
    }
}
