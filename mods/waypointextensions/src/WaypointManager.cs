using Newtonsoft.Json;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Channels;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using WaypointManager.ExtensionMethods;
using WaypointManager.Utilities;
using WaypointManager.Networking;
using WaypointManager.Models;

namespace Vintagestory.ServerMods.WaypointManager
{
    public class WaypointManager : ModSystem
    {
        private readonly string _channel = "waypointmanagement";

        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;

        private IServerNetworkChannel serverChannel;
        private IClientNetworkChannel clientChannel;

        public WaypointManager()
        {}
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server || side == EnumAppSide.Client;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api ?? throw new ArgumentException("Server API is null");

            serverChannel = api.Network.RegisterChannel(_channel)
                .RegisterMessageType(typeof(WaypointImportMessage))
                .RegisterMessageType(typeof(WaypointImportResponse))
                .SetMessageHandler<WaypointImportResponse>(OnClientMessage);

            api.RegisterCommand("wpe", "Functions for managing waypoints", "[import|export]", OnCmdWpe, Privilege.chat);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api ?? throw new ArgumentException("Client API is null");

            clientChannel = api.Network.RegisterChannel(_channel)
                .RegisterMessageType(typeof(WaypointImportMessage))
                .RegisterMessageType(typeof(WaypointImportResponse))
                .SetMessageHandler<WaypointImportMessage>(OnServerMessage);
        }

        private void OnServerMessage(WaypointImportMessage networkMessage)
        {
            var waypoints = networkMessage.message;
            if (waypoints == null)
            {
                return;
            }
            waypoints.ToList().ForEach(w =>
            {
                capi.SendChatMessage($"/waypoint addati {w.Icon} {w.Position.X} {w.Position.Y} {w.Position.Z} {w.Pinned.ToString().ToLower()} {ColorUtil.Int2Hex(w.Color):X} {w.Title}");
            });
            clientChannel.SendPacket(new WaypointImportResponse()
            {
                response = $"{waypoints.Count} waypoints added to map."
            });
        }

        private void OnClientMessage(IPlayer fromPlayer, WaypointImportResponse networkMessage)
        {
            sapi.SendMessageToGroup(
                GlobalConstants.GeneralChatGroup,
                $"{fromPlayer.PlayerName}: {networkMessage.response}",
                EnumChatType.Notification
            );
        }

        private async void OnCmdWpe(IServerPlayer player, int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();
            Vec3d spawnpos = sapi.World.DefaultSpawnPosition.XYZ;
            var waypoints = GetWaypoints().ToList();

            switch (cmd)
            {
                case "export":
                    await ExportWaypoints(player, groupId, waypoints, spawnpos);
                    break;

                case "import":
                    await ImportWaypoints(player, groupId, waypoints);
                    break;
            }
        }

        private async Task ExportWaypoints(IServerPlayer player, int groupId, IList<Waypoint> waypoints, Vec3d spawnpos)
        {
            player.SendMessage(groupId, $"Found {waypoints.Count} waypoints", EnumChatType.CommandSuccess);
            var json = BuildWaypointJson(waypoints, spawnpos);
            try
            {
                await FileUtilities.WriteTextAsync($"{sapi.DataBasePath}/ModData/WaypointManager", "waypoints.json", json);
                player.SendMessage(groupId, $"Exported {waypoints.Count} waypoints to {sapi.DataBasePath}/ModData/WaypointManager/waypoints.json", EnumChatType.CommandSuccess);
            }
            catch (Exception e)
            {
                player.SendMessage(groupId, $"Unable to export waypoints: {e.Message}", EnumChatType.CommandError);
            }
        }

