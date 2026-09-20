using KBot.App.BotBrain;
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

var positioned = JsonSerializer.Deserialize<NativeStatus>("{\"nativeOnline\":true,\"clientFound\":true,\"pid\":4242,\"processName\":\"PokeAlliance_gl.exe\",\"readerStatus\":\"READY\",\"hasPosition\":true,\"posX\":4066,\"posY\":3458,\"posZ\":5}");
Check(positioned is { HasPosition: true, PosX: 4066, PosY: 3458, PosZ: 5 }, "Native position mapping");
Check(!status.HasPosition && status.PosX == 0 && status.ReaderStatus is null, "Missing position defaults to unavailable");

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

// ---------- BotBrain: core + modules ----------
var prof = new ProfileView(new BotProfile
{
    AutoReviveEnabled = true, ReviveItemHotkey = "F9",
    AutoMedicine = true, MedicineHotkey = "F11",
    HealPlayer = true, HealHotkey = "F12", CureAtPercent = 70,
    AttackerEnabled = true, AreaCombo = true, AutoSummon = true,
    CatchEnabled = true, LootEnabled = true
});

// Healing: fainted active -> revive hotkey first (highest priority wins the tick).
{
    var brain = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 1000, ActiveAlive = false }, prof);
    brain.Register(new HealingModule(), new TargetingModule());
    Check(brain.Tick() is { Channel: ActionChannel.Hotkey, Payload: "F9" }, "Healing revives fainted active");
}

// Healing: low hp -> medicine (before heal) and only when hp readable.
{
    var b2 = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 2000, ActiveAlive = true, ActiveHpPercent = 40 }, prof);
    b2.Register(new HealingModule());
    Check(b2.Tick() is { Channel: ActionChannel.Hotkey, Payload: "F11" }, "Healing casts medicine under threshold");

    var b3 = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 1000, ActiveAlive = true, ActiveHpPercent = null }, prof);
    b3.Register(new HealingModule());
    Check(b3.Tick() is null, "Healing skips when hp unknown");
}

// Targeting: in battle area-combo; one-by-one overrides to single.
{
    var b = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 5, InBattle = true, ActiveAlive = true }, prof);
    b.Register(new TargetingModule());
    Check(b.Tick() is { Channel: ActionChannel.Command, Payload: "attack:combo" }, "Targeting area combo in battle");

    var oneByOne = new ProfileView(new BotProfile { AttackerEnabled = true, AttackOneByOne = true });
    var bO = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 5, InBattle = true, ActiveAlive = true }, oneByOne);
    bO.Register(new TargetingModule());
    Check(bO.Tick() is { Payload: "attack:single" }, "Targeting one-by-one wins over combo");
}

// Targeting: auto-summon requests send-out when field empty and not in battle.
{
    var b = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 5, InBattle = false, ActiveAlive = false }, prof);
    b.Register(new TargetingModule());
    Check(b.Tick() is { Channel: ActionChannel.Command, Payload: "summon:0" }, "Targeting auto-summon when empty field");
}

// Route: fires waypoint action on arrival, then steps toward the next waypoint.
{
    var route = new RouteModule();
    route.Load(new[] { (10, 10, WaypointAction.Wait), (10, 14, WaypointAction.Walk) });
    route.Start();
    var b = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 0, X = 10, Y = 10, Z = 0 }, new ProfileView(new BotProfile()));
    b.Register(route);
    Check(b.Tick() is { Channel: ActionChannel.Command, Payload: "wait:300" }, "Route fires waypoint action on arrival");

    var b2 = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 0, X = 10, Y = 16, Z = 0 }, new ProfileView(new BotProfile()));
    b2.Register(route);
    Check(b2.Tick() is { Channel: ActionChannel.Move, Payload: "W" }, "Route steps toward next waypoint");
}

// Alerts: pulled flag raises a pulled alert command.
{
    var b = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 0, Pulled = true }, prof);
    b.Register(new AlertsModule());
    Check(b.Tick() is { Payload: "alert:pulled" }, "Alerts raise pulled alert");
}

// Priority ordering: healing outruns targeting when both would act.
{
    var b = new BotBrain(new FakeSink(), () => new GameState
    { ClientConnected = true, InGame = true, HasPosition = true, NowMs = 0, InBattle = true, ActiveAlive = false, ActiveHpPercent = 10 }, prof);
    b.Register(new TargetingModule(), new HealingModule());
    Check(b.Tick() is { Channel: ActionChannel.Hotkey, Payload: "F9" }, "Healing has priority over targeting");
}

