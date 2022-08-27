using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using WaypointManager.ExtensionMethods;
using WaypointManager.Utilities;
using WaypointManager.Models.Networking;
using WaypointManager.Models;
using Vintagestory.GameContent;
using Vintagestory.API.MathTools;
using System.Reflection;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Newtonsoft.Json.Linq;
using Microsoft.SqlServer.Server;
using System.Runtime.Remoting.Contexts;

namespace Vintagestory.ServerMods.WaypointManager
{
    public class WaypointElement
    {
        public ElementBounds ElementBounds;
        public Waypoint Waypoint;
    }
    public class WaypointGUI : GuiDialog
    {
        public override string ToggleKeyCombinationCode => "waypointmanagergui";

        public IList<Waypoint> Waypoints { get; set; }
        public Vec3d WorldMiddle { get; set; }

        private ICoreClientAPI _clientApi;
        private int ListWidth = 1100;
        private int ListHeight = 600;
        private int RowHeight = 30;
        private ElementBounds[] Bounds = new ElementBounds[5];

        private readonly string[] IconNames = new string[] { "circle", "bee", "cave", "home", "ladder", "pick", "rocks", "ruins", "spiral", "star1", "star2", "trader", "vessel" };
        private readonly string[] Icons = new string[] { "wpCircle", "wpBee", "wpCave", "wpHome", "wpLadder", "wpPick", "wpRocks", "wpRuins", "wpSpiral", "wpStar1", "wpStar2", "wpTrader", "wpVessel" };
        private Dictionary<string, string> IconMap = new Dictionary<string, string>();

    public WaypointGUI(ICoreClientAPI clientApi) : base(clientApi)
        {
            _clientApi = clientApi;

            for (int i = 0; i < Icons.Length; i++)
            {
                IconMap[IconNames[i]] = Icons[i];
            }
        }

        public void OnGuiDataReceived()
        {
            Bounds[0] = ElementBounds.FixedSize(200, RowHeight);
            Bounds[1] = ElementBounds.FixedSize(30, RowHeight).FixedRightOf(Bounds[0], 10);
            Bounds[2] = ElementBounds.FixedSize(180, RowHeight).FixedRightOf(Bounds[1], 20);
            Bounds[3] = ElementBounds.FixedSize(80, RowHeight).FixedRightOf(Bounds[2], 10);
            Bounds[4] = ElementBounds.FixedSize(20, RowHeight).FixedRightOf(Bounds[3], 10);

            var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.None).WithAlignment(EnumDialogArea.CenterMiddle).WithFixedPosition(0, 70);

            var contentHeaderBounds = ElementBounds.Fixed(0, 25, ListWidth, 5);
            var contentBounds = ElementBounds.Fixed(0, 50, ListWidth, ListHeight);
            var clipBounds = contentBounds.ForkBoundingParent();
            var insetBounds = contentBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, 0);
            var scrollbarBounds = insetBounds.CopyOffsetedSibling(3 + contentBounds.fixedWidth + 7).WithFixedWidth(20);

            var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
            var tabBounds = ElementBounds.Fixed(-200, 35, 200, 545);

            bgBounds.BothSizing = ElementSizing.FitToChildren;
            bgBounds.WithChildren(insetBounds, scrollbarBounds);

            SingleComposer = capi.Gui.CreateCompo("waypointManager", dialogBounds)
                .AddShadedDialogBG(bgBounds, true)
                .AddDialogTitleBar("Waypoint Manager", OnTitleBarCloseClicked)
                .AddVerticalTabs(GenerateTabs(out int currentTab), tabBounds, OnTabClicked, "verticalTabs")
                .BeginChildElements(bgBounds)
                .AddContainer(contentHeaderBounds, "waypointlistheader")
                    .BeginClip(clipBounds)
                        .AddInset(insetBounds, 3)
                        .AddContainer(contentBounds, "waypointlist");

            GuiElementContainer guiElementContainerHeader = SingleComposer.GetContainer("waypointlistheader");
            foreach (var element in AddHeaderElements())
            {
                if (element == null)
                {
                    throw new ArgumentException();
                }
                guiElementContainerHeader.Add(element);
            }

            GuiElementContainer guiElementContainer = SingleComposer.GetContainer("waypointlist");

            foreach (var element in AddPageElements(Waypoints))
            {
                if (element == null)
                {
                    throw new ArgumentException();
                }
                guiElementContainer.Add(element);
            }

            SingleComposer.EndClip();
            SingleComposer.AddVerticalScrollbar(OnScroll, scrollbarBounds, "scrollbar");
            SingleComposer.EndChildElements();
            SingleComposer.Compose();