        private async Task ImportWaypoints(IServerPlayer player, int groupId, List<Waypoint> waypoints)
        {
            try
            {
                var wpJson = await FileUtilities.ReadTextAsync($"{sapi.DataBasePath}/ModData/WaypointManager/waypoints.json");
                var importedData = JsonConvert.DeserializeObject<WaypointsWithSpawnPos>(wpJson);
                var importedWaypoints = importedData.waypoints ?? new List<Waypoint>();
                var importedCount = importedWaypoints.Count;
                var spawnpos = sapi.World.DefaultSpawnPosition.XYZ.WithoutYComponent();
                player.SendMessage(groupId, $"Importing {importedCount} waypoints", EnumChatType.CommandSuccess);

                // De-duping logic to prevent same waypoints being imported
                importedWaypoints = importedWaypoints.Select(importedWaypoint =>
                {
                    // Attribute waypoints to player who imported them
                    importedWaypoint.OwningPlayerUid = player.PlayerUID;
                    // Normalize waypoints from map center
                    importedWaypoint.NormalizePosition(importedData.worldSpawnPos.WithoutYComponent());
                    return importedWaypoint;
                }).Where(importedWaypoint =>
                {
                    return !waypoints.Select(waypoint => waypoint.NormalizePosition(spawnpos)).Any(waypoint =>
                    {
                        return importedWaypoint.Title == waypoint.Title
                        && importedWaypoint.Icon == waypoint.Icon
                        && importedWaypoint.Position == waypoint.Position;
                    });
                }).ToList() ?? new List<Waypoint>();
                var duplicates = importedCount - importedWaypoints.Count;
                player.SendMessage(groupId, $"Imported {importedWaypoints.Count} waypoints - {duplicates} were duplicates", EnumChatType.CommandSuccess);

                // Let client know to begin importing waypoints
                serverChannel.BroadcastPacket(new WaypointImportMessage()
                {
                    message = importedWaypoints,
                    worldSpawnPos = spawnpos
                });

                // Add waypoints to global list and save, returning the original coordinates, adjusted for current world size
                waypoints.AddRange(importedWaypoints.Select(w =>
                {
                    w.Position = w.Position + spawnpos;
                    return w;
                }));
                sapi.WorldManager.SaveGame.StoreData("playerMapMarkers_v2", SerializerUtil.Serialize(waypoints));
            }
            catch (Exception e)
            {
                player.SendMessage(groupId, $"Error importing waypoints: {e.Message}", EnumChatType.CommandError);
            }
        }

        private IList<Waypoint> GetWaypoints()
        {
            // Copied from the Essentials mod lol
            var waypoints = new List<Waypoint>();
            if (sapi != null)
            {
                try
                {
                    byte[] data = sapi.WorldManager.SaveGame.GetData("playerMapMarkers_v2");
                    if (data != null)
                    {
                        waypoints = SerializerUtil.Deserialize<List<Waypoint>>(data);
                        sapi.World.Logger.Notification("Successfully loaded " +waypoints.Count + " waypoints");
                    }
                    else
                    {
                        data = sapi.WorldManager.SaveGame.GetData("playerMapMarkers");
                        if (data != null) waypoints = JsonUtil.FromBytes<List<Waypoint>>(data);
                    }

                    for (int i = 0; i < waypoints.Count; i++)
                    {
                        var wp = waypoints[i];
                        if (wp.Title == null) wp.Title = wp.Text; // Not sure how this happenes. For some reason the title moved into text
                        if (wp == null)
                        {
                            sapi.World.Logger.Error("Waypoint with no position loaded, will remove");
                            waypoints.RemoveAt(i);
                            i--;
                        }
                    }
                }
                catch (Exception e)
                {
                    sapi.World.Logger.Error("Failed deserializing player map markers. Won't load them, sorry! Exception thrown: ", e);
                }
                return waypoints;
            }
            return waypoints;
        }

        private string BuildWaypointJson(IList<Waypoint> waypoints, Vec3d spawnpos)
        {
            return JsonConvert.SerializeObject(new WaypointsWithSpawnPos() { waypoints = waypoints, worldSpawnPos = spawnpos });
        }
    }
}