// Command routing: an offset-free command (catch) resolves to its configured hotkey.
{
    var keys = new FakeKeys();
    var sink = new BrainActionSink(keys);
    var p = new ProfileView(new BotProfile { CatchEnabled = true, CatchHotkey = "F5" });
    sink.SetProfile(p);
    Check(sink.Execute(ActionIntent.Command("catch"), GameState.Empty(0)), "Command catch accepted");
    Check(keys.Hotkeys.Contains("F5"), "Command catch presses its hotkey");
}

// Command routing: an unbound command (attack) stays pending, nothing pressed.
{
    var keys = new FakeKeys();
    var sink = new BrainActionSink(keys);
    sink.SetProfile(new ProfileView(new BotProfile()));
    sink.Execute(ActionIntent.Command("attack", "combo"), GameState.Empty(0));
    Check(keys.Hotkeys.Count == 0 && sink.PendingCommand is not null, "Unbound command stays pending");
}

// Command routing: catch enabled but no key set -> nothing pressed, still handled.
{
    var keys = new FakeKeys();
    var sink = new BrainActionSink(keys);
    sink.SetProfile(new ProfileView(new BotProfile { CatchEnabled = true }));
    var ok = sink.Execute(ActionIntent.Command("catch"), GameState.Empty(0));
    Check(ok && keys.Hotkeys.Count == 0, "Catch without a binding is a no-op");
}

// Out of game: brain idles and dispatches nothing.
{
    var sinkO = new FakeSink();
    var b = new BotBrain(sinkO, () => new GameState { ClientConnected = true, InGame = false, NowMs = 0 }, prof);
    b.Register(new HealingModule(), new RouteModule());
    Check(b.Tick() is null && sinkO.Executed.Count == 0, "Brain idle out of game");
}

// Client-side (memory) InGame gate: a valid non-degenerate position read is
// proof the character is loaded, independent of the color-based vision path.
{
    var detector = new KBot.App.Services.ClientStateDetector();

    var readyPos = new KBot.App.Models.NativeStatus { Pid = 42, ReaderStatus = "READY", HasPosition = true, PosX = 120, PosY = -34, PosZ = 7 };
    var posEvidence = detector.Detect(readyPos, 42);
    Check(posEvidence.Reliable && posEvidence.State == KBot.App.Models.CharacterPresence.InGame,
        "Memory: valid position read => InGame (vision-independent)");

    var readyNoPos = new KBot.App.Models.NativeStatus { Pid = 42, ReaderStatus = "READY", HasPosition = false };
    Check(!detector.Detect(readyNoPos, 42).Reliable, "Memory: no position => not reliable");

    var readyZeroPos = new KBot.App.Models.NativeStatus { Pid = 42, ReaderStatus = "READY", HasPosition = true, PosX = 0, PosY = 0, PosZ = 0 };
    Check(!detector.Detect(readyZeroPos, 42).Reliable, "Memory: degenerate (0,0,0) => not reliable");

    var readyNotReady = new KBot.App.Models.NativeStatus { Pid = 42, ReaderStatus = "NOT_CONFIGURED", HasPosition = true, PosX = 5 };
    Check(!detector.Detect(readyNotReady, 42).Reliable, "Memory: reader not READY => not reliable");

    var explicitIngame = new KBot.App.Models.NativeStatus { Pid = 42, ReaderStatus = "READY", CharacterState = "INGAME" };
    Check(detector.Detect(explicitIngame, 42).State == KBot.App.Models.CharacterPresence.InGame,
        "Memory: explicit INGAME state wins over position");

    Check(!detector.Detect(new KBot.App.Models.NativeStatus { Pid = 43, ReaderStatus = "READY", HasPosition = true, PosX = 9 }, 42).Reliable,
        "Memory: pid mismatch => not reliable");
}

Console.WriteLine("All checks passed.");

sealed class FakeSink : IActionSink
{
    public readonly List<ActionIntent> Executed = new();
    public bool Fail = false;
    public bool Execute(ActionIntent intent, GameState state) { if (!Fail) Executed.Add(intent); return !Fail; }
}

sealed class FakeKeys : IKeySender
{
    public readonly List<string> Moves = new();
    public readonly List<string> Hotkeys = new();
    public bool TrySendMove(string direction) { Moves.Add(direction); return true; }
    public bool TrySendHotkey(string combo) { Hotkeys.Add(combo); return true; }
}
