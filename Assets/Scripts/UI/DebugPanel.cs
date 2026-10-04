using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

namespace SmartRoom
{
    /// <summary>On-screen text panel showing everything the twin has received, for checking the MQTT link.</summary>
    public class DebugPanel : MonoBehaviour
    {
        [SerializeField] MqttConnection connection;
        [SerializeField] RoomModel model;
        [SerializeField] TMP_Text text;

        const string Good = "#6EE787";
        const string Bad = "#FF7B72";
        const string Dim = "#9AA4AF";

        readonly StringBuilder sb = new StringBuilder(1024);
        bool dirty = true;

        void OnEnable()
        {
            if (model != null)
                model.Changed += MarkDirty;
            dirty = true;
        }

        void OnDisable()
        {
            if (model != null)
                model.Changed -= MarkDirty;
        }

        void MarkDirty() => dirty = true;

        void Update()
        {
            if (!dirty || text == null || model == null || connection == null)
                return;
            dirty = false;
            text.text = Build();
        }

        string Build()
        {
            sb.Clear();
            var s = connection.Settings;

            sb.Append("<b>SMART ROOM</b>\n");
            sb.Append("Broker  ").Append(Flag(connection.IsConnected, "connected", "disconnected"));
            if (s != null)
                sb.Append("  <color=").Append(Dim).Append('>').Append(s.host).Append(':').Append(s.port).Append("</color>");
            sb.Append('\n');
            sb.Append("ESP32   ").Append(Flag(model.Online, "online", "offline")).Append("\n\n");

            sb.Append("<b>TELEMETRY</b>");
            var t = model.Telemetry;
            if (t == null)
            {
                sb.Append("\n<color=").Append(Dim).Append(">nothing received yet</color>\n");
            }
            else
            {
                sb.Append("  <color=").Append(Dim).Append('>').Append(model.LastTelemetryTime.ToString("HH:mm:ss")).Append("</color>\n");
                Row("Mode", t.Mode);
                Row("Occupied", YesNo(t.Occ));
                Row("PIR", YesNo(t.Pir));
                Row("Door open", YesNo(t.DoorOpen));
                Row("Distance", Num(t.DistCm, "0.0", " cm"));
                Row("Temperature", Num(t.TempC, "0.0", " °C"));
                Row("Setpoint", Num(t.SetC, "0.0", " °C"));
                Row("Ambient light", Num(t.LightPct, "0", " %"));
                Row("Light output", Num(t.Light, "0", " %"));
                Row("Fan output", Num(t.Fan, "0", " %"));
                Row("Power", Num(t.PowerW, "0.00", " W"));
                Row("Energy", Num(t.EnergyWh, "0.00", " Wh"));
                Row("Saved", Num(t.SavedPct, "0.0", " %"));
                Row("Lights override", YesNo(t.LightsOvr));
                Row("AC override", YesNo(t.AcOvr));
                Row("AC paused", YesNo(t.AcPaused));
                Row("Uptime", t.Ts.HasValue ? (t.Ts.Value / 1000).ToString(CultureInfo.InvariantCulture) + " s" : null);
            }

            sb.Append("\n<b>FAULTS</b>\n");
            if (!model.Faults.Any)
            {
                sb.Append("<color=").Append(Good).Append(">none</color>\n");
            }
            else
            {
                foreach (var f in model.Faults.BySensor)
                    sb.Append("<color=").Append(Bad).Append('>').Append(f.Key).Append("</color>  ").Append(f.Value).Append('\n');
            }

            sb.Append("\n<b>EVENTS</b>\n");
            if (model.Events.Count == 0)
                sb.Append("<color=").Append(Dim).Append(">none yet</color>\n");
            foreach (var e in model.Events)
                sb.Append("<color=").Append(Dim).Append('>').Append(e.Time.ToString("HH:mm:ss")).Append("</color> <noparse>").Append(e.Json).Append("</noparse>\n");

            return sb.ToString();
        }

        void Row(string label, string value)
        {
            sb.Append("<color=").Append(Dim).Append('>').Append(label).Append("</color><pos=55%>");
            if (value == null)
                sb.Append("<color=").Append(Bad).Append(">--</color>");
            else
                sb.Append(value);
            sb.Append('\n');
        }

        static string Flag(bool ok, string yes, string no) =>
            "<color=" + (ok ? Good : Bad) + ">" + (ok ? yes : no) + "</color>";

        static string YesNo(int? v) => v.HasValue ? (v.Value != 0 ? "yes" : "no") : null;

        static string Num(float? v, string format, string unit) =>
            v.HasValue ? v.Value.ToString(format, CultureInfo.InvariantCulture) + unit : null;

        static string Num(int? v, string format, string unit) =>
            v.HasValue ? v.Value.ToString(format, CultureInfo.InvariantCulture) + unit : null;
    }
}