            GuiElementScrollbar scrollbar = SingleComposer.GetScrollbar("scrollbar");
            scrollbar.SetHeights((float)ListHeight, (float)guiElementContainer.Bounds.fixedHeight);

            SingleComposer.GetVerticalTab("verticalTabs").SetValue(currentTab, false);
        }

        private void OnTitleBarCloseClicked()
        {
            TryClose();
        }

        private void OnScroll(float position)
        {
            GuiElementContainer container = SingleComposer.GetContainer("waypointlist");
            container.Bounds.fixedY = 3 - position;
            container.Bounds.CalcWorldBounds();
        }

        private void OnTabClicked(int tabIndex, GuiTab tab)
        {
            
        }

        private GuiTab[] GenerateTabs(out int currentTab)
        {
            GuiTab[] tabs = new GuiTab[1];

            tabs[0] = new GuiTab()
            {
                DataInt = 0,
                Name = "Waypoints"
            };

            currentTab = 0;

            return tabs;
        }

        private IEnumerable<GuiElement> AddPageElements(IEnumerable<Waypoint> waypoints)
        {
            foreach (var w in waypoints)
            {
                string key = $"{w.Title}-{w.Icon}-{ColorUtil.Int2Hex(w.Color)}-{w.Position.X}-{w.Position.Y}-{w.Position.Z}-{(w.Pinned ? "pinned" : "")}";

                var pos = w.NormalizePosition(WorldMiddle).Position;
                var coloredText = CairoFont.WhiteSmallText();
                coloredText.Color = ColorUtil.Hex2Doubles(ColorUtil.Int2Hex(w.Color));

                yield return new GuiElementStaticText(capi, $"{w.Title}", EnumTextOrientation.Left, Bounds[0].FlatCopy(), CairoFont.WhiteSmallText());
                yield return GetIconElement(w, Bounds[1]);
                yield return new GuiElementStaticText(capi, $"({pos.XInt}, {pos.YInt}, {pos.ZInt})", EnumTextOrientation.Left, Bounds[2].FlatCopy(), CairoFont.WhiteSmallText());
                yield return new GuiElementStaticText(capi, $"{ColorUtil.Int2Hex(w.Color)}", EnumTextOrientation.Left, Bounds[3].FlatCopy(), coloredText);
                yield return new GuiElementStaticText(capi, $"{(w.Pinned ? "Yes" : "")}", EnumTextOrientation.Left, Bounds[4].FlatCopy(), CairoFont.WhiteSmallText());

                Bounds = BatchBelowCopy(Bounds);
            }
        }

        private IEnumerable<GuiElementControl> AddHeaderElements()
        {
            var headerFont = CairoFont.WhiteSmallText();
            headerFont.WithFontSize(20);
            yield return new GuiElementStaticText(capi, "Title", EnumTextOrientation.Left, Bounds[0].FlatCopy(), headerFont);
            yield return new GuiElementStaticText(capi, "Icon", EnumTextOrientation.Left, Bounds[1].FlatCopy(), headerFont);
            yield return new GuiElementStaticText(capi, "Position", EnumTextOrientation.Left, Bounds[2].FlatCopy(), headerFont);
            yield return new GuiElementStaticText(capi, "Color", EnumTextOrientation.Left, Bounds[3].FlatCopy(), headerFont);
            yield return new GuiElementStaticText(capi, "Pinned", EnumTextOrientation.Left, Bounds[4].FlatCopy(), headerFont);
        }

        private ElementBounds[] BatchBelowCopy(ElementBounds[] bounds)
        {
            ElementBounds[] newBounds = new ElementBounds[bounds.Length];
            for(int i = 0; i < bounds.Length; i++)
            {
                newBounds[i] = bounds[i].BelowCopy(0.0, 1.0, 0.0, 0.0);
            }
            return newBounds;
        }

        private GuiElement GetIconElement(Waypoint w, ElementBounds bounds)
        {
            var coloredText = CairoFont.WhiteSmallText();
            coloredText.Color = ColorUtil.Hex2Doubles(ColorUtil.Int2Hex(w.Color));
            RichTextComponentBase[] components = new RichTextComponent[1] { new RichTextComponent(capi, GetIcon(w.Icon), coloredText) };
            //var iconElement = new GuiElementRichtext(capi, components, bounds);
            var iconElement = new GuiElementToggleButton(capi, GetIcon(w.Icon), "", coloredText, null, bounds);

            return iconElement;
        }

        private string GetIcon(string iconName)
        {
            if (IconMap.TryGetValue(iconName, out var icon))
            {
                return icon;
            }
            return iconName;
        }

        private string GetIconVAML(string icon)
        {
            return $"<icon name=\"{icon}\"></icon>";
        }
    }
}
