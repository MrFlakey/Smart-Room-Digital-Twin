using Newtonsoft.Json.Linq;

namespace SmartRoom
{
    /// <summary>Turns a room event (as on the event topic) into a short sentence for the logs.</summary>
    public static class EventText
    {
        /// <summary>
        /// Returns false for events that aren't shown. "status" events (online/offline) come from the history logger.
        /// </summary>
        public static bool TryDescribe(JObject e, bool manual, out LogKind kind, out string text)
        {
            kind = LogKind.Esp;
            text = null;
            string type = (string)e["type"];
            switch (type)
            {
                case "occupancy":
                    kind = LogKind.Occupancy;
                    text = (int?)e["value"] == 1 ? "Room occupied" : "Room empty";
                    return true;
                case "door":
                    kind = LogKind.Door;
                    text = (int?)e["value"] == 1 ? (manual ? "Door opened" : "Door opened, AC paused") : "Door closed";
                    return true;
                case "button":
                    kind = LogKind.Override;
                    int? id = (int?)e["id"];
                    text = id == 1 ? "Lights button pressed (B1)"
                         : id == 2 ? "Fan button pressed (B2)"
                         : id == 3 ? "Mode button pressed (B3)"
                         : "Button " + id + " pressed";
                    return true;
                case "fault":
                {
                    kind = LogKind.Fault;
                    text = FaultInfo.ShortName((string)e["sensor"]) + " failed";
                    bool fan = false, lights = false;
                    if (e["off"] is JArray off)
                    {
                        foreach (var item in off)
                        {
                            string device = (string)item;
                            if (device == "fan" || device == "ac") fan = true;
                            if (device == "lights" || device == "light") lights = true;
                        }
                    }
                    if (lights && fan) text += ", lights and fan switched off";
                    else if (fan) text += ", fan switched off";
                    else if (lights) text += ", lights switched off";
                    return true;
                }
                case "recovered":
                    kind = LogKind.Recovered;
                    text = FaultInfo.ShortName((string)e["sensor"]) + " recovered";
                    return true;
                case "status":
                    kind = LogKind.Esp;
                    text = (string)e["value"] == "online" ? "ESP connected" : "ESP disconnected";
                    return true;
                default:
                    return false;
            }
        }
    }
}
