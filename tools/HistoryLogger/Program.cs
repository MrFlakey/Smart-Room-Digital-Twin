// Smart Room history logger.
// Records everything the ESP32 publishes into a SQLite database and answers the twin's history requests.
// Author: Mohamed Khamis
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using SmartRoom.HistoryLogger;

// Only one logger at a time (the Startup shortcut and the twin may both try to start it).
using var single = new Mutex(true, @"Local\SmartRoomHistoryLogger", out bool first);
if (!first)
{
    Console.WriteLine("History logger is already running.");
    return;
}

var settings = Settings.Load();
string logFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settings.DatabasePath))!, "history-logger.log");
using var db = new Database(settings.DatabasePath);
string baseTopic = settings.BaseTopic;
Log($"History logger: broker {settings.BrokerHost}:{settings.BrokerPort}, room {settings.RoomId}, database {settings.DatabasePath}");

var client = new MqttFactory().CreateMqttClient();
var options = new MqttClientOptionsBuilder()
    .WithTcpServer(settings.BrokerHost, settings.BrokerPort)
    .WithClientId("history-logger-" + Environment.MachineName)
    .WithCleanSession()
    .WithTimeout(TimeSpan.FromSeconds(5))
    .Build();

// Messages are handled one at a time, in order, so the database only ever has one writer.
var inbox = Channel.CreateUnbounded<(string Topic, string Payload)>();
client.ApplicationMessageReceivedAsync += e =>
{
    inbox.Writer.TryWrite((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString() ?? ""));
    return Task.CompletedTask;
};

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

string? lastStatus = db.LastText("status", "value");
string? lastFaults = db.LastText("faults", "json");
long telemetryCount = 0, eventCount = 0, requestCount = 0;
var lastSummary = DateTime.Now;

var worker = Task.Run(async () =>
{
    try
    {
        await foreach (var (topic, payload) in inbox.Reader.ReadAllAsync(stop.Token))
        {
            try
            {
                await Handle(topic, payload);
            }
            catch (Exception ex)
            {
                Log($"Error handling {topic}: {ex.Message}");
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
});

// Connect, subscribe, and reconnect every 5 s if the broker goes away.
bool wasConnected = false;
while (!stop.IsCancellationRequested)
{
    if (!client.IsConnected)
    {
        if (wasConnected)
            Log("Disconnected from broker, retrying...");
        wasConnected = false;
        try
        {
            await client.ConnectAsync(options, stop.Token);
            var subscribe = new MqttClientSubscribeOptionsBuilder();
            foreach (var t in new[] { "telemetry", "state", "status", "faults", "event", "history/req" })
                subscribe.WithTopicFilter(baseTopic + t, MqttQualityOfServiceLevel.AtLeastOnce);
            await client.SubscribeAsync(subscribe.Build(), stop.Token);
            wasConnected = true;
            Log("Connected to broker.");
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Log($"Broker not reachable ({ex.GetBaseException().Message}), retrying in 5 s.");
        }
    }

    if ((DateTime.Now - lastSummary).TotalMinutes >= 10)
    {
        lastSummary = DateTime.Now;
        Log($"Stored {telemetryCount} telemetry rows and {eventCount} events, answered {requestCount} history requests since start.");
    }

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

inbox.Writer.TryComplete();
await worker;
if (client.IsConnected)
    await client.DisconnectAsync();
Log("History logger stopped.");
return;

async Task Handle(string topic, string payload)
{
    if (!topic.StartsWith(baseTopic, StringComparison.Ordinal))
        return;
    string sub = topic.Substring(baseTopic.Length);
    var now = DateTimeOffset.UtcNow;

    switch (sub)
    {
        case "telemetry":
            using (var doc = JsonDocument.Parse(payload))
                db.InsertTelemetry(now, doc.RootElement);
            telemetryCount++;
            break;

        case "event":
            string? type = null;
            using (var doc = JsonDocument.Parse(payload))
            {
                if (doc.RootElement.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                    type = t.GetString();
            }
            db.InsertEvent(now, payload.Trim(), type);
            eventCount++;
            break;

        case "status":
            // Store changes only (the retained status is re-sent on every reconnect).
            string status = payload.Trim();
            if (status != lastStatus)
            {
                db.InsertStatus(now, status);
                lastStatus = status;
                Log("ESP is " + status + ".");
            }
            break;

        case "faults":
            string faults = payload.Trim();
            if (faults != lastFaults)
            {
                db.InsertFaults(now, faults);
                lastFaults = faults;
            }
            break;

        case "history/req":
            await Answer(payload);
            break;
    }
}

async Task Answer(string payload)
{
    string? day = null, clientId = null;
    try
    {
        using var doc = JsonDocument.Parse(payload);
        if (doc.RootElement.TryGetProperty("day", out var d)) day = d.GetString();
        if (doc.RootElement.TryGetProperty("client", out var c)) clientId = c.GetString();
    }
    catch (JsonException)
    {
    }
    if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(day) ||
        clientId.Contains('/') || clientId.Contains('#') || clientId.Contains('+'))
    {
        Log("Ignored history request without a valid day and client: " + payload);
        return;
    }

    JsonObject answer;
    if (day == "list")
        answer = HistoryQuery.DayList(db);
    else if (DateTime.TryParseExact(day, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
        answer = HistoryQuery.Day(db, day);
    else
        answer = new JsonObject { ["error"] = "day must be yyyy-MM-dd or \"list\"" };

    var message = new MqttApplicationMessageBuilder()
        .WithTopic(baseTopic + "history/resp/" + clientId)
        .WithPayload(Encoding.UTF8.GetBytes(answer.ToJsonString()))
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build();
    await client.PublishAsync(message);
    requestCount++;
}

// Each line goes to the console (when there is one) and to history-logger.log next to the database.
void Log(string text)
{
    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}";
    Console.WriteLine(line);
    try
    {
        File.AppendAllText(logFile, line + Environment.NewLine);
    }
    catch (IOException)
    {
    }
}
