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
using WaypointManager.Models.Networking;
using WaypointManager.Models;

namespace Vintagestory.ServerMods.WaypointManager
{
    public class WaypointManager : ModSystem
    {
        private readonly string _waypointManagerChannel = "waypointmanager";

        private ICoreServerAPI ServerApi;
        private ICoreClientAPI ClientApi;

        private IServerNetworkChannel ServerChannel;
        private IClientNetworkChannel ClientChannel;

        private WaypointGUI WaypointGUIDialog;

        public WaypointManager()
        {}
        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server || side == EnumAppSide.Client;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerApi = api ?? throw new ArgumentException("Server API is null");

            ServerChannel = api.Network.RegisterChannel(_waypointManagerChannel)
                .RegisterMessageType(typeof(WaypointImportMessage))
                .RegisterMessageType(typeof(WaypointImportResponse))
                .RegisterMessageType(typeof(WaypointGuiMessage))
                .RegisterMessageType(typeof(WaypointGuiDataRequestMessage))
                .RegisterMessageType(typeof(WaypointGuiResponse))
                .SetMessageHandler<WaypointImportResponse>(OnImportComplete)
                .SetMessageHandler<WaypointGuiDataRequestMessage>(OnGuiDataRequest);

            api.RegisterCommand("wpe", "Functions for managing waypoints", "[import|export]", OnCmdWpe, Privilege.chat);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api ?? throw new ArgumentException("Client API is null");

            ClientApi.Input.RegisterHotKey("waypointmanagergui", "Open Waypoint Manager", GlKeys.U, HotkeyType.HelpAndOverlays);
            ClientApi.Input.SetHotKeyHandler("waypointmanagergui", ToggleGui);

            ClientChannel = api.Network.RegisterChannel(_waypointManagerChannel)
                .RegisterMessageType(typeof(WaypointImportMessage))
                .RegisterMessageType(typeof(WaypointImportResponse))
                .RegisterMessageType(typeof(WaypointGuiMessage))
                .RegisterMessageType(typeof(WaypointGuiDataRequestMessage))
                .RegisterMessageType(typeof(WaypointGuiResponse))
                .SetMessageHandler<WaypointImportMessage>(OnImportRequest)
                .SetMessageHandler<WaypointGuiMessage>(OnGuiDataRequestFulfilled);

            WaypointGUIDialog = new WaypointGUI(ClientApi);

            WaypointGUIDialog.OnOpened += OnGuiOpened;
        }

        private void OnImportRequest(WaypointImportMessage networkMessage)
        {
            var waypoints = networkMessage.Message;
            if (waypoints == null)
            {
                return;
            }
            waypoints.ToList().ForEach(w =>
            {
                ClientApi.SendChatMessage($"/waypoint addati {w.Icon} {w.Position.X} {w.Position.Y} {w.Position.Z} {w.Pinned.ToString().ToLower()} {ColorUtil.Int2Hex(w.Color):X} {w.Title}");
            });
            ClientChannel.SendPacket(new WaypointImportResponse()
            {
                Response = $"{waypoints.Count} waypoints added to map."
            });
        }

        private void OnGuiOpened()
        {
            ClientApi.SendChatMessage("GUI Opened!");
            ClientChannel.SendPacket(new WaypointGuiDataRequestMessage() { Message = "Waypoints" });
        }

        private void OnGuiDataRequest(IPlayer fromPlayer, WaypointGuiDataRequestMessage networkMessage)
        {
            ServerChannel.SendPacket(new WaypointGuiMessage() { Waypoints = GetWaypoints(), WorldSpawnPos = ServerApi.World.DefaultSpawnPosition.XYZ }, (IServerPlayer)fromPlayer);
        }

        private void OnGuiDataRequestFulfilled(WaypointGuiMessage networkMessage)
        {
            if (WaypointGUIDialog != null)
            {
                WaypointGUIDialog.Waypoints = networkMessage.Waypoints;
                WaypointGUIDialog.WorldMiddle = networkMessage.WorldSpawnPos;
                WaypointGUIDialog.OnGuiDataReceived();
            }
        }

