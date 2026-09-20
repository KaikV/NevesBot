using KBot.App.Models;
using KBot.App.Services;
using System.Text.Json;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

const string legacy = """
[{"Waypoints":["{\"name\":\"Entrada\",\"position\":\"4066, 3458, 5\",\"action\":\"Walk\"}","{\"position\":\"4072, 3455, 5\",\"action\":\"Wait\"}"]}]
""";
var imported = CavebotScriptService.Parse(legacy);
Check(imported.Count == 2, "Legacy route count");
Check(imported[0].Name == "Entrada" && imported[0].X == 4066 && imported[0].Z == 5, "Legacy position");
Check(imported[1].Action == WaypointAction.Wait, "Legacy action");

var roundTrip = CavebotScriptService.Parse(CavebotScriptService.Serialize(imported));
Check(roundTrip.Count == 2 && roundTrip[0].Name == "Entrada" && roundTrip[1].Y == 3455, "KBot round trip");

var invalidRejected = false;
try { CavebotScriptService.Parse("{\"waypoints\":[{\"position\":\"bad\",\"action\":\"Walk\"}]}"); }
catch (InvalidDataException) { invalidRejected = true; }
Check(invalidRejected, "Invalid waypoint must be rejected");

var status = JsonSerializer.Deserialize<NativeStatus>("{\"nativeOnline\":true,\"clientFound\":false,\"pid\":0,\"processName\":\"PokeAlliance.exe\"}");
Check(status is { NativeOnline: true, ClientFound: false, Pid: 0, ProcessName: "PokeAlliance.exe" }, "Native status mapping");

var migratedClient = JsonSerializer.Deserialize<GameInstallation>("{\"Name\":\"PokeAlliance\",\"ExecutablePath\":\"C:\\\\Games\\\\PokeAlliance\\\\PokeAlliance.exe\",\"WorkingDirectory\":\"C:\\\\Games\\\\PokeAlliance\"}")!;
migratedClient.Normalize();
Check(migratedClient.LauncherPath == migratedClient.ExecutablePath &&
      migratedClient.InstallDirectory == migratedClient.WorkingDirectory,
      "Legacy client configuration migrates to launcher installation");

const string legacyProfile = """
[{"MonstersToAttack":["Pidgey","Rattata"],"Hotkeys":{"enabled":true,"ReviveHotkey":"Delete"},"Revive":{"enabled":true,"AutoReviveHP":40,"ReviveItemHotkey":"{F9}"},"Food":{"FoodHotkey":"{F10}"},"Alarms":{"enabled":true},"Spells":{"F1":{"enabled":true,"cooldown":15}}}]
""";
var profile = BotProfileService.Import(legacyProfile);
Check(profile.MonstersToAttack.SequenceEqual(new[] { "Pidgey", "Rattata" }), "Target priority import");
Check(profile.AutoReviveEnabled && profile.ReviveHp == 40 && profile.ReviveItemHotkey == "F9", "Revive import");
Check(profile.FoodEnabled && profile.FoodHotkey == "F10", "Food import");
Check(!BotProfileService.Import("[{\"Revive\":{\"enabled\":true}}]").FoodEnabled,
      "Missing food setting stays disabled");
Check(profile.AlertsEnabled && profile.HotkeysEnabled && profile.Spells.Count == 9 && profile.Spells[0].CooldownSeconds == 15, "Profile settings import");
profile.FishingEnabled = true;
profile.AttackerEnabled = true;
profile.FishingX = 420;
profile.FishingY = 315;
profile.FishingHotkey = "Ctrl+Z";
profile.CatchEnabled = true;
profile.CatchHotkey = "F11";
profile.LootEnabled = true;
profile.LootHotkey = "F12";
var savedProfile = BotProfileService.Parse(BotProfileService.Serialize(profile));
var importedKBotProfile = BotProfileService.Import(BotProfileService.Serialize(profile));
Check(savedProfile.MonstersToAttack.SequenceEqual(profile.MonstersToAttack) &&
      savedProfile.ReviveHp == 40 && savedProfile.Spells[0].CooldownSeconds == 15 &&
      savedProfile.AttackerEnabled && savedProfile.FoodEnabled &&
      savedProfile.FishingEnabled && savedProfile.FishingX == 420 && savedProfile.FishingY == 315 &&
      savedProfile.CatchEnabled && savedProfile.CatchHotkey == "F11" &&
      savedProfile.LootEnabled && savedProfile.LootHotkey == "F12",
      "KBot profile round trip");
Check(importedKBotProfile.FishingEnabled && importedKBotProfile.CatchHotkey == "F11" &&
      importedKBotProfile.ReviveHp == 40, "KBot profile import");

if (args.Length > 0)
{
    int files = 0, waypoints = 0;
    foreach (var file in Directory.EnumerateFiles(args[0], "*.json"))
    {
        var route = CavebotScriptService.Parse(File.ReadAllText(file));
        Check(route.Count > 0, $"Empty route: {file}");
        files++;
        waypoints += route.Count;
    }
    Console.WriteLine($"Imported {files} routes with {waypoints} waypoints.");
}

if (args.Length > 1)
{
    var realProfile = BotProfileService.Import(File.ReadAllText(args[1]));
    Check(realProfile.Spells.Count == 9, "Real profile spell count");
    Console.WriteLine($"Imported profile with {realProfile.Spells.Count} spell slots.");
}

Console.WriteLine("All checks passed.");
