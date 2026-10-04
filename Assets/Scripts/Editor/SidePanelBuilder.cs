using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace SmartRoom.EditorTools
{
    /// <summary>
    /// Builds the side panel under TwinCanvas in the open scene and wires it to SidePanel.
    /// Run it again after changing the layout here; it replaces the old panel.
    /// </summary>
    public static class SidePanelBuilder
    {
        const string SpriteFolder = "Assets/UI";
        const string FillSpritePath = SpriteFolder + "/RoundedFill.png";
        const string RingSpritePath = SpriteFolder + "/RoundedRing.png";
        const int SpriteRadius = 32;   // radius baked into both sprites, in pixels
        const float RingStroke = 3f;

        static Sprite fill;
        static Sprite ring;

        static readonly Color TextColor = Hex("E8ECF1");
        static readonly Color Secondary = new Color(0.91f, 0.925f, 0.945f, 0.58f);
        static readonly Color Caption = new Color(0.91f, 0.925f, 0.945f, 0.6f);
        static readonly Color Teal = Hex("2EC4B6");
        static readonly Color Green = Hex("7EE2A8");
        static readonly Color Yellow = Hex("FFD27A");
        static readonly Color Red = Hex("FF5D5D");
        static readonly Color RedText = Hex("FF8A8A");
        static readonly Color Orange = Hex("FFB36B");

        [MenuItem("Smart Room/Build Side Panel")]
        public static void Build()
        {
            EnsureSprites();

            var canvasGo = GameObject.Find("TwinCanvas");
            if (canvasGo == null)
            {
                Debug.LogError("SidePanelBuilder: no TwinCanvas in the scene.");
                return;
            }

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // The controls need raycasts and an event system.
            if (canvasGo.GetComponent<GraphicRaycaster>() == null)
                canvasGo.AddComponent<GraphicRaycaster>();
            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            var old = canvasGo.transform.Find("SidePanel");
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var debug = canvasGo.transform.Find("DebugPanel");
            if (debug != null)
            {
                debug.gameObject.SetActive(false);
                // Top-left, short enough to stay clear of the event log in the bottom-left corner.
                var drt = (RectTransform)debug;
                drt.anchorMin = drt.anchorMax = new Vector2(0, 1);
                drt.pivot = new Vector2(0, 1);
                drt.anchoredPosition = new Vector2(16, -16);
                drt.sizeDelta = new Vector2(360, 900 - 16 - 22 - EventLogExpanded - 16);
            }

            var oldLog = canvasGo.transform.Find("EventLog");
            if (oldLog != null)
                Object.DestroyImmediate(oldLog.gameObject);
            BuildEventLog(canvasGo.transform, GameObject.Find("SmartRoom"));

            var room = GameObject.Find("SmartRoom");
            var panel = BuildPanel(canvasGo.transform, room, debug != null ? debug.gameObject : null);

            // History window last, so it covers everything else when open.
            var oldHistory = canvasGo.transform.Find("HistoryWindow");
            if (oldHistory != null)
                Object.DestroyImmediate(oldHistory.gameObject);
            var historyButton = panel.transform.Find("Content/Header/History").GetComponent<Button>();
            BuildHistoryWindow(canvasGo.transform, room, historyButton);

            EditorUtility.SetDirty(canvasGo);
            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Debug.Log("Side panel built.");
        }

        static SidePanel BuildPanel(Transform canvas, GameObject room, GameObject debugPanel)
        {
            // Panel: 400 wide, 22 from the top, right and bottom edges.
            var panelRt = NewRect("SidePanel", canvas);
            panelRt.anchorMin = new Vector2(1, 0);
            panelRt.anchorMax = new Vector2(1, 1);
            panelRt.pivot = new Vector2(1, 0.5f);
            panelRt.sizeDelta = new Vector2(400, -44);
            panelRt.anchoredPosition = new Vector2(-22, 0);
            Rounded(panelRt.gameObject, fill, new Color(14 / 255f, 17 / 255f, 21 / 255f, 0.88f), 14);
            var border = Stretch(NewRect("Border", panelRt), 0);
            Rounded(border.gameObject, ring, new Color(1, 1, 1, 0.08f), 14);

            var content = Stretch(NewRect("Content", panelRt), 22);
            var side = panelRt.gameObject.AddComponent<SidePanel>();

            // Header
            var header = Top(NewRect("Header", content), 0, 46);
            Label(Box(header, 0, 0, 220, 28), "Smart Room", 22, TextColor, bold: true);
            Label(Box(header, 0, 30, 220, 18), room != null && room.TryGetComponent<MqttConnection>(out var c) && c.Settings != null ? c.Settings.roomId : "room1", 14, Secondary);

            var pill = BoxRight(header, 0, 0, 138, 26);
            var pillImage = Rounded(pill.gameObject, fill, Hex("3A3F47"), 13);
            var pillDot = Box(pill, 12, 9, 8, 8);
            var pillDotImage = Rounded(pillDot.gameObject, fill, Hex("C0C6CE"), 4);
            var pillText = Label(Box(pill, 26, 0, 108, 26), "ESP32 offline", 13, Hex("C0C6CE"), bold: true);
            pillText.verticalAlignment = VerticalAlignmentOptions.Middle;
            // Next to the room id, clear of the title and the status pill.
            var historyRt = Box(header, 64, 27, 66, 20);
            historyRt.name = "History";
            var historyBg = Rounded(historyRt.gameObject, fill, Teal, 10);
            historyBg.raycastTarget = true;
            var historyText = Label(Stretch(NewRect("Text", historyRt), 0), "History", 12, Hex("0E1115"), bold: true);
            historyText.alignment = TextAlignmentOptions.Center;
            var historyButton = historyRt.gameObject.AddComponent<Button>();
            historyButton.targetGraphic = historyBg;
            var hc = historyButton.colors;
            hc.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            hc.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            historyButton.colors = hc;
            var broker = Label(BoxRight(header, 0, 32, 200, 16), "Broker: Disconnected", 12, Secondary);
            broker.horizontalAlignment = HorizontalAlignmentOptions.Right;

            // Fault banners: two slots under the header; SidePanel shows them and moves the body down.
            var faultArea = Top(NewRect("Faults", content), 58, 0);
            var bannerRoots = new GameObject[2];
            var bannerTitles = new TMP_Text[2];
            var bannerDetails = new TMP_Text[2];
            for (int i = 0; i < 2; i++)
            {
                var b = Top(NewRect("Banner " + (i + 1), faultArea), 0, 58);
                Rounded(b.gameObject, fill, new Color(Red.r, Red.g, Red.b, 0.14f), 10);
                Rounded(Stretch(NewRect("Border", b), 0).gameObject, ring, Red, 10);
                var icon = Box(b, 12, 10, 18, 18);
                icon.name = "Icon";
                Rounded(icon.gameObject, fill, Red, 9);
                var bang = Label(Stretch(NewRect("Text", icon), 0), "!", 13, Hex("0E1115"), bold: true);
                bang.alignment = TextAlignmentOptions.Center;
                var title = Label(Box(b, 38, 9, 356 - 38 - 12, 18), "", 14, RedText, bold: true);
                title.overflowMode = TextOverflowModes.Ellipsis;
                var detail = Label(Box(b, 38, 28, 356 - 38 - 12, 26), "", 11, new Color(TextColor.r, TextColor.g, TextColor.b, 0.85f));
                detail.textWrappingMode = TextWrappingModes.Normal;
                detail.overflowMode = TextOverflowModes.Ellipsis;
                b.gameObject.SetActive(false);
                bannerRoots[i] = b.gameObject;
                bannerTitles[i] = title;
                bannerDetails[i] = detail;
            }

            // Body: everything that dims while the board is offline.
            var body = NewRect("Body", content);
            body.anchorMin = Vector2.zero;
            body.anchorMax = Vector2.one;
            body.offsetMin = Vector2.zero;
            body.offsetMax = new Vector2(0, -58);
            var bodyGroup = body.gameObject.AddComponent<CanvasGroup>();
            bodyGroup.interactable = true;
            bodyGroup.blocksRaycasts = true;

            // Shown instead of the body until the ESP32 is live: a message and three pulsing dots.
            var waiting = NewRect("Waiting", content);
            waiting.anchorMin = Vector2.zero;
            waiting.anchorMax = Vector2.one;
            waiting.offsetMin = Vector2.zero;
            waiting.offsetMax = new Vector2(0, -58);
            var waitingText = Label(NewRect("Text", waiting), "Waiting for ESP to connect", 18, new Color(TextColor.r, TextColor.g, TextColor.b, 0.7f));
            var wt = (RectTransform)waitingText.transform;
            wt.anchorMin = new Vector2(0, 0.5f);
            wt.anchorMax = new Vector2(1, 0.5f);
            wt.sizeDelta = new Vector2(0, 26);
            wt.anchoredPosition = new Vector2(0, 30);
            waitingText.alignment = TextAlignmentOptions.Center;
            var dots = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var d = NewRect("Dot " + (i + 1), waiting);
                d.anchorMin = d.anchorMax = new Vector2(0.5f, 0.5f);
                d.sizeDelta = new Vector2(8, 8);
                d.anchoredPosition = new Vector2((i - 1) * 16, 0);
                dots[i] = Rounded(d.gameObject, fill, Teal, 4);
            }

            // Energy card
            var card = Top(NewRect("Energy", body), 0, 224);
            Rounded(card.gameObject, fill, new Color(Teal.r, Teal.g, Teal.b, 0.08f), 10);
            var energyBorder = Rounded(Stretch(NewRect("Border", card), 0).gameObject, ring, new Color(Teal.r, Teal.g, Teal.b, 0.35f), 10);
            var e = Stretch(NewRect("Content", card), 16);
            var caption = Label(Top(NewRect("Caption", e), 0, 16), "POWER NOW", 12, Caption, bold: true);
            caption.characterSpacing = 8;
            var power = Label(Top(NewRect("Power", e), 16, 50), "--", 46, Teal, bold: true);
            var sparkGroup = Top(NewRect("Spark", e), 70, 60);
            var spark = Top(NewRect("Sparkline", sparkGroup), 0, 44).gameObject.AddComponent<Sparkline>();
            spark.color = Teal;
            spark.raycastTarget = false;
            var axis = Top(NewRect("Axis", sparkGroup), 46, 14);
            var sparkStart = Label(Stretch(NewRect("Start", axis), 0), "-2 min", 11, new Color(1, 1, 1, 0.5f));
            Label(Stretch(NewRect("End", axis), 0), "now", 11, new Color(1, 1, 1, 0.5f)).horizontalAlignment = HorizontalAlignmentOptions.Right;
            var lower = Top(NewRect("Lower", e), 136, 56);
            var energy = Row(lower, 0, "Energy used", TextColor);
            var saved = Row(lower, 22, "Saved vs always-on", Green);
            var track = Top(NewRect("SavedBar", lower), 48, 8);
            Rounded(track.gameObject, fill, new Color(1, 1, 1, 0.1f), 4);
            var savedFill = NewRect("Fill", track);
            savedFill.anchorMin = Vector2.zero;
            savedFill.anchorMax = new Vector2(0, 1);
            savedFill.offsetMin = Vector2.zero;
            savedFill.offsetMax = Vector2.zero;
            Rounded(savedFill.gameObject, fill, Green, 4);

            // 2x2 tiles
            const float tileTop = 236, tileW = 173, tileH = 114, gap = 10;
            var temp = Tile(body, "Temperature", 0, tileTop, tileW, tileH, "TEMPERATURE", out var tempValue, out var tempDetail);
            var acTile = Tile(body, "AC", tileW + gap, tileTop, tileW, tileH, "AC (FAN)", out var acValue, out var acDetail);
            var lightsTile = Tile(body, "Lights", 0, tileTop + tileH + gap, tileW, tileH, "LIGHTS", out var lightValue, out var lightDetail);
            var status = Tile(body, "Status", tileW + gap, tileTop + tileH + gap, tileW, tileH, "STATUS", out var unusedValue, out var unusedDetail);
            Object.DestroyImmediate(unusedValue.gameObject);
            Object.DestroyImmediate(unusedDetail.gameObject);
            var s = status.Find("Content");
            var dot = Box(s, 0, 21, 10, 10);
            var dotImage = Rounded(dot.gameObject, fill, Hex("6B727C"), 5);
            var occ = Label(Box(s, 16, 14, 133, 24), "--", 18, TextColor, bold: true);
            occ.enableAutoSizing = true;
            occ.fontSizeMin = 11;
            occ.fontSizeMax = 18;
            var door = Label(Top(NewRect("Door", s), 40, 16), "Door: --", 13, Secondary);
            var mode = Label(Top(NewRect("Mode", s), 56, 16), "Mode: --", 13, Secondary);
            var tags = Top(NewRect("Tags", s), 74, 16);
            var lightsTag = Tag(tags, "Lights override");
            var acTag = Tag(tags, "AC override");

            var controlsCard = BuildControls(body, room, 486, 190);
            var tiles = new[] { temp, acTile, lightsTile, status };

            // Footer
            var footerRt = NewRect("Footer", body);
            footerRt.anchorMin = new Vector2(0, 0);
            footerRt.anchorMax = new Vector2(1, 0);
            footerRt.pivot = new Vector2(0.5f, 0);
            footerRt.sizeDelta = new Vector2(0, 18);
            footerRt.anchoredPosition = Vector2.zero;
            var footer = Label(footerRt, "Waiting for data", 13, new Color(1, 1, 1, 0.5f));
            footer.horizontalAlignment = HorizontalAlignmentOptions.Right;

            // Wire up
            var so = new SerializedObject(side);
            if (room != null)
            {
                so.FindProperty("connection").objectReferenceValue = room.GetComponent<MqttConnection>();
                so.FindProperty("model").objectReferenceValue = room.GetComponent<RoomModel>();
            }
            so.FindProperty("statusPill").objectReferenceValue = pillImage;
            so.FindProperty("statusDot").objectReferenceValue = pillDotImage;
            so.FindProperty("statusText").objectReferenceValue = pillText;
            so.FindProperty("brokerText").objectReferenceValue = broker;
            so.FindProperty("body").objectReferenceValue = bodyGroup;
            so.FindProperty("waiting").objectReferenceValue = waiting.gameObject;
            so.FindProperty("waitingText").objectReferenceValue = waitingText;
            SetArray(so.FindProperty("waitingDots"), dots);
            so.FindProperty("powerValue").objectReferenceValue = power;
            so.FindProperty("sparkline").objectReferenceValue = spark;
            so.FindProperty("sparklineStart").objectReferenceValue = sparkStart;
            so.FindProperty("energyValue").objectReferenceValue = energy;
            so.FindProperty("savedValue").objectReferenceValue = saved;
            so.FindProperty("savedFill").objectReferenceValue = savedFill;
            so.FindProperty("tempValue").objectReferenceValue = tempValue;
            so.FindProperty("tempTarget").objectReferenceValue = tempDetail;
            so.FindProperty("acValue").objectReferenceValue = acValue;
            so.FindProperty("acDetail").objectReferenceValue = acDetail;
            so.FindProperty("lightValue").objectReferenceValue = lightValue;
            so.FindProperty("lightDetail").objectReferenceValue = lightDetail;
            so.FindProperty("occupancyDot").objectReferenceValue = dotImage;
            so.FindProperty("occupancyText").objectReferenceValue = occ;
            so.FindProperty("doorText").objectReferenceValue = door;
            so.FindProperty("modeText").objectReferenceValue = mode;
            so.FindProperty("lightsOverrideTag").objectReferenceValue = lightsTag;
            so.FindProperty("acOverrideTag").objectReferenceValue = acTag;
            so.FindProperty("footer").objectReferenceValue = footer;
            SetArray(so.FindProperty("banners"), bannerRoots);
            SetArray(so.FindProperty("bannerTitles"), bannerTitles);
            SetArray(so.FindProperty("bannerDetails"), bannerDetails);
            so.FindProperty("energyCard").objectReferenceValue = card;
            so.FindProperty("sparkGroup").objectReferenceValue = sparkGroup.gameObject;
            so.FindProperty("energyLower").objectReferenceValue = lower;
            so.FindProperty("energyBorder").objectReferenceValue = energyBorder;
            SetArray(so.FindProperty("belowEnergy"), new Object[] { temp, acTile, lightsTile, status, controlsCard });
            var tileBgs = new Object[4];
            var tileAlerts = new Object[4];
            var tileTitles = new Object[4];
            for (int i = 0; i < 4; i++)
            {
                tileBgs[i] = tiles[i].GetComponent<Image>();
                tileAlerts[i] = tiles[i].Find("Alert").GetComponent<Image>();
                tileTitles[i] = tiles[i].Find("Content/Title").GetComponent<TMP_Text>();
            }
            SetArray(so.FindProperty("tileBgs"), tileBgs);
            SetArray(so.FindProperty("tileAlerts"), tileAlerts);
            SetArray(so.FindProperty("tileTitles"), tileTitles);
            so.FindProperty("debugPanel").objectReferenceValue = debugPanel;
            so.ApplyModifiedPropertiesWithoutUndo();
            return side;
        }

        static RectTransform Tile(RectTransform parent, string name, float x, float y, float w, float h, string title,
                                  out TMP_Text value, out TMP_Text detail)
        {
            var tile = Box(parent, x, y, w, h);
            tile.name = name;
            Rounded(tile.gameObject, fill, new Color(1, 1, 1, 0.05f), 10);
            Rounded(Stretch(NewRect("Alert", tile), 0).gameObject, ring, Color.clear, 10);
            var c = Stretch(NewRect("Content", tile), 12);
            var t = Label(Top(NewRect("Title", c), 0, 14), title, 11, Secondary, bold: true);
            t.characterSpacing = 6;
            value = Label(Top(NewRect("Value", c), 16, 34), "--", 28, TextColor, bold: true);
            detail = Label(Top(NewRect("Detail", c), 52, 18), "", 13, Secondary);
            return tile;
        }

        // ---- Controls card ----

        static RectTransform BuildControls(RectTransform body, GameObject room, float top, float height)
        {
            var cardRt = Top(NewRect("Controls", body), top, height);
            Rounded(cardRt.gameObject, fill, new Color(1, 1, 1, 0.035f), 10);
            Rounded(Stretch(NewRect("Border", cardRt), 0).gameObject, ring, new Color(1, 1, 1, 0.07f), 10);
            var cardGroup = cardRt.gameObject.AddComponent<CanvasGroup>();
            var c = Stretch(NewRect("Content", cardRt), 12);
            var controls = cardRt.gameObject.AddComponent<ControlsPanel>();

            // Mode row: label and a two-segment switch.
            var modeRow = Top(NewRect("Mode", c), 0, 26);
            Label(Stretch(NewRect("Label", modeRow), 0), "Mode (B3)", 13, Secondary).verticalAlignment = VerticalAlignmentOptions.Middle;
            var segTrack = BoxRight(modeRow, 0, 0, 170, 26);
            Rounded(segTrack.gameObject, fill, new Color(1, 1, 1, 0.08f), 7);
            var autoSeg = Segment(segTrack, "Auto", 0, out var autoBg, out var autoLabel);
            var manualSeg = Segment(segTrack, "Manual", 1, out var manualBg, out var manualLabel);

            // One on/off switch per device; in Auto a switch also starts that device's override.
            var lightSwitch = SwitchRow(c, 34, "Lights (B1)", out var lightTrack, out var lightKnob, out var lightStatus, out var lightBadge);
            var fanSwitch = SwitchRow(c, 76, "Fan / AC (B2)", out var fanTrack, out var fanKnob, out var fanStatus, out var fanBadge);

            // Target temperature stepper.
            var tRow = Top(NewRect("Target", c), 118, 26);
            Label(Stretch(NewRect("Label", tRow), 0), "Target temperature", 13, Secondary).verticalAlignment = VerticalAlignmentOptions.Middle;
            var plus = StepButton(BoxRight(tRow, 0, 0, 26, 26), "+");
            var value = Label(BoxRight(tRow, 30, 0, 64, 26), "--", 16, TextColor, bold: true);
            value.alignment = TextAlignmentOptions.Center;
            var minus = StepButton(BoxRight(tRow, 98, 0, 26, 26), "-");

            var feedback = Label(Top(NewRect("Feedback", c), 152, 14), "", 12, Secondary);

            var so = new SerializedObject(controls);
            if (room != null)
            {
                so.FindProperty("connection").objectReferenceValue = room.GetComponent<MqttConnection>();
                so.FindProperty("model").objectReferenceValue = room.GetComponent<RoomModel>();
            }
            so.FindProperty("card").objectReferenceValue = cardGroup;
            so.FindProperty("autoButton").objectReferenceValue = autoSeg;
            so.FindProperty("autoBg").objectReferenceValue = autoBg;
            so.FindProperty("autoLabel").objectReferenceValue = autoLabel;
            so.FindProperty("manualButton").objectReferenceValue = manualSeg;
            so.FindProperty("manualBg").objectReferenceValue = manualBg;
            so.FindProperty("manualLabel").objectReferenceValue = manualLabel;
            so.FindProperty("lightSwitch").objectReferenceValue = lightSwitch;
            so.FindProperty("lightSwitchTrack").objectReferenceValue = lightTrack;
            so.FindProperty("lightSwitchKnob").objectReferenceValue = lightKnob;
            so.FindProperty("lightStatus").objectReferenceValue = lightStatus;
            so.FindProperty("lightOverrideBadge").objectReferenceValue = lightBadge;
            so.FindProperty("fanSwitch").objectReferenceValue = fanSwitch;
            so.FindProperty("fanSwitchTrack").objectReferenceValue = fanTrack;
            so.FindProperty("fanSwitchKnob").objectReferenceValue = fanKnob;
            so.FindProperty("fanStatus").objectReferenceValue = fanStatus;
            so.FindProperty("fanOverrideBadge").objectReferenceValue = fanBadge;
            so.FindProperty("setpointDown").objectReferenceValue = minus;
            so.FindProperty("setpointUp").objectReferenceValue = plus;
            so.FindProperty("setpointValue").objectReferenceValue = value;
            so.FindProperty("feedback").objectReferenceValue = feedback;
            so.ApplyModifiedPropertiesWithoutUndo();
            return cardRt;
        }

        // ---- History window ----

        static void BuildHistoryWindow(Transform canvas, GameObject room, Button openButton)
        {
            var history = room != null ? room.GetComponent<HistoryClient>() : null;
            if (room != null && history == null)
            {
                history = room.AddComponent<HistoryClient>();
                var hso = new SerializedObject(history);
                hso.FindProperty("connection").objectReferenceValue = room.GetComponent<MqttConnection>();
                hso.ApplyModifiedPropertiesWithoutUndo();
            }

            // The holder stays active so the window's script can open it; "Overlay" is what shows and hides.
            var holder = Stretch(NewRect("HistoryWindow", canvas), 0);
            var window = holder.gameObject.AddComponent<HistoryWindow>();
            var overlay = Stretch(NewRect("Overlay", holder), 0);
            var dim = Rounded(overlay.gameObject, fill, new Color(0, 0, 0, 0.45f), 1);
            dim.type = Image.Type.Simple;
            dim.sprite = null;
            dim.raycastTarget = true;

            var card = NewRect("Card", overlay);
            card.anchorMin = card.anchorMax = new Vector2(0, 1);
            card.pivot = new Vector2(0, 1);
            card.anchoredPosition = new Vector2(59, -40);
            card.sizeDelta = new Vector2(1060, 820);
            Rounded(card.gameObject, fill, new Color(14 / 255f, 17 / 255f, 21 / 255f, 1f), 14).raycastTarget = true;
            Rounded(Stretch(NewRect("Border", card), 0).gameObject, ring, new Color(1, 1, 1, 0.08f), 14);
            var c = Stretch(NewRect("Content", card), 24);

            // Header: title, day navigation, day picker, close.
            Label(Box(c, 0, 4, 200, 28), "History", 22, TextColor, bold: true);
            var close = HeaderButton(BoxRight(c, 0, 2, 80, 32), "Close");
            var pick = HeaderButton(BoxRight(c, 92, 2, 96, 32), "Pick day");
            var next = HeaderButton(BoxRight(c, 200, 2, 32, 32), ">");
            var date = Label(BoxRight(c, 240, 2, 200, 32), "Today", 15, TextColor, bold: true);
            date.alignment = TextAlignmentOptions.Center;
            var prev = HeaderButton(BoxRight(c, 448, 2, 32, 32), "<");

            // Stat cards.
            var statNames = new[] { "ENERGY USED", "SAVED VS ALWAYS-ON", "OCCUPIED", "FAULTS" };
            var statColors = new[] { Teal, Green, TextColor, RedText };
            var statValues = new TMP_Text[4];
            for (int i = 0; i < 4; i++)
            {
                var s = Box(c, i * (244 + 12), 52, 244, 84);
                s.name = "Stat " + statNames[i];
                Rounded(s.gameObject, fill, new Color(1, 1, 1, 0.04f), 10);
                var cap = Label(Box(s, 14, 12, 220, 14), statNames[i], 11, Secondary, bold: true);
                cap.characterSpacing = 6;
                statValues[i] = Label(Box(s, 14, 32, 220, 36), "--", 26, statColors[i], bold: true);
            }

            // Charts and the events list, 2x2.
            var power = ChartCard(c, "Power (W)", 0, 152, Teal, out var powerNote, out var powerAxis);
            var temp = ChartCard(c, "Temperature vs target", 514, 152, Orange, out var tempNote, out var tempAxis);
            var saved = ChartCard(c, "Energy saved", 0, 458, Green, out var savedNote, out var savedAxis);
            var events = Card(c, "Day's events", 514, 458);
            var viewport = Box(events, 14, 36, 470, 240);
            viewport.name = "Viewport";
            viewport.gameObject.AddComponent<RectMask2D>();
            var vpImage = viewport.gameObject.AddComponent<Image>();
            vpImage.color = Color.clear;
            var content = NewRect("Rows", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = new Vector2(0, 0);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 2;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 20;
            var row = NewRect("Row", content);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
            var rowTime = Label(Box(row, 0, 0, 64, 20), "", 12, Secondary);
            rowTime.name = "Time";
            rowTime.verticalAlignment = VerticalAlignmentOptions.Middle;
            var rowDot = Box(row, 68, 6, 8, 8);
            rowDot.name = "Dot";
            Rounded(rowDot.gameObject, fill, Color.white, 4);
            var rowText = Label(Box(row, 84, 0, 380, 20), "", 12, TextColor);
            rowText.name = "Text";
            rowText.verticalAlignment = VerticalAlignmentOptions.Middle;
            rowText.overflowMode = TextOverflowModes.Ellipsis;
            var eventsEmpty = Label(Box(events, 14, 120, 470, 20), "No events", 12, Secondary);
            eventsEmpty.alignment = TextAlignmentOptions.Center;

            Label(Top(NewRect("Footnote", c), 756, 16), "Green bands = room occupied. Dashed yellow = target temperature.", 11, Secondary);

            // Loading / logger offline / no data, over the stats and charts.
            var message = Box(c, 0, 52, 1012, 704);
            message.name = "Message";
            Rounded(message.gameObject, fill, new Color(14 / 255f, 17 / 255f, 21 / 255f, 0.94f), 10);
            var messageText = Label(Stretch(NewRect("Text", message), 0), "Loading...", 18, new Color(TextColor.r, TextColor.g, TextColor.b, 0.7f));
            messageText.alignment = TextAlignmentOptions.Center;

            // Day picker: a scrolling list under "Pick day", built last so it sits on top.
            var picker = BoxRight(c, 92, 40, 200, 260);
            picker.name = "Picker";
            Rounded(picker.gameObject, fill, new Color(22 / 255f, 26 / 255f, 32 / 255f, 0.98f), 10).raycastTarget = true;
            Rounded(Stretch(NewRect("Border", picker), 0).gameObject, ring, new Color(1, 1, 1, 0.12f), 10);
            var pView = Stretch(NewRect("Viewport", picker), 6);
            pView.gameObject.AddComponent<RectMask2D>();
            pView.gameObject.AddComponent<Image>().color = Color.clear;
            var pContent = NewRect("Days", pView);
            pContent.anchorMin = new Vector2(0, 1);
            pContent.anchorMax = new Vector2(1, 1);
            pContent.pivot = new Vector2(0.5f, 1);
            pContent.sizeDelta = Vector2.zero;
            var pLayout = pContent.gameObject.AddComponent<VerticalLayoutGroup>();
            pLayout.spacing = 2;
            pLayout.childControlHeight = true;
            pLayout.childControlWidth = true;
            pLayout.childForceExpandHeight = false;
            pContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var pScroll = pView.gameObject.AddComponent<ScrollRect>();
            pScroll.content = pContent;
            pScroll.viewport = pView;
            pScroll.horizontal = false;
            pScroll.movementType = ScrollRect.MovementType.Clamped;
            pScroll.scrollSensitivity = 20;
            var item = NewRect("Day", pContent);
            item.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
            var itemBg = Rounded(item.gameObject, fill, new Color(1, 1, 1, 0.04f), 6);
            itemBg.raycastTarget = true;
            var itemText = Label(Stretch(NewRect("Text", item), 0), "", 13, TextColor);
            ((RectTransform)itemText.transform).offsetMin = new Vector2(10, 0);
            itemText.verticalAlignment = VerticalAlignmentOptions.Middle;
            var itemButton = item.gameObject.AddComponent<Button>();
            itemButton.targetGraphic = itemBg;

            overlay.gameObject.SetActive(false);

            var so = new SerializedObject(window);
            so.FindProperty("history").objectReferenceValue = history;
            so.FindProperty("root").objectReferenceValue = overlay.gameObject;
            so.FindProperty("openButton").objectReferenceValue = openButton;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("prevButton").objectReferenceValue = prev;
            so.FindProperty("nextButton").objectReferenceValue = next;
            so.FindProperty("pickButton").objectReferenceValue = pick;
            so.FindProperty("dateLabel").objectReferenceValue = date;
            so.FindProperty("picker").objectReferenceValue = picker.gameObject;
            so.FindProperty("pickerContent").objectReferenceValue = pContent;
            so.FindProperty("pickerItemTemplate").objectReferenceValue = item.gameObject;
            so.FindProperty("energyValue").objectReferenceValue = statValues[0];
            so.FindProperty("savedValue").objectReferenceValue = statValues[1];
            so.FindProperty("occupiedValue").objectReferenceValue = statValues[2];
            so.FindProperty("faultsValue").objectReferenceValue = statValues[3];
            so.FindProperty("powerChart").objectReferenceValue = power;
            so.FindProperty("powerNote").objectReferenceValue = powerNote;
            SetArray(so.FindProperty("powerAxis"), powerAxis);
            so.FindProperty("tempChart").objectReferenceValue = temp;
            so.FindProperty("tempNote").objectReferenceValue = tempNote;
            SetArray(so.FindProperty("tempAxis"), tempAxis);
            so.FindProperty("savedChart").objectReferenceValue = saved;
            so.FindProperty("savedNote").objectReferenceValue = savedNote;
            SetArray(so.FindProperty("savedAxis"), savedAxis);
            so.FindProperty("eventsContent").objectReferenceValue = content;
            so.FindProperty("eventRowTemplate").objectReferenceValue = row.gameObject;
            so.FindProperty("eventsEmpty").objectReferenceValue = eventsEmpty;
            so.FindProperty("messageRoot").objectReferenceValue = message.gameObject;
            so.FindProperty("messageText").objectReferenceValue = messageText;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Button HeaderButton(RectTransform rt, string text)
        {
            rt.name = text;
            var bg = Rounded(rt.gameObject, fill, new Color(1, 1, 1, 0.08f), 8);
            bg.raycastTarget = true;
            var label = Label(Stretch(NewRect("Text", rt), 0), text, 13, TextColor, bold: text.Length == 1);
            label.alignment = TextAlignmentOptions.Center;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            var colors = b.colors;
            colors.normalColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = Color.white;
            colors.selectedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.3f);
            colors.colorMultiplier = 1.6f;
            b.colors = colors;
            return b;
        }

        /// <summary>A rounded chart/list card, 498 x 290, with a title.</summary>
        static RectTransform Card(RectTransform parent, string title, float x, float y)
        {
            var card = Box(parent, x, y, 498, 290);
            card.name = title;
            Rounded(card.gameObject, fill, new Color(1, 1, 1, 0.035f), 10);
            Rounded(Stretch(NewRect("Border", card), 0).gameObject, ring, new Color(1, 1, 1, 0.07f), 10);
            Label(Box(card, 14, 10, 300, 18), title, 13, TextColor, bold: true);
            return card;
        }

        /// <summary>A chart card: title, note on the right, y labels (bottom, middle, top) and 00:00-24:00 below.</summary>
        static LineChart ChartCard(RectTransform parent, string title, float x, float y, Color noteColor,
                                   out TMP_Text note, out TMP_Text[] axis)
        {
            var card = Card(parent, title, x, y);
            note = Label(BoxRight(card, 14, 10, 220, 18), "", 12, noteColor, bold: true);
            note.horizontalAlignment = HorizontalAlignmentOptions.Right;

            const float left = 54, top = 36, width = 430, height = 222;
            var plot = Box(card, left, top, width, height);
            plot.name = "Plot";
            var chart = plot.gameObject.AddComponent<LineChart>();
            chart.raycastTarget = false;

            axis = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                float cy = top + height - height * i / 2f;   // bottom, middle, top
                var t = Label(Box(card, 4, cy - 8, left - 10, 16), "", 11, Secondary);
                t.horizontalAlignment = HorizontalAlignmentOptions.Right;
                t.verticalAlignment = VerticalAlignmentOptions.Middle;
                axis[i] = t;
            }
            for (int i = 0; i <= 4; i++)
            {
                var t = Label(Box(card, left + width * i / 4f - 24, top + height + 3, 48, 14), (i * 6).ToString("00") + ":00", 11, Secondary);
                t.alignment = TextAlignmentOptions.Center;
            }
            return chart;
        }

        // ---- Event log (bottom-left) ----

        const float EventLogExpanded = 168;
        const int EventLogRows = 6;

        static void BuildEventLog(Transform canvas, GameObject room)
        {
            var cardRt = NewRect("EventLog", canvas);
            cardRt.anchorMin = cardRt.anchorMax = Vector2.zero;
            cardRt.pivot = Vector2.zero;
            cardRt.anchoredPosition = new Vector2(22, 22);
            cardRt.sizeDelta = new Vector2(380, EventLogExpanded);
            Rounded(cardRt.gameObject, fill, new Color(14 / 255f, 17 / 255f, 21 / 255f, 0.85f), 12);
            Rounded(Stretch(NewRect("Border", cardRt), 0).gameObject, ring, new Color(1, 1, 1, 0.08f), 12);
            var panel = cardRt.gameObject.AddComponent<EventLogPanel>();

            // Header bar: caption and a hide/show link.
            var header = NewRect("Header", cardRt);
            header.anchorMin = new Vector2(0, 1);
            header.anchorMax = new Vector2(1, 1);
            header.pivot = new Vector2(0.5f, 1);
            header.offsetMin = new Vector2(14, -30);
            header.offsetMax = new Vector2(-14, -12);
            var caption = Label(Stretch(NewRect("Caption", header), 0), "EVENT LOG", 12, Caption, bold: true);
            caption.characterSpacing = 8;
            caption.verticalAlignment = VerticalAlignmentOptions.Middle;
            var toggleRt = BoxRight(header, 0, 0, 50, 18);
            toggleRt.name = "Toggle";
            var toggleBg = Rounded(toggleRt.gameObject, fill, Color.clear, 4);
            toggleBg.raycastTarget = true;
            var toggleLabel = Label(Stretch(NewRect("Text", toggleRt), 0), "hide", 12, Teal);
            toggleLabel.alignment = TextAlignmentOptions.MidlineRight;
            var toggle = toggleRt.gameObject.AddComponent<Button>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = toggleBg;

            // Rows, newest on top.
            var list = NewRect("List", cardRt);
            list.anchorMin = Vector2.zero;
            list.anchorMax = Vector2.one;
            list.offsetMin = new Vector2(14, 10);
            list.offsetMax = new Vector2(-14, -38);
            var empty = Label(Top(NewRect("Empty", list), 0, 18), "No events yet", 12, Secondary);
            var rows = new GameObject[EventLogRows];
            var times = new TMP_Text[EventLogRows];
            var dots = new Image[EventLogRows];
            var texts = new TMP_Text[EventLogRows];
            for (int i = 0; i < EventLogRows; i++)
            {
                var row = Top(NewRect("Row " + (i + 1), list), i * 20, 18);
                times[i] = Label(Box(row, 0, 0, 62, 18), "", 12, Secondary);
                times[i].verticalAlignment = VerticalAlignmentOptions.Middle;
                var dot = Box(row, 66, 5, 8, 8);
                dots[i] = Rounded(dot.gameObject, fill, Color.white, 4);
                texts[i] = Label(Box(row, 82, 0, 352 - 82, 18), "", 12, TextColor);
                texts[i].verticalAlignment = VerticalAlignmentOptions.Middle;
                texts[i].overflowMode = TextOverflowModes.Ellipsis;
                row.gameObject.SetActive(false);
                rows[i] = row.gameObject;
            }

            var so = new SerializedObject(panel);
            if (room != null)
                so.FindProperty("model").objectReferenceValue = room.GetComponent<RoomModel>();
            so.FindProperty("card").objectReferenceValue = cardRt;
            so.FindProperty("list").objectReferenceValue = list.gameObject;
            so.FindProperty("toggle").objectReferenceValue = toggle;
            so.FindProperty("toggleLabel").objectReferenceValue = toggleLabel;
            so.FindProperty("emptyText").objectReferenceValue = empty;
            SetArray(so.FindProperty("rows"), rows);
            SetArray(so.FindProperty("times"), times);
            SetArray(so.FindProperty("dots"), dots);
            SetArray(so.FindProperty("texts"), texts);
            so.FindProperty("expandedHeight").floatValue = EventLogExpanded;
            so.FindProperty("collapsedHeight").floatValue = 42;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Button Segment(RectTransform track, string text, int index, out Image bg, out TMP_Text label)
        {
            var rt = Box(track, 2 + index * 83, 2, 83, 22);
            rt.name = text;
            bg = Rounded(rt.gameObject, fill, Color.clear, 5);
            bg.raycastTarget = true;
            label = Label(Stretch(NewRect("Text", rt), 0), text, 13, TextColor);
            label.alignment = TextAlignmentOptions.Center;
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = bg;
            return b;
        }

        static Button StepButton(RectTransform rt, string text)
        {
            rt.name = text == "+" ? "Up" : "Down";
            var bg = Rounded(rt.gameObject, fill, new Color(1, 1, 1, 0.08f), 6);
            bg.raycastTarget = true;
            var label = Label(Stretch(NewRect("Text", rt), 0), text, 18, TextColor, bold: true);
            label.alignment = TextAlignmentOptions.Center;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            var colors = b.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.pressedColor = new Color(1f, 1f, 1f, 1f);
            colors.selectedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.35f);
            colors.colorMultiplier = 1.5f;
            b.colors = colors;
            return b;
        }

        /// <summary>
        /// A row with a label and small status line on the left, an on/off switch on the right,
        /// and a hidden yellow "OVERRIDE" badge just left of the switch.
        /// </summary>
        static Button SwitchRow(RectTransform parent, float top, string title,
                                out Image track, out RectTransform knob, out TMP_Text status, out GameObject badge)
        {
            // No "/" in object names: it would break hierarchy paths like Transform.Find.
            var row = Top(NewRect(title.Replace(" / ", " ").Replace("/", " "), parent), top, 34);
            Label(Top(NewRect("Label", row), 1, 16), title, 13, TextColor);
            status = Label(Top(NewRect("Status", row), 19, 14), "--", 11, Secondary);

            var badgeRt = BoxRight(row, 60, 9, 70, 16);
            badgeRt.name = "Override";
            Rounded(badgeRt.gameObject, ring, Yellow, 5);
            var badgeText = Label(Stretch(NewRect("Text", badgeRt), 0), "OVERRIDE", 10, Yellow, bold: true);
            badgeText.alignment = TextAlignmentOptions.Center;
            badgeText.characterSpacing = 4;
            badgeRt.sizeDelta = new Vector2(Mathf.Ceil(badgeText.GetPreferredValues("OVERRIDE").x) + 14, 16);
            badge = badgeRt.gameObject;
            badge.SetActive(false);

            return Switch(BoxRight(row, 0, 3, 52, 28), out track, out knob);
        }

        /// <summary>An on/off switch: a pill track with a round knob that slides left (off) or right (on).</summary>
        static Button Switch(RectTransform rt, out Image track, out RectTransform knob)
        {
            rt.name = "Switch";
            track = Rounded(rt.gameObject, fill, new Color(1, 1, 1, 0.15f), rt.sizeDelta.y / 2f);
            track.raycastTarget = true;
            float size = rt.sizeDelta.y - 6f;
            knob = Box(rt, 3, 3, size, size);
            knob.name = "Knob";
            knob.anchorMin = knob.anchorMax = new Vector2(0, 0.5f);
            knob.pivot = new Vector2(0, 0.5f);
            knob.anchoredPosition = new Vector2(3, 0);
            Rounded(knob.gameObject, fill, Color.white, size / 2f);
            var b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = track;
            return b;
        }

        static void SetArray(SerializedProperty prop, Object[] items)
        {
            prop.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static TMP_Text Row(RectTransform parent, float top, string label, Color valueColor)
        {
            var row = Top(NewRect(label, parent), top, 20);
            Label(Stretch(NewRect("Label", row), 0), label, 14, Secondary);
            var v = Label(Stretch(NewRect("Value", row), 0), "--", 14, valueColor, bold: true);
            v.horizontalAlignment = HorizontalAlignmentOptions.Right;
            return v;
        }

        static RectTransform Tag(RectTransform parent, string text)
        {
            var rt = Box(parent, 0, 0, 10, 16);
            rt.name = text;
            Rounded(rt.gameObject, ring, Yellow, 5);
            var label = Label(Stretch(NewRect("Text", rt), 0), text, 10, Yellow, bold: true);
            label.alignment = TextAlignmentOptions.Center;
            rt.sizeDelta = new Vector2(Mathf.Ceil(label.GetPreferredValues(text).x) + 12, 16);
            rt.gameObject.SetActive(false);
            return rt;
        }

        static TMP_Text Label(RectTransform rt, string text, float size, Color color, bool bold = false)
        {
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.alignment = TextAlignmentOptions.TopLeft;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.richText = true;
            t.raycastTarget = false;
            return t;
        }

        static Image Rounded(GameObject go, Sprite sprite, Color color, float radius)
        {
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(radius, 0.5f);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Fills the parent, inset by the given padding.</summary>
        static RectTransform Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Full width, at the given distance from the parent's top.</summary>
        static RectTransform Top(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = new Vector2(0, -(top + height));
            rt.offsetMax = new Vector2(0, -top);
            return rt;
        }

        /// <summary>A fixed-size box measured from the parent's top-left corner.</summary>
        static RectTransform Box(Transform parent, float x, float y, float w, float h)
        {
            var rt = NewRect("Box", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>A fixed-size box measured from the parent's top-right corner.</summary>
        static RectTransform BoxRight(Transform parent, float right, float y, float w, float h)
        {
            var rt = NewRect("Box", parent);
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-right, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static void EnsureSprites()
        {
            if (!AssetDatabase.IsValidFolder(SpriteFolder))
                AssetDatabase.CreateFolder("Assets", "UI");
            fill = EnsureSprite(FillSpritePath, ring: false);
            ring = EnsureSprite(RingSpritePath, ring: true);
        }

        /// <summary>Writes a 128 px rounded-rectangle texture (filled or outline) and imports it as a 9-slice sprite.</summary>
        static Sprite EnsureSprite(string path, bool ring)
        {
            if (!File.Exists(path))
            {
                const int size = 128;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // Signed distance to a rounded square, negative inside.
                        float px = Mathf.Abs(x + 0.5f - size / 2f) - (size / 2f - SpriteRadius);
                        float py = Mathf.Abs(y + 0.5f - size / 2f) - (size / 2f - SpriteRadius);
                        float outside = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0);
                        float d = outside - SpriteRadius;
                        float a = Mathf.Clamp01(0.5f - d);
                        if (ring)
                            a *= Mathf.Clamp01(d + RingStroke + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255));
                    }
                }
                tex.SetPixels32(pixels);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var border = new Vector4(SpriteRadius, SpriteRadius, SpriteRadius, SpriteRadius);
            if (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = border;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