        private void OnImportComplete(IPlayer fromPlayer, WaypointImportResponse networkMessage)
        {
            ServerApi.SendMessageToGroup(
                GlobalConstants.GeneralChatGroup,
                $"{fromPlayer.PlayerName}: {networkMessage.Response}",
                EnumChatType.Notification
            );
        }

        private async void OnCmdWpe(IServerPlayer player, int groupId, CmdArgs args)
        {
            string cmd = args.PopWord();
            Vec3d spawnpos = ServerApi.World.DefaultSpawnPosition.XYZ;
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
                await FileUtilities.WriteTextAsync($"{ServerApi.DataBasePath}/ModData/WaypointManager", "waypoints.json", json);
                player.SendMessage(groupId, $"Exported {waypoints.Count} waypoints to {ServerApi.DataBasePath}/ModData/WaypointManager/waypoints.json", EnumChatType.CommandSuccess);
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
                var wpJson = await FileUtilities.ReadTextAsync($"{ServerApi.DataBasePath}/ModData/WaypointManager/waypoints.json");
                var importedData = JsonConvert.DeserializeObject<WaypointsWithSpawnPos>(wpJson);
                var importedWaypoints = importedData.Waypoints ?? new List<Waypoint>();
                var importedCount = importedWaypoints.Count;
                var spawnpos = ServerApi.World.DefaultSpawnPosition.XYZ.WithoutYComponent();
                player.SendMessage(groupId, $"Importing {importedCount} waypoints", EnumChatType.CommandSuccess);

                // De-duping logic to prevent same waypoints being imported
                importedWaypoints = importedWaypoints.Select(importedWaypoint =>
                {
                    // Attribute waypoints to player who imported them
                    importedWaypoint.OwningPlayerUid = player.PlayerUID;
                    // Normalize waypoints from map center
                    importedWaypoint.NormalizePosition(importedData.WorldSpawnPos.WithoutYComponent());
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
                ServerChannel.BroadcastPacket(new WaypointImportMessage()
                {
                    Message = importedWaypoints,
                    WorldSpawnPos = spawnpos
                });

                // Add waypoints to global list and save, returning the original coordinates, adjusted for current world size
                waypoints.AddRange(importedWaypoints.Select(w =>
                {
                    w.Position = w.Position + spawnpos;
                    return w;
                }));
                ServerApi.WorldManager.SaveGame.StoreData("playerMapMarkers_v2", SerializerUtil.Serialize(waypoints));
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
            if (ServerApi != null)
            {
                try
                {
                    byte[] data = ServerApi.WorldManager.SaveGame.GetData("playerMapMarkers_v2");
                    if (data != null)
                    {
                        waypoints = SerializerUtil.Deserialize<List<Waypoint>>(data);
                        ServerApi.World.Logger.Notification("Successfully loaded " +waypoints.Count + " waypoints");
                    }
                    else
                    {
                        data = ServerApi.WorldManager.SaveGame.GetData("playerMapMarkers");
                        if (data != null) waypoints = JsonUtil.FromBytes<List<Waypoint>>(data);
                    }

                    for (int i = 0; i < waypoints.Count; i++)
                    {
                        var wp = waypoints[i];
                        if (wp.Title == null) wp.Title = wp.Text; // Not sure how this happenes. For some reason the title moved into text
                        if (wp == null)
                        {
                            ServerApi.World.Logger.Error("Waypoint with no position loaded, will remove");
                            waypoints.RemoveAt(i);
                            i--;
                        }
                    }
                }
                catch (Exception e)
                {
                    ServerApi.World.Logger.Error("Failed deserializing player map markers. Won't load them, sorry! Exception thrown: ", e);
                }
                return waypoints;
            }
            return waypoints;
        }

        private string BuildWaypointJson(IList<Waypoint> waypoints, Vec3d spawnpos)
        {
            return JsonConvert.SerializeObject(new WaypointsWithSpawnPos() { Waypoints = waypoints, WorldSpawnPos = spawnpos });
        }

        private bool ToggleGui(KeyCombination comb)
        {
            if (WaypointGUIDialog.IsOpened()) WaypointGUIDialog.TryClose();
            else WaypointGUIDialog.TryOpen();

            return true;
        }
    }
}
