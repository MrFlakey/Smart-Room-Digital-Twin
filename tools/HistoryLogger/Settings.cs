using System.Text.Json;

namespace SmartRoom.HistoryLogger;

/// <summary>Logger settings, read from appsettings.json next to the program.</summary>
public sealed class Settings
{
    public string BrokerHost { get; set; } = "192.168.1.150";
    public int BrokerPort { get; set; } = 1883;
    public string RoomId { get; set; } = "room1";
    public string DatabasePath { get; set; } = @"D:\Smart Room Data\history.db";

    public string BaseTopic => "smartroom/" + RoomId + "/";

    public static Settings Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return new Settings();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), options) ?? new Settings();
    }
}
