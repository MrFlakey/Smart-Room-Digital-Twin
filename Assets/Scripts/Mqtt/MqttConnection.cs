using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using UnityEngine;

namespace SmartRoom
{
    /// <summary>
    /// Keeps one MQTT connection to the broker, retries every few seconds,
    /// and hands incoming messages to the rest of the twin on the Unity main thread.
    /// </summary>
    public class MqttConnection : MonoBehaviour
    {
        public static MqttConnection Instance { get; private set; }

        [SerializeField] MqttSettings settings;
        [SerializeField] float reconnectSeconds = 5f;

        public bool IsConnected => client != null && client.IsConnected;
        /// <summary>This twin's MQTT client id; also used as the history "client" so answers come back to us.</summary>
        public string ClientId { get; } = "twin-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        public MqttSettings Settings => settings;

        /// <summary>Subtopic (for example "telemetry") and payload of every message.</summary>
        public event Action<string, string> MessageReceived;
        public event Action<string> TelemetryReceived;
        public event Action<string> StateReceived;
        public event Action<string> StatusReceived;
        public event Action<string> FaultsReceived;
        public event Action<string> EventReceived;
        public event Action<bool> ConnectionChanged;

        readonly ConcurrentQueue<(string topic, string payload)> inbox = new ConcurrentQueue<(string, string)>();
        IMqttClient client;
        CancellationTokenSource cts;
        string baseTopic;
        bool lastConnected;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            if (settings == null)
            {
                Debug.LogError("MqttConnection: no MqttSettings assigned.");
                return;
            }

            baseTopic = settings.BaseTopic;
            client = new MqttFactory().CreateMqttClient();
            client.ApplicationMessageReceivedAsync += e =>
            {
                inbox.Enqueue((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString() ?? ""));
                return Task.CompletedTask;
            };

            cts = new CancellationTokenSource();
            var options = BuildOptions();
            var token = cts.Token;
            Task.Run(() => ConnectLoop(options, token));
        }

        MqttClientOptions BuildOptions()
        {
            var builder = new MqttClientOptionsBuilder()
                .WithClientId(ClientId)
                .WithCleanSession()
                .WithTimeout(TimeSpan.FromSeconds(4));

            if (settings.useCloud && !string.IsNullOrEmpty(settings.cloudHost))
            {
                builder.WithTcpServer(settings.cloudHost, settings.cloudPort);
                if (!string.IsNullOrEmpty(settings.cloudUser))
                    builder.WithCredentials(settings.cloudUser, settings.cloudPassword);
                if (settings.cloudUseTls)
                    builder.WithTlsOptions(o => o.UseTls());
            }
            else
            {
                builder.WithTcpServer(settings.host, settings.port);
            }
            return builder.Build();
        }

        async Task ConnectLoop(MqttClientOptions options, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!client.IsConnected)
                {
                    try
                    {
                        await client.ConnectAsync(options, token).ConfigureAwait(false);
                        await client.SubscribeAsync(baseTopic + "#", cancellationToken: token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        // Broker not reachable; try again after the delay.
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(reconnectSeconds), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        void Update()
        {
            bool connected = IsConnected;
            if (connected != lastConnected)
            {
                lastConnected = connected;
                Debug.Log(connected ? "MQTT connected" : "MQTT disconnected");
                ConnectionChanged?.Invoke(connected);
            }

            while (inbox.TryDequeue(out var msg))
            {
                if (!msg.topic.StartsWith(baseTopic, StringComparison.Ordinal))
                    continue;
                string sub = msg.topic.Substring(baseTopic.Length);

                MessageReceived?.Invoke(sub, msg.payload);
                switch (sub)
                {
                    case "telemetry": TelemetryReceived?.Invoke(msg.payload); break;
                    case "state": StateReceived?.Invoke(msg.payload); break;
                    case "status": StatusReceived?.Invoke(msg.payload); break;
                    case "faults": FaultsReceived?.Invoke(msg.payload); break;
                    case "event": EventReceived?.Invoke(msg.payload); break;
                }
            }
        }

        /// <summary>Publishes to smartroom/&lt;room&gt;/&lt;subtopic&gt;. Returns false if not connected.</summary>
        public bool Publish(string subtopic, string json, bool retain = false, bool atLeastOnce = false)
        {
            if (!IsConnected)
                return false;

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(baseTopic + subtopic)
                .WithPayload(json)
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(atLeastOnce ? MqttQualityOfServiceLevel.AtLeastOnce : MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            client.PublishAsync(message).ContinueWith(
                t => Debug.LogWarning("MQTT publish failed: " + t.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }

        void OnApplicationQuit() => Shutdown();

        void OnDestroy()
        {
            Shutdown();
            if (Instance == this)
                Instance = null;
        }

        void Shutdown()
        {
            if (cts == null)
                return;

            cts.Cancel();
            cts.Dispose();
            cts = null;

            var c = client;
            client = null;
            if (c == null)
                return;

            try
            {
                if (c.IsConnected)
                    c.DisconnectAsync().Wait(1000);
            }
            catch (Exception)
            {
                // Already gone.
            }
            c.Dispose();
        }
    }
}
