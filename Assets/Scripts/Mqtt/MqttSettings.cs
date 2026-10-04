using UnityEngine;

namespace SmartRoom
{
    /// <summary>Broker settings for the twin. Leave the cloud fields empty until the fallback is needed.</summary>
    [CreateAssetMenu(fileName = "MqttSettings", menuName = "Smart Room/MQTT Settings")]
    public class MqttSettings : ScriptableObject
    {
        [Header("Local broker (Mosquitto)")]
        public string host = "192.168.1.150";
        public int port = 1883;
        public string roomId = "room1";

        [Header("Cloud fallback (HiveMQ Cloud) - not used yet")]
        public bool useCloud = false;
        public string cloudHost = "";
        public int cloudPort = 8883;
        public string cloudUser = "";
        public string cloudPassword = "";
        public bool cloudUseTls = true;

        [Header("History logger (PC)")]
        [Tooltip("If the logger isn't running when the twin starts, start it (Windows only). It normally starts with Windows.")]
        public bool launchLoggerIfMissing = true;
        public string loggerPath = @"D:\Smart Room Digital Twin\tools\HistoryLogger\publish\HistoryLogger.exe";

        public string BaseTopic => "smartroom/" + roomId + "/";
    }
}
