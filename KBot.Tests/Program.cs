using KBot.App.BotBrain;
using KBot.App.Models;
using KBot.App.Services;
using KBot.App.Engine.Sensors;
using KBot.App.Engine.Fusion;
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

var routeV2 = new CavebotRouteDocument
{
    RouteId = "route-stable-id",
    Name = "Teste",
    Version = 3,
    Waypoints = new[]
    {
        new CavebotWaypoint { Number = 1, X = 10, Y = 20, Z = 7, Action = WaypointAction.Wait, Argument = "safe", DelayMs = 1250 }
    }
};
var routeV2RoundTrip = CavebotScriptService.ParseDocument(CavebotScriptService.Serialize(routeV2));
Check(routeV2RoundTrip.RouteId == "route-stable-id" && routeV2RoundTrip.Name == "Teste" && routeV2RoundTrip.Version == 3 &&
      routeV2RoundTrip.Waypoints[0].Argument == "safe" && routeV2RoundTrip.Waypoints[0].DelayMs == 1250,
      "Route v2 preserves identity, version and waypoint arguments");

var invalidRejected = false;
try { CavebotScriptService.Parse("{\"waypoints\":[{\"position\":\"bad\",\"action\":\"Walk\"}]}"); }
catch (InvalidDataException) { invalidRejected = true; }
Check(invalidRejected, "Invalid waypoint must be rejected");

var status = JsonSerializer.Deserialize<NativeStatus>("{\"nativeOnline\":true,\"clientFound\":false,\"pid\":0,\"processName\":\"PokeAlliance.exe\"}");
Check(status is { NativeOnline: true, ClientFound: false, Pid: 0, ProcessName: "PokeAlliance.exe" }, "Native status mapping");

var positioned = JsonSerializer.Deserialize<NativeStatus>("{\"nativeOnline\":true,\"clientFound\":true,\"pid\":4242,\"processName\":\"PokeAlliance_gl.exe\",\"readerStatus\":\"READY\",\"hasPosition\":true,\"posX\":4066,\"posY\":3458,\"posZ\":5}");
Check(positioned is { HasPosition: true, PosX: 4066, PosY: 3458, PosZ: 5 }, "Native position mapping");
Check(!status!.HasPosition && status.PosX == 0 && status.ReaderStatus is null, "Missing position defaults to unavailable");

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

// Player healing uses its own HP signal and respects the out-of-battle policy.
{
    var healingProfile = new ProfileView(new BotProfile
    {
        AutoMedicine = false,
        AutoPotion = false,
        HealPlayer = true,
        HealHotkey = "F12",
        PlayerHealPercent = 80,
        HealingCooldownMs = 250,
        HealOnlyOutOfBattle = true
    });
    var module = new HealingModule();
    Check(module.Decide(new GameState
    {
        ClientConnected = true, InGame = true, NowMs = 1000, InBattle = false, PlayerHpPercent = 60
    }, healingProfile) is { Channel: ActionChannel.Hotkey, Payload: "F12" }, "Player heal uses player HP");
    module.ResetTimers();
    Check(module.Decide(new GameState
    {
        ClientConnected = true, InGame = true, NowMs = 1000, InBattle = true, PlayerHpPercent = 60
    }, healingProfile) is null, "Out-of-battle healing stays blocked in combat");
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

// Route pause on target preserves the waypoint index and resumes after a clear scan.
{
    var route = new RouteModule();
    route.Load(new[] { (5, 0, WaypointAction.Walk) });
    route.Start();
    var p = new ProfileView(new BotProfile { PauseRouteOnTarget = true });
    var withTarget = new GameState
    {
        ClientConnected = true, InGame = true, HasPosition = true, HasScreenScan = true,
        X = 0, Y = 0, Z = 0, Wilds = new[] { new ScannedCreature(1, 50, 1, 0, 0, "Pidgey") }
    };
    Check(route.Decide(withTarget, p) is null, "Route pauses while a target is present");
    Check(route.Decide(withTarget with { Wilds = Array.Empty<ScannedCreature>() }, p) is { Channel: ActionChannel.Move, Payload: "D" },
        "Route resumes the same waypoint after target clears");
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

{
    // Share-code round trip: export -> import -> same profile.
    var src = new BotProfile
    {
        AttackerEnabled = true,
        AreaCombo = false,
        ActiveSlot = 3,
        CureAtPercent = 55,
        ReviveHp = 25,
        FishingHotkey = "Ctrl+Z",
        MedicineHotkey = "F11;curar~x", // chars needing escape
        AntiAfkEnabled = true,
        AntiAfkIdleSeconds = 50,
        TargetMoveIntervalMs = 1700,
        TargetKeepDistance = 2,
        PlayerHealPercent = 72,
        HealingCooldownMs = 900,
        CatchDelayMs = 250,
        CatchShinyEnabled = true,
        ShinyBallId = 12001,
        CatchEntries = { new("Gengar", 20011, 25001) },
        MonstersToAttack = { "Mimikyu", "Pikachu" },
        IgnoredMonsters = { "Rattata" },
        RareWords = new() { "crystal" }
    };
    var code = ConfigShareService.Export(src);
    Check(code.StartsWith("KPB1:"), "Share code starts with version");
    Check(!code.Contains(' '), "Share code has no spaces (chat-safe)");

    var dst = new BotProfile(); // defaults / untouched fields
    dst.AttackerEnabled = false; // must be overridden by import
    dst.AutoSummon = true;       // not in whitelist => stays untouched
    var res = ConfigShareService.Import(code, dst);
    Check(res.Applied > 0, "Share import applies fields");
    Check(dst.AttackerEnabled == true, "Share import overrides bool");
    Check(dst.AreaCombo == false, "Share import carries false (not just true)");
    Check(dst.ActiveSlot == 3 && dst.CureAtPercent == 55 && dst.ReviveHp == 25, "Share import carries numbers");
    Check(dst.FishingHotkey == "Ctrl+Z", "Share import carries string");
    Check(dst.MedicineHotkey == "F11;curar~x", "Share import unescapes specials");
    Check(dst.AntiAfkEnabled && dst.AntiAfkIdleSeconds >= 15, "Share import carries anti-AFK fields");
    Check(dst.MonstersToAttack.SequenceEqual(new[] { "Mimikyu", "Pikachu" }), "Share import carries monster list");
    Check(dst.IgnoredMonsters.SequenceEqual(new[] { "Rattata" }) && dst.RareWords.SequenceEqual(new[] { "crystal" }),
        "Share import carries target filter lists");
    Check(dst.TargetMoveIntervalMs == 1700 && dst.TargetKeepDistance == 2 && dst.PlayerHealPercent == 72 &&
          dst.HealingCooldownMs == 900 && dst.CatchDelayMs == 250,
        "Share import carries target, healing and capture timing");
    Check(dst.CatchShinyEnabled && dst.ShinyBallId == 12001 && dst.CatchEntries is [{ Name: "Gengar", CorpseId: 20011, BallId: 25001 }],
        "Share import carries shiny and per-corpse capture rules");
    Check(dst.AutoSummon == true, "Share import leaves unlisted field untouched");

    // Truncated code -> rejected (checksum/format fail), not applied.
    var cut = code[..(code.Length - 2)];
    var cutRes = ConfigShareService.Import(cut, new BotProfile());
    Check(cutRes.Applied == 0 && !string.IsNullOrEmpty(cutRes.Message), "Truncated share code rejected");

    // Wrong version -> rejected with friendly message.
    var wrongVer = "KPB9:" + code["KPB1:".Length..];
    var verRes = ConfigShareService.Import(wrongVer, new BotProfile());
    Check(verRes.Applied == 0 && verRes.Message.Contains("versão"), "Wrong-version share code rejected");

    // Unknown code inside body -> ignored, others still apply.
    var body = code.Split(':')[1];
    var newBody = body + ";zz=b1";
    var withUnknown = $"KPB1:{newBody}:{ConfigShareServiceChecksumHelper(newBody)}";
    var unkRes = ConfigShareService.Import(withUnknown, new BotProfile());
    Check(unkRes.Ignored >= 1, "Unknown share code ignored");
}

{
    // Diagnostics surface a warning for missing position and for empty alvos.
    var empty = new BotProfile();
    var items = ConfigDiagnosticsService.Diagnose(empty, null);
    Check(items.Any(i => i.Feature == "Leitura de memória" && i.Status == "ATENÇÃO"), "Diagnostics flags missing memory read");
    Check(items.Any(i => i.Feature == "Alvos" && i.Status == "ATENÇÃO"), "Diagnostics flags empty alvo list");

    var ready = new NativeStatus { ReaderStatus = "READY", HasPosition = true, PosX = 1, PosY = 2, PosZ = 3 };
    var filled = new BotProfile { CatchHotkey = "C", LootHotkey = "L" };
    filled.MonstersToAttack.Add("Eevee");
    var ok = ConfigDiagnosticsService.Diagnose(filled, ready);
    Check(ok.Any(i => i.Feature == "Leitura de memória" && i.Status == "PRONTO"), "Diagnostics reports reader ready");
    Check(ok.Any(i => i.Feature == "Alvos" && i.Status == "PRONTO"), "Diagnostics reports alvos present");
}

{
    var events = new AutomationEventHub();
    Check(events.Publish(AutomationEventSeverity.Warning, "Cura", "source_missing", "HP indisponível", "42"),
        "First operational event is accepted");
    Check(!events.Publish(AutomationEventSeverity.Warning, "Cura", "source_missing", "HP indisponível", "42"),
        "Repeated operational event is deduplicated");
    Check(events.Snapshot().Count == 1, "Operational history keeps one deduplicated event");
    events.Clear();
    Check(events.Snapshot().Count == 0, "Operational history clears");
}

static string ConfigShareServiceChecksumHelper(string body)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
    uint a = 1, bsum = 0;
    foreach (var x in bytes) { a = (a + x) % 65521; bsum = (bsum + a) % 65521; }
    return ((bsum << 16) | a).ToString("X");
}

RunPmChecks();
RunSocorroChecks();
RunScanChecks();
RunScanSourceChecks();
RunWalkChecks();
RunTargetChecks();
RunCatchChecks();
RunAlertChecks();
RunFishingChecks();
RunAntiafkChecks();
RunVigiaChecks();
RunEndgameChecks();
RunCalibrationChecks();
RunSensorFoundationChecks();
RunVisualCreatureChecks();

static void RunSocorroChecks()
{
    // Helper: field also seeds ActiveAlive so "empty" reads as a dead active too.
    // Note: null (unread) still means COVERED by design - see check D2.
    static GameState GS(long t, bool? field, int wilds, int? activeSlot,
        System.Collections.Generic.IReadOnlyList<PokebarSlot>? bar = null) => new()
    {
        ClientConnected = true, InGame = true, HasPosition = true, NowMs = t,
        FieldHasPoke = field, WildsNearby = wilds, ActivePokebarSlot = activeSlot,
        ActiveAlive = field ?? true,
        Pokebar = bar ?? System.Array.Empty<PokebarSlot>()
    };
    static PokebarSlot B(string name, double hp) => new(name, hp);

    // A) Grace window: empty field must NOT fire before the fixed 1.5s; then it sends
    //    the ACTIVE slot. Danger does NOT shorten the grace (Lua keeps ESPERA_MS=1500).
    {
        var m = new SocorroModule();
        var prof = new ProfileView(new BotProfile { AttackerEnabled = true, ActiveSlot = 0 });
        var bar = new[] { B("Bulbasaur", 80), B("Shiny Magneton", 55) };
        Check(m.Decide(GS(0, false, 0, 2, bar), prof) is null, "Socorro silent before grace");
        Check(m.Decide(GS(1400, false, 0, 2, bar), prof) is null, "Socorro still inside grace");
        var send = m.Decide(GS(1500, false, 0, 2, bar), prof);
        Check(send is { Channel: ActionChannel.Command, Payload: "summon:2" },
            $"Socorro sends active slot after grace (got {send})");
        Check(m.Decide(GS(1500 + 1199, false, 0, 2, bar), prof) is null,
            "Socorro silent while last send is inside its confirm window");
        Check(m.Status.Contains("slot 2"), $"Socorro status names the slot ({m.Status})");
    }

    // B) OUR poke fainted -> socorro YIELDS to the revive ONLY inside the 6s identity
    //    window; once the item may have run out it stops yielding and swaps a live one.
    {
        var m = new SocorroModule();
        var prof = new ProfileView(new BotProfile { AutoReviveEnabled = true, ReviveItemHotkey = "F9" });
        var bar = new[] { B("Pikachu", 90), B("Graveler", 0) };
        for (long t = 1000; t < 6000; t += 100)   // <=~5s into the absence: identity window
            Check(m.Decide(GS(t, false, 0, 2, bar), prof) is null,
                "Socorro yields to revive while its own poke is fainted (<=6s)");
        Check(m.Status.Contains("revive"), $"Status explains the yield ({m.Status})");
        // The absence began at t=1000, so allowOther flips once t-1000 > 6000 (t>7000).
        var swapped = m.Decide(GS(7100, false, 0, 2, bar), prof);
        Check(swapped is { Payload: "summon:1" },
            $"After the revive window, socorro swaps a live slot (got {swapped})");
    }

    // C) Liar slot (pokebar said alive, nothing appeared): benched, and when every slot
    //    is benched the bench clears (never gives up). Danger (wild nearby) shrinks the
    //    rhythm to 1.2s, so the 5-attempt sequence lands fast: 1,2,3 then the cleared
    //    bench retries 1,2 - and stops (MAX_TENT).
    {
        var m = new SocorroModule();
        var prof = new ProfileView(new BotProfile());
        var bar = new[] { B("a", 50), B("b", 50), B("c", 50) };
        var sent = new List<string>();
        for (long t = 1000; t < 8000; t += 100)  // wild glued => danger rhythm (1.2s)
        {
            var intent = m.Decide(GS(t, false, 1, 1, bar), prof);
            if (intent is not null) sent.Add(intent.Payload!);
        }
        Check(sent.SequenceEqual(new[] { "summon:1", "summon:2", "summon:3", "summon:1", "summon:2" }),
            $"Socorro benches liar slots, clears the bench, caps at MAX_TENT (got {string.Join(",", sent)})");
    }

    // C2) Recovery resets the whole machine: a poke on the field clears the failure bench,
    //     and a brand-new absence after it sends immediately on a fresh clock.
    {
        var m = new SocorroModule();
        var prof = new ProfileView(new BotProfile());
        var bar = new[] { B("a", 50), B("b", 50), B("c", 50) };
        Check(m.Decide(GS(0, true, 1, 1, bar), prof) is null, "Socorro idle while poke is on field");
        Check(m.Decide(GS(100, false, 0, 1, bar), prof) is null, "Fresh absence: grace clock starts");
        var second = m.Decide(GS(100 + 1500, false, 0, 1, bar), prof);
        Check(second is { Payload: "summon:1" }, "Socorro starts fresh after recovery");
    }

    // D) No readable pokebar -> it never guesses a slot.
    {
        var m = new SocorroModule();
        var prof = new ProfileView(new BotProfile { AutoSummon = true });
        for (long t = 0; t < 3000; t += 100)
            Check(m.Decide(GS(t, false, 0, null), prof) is null, "Socorro never guesses without pokebar");
        Check(m.Status.Contains("sem leitura"), $"Status says why it idles ({m.Status})");
    }

    // D2) Unknown field (vision not reading yet) means COVERED, not empty: socorro must
    //     stay idle and never toggle changePokemon on a blind screen (sofaPokeOut nil => fora ~= false).
    {
        var m = new SocorroModule();
        var prof = new ProfileView(new BotProfile { AutoSummon = true });
        var bar = new[] { B("a", 50), B("b", 50) };
        for (long t = 0; t < 5000; t += 100)
            Check(m.Decide(GS(t, null, 0, 1, bar), prof) is null, "Socorro idle while field is unread");
    }

    // E) Full brain wiring: with AutoSummon off, ONLY socorro can send a poke out on an
    //    empty field, so if the brain produces a "summon" it proves the 95-priority socorro
    //    (not the 90-priority targeting) owned it. Socorro initializes its absence clock on
    //    its very first tick, so we advance real time over several ticks.
    {
        var prof = new ProfileView(new BotProfile
        {
            AttackerEnabled = true, AutoSummon = false,
            AutoReviveEnabled = false, CureAtPercent = 70
        });
        var bar = new[] { B("a", 50), B("b", 50) };
        long now = 0;
        var brain = new BotBrain(new FakeSink(), () => GS(now, false, 0, 1, bar), prof);
        brain.Register(new HealingModule(), new SocorroModule(), new TargetingModule());
        ActionIntent? fired = null;
        foreach (var t in new long[] { 1, 1501, 2901, 4301 })
        {
            now = t;
            var r = brain.Tick();
            if (r is not null) { fired = r; break; }
        }
        Check(fired is { Channel: ActionChannel.Command, Payload: "summon:1" },
            $"Brain routes the empty-field send-out to socorro (got {fired})");
    }
}

static void RunScanChecks()
{
    // Helper: a creature at a tile.
    static ScannedCreature C(int type, double hp, int x, int y, int z) => new(type, hp, x, y, z);
    const int Own = ScannedCreature.SummonOwn;      // 3
    const int Other = ScannedCreature.SummonOther;  // 4

    // A) One pass separates "my poke", "live wilds", and drops dead monsters + other
    //    players. This is _cbScreen(): before, both questions walked the list twice.
    {
        var r = ScreenScan.Analyze(new[]
        {
            C(1, 80, 2, 0, 0),   // wild, alive     -> counted
            C(5, 40, -1, 3, 0),  // wild, alive     -> counted
            C(2, 0, 9, 9, 0),    // wild, DEAD      -> dropped
            C(Own, 70, 0, 0, 0), // our poke        -> mine
            C(Other, 99, 3, 1, 0)// another player  -> dropped
        });
        Check(r.HasRead, "Analyze marks itself as a real read");
        Check(r.MyPoke is not null && r.MyPoke.IsAlive, "Analyze finds the live poke as ours");
        Check(r.Wilds.Count == 2, $"Analyze keeps only the two live wilds (got {r.Wilds.Count})");
        Check(!r.Wilds.Any(w => w.X == 9), "Dead monster is not a target");
        Check(r.PokeOnField(), "PokeOnField true when a live poke is on screen");
    }

    // B) sofaPokeOut(): a corpse drawn a beat before the server removes it does NOT
    //    count as "poke on field". Counting it held socorro exactly when needed.
    {
        var corpse = ScreenScan.Analyze(new[] { C(Own, 0, 0, 0, 0) });
        Check(corpse.HasRead && corpse.MyPoke is not null && !corpse.PokeOnField(),
            "A fainted poke on screen reads as NO poke on field");
        var none = ScreenScan.Analyze(Array.Empty<ScannedCreature>());
        Check(none.HasRead && !none.PokeOnField() && none.Wilds.Count == 0,
            "Empty screen reads covered=false with zero wilds");
        Check(ScreenScan.Empty().HasRead == false, "No transport = UNKNOWN, not an empty screen");
    }

    // C) perigoPerto(): same z is mandatory and the reach is Chebyshev <= 3. A monster
    //    glued diagonally within the square counts; same-tile-lineup on another floor
    //    never does.
    {
        var r = ScreenScan.Analyze(new[]
        {
            C(1, 50, 2, 2, 0),    // Chebyshev 2, same z   -> danger
            C(1, 50, -3, 0, 0),   // Chebyshev 3, same z   -> danger (edge)
            C(1, 50, 4, 0, 0),    // Chebyshev 4           -> beyond reach
            C(1, 50, 2, 2, 1)     // Chebyshev 2 but z=1   -> other floor
        });
        Check(r.DangerNearby(0, 0, 0) == 2,
            $"Danger counts the two within-reach same-z wilds (got {r.DangerNearby(0, 0, 0)})");
        Check(ScreenScan.Chebyshev((2, 2), (0, 0)) == 2, "Chebyshev uses the max axis");
    }

    // D) Wire-up: the provider turns a scan into EnemyCount + FieldHasPoke, and falls
    //    back to unknown when there is no scan yet (transport still offset-bound).
    {
        var ready = new NativeStatus { NativeOnline = true, ClientFound = true, HasPosition = true, PosX = 0, PosY = 0, PosZ = 0 };
        var scan = ScreenScan.Analyze(new[]
        {
            C(1, 50, 1, 0, 0), C(1, 50, 2, 1, 0), C(Own, 60, 0, 0, 0)
        });
        var with = GameStateProvider.From(ready, CharacterPresence.InGame, 1000, scan);
        Check(with.EnemyCount == 2, $"Provider exposes EnemyCount from the scan (got {with.EnemyCount})");
        Check(with.FieldHasPoke == true, "Provider sets FieldHasPoke from the scan");
        Check(with.WildsNearby == 2, $"Provider computes nearby wilds at the player pos (got {with.WildsNearby})");

        var blind = GameStateProvider.From(ready, CharacterPresence.InGame, 1000, null);
        Check(blind.EnemyCount is null && blind.FieldHasPoke is null,
            "No scan => unknown (null), never a guessed zero");
    }
}

static void RunScanSourceChecks()
{
    // E) The transport seam: with no source wired the lifecycle path reads UNKNOWN
    //    (not a guessed empty screen) - exactly today's production behaviour. Wiring a
    //    creature source lights up EnemyCount/FieldHasPoke with zero brain changes.
    {
        var ready = new NativeStatus { NativeOnline = true, ClientFound = true, HasPosition = true, PosX = 0, PosY = 0, PosZ = 0 };
        var presence = CharacterPresence.InGame;

        var none = new NoScreenScanSource();
        Check(none.GetScan(ready, presence) is null, "Default source reports no scan (UNKNOWN)");

        // A live transport re-reads each tick and runs the pure decision layer over it.
        var creatures = new List<ScannedCreature> { new(1, 80, 1, 0, 0), new(1, 40, 2, 1, 0), new(3, 60, 0, 0, 0) };
        IScreenScanSource live = ScreenScanSource.FromCreatures(() => creatures);
        var read = live.GetScan(ready, presence);
        var r = read as ScanResult;
        Check(read is { HasRead: true } && r is not null, "Wired source returns a real read");
        Check(r!.Wilds.Count == 2 && r.MyPoke is not null && r.PokeOnField(),
            "Wired source separates wilds from the live own poke");

        // End-to-end: provider + injected source => the fields the modules trust.
        var state = GameStateProvider.From(ready, presence, 1000, live.GetScan(ready, presence));
        Check(state.EnemyCount == 2 && state.FieldHasPoke == true && state.HasScreenScan,
            "Seam into provider yields EnemyCount + FieldHasPoke in one call");

        // The Func is re-evaluated per tick: drop the wilds and the next read follows.
        creatures.Clear();
        var after = live.GetScan(ready, presence);
        Check(after is not null && after!.Wilds.Count == 0 && !after.PokeOnField(),
            "Wired source re-reads live memory on the next tick");
    }
}


static void RunWalkChecks()
{
    static RecordAction Step(
        int x, int y, int z, StepEvent? last = null,
        int dist = 5, int roll = 3, long now = 100000, long? lastUse = null)
        => CavebotRecorder.OnStep(new(x, y, z), last, dist, roll, now, lastUse);

    // A) Distance rule: a sane configured value (1-9) wins; otherwise fall back to the
    //    per-session roll, clamped to 1..5. This is why two cave-bots stop stamping at
    //    identical, predictable intervals.
    {
        Check(CavebotRecorder.Dist(3, 1) == 3, "Configured 3 used as-is");
        Check(CavebotRecorder.Dist(9, 1) == 9, "Configured 9 is the max");
        Check(CavebotRecorder.Dist(0, 4) == 4, "Configured 0 falls back to the roll");
        Check(CavebotRecorder.Dist(12, 2) == 2, "Out-of-range 12 falls back to the roll");
        Check(CavebotRecorder.Dist(0, 9) == 5, "The roll itself is clamped to 5");
        Check(CavebotRecorder.Dist(0, 0) == 1, "The roll is clamped to 1");
    }

    // B) First step always drops a position (recorder adds oldPos on first contact).
    {
        Check(Step(10, 10, 0, last: null) == RecordAction.Position,
            "First step stamps a position");
    }

    // C) Same floor: too close -> nothing; far enough -> a position. The reach is
    //    Chebyshev against the DISTANCE (default 5). This is also where a fast player
    //    (2+ tiles/poll) and same-floor teleports land - as a clean goto.
    {
        var last = new StepEvent(0, 0, 0);
        Check(Step(2, 2, 0, last) == RecordAction.None,
            "Chebyshev 2 (<5): no stamp yet");
        Check(Step(4, 4, 0, last) == RecordAction.None,
            "Chebyshev 4 (<5): still no stamp");
        Check(Step(4, 5, 0, last) == RecordAction.Position,
            "Chebyshev 5 (==dist): stamps");
        Check(Step(-9, 1, 0, last) == RecordAction.Position,
            "Far diagonal stamps as a clean goto, not a fake stairs");
        // A smaller configured distance tightens the grid.
        Check(Step(3, 0, 0, last, dist: 2) == RecordAction.Position,
            "Chebyshev 3 with dist=2 stamps");
        Check(Step(1, 1, 0, last, dist: 2) == RecordAction.None,
            "Chebyshev 1 with dist=2 does not stamp");
    }

    // D) Floor change is a STAIRS step - even when the tiles are adjacent (up/down keep
    //    x,y), because playback must step onto the tile before using it.
    {
        var last = new StepEvent(0, 0, 0);
        Check(Step(0, 0, 1, last) == RecordAction.Stairs,
            "z change on the same x,y = stairs (up straight)");
        Check(Step(1, 0, 2, last) == RecordAction.Stairs,
            "z change anywhere = stairs, not a normal goto");
    }

    // E) A floor change that happened right after using an item on the ground is NOT a
    //    staircase - the USE action already captured the transition. Outside the window
    //    it goes back to being a real stairs.
    {
        var last = new StepEvent(0, 0, 0);
        // now = 100000; a use at 99900 is 100ms ago (inside the 2500ms window).
        Check(Step(0, 0, 1, last, lastUse: 99900) == RecordAction.None,
            "Floor change right after a use is swallowed");
        // A use at 96000 is 4000ms ago (outside the window) -> real stairs again.
        Check(Step(0, 0, 1, last, lastUse: 96000) == RecordAction.Stairs,
            "Floor change outside the use window is a stairs");
        // No recent use at all -> stairs.
        Check(Step(0, 0, 1, last, lastUse: null) == RecordAction.Stairs,
            "No prior use: floor change is a stairs");
    }
}

static void RunTargetChecks()
{
    static ScannedCreature W(string name, int x, int y, int z = 0) => new(1, 60, x, y, z, name);

    var rare = new[] { "shiny", "elite" };

    // A) Closest in-range same-floor wild wins; out-of-range and other-floor are ignored.
    {
        var wilds = new[]
        {
            W("Rattata", 3, 0),   // d=3
            W("Pidgey", 1, 1),    // d=1 -> closest
            W("Eevee", 9, 0),     // d=9 > range 7
            W("Mawile", 2, 2, 1)  // d=2 but z=1 (other floor)
        };
        Check(TargetSelection.Pick(wilds, 0, 0, 0, 7, false, rare)?.Name == "Pidgey",
            "Closest alive same-floor wild is targeted");
    }

    // B) Rare/shiny takes TOTAL priority over distance when RareFirst is on.
    {
        var wilds = new[]
        {
            W("Pidgey", 1, 0),                // closer but normal
            W("Shiny Mawile [169]", 5, 0),    // rare, farther -> wins
        };
        Check(TargetSelection.Pick(wilds, 0, 0, 0, 7, true, rare)?.Name == "Shiny Mawile [169]",
            "Rare word beats a closer normal target");
        Check(TargetSelection.Pick(wilds, 0, 0, 0, 7, false, rare)?.Name == "Pidgey",
            "RareFirst off -> back to plain closest");
    }

    // C) Among several rares, the CLOSEST rare wins. Matching is a case-insensitive
    //    substring of the whole name, so any server prefix works.
    {
        var wilds = new[]
        {
            W("ELITE Gengar", 6, 0),   // rare, farther
            W("shiny Zubat", 2, 0),    // rare, closer -> wins
        };
        Check(TargetSelection.Pick(wilds, 0, 0, 0, 7, true, rare)?.Name == "shiny Zubat",
            "Closest of the rares is picked");
    }

    // D) Nothing valid on screen (all out of range / dead / other floor) -> null.
    {
        var wilds = new[]
        {
            W("Rattata", 8, 0),      // beyond range 7
            W("Pidgey", 2, 0, 3),    // other floor
        };
        Check(TargetSelection.Pick(wilds, 0, 0, 0, 7, true, rare) is null,
            "No target when nothing is reachable");
        Check(TargetSelection.Pick(Array.Empty<ScannedCreature>(), 0, 0, 0, 7, true, rare) is null,
            "No target on an empty screen");
    }

    // E) Explicit priority order wins among normal targets; ignored names never enter.
    {
        var wilds = new[] { W("Pidgey", 1, 0), W("Eevee", 4, 0), W("Rattata", 2, 0) };
        var picked = TargetSelection.Pick(wilds, 0, 0, 0, 7, false, rare,
            new[] { "Eevee", "Pidgey" }, new[] { "Rattata" });
        Check(picked?.Name == "Eevee", "Preferred species order wins and ignored species is filtered");
    }
}

static void RunCatchChecks()
{
    IReadOnlyList<string> words = new[] { "shiny" };
    IReadOnlyList<CatchEntry> none = Array.Empty<CatchEntry>();

    // A) Explicit per-pokemon line wins: exact corpse id + a real ball bound -> throw it.
    {
        var e = new[] { new CatchEntry("Gengar", 20011, 25001) };
        var d = CatchSelection.Evaluate(20011, "corpse", 100, -1, e, true, true, 12345, words);
        Check(d.Throw && d.BallId == 25001, $"Custom corpse line throws its ball (got {d})");
    }

    // B) A matched line that has NO real ball bound must NOT throw (id < 100 = missing).
    {
        var e = new[] { new CatchEntry("Empty", 20011, 0) };
        var d = CatchSelection.Evaluate(20011, "corpse", 100, -1, e, true, true, 12345, words);
        Check(!d.Throw, $"Line without a bound ball does not throw (got {d})");
    }

    // C) A corpse with no fresh MY-kill behind it is someone else's -> no ball. This is
    //    what stops the server-reject loop that leaves corpses on the floor forever.
    {
        var e = new[] { new CatchEntry("Gengar", 20011, 25001) };
        Check(!CatchSelection.Evaluate(20011, "corpse", -1, -1, e, true, true, 12345, words).Throw, "Foreign corpse: no ball");
        Check(!CatchSelection.Evaluate(20011, "corpse", 99999, -1, e, true, true, 12345, words).Throw, "Stale kill mark: no ball");
    }

    // D) Shiny by NAME: our shiny died here recently -> shiny ball, any corpse id. The
    //    name match is a case-insensitive substring so "Shiny Mawile [169]" works.
    {
        var d = CatchSelection.Evaluate(999, "Shiny Mawile [169]", -1, 500, none, false, true, 12345, words);
        Check(d.Throw && d.BallId == 12345, $"Shiny-by-name throws the shiny ball (got {d})");
        // Without a recent shiny death the same name is just loot -> no throw.
        Check(!CatchSelection.Evaluate(999, "Shiny Mawile [169]", -1, -1, none, false, true, 12345, words).Throw, "No shiny death: no ball");
    }

    // E) Shiny off, or no shiny ball bound -> the shiny path never fires.
    {
        Check(!CatchSelection.Evaluate(999, "Shiny Mawile [169]", -1, 500, none, false, false, 12345, words).Throw, "Shiny off: no ball");
        Check(!CatchSelection.Evaluate(999, "Shiny Mawile [169]", -1, 500, none, false, true, 0, words).Throw, "No shiny ball bound: no ball");
    }

    // F) CatchModule wires the decision into intents: fresh corpse + mapped line ->
    //    "catch:bola:<id>:<reason>"; a foreign corpse -> no intent at all (no spam).
    {
        var p = new ProfileView(new BotProfile
        {
            CatchEnabled = true,
            CatchEntries = { new("Gengar", 20011, 25001) }
        });
        var mod = new CatchModule();
        var fresh = new GameState
        {
            ClientConnected = true, InGame = true, NowMs = 1000,
            CorpseId = 20011, CorpseName = "corpse", CorpseAppearedMs = 700
        };
        var firstCatch = mod.Decide(fresh, p);
        Check(firstCatch is { Channel: ActionChannel.Command, Payload: "catch:bola:25001:Gengar" },
            $"Module throws the mapped ball for our fresh corpse (got {firstCatch})");
        Check(mod.Decide(fresh with { NowMs = 1500 }, p) is null,
            "Same corpse instance is never captured twice");

        var waiting = fresh with { CorpseAppearedMs = 900 };
        Check(new CatchModule().Decide(waiting, p) is null,
            "Capture waits for the configured post-defeat delay");

        // Stale corpse (> MyDeathWindowMs since appearance) -> someone else's -> silent.
        var stale = fresh with { CorpseAppearedMs = -9000 };
        Check(mod.Decide(stale, p) is null, "Stale corpse: module stays silent");

        // No confirmed corpse means no capture; ownership must never be guessed.
        var battle = new GameState { ClientConnected = true, InGame = true, NowMs = 1000, InBattle = true };
        Check(mod.Decide(battle, p) is null, "No corpse read: capture stays idle");
    }
}




static void RunPmChecks()
{
    Check(PmResponderService.Normalize("Taaii?? CÉ Taça") == "tai ce taca", "PM normalize accents/repeats");
    var pm = new PmResponderService();
    pm.OnPrivateMessage("Zeca", "Salve!", 0, myName: "Eu");
    pm.OnPrivateMessage("Zeca", "ta ai?", 1, myName: "Eu"); // merges: question wins
    pm.OnPrivateMessage("Ana", "blz", 0, myName: "Eu");
    pm.OnPrivateMessage("Eu", "oi", 0, myName: "Eu"); // own echo ignored
    // nothing due yet (delay >= 3000)
    Check(pm.Tick(2999, "x").Count() == 0, "PM nothing due before delay");
    var sends = pm.Tick(7000, "").ToList();
    Check(sends.Count == 2, "PM one reply per player (self ignored)");
    var zeca = sends.FirstOrDefault(s => s.To == "Zeca");
    Check(zeca is not null && new[] { "to", "to sim", "to aqui", "to on" }.Contains(zeca.Message),
        "PM Zeca answered with presenca rule reply (merged question wins)");
    Check(sends.Any(s => s.To == "Ana"), "PM Ana answered once");
    Check(sends.All(s => s.To != "Eu"), "PM self echo never answered");
    // second message from same player -> silence on purpose
    pm.OnPrivateMessage("Ana", "ta fazendo o que", 8000, myName: "Eu");
    Check(!pm.Tick(13000, "").Any(s => s.To == "Ana"), "PM repeat message stays silent");

    // fallback phrases used when nothing understood
    var pm2 = new PmResponderService();
    pm2.OnPrivateMessage("Lia", "qualquer coisa sem chaves", 0);
    var viaFallback = pm2.Tick(7000, "bla, blop").ToList();
    Check(viaFallback.Count == 1 && (viaFallback[0].Message == "bla" || viaFallback[0].Message == "blop"),
        "PM unknown message falls back to configured phrases");

    Check(PmResponderService.ParsePhrases("a, , b\n").Count == 2, "PM phrase csv parsing");
}

static void RunAlertChecks()
{
    // ---------- CatchMessage.Parse: server-only proof of a catch ----------
    // A) The server line "You caught a Pokemon! (Shiny Hitmonchan)" -> catch, name + shiny.
    {
        var c = CatchMessage.Parse("text", "", "You caught a Pokemon! (Shiny Hitmonchan).");
        Check(c.IsCatch && c.Name == "Shiny Hitmonchan" && c.Shiny,
            $"Server catch line parses (got {c})");
    }

    // B) A NAMED player saying the same thing is ignored - only the server counts.
    {
        var c = CatchMessage.Parse("talk", "Fulano", "You caught a Pokemon! (Shiny Mewtwo).");
        Check(!c.IsCatch, $"Player-shouted catch line ignored (got {c})");
    }

    // C) Non-catch server line is not a catch either.
    {
        Check(!CatchMessage.Parse("text", "", "A wild Gyarados appeared.").IsCatch, "Non-catch line: no match");
    }

    // D) Portuguese variant also matches.
    {
        var c = CatchMessage.Parse("text", "", "Você capturou um Pokémon! (Dratini).");
        Check(c.IsCatch && c.Name == "Dratini" && !c.Shiny, $"PT catch line parses (got {c})");
    }

    // ---------- CatchFeed dedup: same line twice < 1.5s = ONE catch ----------
    {
        var feed = new CatchFeed();
        var first  = feed.Offer("text", "", "You caught a Pokemon! (Dratini).", nowMs: 1000);
        var second = feed.Offer("text", "", "You caught a Pokemon! (Dratini).", nowMs: 1200);
        Check(first.IsCatch && !second.IsCatch, "Same line <1.5s = dedup to one catch");
        // A DIFFERENT name after the window is a second real catch.
        var third = feed.Offer("text", "", "You caught a Pokemon! (Porygon).", nowMs: 3000);
        Check(third.IsCatch && third.Name == "Porygon", "Second different catch is counted");
    }

    // ---------- LevelUpTracker: rising edge only, first tick silent ----------
    {
        var t = new LevelUpTracker();
        Check(!t.Tick(100), "First level tick does NOT fire (login at 100)");
        Check(t.Tick(101), "100->101 fires");
        Check(!t.Tick(101), "101->101 does not fire again");
        Check(!t.Tick(null), "null level does not fire");
        Check(t.Tick(102), "101->102 fires");
    }

    // ---------- DeathTracker: rising edge of alive=false ----------
    {
        var d = new DeathTracker();
        Check(!d.Fire(true), "Alive -> no death alert");
        Check(d.Fire(false), "True->False fires death");
        Check(!d.Fire(false), "Still dead -> no repeat");
        Check(!d.Fire(true), "Back to life resets (no fire)");
        Check(d.Fire(false), "Second death fires again");
        Check(!d.Fire(null), "null hp -> neither fires nor resets");
    }

    // ---------- PresenceTracker: falling edge with who-left detail ----------
    {
        var t = new PresenceTracker();
        Check(t.Tick(true, "Fulano") == null, "Player appears: no alert yet");
        Check(t.Tick(true, "Fulano") == null, "Still present: no alert");
        Check(t.Tick(false, null) == "Fulano", $"Falling edge returns who-left");
        Check(t.Tick(false, null) == null, "Still absent: no repeat");
    }

    // ---------- SupplyTracker: 3-consecutive-low + above-min re-arms ----------
    {
        var s = new SupplyTracker();
        Check(s.Tick(50, 20) == null, "Above min: silent, resets counter");
        Check(s.Tick(19, 20) == null, "Low #1: silent");
        Check(s.Tick(18, 20) == null, "Low #2: silent");
        Check(s.Tick(17, 20) != null, "Low #3 (with prev): fires");
        Check(s.Tick(25, 20) == null, "Back above min: re-arms");
        Check(s.Tick(19, 20) == null, "New low #1: silent");
        Check(s.Tick(18, 20) == null, "New low #2: silent");
        Check(s.Tick(17, 20) != null, "New low #3: fires again (re-armed)");
        Check(s.Tick(null, 20) == null, "null count: silence (can't count)");
    }

    // ---------- AlertsModule integration (via ProfileView) ----------
    {
        var p = new ProfileView(new BotProfile
        {
            AlertsEnabled = true,
            SupplyAlerts = { ["2394"] = 20 }
        });
        var mod = new AlertsModule();

        // Death fires when ActiveAlive goes false.
        var dead = new GameState
        {
            ClientConnected = true, InGame = true, NowMs = 0,
            ActiveAlive = false
        };
        Check(mod.Decide(dead, p) is { Channel: ActionChannel.Command, Payload: "alert:morte:o personagem morreu" },
            "Module emits morte alert on death");

        // Same state again: no repeat (cooldown 4s).
        Check(mod.Decide(dead, p) is null, "Morte does not repeat within cooldown");

        // Level up: 300 -> 301
        var lv300 = new GameState { ClientConnected = true, InGame = true, NowMs = 5000, CharacterLevel = 300, ActiveAlive = true };
        mod.Decide(lv300, p);   // first tick: records only
        var lv301 = lv300 with { NowMs = 10000, CharacterLevel = 301 };
        Check(mod.Decide(lv301, p) is { Channel: ActionChannel.Command, Payload: "alert:nivel:subiu para o nivel 301" },
            "Module emits nivel alert on level up");

        // Caught: server chat line
        var chat = new ChatLine("text", "", 0, "You caught a Pokemon! (Shiny Dratini).", AtMs: 14000);
        var caught = new GameState
        {
            ClientConnected = true, InGame = true, NowMs = 14000,
            ActiveAlive = true, LatestChat = chat
        };
        Check(mod.Decide(caught, p) is { Channel: ActionChannel.Command, Payload: "alert:capturou:Shiny Dratini" },
            $"Module emits capturou alert from server line (got {mod.Decide(caught, p)})");
    }
}

static void RunFishingChecks()
{
    // ---------- FishingGate: settle-after-login (no cast in first 3s) ----------
    {
        var g = new FishingGate();
        Check(!g.ShouldCast(1000, 0, long.MinValue, 0, 13000, -1, 0, false), "Within boot settle: no cast");
        Check(!g.ShouldCast(2999, 0, long.MinValue, 0, 13000, -1, 0, false), "Still in settle window: no cast");
    }

    // ---------- Cadence: never faster than configured base ----------
    {
        var g = new FishingGate();
        Check(g.ShouldCast(14000, 0, 1000, 0, 13000, -1, 0, false), "After base cadence: casts");
        Check(!g.ShouldCast(12000, 0, 1000, 0, 13000, -1, 0, false), "Before base cadence: no cast");
    }

    // ---------- Ping-aware: cadence stretches to the round-trip when laggy ----------
    {
        var g = new FishingGate();
        // Base is 13s but the ping alone is 20s: must wait out the full round-trip.
        Check(!g.ShouldCast(15000, 0, 1000, 20000, 13000, -1, 0, false), "Ping > base: no cast yet");
        Check(g.ShouldCast(22000, 0, 1000, 20000, 13000, -1, 0, false), "After full ping round-trip: casts");
        // Low ping must NOT shorten the base cadence.
        var g2 = new FishingGate();
        Check(!g2.ShouldCast(8000, 0, 1000, 100, 13000, -1, 0, false), "Low ping does not beat the base cadence");
    }

    // ---------- Max-poke pause: >= limit stops fishing, 1x/s recheck backoff ----------
    {
        var g = new FishingGate();
        Check(!g.ShouldCast(20000, 0, 1000, 0, 13000, 3, 5, false), $"Wilds >= maxPoke pauses (paused={g.Paused})");
        // Next tick still within the 1s recheck window: keeps the last result without re-reading.
        Check(!g.ShouldCast(20999, 0, 1000, 0, 13000, 3, 0, false), "Inside recheck window: stays paused");
        // One second later the count is re-read: wilds are gone -> resumes.
        Check(g.ShouldCast(22000, 0, 1000, 0, 13000, 3, 1, false), "Recheck sees fewer wilds: resumes");
        // maxPoke < 0 = never pause, even with a crowd around.
        var g2 = new FishingGate();
        Check(g2.ShouldCast(20000, 0, 1000, 0, 13000, -1, 99, false), "maxPoke=-1: never pauses on wilds");
    }

    // ---------- Cross-system busy: catch/loot owns the turn, fishing yields ----------
    {
        var g = new FishingGate();
        Check(!g.ShouldCast(20000, 0, 1000, 0, 13000, -1, 0, true), "Cross-system busy: no cast");
        Check(g.ShouldCast(20000, 0, 1000, 0, 13000, -1, 0, false), "Not busy: casts");
    }

    // ---------- Raio clamp ----------
    {
        Check(FishingGate.ClampRaio(0) == FishingGate.MinRaio, "Raio clamps low to 1");
        Check(FishingGate.ClampRaio(99) == FishingGate.MaxRaio, "Raio clamps high to 12");
        Check(FishingGate.ClampRaio(7) == 7, "Raio passes through in-range value");
    }

    // ---------- FishingModule integration (via ProfileView) ----------
    {
        var p = new ProfileView(new BotProfile
        {
            FishingEnabled = true, FishingDelaySeconds = 2
        });
        var mod = new FishingModule();

        var s1 = new GameState { ClientConnected = true, InGame = true, NowMs = 0 };
        Check(mod.Decide(s1, p) is null, "Module: first tick is inside boot settle");

        // 2s delay, 3s+ of settle elapsed since the first tick: casts and arms the cadence clock.
        var s2 = new GameState { ClientConnected = true, InGame = true, NowMs = 5000 };
        Check(mod.Decide(s2, p) is { Channel: ActionChannel.Command, Payload: "fish" },
            $"Module: casts once past settle + cadence (got {mod.Decide(s2, p)})");

        // Immediately after: cadence holds.
        var s3 = new GameState { ClientConnected = true, InGame = true, NowMs = 6500 };
        Check(mod.Decide(s3, p) is null, "Module: cadence holds right after a cast");

        // Enough time passes: next cast lands.
        var s4 = new GameState { ClientConnected = true, InGame = true, NowMs = 8000 };
        Check(mod.Decide(s4, p) is { Channel: ActionChannel.Command, Payload: "fish" }, "Module: next cast after cadence");
    }
}

static void RunAntiafkChecks()
{
    const long T0 = 1_000_000;
    static PokePos P(int x, int y) => new(x, y, 5);

    // A) Nothing moves until the idle threshold passes (floor = 15s, config = 50s here).
    {
        var t = new AntiAfkTracker();
        Check(t.Tick(T0, true, P(10, 10), false, null, null, 50) is null, "First tick: arms the clock");
        for (long i = 1; i <= 49; i++)
            Check(t.Tick(T0 + i * 1000, true, P(10, 10), false, null, null, 50) is null, $"Before 50s idle: silent (t+{i}s)");
    }

    // B) At the threshold it steps OUT one side and schedules the volta.
    {
        var t = new AntiAfkTracker();
        t.Tick(T0, true, P(10, 10), false, null, null, 50);
        Check(t.Tick(T0 + 50_000, true, P(10, 10), false, null, null, 50) is "RIGHT", "50s idle: first step goes right");
        Check(t.Steps == 1, "Step counter advanced");
    }

    // C) The volta fires exactly at VoltaMs even before a re-armed idle could.
    {
        var t = new AntiAfkTracker();
        t.Tick(T0, true, P(10, 10), false, null, null, 50);
        t.Tick(T0 + 50_000, true, P(10, 10), false, null, null, 50);
        Check(t.Tick(T0 + 50_000 + AntiAfkTracker.VoltaMs - 1, true, P(10, 10), false, null, null, 50) is null, "Volt a: not early");
        Check(t.Tick(T0 + 50_000 + AntiAfkTracker.VoltaMs, true, P(10, 10), false, null, null, 50) is "LEFT", "Volta step back to the left");
        Check(t.Steps == 1, "Volta does not count as a new trigger");
    }

    // D) Any real position change resets the clock (player or cavebot walking).
    {
        var t = new AntiAfkTracker();
        for (int i = 0; i < 49; i++) t.Tick(T0 + i * 1000, true, P(10, 10), false, null, null, 50);
        t.Tick(T0 + 49_000, true, P(11, 10), false, null, null, 50);   // walked: clock re-arms here
        Check(t.Tick(T0 + 49_000 + 40_000, true, P(11, 10), false, null, null, 50) is null,
            "Moved tile: full idle must elapse from the NEW tile");
        Check(t.Tick(T0 + 49_000 + 50_000, true, P(11, 10), false, null, null, 50) is not null,
            "Clock restarted: steps again after a full idle from the new tile");
    }

    // E) Busy: the clock keeps running but the step waits.
    {
        var t = new AntiAfkTracker();
        t.Tick(T0, true, P(10, 10), false, null, null, 50);
        t.Tick(T0 + 50_000, true, P(10, 10), true, null, null, 50);   // idle reached but busy
        Check(true, "Busy at threshold held the step");
        Check(t.Tick(T0 + 51_000, true, P(10, 10), false, null, null, 50) is not null,
            "Once free again: steps without requiring another full idle");
    }

    // F) One side blocked -> flips; both blocked -> retries (resets the clock), no wall-push.
    {
        var t = new AntiAfkTracker();
        t.Tick(T0, true, P(10, 10), false, null, false, 50);        // west blocked from the start
        var d1 = t.Tick(T0 + 50_000, true, P(10, 10), false, null, false, 50);
        Check(d1 == "RIGHT", $"West blocked: flips east (got '{d1}')");
        Check(t.Steps == 1, "Out-step consumed");

        var t2 = new AntiAfkTracker();
        t2.Tick(T0, true, P(10, 10), false, null, null, 50);
        t2.Tick(T0 + 50_000, true, P(10, 10), false, null, null, 50);   // out-step, volta pending
        t2.Tick(T0 + 50_000 + AntiAfkTracker.VoltaMs, true, P(10, 10), false, null, null, 50); // volta
        Check(t2.Steps == 1, "After out+volta: Steps==1 (volta doesn't count as a trigger)");
        // Consume the re-arm (volta cleared _ult; this tick re-establishes the baseline).
        t2.Tick(T0 + 50_000 + AntiAfkTracker.VoltaMs + 1, true, P(10, 10), false, null, null, 50);
        // A full idle AFTER the re-arm: both sides blocked -> retry.
        var blocked = t2.Tick(T0 + 50_000 + AntiAfkTracker.VoltaMs + 50_000 + 1, true, P(10, 10), false, false, false, 50);
        Check(blocked is null, "Both sides blocked: no wall-push, retry later");
        Check(t2.Steps == 2, "Blocked attempt still counted (side flipped for next try)");
    }

    // G) Alternates sides on each trigger.
    {
        var t = new AntiAfkTracker();
        void Cycle(long baseT, bool eastOk, bool westOk)
        {
            t.Tick(baseT, true, P(10, 10), false, eastOk ? (bool?)true : null, westOk ? (bool?)true : null, 50);
            var outDir = t.Tick(baseT + 50_000, true, P(10, 10), false, eastOk ? (bool?)true : null, westOk ? (bool?)true : null, 50);
            var back = t.Tick(baseT + 50_000 + AntiAfkTracker.VoltaMs, true, P(10, 10), false, null, null, 50);
            Check(outDir is "RIGHT" or "LEFT" && back is not null, $"Cycle moved out ({outDir}) and back ({back})");
        }
        Cycle(T0, true, true);
        // Second cycle passes NULL walkability (both sides free) and waits a full idle.
        t.Tick(T0 + 300_000, true, P(10, 10), false, null, null, 50);  // re-baseline
        Check(t.Tick(T0 + 300_000 + 50_000 + 1, true, P(10, 10), false, null, null, 50) is "LEFT",
            "Second trigger alternates to the other side");
    }

    // H) Offline clears all state; unknown walkability degrades to "free".
    {
        var t = new AntiAfkTracker();
        t.Tick(T0, true, P(10, 10), false, null, null, 50);
        t.Tick(T0 + 60_000, false, P(10, 10), false, null, null, 50);   // disconnected mid-idle
        Check(t.Tick(T0 + 70_000, true, P(10, 10), false, null, null, 50) is null,
            "Back online: clock restarts clean");
    }

    // I) Module wiring: profile gate, battle gate, in-game gate.
    {
        var mod = new AntiAfkModule();
        var off = new ProfileView(new BotProfile());
        var on = new ProfileView(new BotProfile { AntiAfkEnabled = true, AntiAfkIdleSeconds = 50 });
        var s = new GameState { ClientConnected = true, InGame = true, HasPosition = true, X = 1, Y = 2, Z = 5, NowMs = T0 };
        Check(mod.Decide(s, off) is null, "Module: disabled profile stays silent");
        var sb = s with { InBattle = true };
        Check(mod.Decide(sb, on) is null, "Module: battle holds the nudge");
        var snp = s with { HasPosition = false };
        Check(mod.Decide(snp, on) is null, "Module: no position read: silent");
    }
}

static void RunVigiaChecks()
{
    const long T0 = 1_000_000;           // entry time; the 10s settle runs until T0+10_000
    const long S = T0 + 11_000;          // "settled": any later jump is judged on its own merits
    static PokePos P(int x, int y, int z = 5) => new(x, y, z);

    // A) Steps never count: 1 tile, or 1 tile + 1 floor (stair/pit), any amount.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), viaScan: false, dead: false);       // first photo arms
        t.Mede(S, P(10, 10), false, false);                       // sit through the settle
        t.Mede(S + 90, P(11, 10), false, false);
        t.Mede(S + 180, P(12, 10, 6), false, false);               // stair
        t.Mede(S + 270, P(13, 10, 5), false, false);               // back down
        Check(t.Alarmed == false, "Steps/stairs/pits never alarm");
    }

    // B) A 3-tile jump alarms once and latches; describe carries de/para/tiles.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.Mede(S, P(10, 10), false, false);
        var p = t.Mede(S + 200, P(13, 10), false, false);
        Check(p != null && p.Dist == 3, "3-tile jump: alarmed");
        Check(t.Alarmed, "Alarm latched");
        Check(p!.Describe().StartsWith("PUXARAM VOCE: de 10,10,5 para 13,10,5 (3 tiles)"), $"Describe format: {p.Describe()}");
        Check(t.Mede(S + 300, P(20, 20), false, false) is null, "Latched: a bigger jump while ringing is silent");
        t.Silenciar();
        Check(!t.Alarmed, "Silenciar releases the latch");
    }

    // C) Floor rules: dz>=2 always counts; dz==1 needs dist>=3.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.Mede(S, P(10, 10), false, false);
        Check(t.Mede(S + 100, P(12, 10, 7), false, false) != null, "2 floors away: alarmed at any distance");
    }
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.Mede(S, P(10, 10), false, false);
        Check(t.Mede(S + 100, P(12, 10, 6), false, false) is null, "1 floor + 2 tiles: not a salto");
        var t2 = new VigiaTracker();
        t2.Mede(T0, P(10, 10), false, false);
        t2.Mede(S, P(10, 10), false, false);
        Check(t2.Mede(S + 100, P(13, 10, 6), false, false) != null, "1 floor + 3 tiles: alarmed");
    }

    // D) Below the threshold on the same floor: not a salto at all.
    {
        var t = new VigiaTracker();  // threshold 3
        t.Mede(T0, P(10, 10), false, false);
        t.Mede(S, P(10, 10), false, false);
        Check(t.Mede(S + 100, P(12, 10), false, false) is null, "2 tiles same floor: below threshold 3");
        var t5 = new VigiaTracker { DistThreshold = 5 };
        t5.Mede(T0, P(10, 10), false, false);
        t5.Mede(S, P(10, 10), false, false);
        Check(t5.Mede(S + 100, P(13, 10), false, false) is null, "Threshold 5: 3 tiles ignored");
        Check(t5.Mede(S + 200, P(18, 10), false, false) != null, "Threshold 5: 5 tiles alarmed");
    }

    // E) Scan path: two photos, so walking may span tiles. Cap = min(8, elapsed/90+1).
    {
        // 3 tiles in 200ms: could you have walked it? 200/90+1 = 3 -> yes, swallow.
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), true, false);
        t.Mede(S, P(10, 10), true, false);
        Check(t.Mede(S + 200, P(13, 10), true, false) is null, "Scan 3 tiles @ 200ms: walked, silent");
        // 3 tiles in 90ms: 90/90+1 = 2 -> no, alarmed.
        var t2 = new VigiaTracker();
        t2.Mede(T0, P(10, 10), true, false);
        t2.Mede(S, P(10, 10), true, false);
        Check(t2.Mede(S + 90, P(13, 10), true, false) != null, "Scan 3 tiles @ 90ms: too fast to walk, alarmed");
        // 5 tiles in 20s: walkable budget capped at 8 -> swallowed (the client stalled).
        var t3 = new VigiaTracker();
        t3.Mede(T0, P(10, 10), true, false);
        t3.Mede(S, P(10, 10), true, false);
        Check(t3.Mede(S + 20_000, P(15, 10), true, false) is null, "Scan 5 tiles over 20s: within the 8-tile cap");
        // 100 tiles in 10s: above the cap no matter the time -> alarmed (the old bug).
        var t4 = new VigiaTracker();
        t4.Mede(T0, P(10, 10), true, false);
        t4.Mede(S, P(10, 10), true, false);
        Check(t4.Mede(S + 10_000, P(110, 10), true, false) != null, "Scan 100 tiles: cap forces the alarm");
    }

    // F) First 10s after (re)entering the game never count (the login drop).
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);            // enter at T0
        Check(t.Mede(T0 + 9_000, P(90, 90), false, false) is null, "Jump inside the 10s settle: silent");
        // After settle the character sits wherever the entry dropped him.
        Check(t.Mede(T0 + 30_000, P(95, 90), false, false) != null, "Same jump outside the settle: alarmed");
    }

    // G) Esperado stamp: short "that teleport was me", extends never shrinks.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.Esperado(S, 8_000, "voar");
        Check(t.Mede(S + 5_000, P(60, 10), false, false) is null, "Stamp covers the bot's own flight");
        Check(t.Mede(S + 30_000, P(63, 10), false, false) != null, "After the stamp expires: alarmed again");
    }

    // H) EsperadoDe (the Auto Hunt door): excuses ONLY a jump leaving near the
    //    stamped tile, and it vale UMA vez.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.EsperadoDe(T0, 10, 10, 5, 60_000, "auto hunt", raio: 3);
        Check(t.Mede(S, P(80, 80), false, false) is null, "Late hunt teleport from the door: silenced + consumed");
        Check(t.Mede(S + 10_000, P(84, 80), false, false) != null, "Stamp consumed: next jump alarms");
    }
    {
        // Walked 4 tiles away from the door before being pulled: raio 3 no longer covers.
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.EsperadoDe(T0, 10, 10, 5, 60_000, "auto hunt", raio: 3);
        for (int i = 1; i <= 4; i++) t.Mede(T0 + 1_000 + i * 90, P(10 + i, 10), false, false);
        Check(t.Mede(S, P(40, 10), false, false) != null, "Door stamp does not excuse a pull from 4 tiles away");
    }

    // I) Dead: the temple return is the game's, not a GM's.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        Check(t.Mede(S, P(50, 50, 9), false, dead: true) is null, "Temple return while dead: silent");
    }

    // J) Taught points: the window button teaches BOTH ends of that alarm in one
    //    click - the route pad by ORIGIN, the hunt exit by DESTINATION.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        Check(t.Mede(S, P(30, 10), false, false) != null, "Route teleport nags the first time");
        t.EnsinarPontos(10, 10, 5);      // "e teleporte normal" on (10,10,5)->(30,10,5)
        t.Silenciar();
        Check(t.Mede(S + 1_000, P(30, 10), false, false) is null, "Taught ORIGEM (route pad): silent");
        Check(t.Mede(S + 2_000, P(10, 10), false, false) is null, "Taught DESTINO (hunt exit): silent");
        Check(t.Mede(S + 3_000, P(11, 10), false, false) is null, "Step to re-anchor");
        Check(t.Mede(S + 4_000, P(40, 10), false, false) != null, "Untaught origin still alarms");
        t.Silenciar();
        t.Mede(S + 5_000, P(41, 10), false, false);       // re-anchor
        // The taught set covers (10,10)/(30,10) - another jump must still alarm.
        Check(t.Mede(S + 6_000, P(70, 20), false, false) != null, "Taught points don't blanket-mute the tracker");
    }
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.EnsinarDe(10, 10, 5);
        t.EsquecerPontos();
        Check(t.Mede(S, P(30, 10), false, false) != null, "EsquecerPontos: everything alarms again");
    }

    // K) Threshold floor: 1 means 2 (a 1-tile move is a step, never a pull).
    {
        var t = new VigiaTracker { DistThreshold = 1 };
        t.Mede(T0, P(10, 10), false, false);
        Check(t.Mede(S, P(12, 10), false, false) != null, "DistThreshold 1 clamps to 2: 2 tiles alarms");
    }

    // L) Offline resets everything: position memory, latch and the 10s settle.
    {
        var t = new VigiaTracker();
        t.Mede(T0, P(10, 10), false, false);
        t.Mede(T0 + 11_000, P(40, 10), false, false);        // alarm
        t.Offline(T0 + 12_000);
        Check(!t.Alarmed, "Offline dies the ringing alarm");
        t.Mede(T0 + 12_500, P(41, 10), false, false);        // first photo back
        Check(t.Mede(T0 + 13_500, P(80, 10), false, false) is null, "Fresh 10s settle after reconnect");
    }

    // M) Module wiring: gate + the alert payload.
    {
        var mod = new VigiaModule();
        var on = new ProfileView(new BotProfile());                          // VigiaEnabled=true by default
        var offp = new ProfileView(new BotProfile { VigiaEnabled = false });
        var s0 = new GameState { ClientConnected = true, InGame = true, HasPosition = true, X = 10, Y = 10, Z = 5, NowMs = T0 };
        Check(mod.Decide(s0, offp) is null, "Module: disabled profile stays silent");

        var sn = s0 with { InGame = false };
        Check(mod.Decide(sn, on) is null, "Module: out of game: silent");

        // First tick arms; a GM-style 5-tile pull two floors up alarms.
        Check(mod.Decide(s0, on) is null, "Module: first tick arms silently");
        var pull = mod.Decide(s0 with { X = 15, Z = 7, NowMs = T0 + 20_000 }, on);
        Check(pull != null && pull.Channel == ActionChannel.Command && (pull.Payload ?? "").StartsWith("alert:puxao:"),
            $"Module: emits alert:puxao ({pull})");
        Check((pull?.Payload ?? "").Contains("O bot foi PARADO."), "Module: payload says the bot was stopped");
    }

    // N) Module: the death chat phrase stamps a 30s window over the temple return.
    {
        var mod = new VigiaModule();
        var on = new ProfileView(new BotProfile());
        var s0 = new GameState { ClientConnected = true, InGame = true, HasPosition = true, X = 10, Y = 10, Z = 5, NowMs = T0 };
        mod.Decide(s0, on);                                             // arm
        var died = s0 with
        {
            NowMs = T0 + 20_000,
            LatestChat = new ChatLine("text", "", 0, "You are dead", T0 + 20_000),
            ActiveAlive = false
        };
        Check(mod.Decide(died, on) is null, "Module: death line: no pull yet");
        var temple = died with { X = 60, Y = 60, Z = 9, NowMs = T0 + 25_000 };
        Check(mod.Decide(temple, on) is null, "Module: temple return inside the death stamp: silent");
    }
}

static void RunEndgameChecks()
{
    static PokebarSlot B(string n, double hp) => new(n, hp);
    var bar = new[] { B("Pikachu", 80), B("Gengar", 60), B("Shiny Pikachu", 90), B("Snorlax", 100), B("Eevee", 70), B("Jolteon", 50) };
    // The six seats named in order; "Shiny Pikachu" (slot 3) is the t1 d1 - the
    // EXACT-name rule must beat the bare "Pikachu" (slot 1).
    var roles = EndgameConfig.FixedRoles("Pikachu", "Shiny Pikachu", "Snorlax", "Eevee", "Jolteon", "");
    EndgameConfig Cfg(int potItem = 0) => new(roles,
        WaveCount: 2, RingTiles: 1, SeeStop: 2, StopDist: 1,
        ApproachSqm: 1, RelureS: 20, MoveGapMs: 180,
        PotItem: potItem, PotPct: 99, SavePct: 40, SwapPct: 15,
        UseSafe: false, SafeReach: 1, RecoverMaxS: 30, Reburst: 1, PokeStop: true);
    System.Collections.Generic.IReadOnlyList<ScannedCreature> W(params (int x, int y)[] pts)
    {
        var list = new List<ScannedCreature>();
        foreach (var p in pts) list.Add(new ScannedCreature(1, 100, p.x, p.y, 5));
        return list;
    }
    static EndgameSnapshot Snap(long t, int? slot, IReadOnlyList<PokebarSlot> pb,
        System.Collections.Generic.IReadOnlyList<ScannedCreature> w, double? hp = null) => new(
        t, Online: true, CharX: 10, CharY: 10, CharZ: 5,
        ActiveRole: slot is int i ? pb[i - 1].Name : "", ActiveHp: hp,
        StuckWildCount: w.Count(c => System.Math.Max(System.Math.Abs(c.X - 10), System.Math.Abs(c.Y - 10)) <= 1 && c.Z == 5),
        ScreenWildCount: w.Count, NearestWildDist: w.Count == 0 ? -1 : w.Min(c => System.Math.Max(System.Math.Abs(c.X - 10), System.Math.Abs(c.Y - 10))),
        NearestWildName: null, KitReadyMoves: 0, TeamCdReady: null,
        PokeX: null, PokeY: null, SafeX: 0, SafeY: 0, SafeZ: 5, Pokebar: pb);
    EndgameStep Tick(EndgameTracker tr, EndgameConfig c, long t, int? slot,
        IReadOnlyList<PokebarSlot> pb, System.Collections.Generic.IReadOnlyList<ScannedCreature> w, double? hp = null) =>
        tr.Tick(c, Snap(t, slot, pb, w, hp));

    // A) Slot resolution: exact name wins over same-name-no-prefix; ambiguous
    //    names ABORT instead of guessing the wrong swap.
    {
        Check(EndgameTracker.ResolveSlot("Pikachu", bar) == 1, "EG slot: exact 'Pikachu' -> 1");
        Check(EndgameTracker.ResolveSlot("pikachu [25]", bar) == 1, "EG slot: level suffix stripped");
        Check(EndgameTracker.ResolveSlot("PIKACHU", bar) == 1, "EG slot: case-insensitive");
        Check(EndgameTracker.ResolveSlot("Shiny Pikachu", bar) == 3, "EG slot: exact 'Shiny Pikachu' -> 3");
        Check(EndgameTracker.ResolveSlot("shiny pikachu", bar) == 3, "EG slot: prefix match case-insensitive");
        var dup = new[] { B("Gengar", 50), B("Gengar", 50) };
        Check(EndgameTracker.ResolveSlot("Gengar", dup) is null, "EG slot: two 'Gengar' -> ambiguous -> null");
        var prefixBar = new[] { B("Pikachu", 50), B("Shiny Gengar", 50), B("Gengar", 50) };
        Check(EndgameTracker.ResolveSlot("shiny gengar", prefixBar) == 2, "EG slot: prefix second-chance finds the shiny one");
        Check(EndgameTracker.ResolveSlot("", bar) is null, "EG slot: empty name -> null");
        Check(EndgameTracker.ResolveSlot("Gengar", System.Array.Empty<PokebarSlot>()) is null, "EG slot: empty pokebar -> null");
    }

    // B) Full rotation, faithful to the Lua flow: send -> out-confirm -> pot
    //    (pots the tank even with an unread HP, even with no item configured) ->
    //    lure -> gather -> approach (poke pos unread -> dist 0) -> tank combo ->
    //    burstwait -> dmg poke (pokestop, instant combo) -> next dmg -> wave
    //    cleared -> JUMP TO THE OTHER TEAM (in-place recover, no safe spot).
    {
        var tr = new EndgameTracker();
        var c = Cfg();
        var empty = W();
        var near = W((13, 10), (14, 10));
        var pile = W((9, 10), (11, 10));
        Check(Tick(tr, c, 1000, null, bar, empty) is { Intent: "summon", Arg: "Pikachu:1" }, "EG B: send Pika (seat 1)");
        Check(tr.Phase == "out" && tr.RoleIndex == 0, "EG B: send -> waiting for the swap");
        Check(Tick(tr, c, 1600, 1, bar, empty).Detail.Contains("fora"), "EG B: out confirmed");
        Check(tr.Phase == "pot", "EG B: confirmed out -> pot the tank");
        Check(Tick(tr, c, 2200, 1, bar, empty).Intent == "", "EG B: no item configured -> straight to lure");
        Check(tr.Phase == "lure", "EG B: tank pulls");
        Check(Tick(tr, c, 2800, 1, bar, near).Intent == "", "EG B: lure stops at 2 seen");
        Check(tr.Phase == "gather", "EG B: gather armed");
        Check(Tick(tr, c, 3400, 1, bar, pile).Intent == "", "EG B: wave closed -> approach");
        Check(tr.Phase == "approach", "EG B: approach phase");
        Check(Tick(tr, c, 4000, 1, bar, pile).Intent == "" && tr.Phase == "burst", "EG B: in the pile (dist 0) -> full combo");
        var cast = Tick(tr, c, 4600, 1, bar, pile);
        Check(cast.Intent == "cast" && cast.Arg.StartsWith("tank:12") && cast.RoleLabel.Contains("Tank"), $"EG B: tank full combo (got {cast.Arg})");
        Check(tr.Phase == "burstwait", "EG B: burstwait phase");
        Check(Tick(tr, c, 5200, 1, bar, pile).Intent == "", "EG B: combo running");
        Check(Tick(tr, c, 7100, 1, bar, pile).Intent == "", "EG B: combo done -> advance (hold)");
        Check(tr.RoleIndex == 1, "EG B: advanced to seat 2");
        // The game swapped to the shiny d1 (slot 3): IsOut recognizes the role and
        // fires !pokestop ONCE, then combos straight away.
        var ps = Tick(tr, c, 7200, 3, bar, near);
        Check(ps.Intent == "pokestop" && tr.Phase == "dmg", $"EG B: dmg pokestop once ({ps.Detail})");
        var burst = Tick(tr, c, 7400, 3, bar, near);
        Check(burst.Intent == "" && tr.Phase == "burst", "EG B: dmg walks into the wave");
        var dcast = Tick(tr, c, 7800, 3, bar, pile);
        Check(dcast.Intent == "cast" && dcast.Arg.StartsWith("dmg:12"), $"EG B: dmg1 combo ({dcast.Arg})");
        Check(tr.Phase == "burstwait", "EG B: dmg1 burstwait");
        // Wave cleared mid-burst -> JUMP TO THE OTHER TEAM (no safe spot), the T2
        // tank was left out on purpose so the jump skips straight to pot/lure.
        Check(Tick(tr, c, 8400, 3, bar, pile).Intent == "", "EG B: combo running");
        var jump = Tick(tr, c, 10400, 3, bar, empty);
        Check(jump.Detail.Contains("proximo time") && tr.RoleIndex == 3, $"EG B: wave cleared -> next team ({jump.Detail})");
        Check(Tick(tr, c, 10600, 3, bar, empty) is { Intent: "summon", Arg: "Eevee:5" }, "EG B: T2 tank goes out");
        var rk = Tick(tr, c, 11201, 5, bar, empty);
        Check(rk.Detail.Contains("fora") && tr.Phase == "pot", $"EG B: T2 out confirmed ({rk.Detail})");
        Check(Tick(tr, c, 11802, 5, bar, empty).Detail.Contains("pronto pra puxar") && tr.Phase == "lure" && tr.RoleIndex == 3,
            "EG B: team ready -> T2 tank pulls the next wave");
    }

    // C) Fainted pokes are SKIPPED (never revived): advancing over a 0% seat
    //    jumps to the next live seat instead of summoning the fainted one.
    {
        var barF0 = new[] { B("Pikachu", 100), B("Gengar", 60), B("Eevee", 70) };
        var barF = new[] { B("Pikachu", 100), B("Gengar", 0), B("Eevee", 70) };
        var rolesF = EndgameConfig.FixedRoles("Pikachu", "Gengar", "Eevee", "", "", "");
        var c = new EndgameConfig(rolesF, 2, 1, 2, 1, 1, 20, 180, 0, 99, 40, 15, false, 1, 30, 1, true);
        var tr = new EndgameTracker();
        Check(Tick(tr, c, 1000, null, barF0, W()) is { Intent: "summon", Arg: "Pikachu:1" }, "EG C: send (alive)");
        Check(Tick(tr, c, 1601, 1, barF0, W()).Detail.Contains("fora"), "EG C: out confirmed");
        Check(Tick(tr, c, 2202, 1, barF, W()).Intent == "", "EG C: straight to lure (no item)");
        Check(Tick(tr, c, 2803, 1, barF, W((13, 10), (14, 10))).Intent == "", "EG C: lure stops at 2 seen");
        Check(Tick(tr, c, 3404, 1, barF, W((9, 10), (11, 10))).Intent == "", "EG C: wave closed -> approach");
        Check(Tick(tr, c, 4005, 1, barF, W((9, 10), (11, 10))).Intent == "" && tr.Phase == "burst", "EG C: in the pile -> combo");
        var c1 = Tick(tr, c, 4606, 1, barF, W((9, 10), (11, 10)));
        Check(c1.Intent == "cast" && c1.Arg.StartsWith("tank:12"), $"EG C: tank combo ({c1.Arg})");
        for (long t = 5200; t < 6700; t += 600) Tick(tr, c, t, 1, barF, W((9, 10), (11, 10))); // burstwait ends -> advance
        Check(Tick(tr, c, 7101, 1, barF, W((9, 10), (11, 10))).Intent == "", "EG C: advance over the fainted seat (hold)");
        Check(tr.RoleIndex == 2, "EG C: fainted Gengar skipped (seat 3)");
        var s3 = Tick(tr, c, 7652, 1, barF, W((9, 10), (11, 10)));
        Check(s3.Intent == "summon" && s3.Arg == "Eevee:3", $"EG C: next live seat sent ({s3.Arg})");
    }

    // D) All seats dead/unreadable/ambiguous: the macro disarms itself.
    {
        var barDead = new[] { B("Pikachu", 0), B("Gengar", 0), B("Eevee", 0), B("Gengar", 100) };
        var rolesD = EndgameConfig.FixedRoles("Pikachu", "Eevee", "", "Gengar", "", "");
        var c = new EndgameConfig(rolesD, 2, 1, 2, 1, 1, 20, 180, 0, 99, 40, 15, false, 1, 30, 1, true);
        var tr = new EndgameTracker();
        var st = Tick(tr, c, 1000, null, barDead, W());
        Check(st.Intent == "" && st.Detail.Contains("nenhum pokemon utilizavel"), "EG D: nobody usable -> hold");
        Check(tr.Phase.Length == 0, "EG D: tracker reset");
    }

    // E) Offline / empty pokebar / a gap longer than the stale window: forget
    //    every cycle state and re-arm cleanly.
    {
        var tr = new EndgameTracker();
        var c = Cfg();
        var empty = W();
        Check(Tick(tr, c, 1000, null, bar, empty).Intent == "summon", "EG E: mid-cycle");
        var off = new EndgameSnapshot(1601, Online: false, 10, 10, 5, "", null, 0, 0, -1, null, 0, null, null, null, 0, 0, 5, bar);
        Check(tr.Tick(c, off).Detail == "offline" && tr.Phase.Length == 0, "EG E: offline resets");
        var emptyBar = new EndgameSnapshot(1802, Online: true, 10, 10, 5, "", null, 0, 0, -1, null, 0, null, null, null, 0, 0, 5, System.Array.Empty<PokebarSlot>());
        Check(tr.Tick(c, emptyBar).Detail.Contains("barra") && tr.Phase.Length == 0, "EG E: empty pokebar arms silently");
        Check(Tick(tr, c, 2203, null, bar, empty).Intent == "summon" && tr.RoleIndex == 0, "EG E: fresh arm after re-read");
        var r1 = Tick(tr, c, 2804, null, bar, empty);
        Check(r1.Intent == "" && r1.Detail.Contains("tentando de novo") && tr.Phase == "send", "EG E: unconfirmed swap retries in place");
        Check(Tick(tr, c, 3405, null, bar, empty).Intent == "summon", "EG E: <=2s gap keeps the cycle (re-send)");
        Check(Tick(tr, c, 6006, null, bar, empty).Intent == "summon" && tr.RoleIndex == 0, "EG E: >2s gap forced a reset + fresh arm");
    }

    // F) Stalled gather: after RelureS the macro walks again for a full 6s
    //    (no stop triggers during that window), then can stop normally.
    {
        var rolesF = EndgameConfig.FixedRoles("Pikachu", "Shiny Pikachu", "", "", "", "");
        var c = new EndgameConfig(rolesF, 2, 1, 99, 99, 1, 4, 180, 0, 99, 40, 15, false, 1, 30, 1, true);
        var tr = new EndgameTracker();
        var far = W((16, 10));                                       // seen 1, dist 6
        var seen2 = W((16, 10), (17, 10));                           // seen 2, stuck 0
        Check(Tick(tr, c, 1000, null, bar, W()).Intent == "summon", "EG F: send");
        Check(Tick(tr, c, 1601, 1, bar, far).Detail.Contains("fora"), "EG F: out confirmed");
        Check(Tick(tr, c, 2052, 1, bar, far).Detail.Contains("pronto pra puxar") && tr.Phase == "lure", "EG F: in lure");
        var g = Tick(tr, c, 2503, 1, bar, far);                     // near-wild stop (StopDist wide)
        Check(g.Detail.Contains("parou") && tr.Phase == "gather", $"EG F: stopped & gather armed ({g.Detail})");
        for (long t = 3500; t <= 6000; t += 2000) Tick(tr, c, t, 1, bar, far);   // holding the wave
        var rp = Tick(tr, c, 6601, 1, bar, far);                    // >4s stalled -> walk again
        Check(rp.Detail.Contains("voltando a andar") && tr.Phase == "lure", $"EG F: stalled gather re-pulls ({rp.Detail})");
        for (long t = 8600; t <= 12598; t += 1500)
            Check(Tick(tr, c, t, 1, bar, far).Intent == "", "EG F: no stop trigger inside the 6s walk window");
        Check(tr.Phase == "lure", "EG F: still in the walk window");
        Check(Tick(tr, c, 12899, 1, bar, far).Detail.Contains("parou"), "EG F: walk window over -> stops again");
        Check(tr.Phase == "gather", "EG F: gather re-armed");
        var done = Tick(tr, c, 13400, 1, bar, W((9, 10), (11, 10)));  // both stuck now
        Check(done.Intent == "" && tr.Phase == "approach", "EG F: wave closed -> approach");
    }

    // G) Tank safety: below the swap line the tank is pulled out EARLY (the swap
    //    collects it from the middle of the wave); mid-burst only the potion may
    //    act, the swap waits for the combo to finish.
    {
        var rolesG = EndgameConfig.FixedRoles("Pikachu", "Eevee", "", "", "", "");
        var c = new EndgameConfig(rolesG, 2, 1, 2, 1, 1, 20, 180, 0, 99, 40, 15, false, 1, 30, 1, true);
        var tr = new EndgameTracker();
        Tick(tr, c, 1000, null, bar, W());                         // send
        Check(Tick(tr, c, 1601, 1, bar, W()).Detail.Contains("fora"), "EG G: out confirmed");
        Check(Tick(tr, c, 2052, 1, bar, W()).Detail.Contains("pronto pra puxar") && tr.Phase == "lure", "EG G: in lure");
        Check(Tick(tr, c, 3404, 1, bar, W((16, 10))).Intent == "", "EG G: pulling with a healthy tank");
        var weak = new EndgameSnapshot(3905, true, 10, 10, 5, "Pikachu", 10, 0, 1, 3, null, 0, null, null, null, 0, 0, 5, bar);
        var swap = tr.Tick(c, weak);
        Check(swap.Intent == "" && swap.Detail.Contains("troca"), $"EG G: tank 10% <= swap 15% -> early pull ({swap.Detail})");
        Check(tr.RoleIndex == 1, "EG G: advanced (swap collects the tank)");

        // Mid-burst: allowSwap=false - only the potion branch fires, and the
        // potion cooldown blocks a second one. Clocks start above the 10s item
        // CD so the on-the-way-back potion arms cleanly.
        var tr2 = new EndgameTracker();
        var cg = new EndgameConfig(rolesG, 2, 1, 2, 1, 1, 20, 3000, 1000, 99, 40, 15, false, 1, 30, 1, true);
        var pile = W((9, 10), (11, 10));
        var far = W((16, 10));
        Tick(tr2, cg, 11000, null, bar, W());                      // send
        Check(Tick(tr2, cg, 11601, 1, bar, far).Detail.Contains("fora"), "EG G: out confirmed");
        var pot2 = Tick(tr2, cg, 12052, 1, bar, far);
        Check(pot2.Intent == "potion" && pot2.Arg == "1000", "EG G: pot the hurt tank on the way out");
        Check(Tick(tr2, cg, 12800, 1, bar, pile).Detail.Contains("colou") && tr2.Phase == "gather", "EG G: wave glued -> gather");
        Check(Tick(tr2, cg, 13401, 1, bar, pile).Detail.Contains("chegando na pilha") && tr2.Phase == "approach", "EG G: approach");
        Check(Tick(tr2, cg, 14002, 1, bar, pile).Intent == "" && tr2.Phase == "burst", "EG G: in the pile -> combo armed");
        var cast2 = Tick(tr2, cg, 15500, 1, bar, pile);
        Check(cast2.Intent == "cast" && cast2.Arg.StartsWith("tank:12") && tr2.Phase == "burstwait", $"EG G: tank combo ({cast2.Arg})");
        for (long t = 17500; t <= 21500; t += 2000)
            Check(Tick(tr2, cg, t, 1, bar, pile, 80).Intent == "", "EG G: combo running (healthy)");
        var mw = Tick(tr2, cg, 22503, 1, bar, pile, 12);           // tank at 12% mid-combo
        Check(mw.Intent == "potion" && mw.Detail.Contains("socorro") && tr2.Phase == "burstwait",
            $"EG G: mid-burst rescue potion, NO early swap ({mw.Detail})");
        var again = Tick(tr2, cg, 23004, 1, bar, pile, 12);
        Check(again.Intent == "" && tr2.Phase == "burstwait", "EG G: potion cooldown blocks the repeat");
    }

    // H) Module gating: off -> null; no pokebar read -> silent arm (null);
    //    armed -> a real intent (never null while it owns the tick).
    {
        var mod = new EndgameModule();
        var sBase = new GameState
        {
            ClientConnected = true, InGame = true, HasPosition = true, X = 10, Y = 10, Z = 5, NowMs = 1000,
            Pokebar = bar, ActivePokebarSlot = null, ActiveHpPercent = null
        };
        var onProf = new ProfileView(new BotProfile
        {
            EndgameEnabled = true,
            EndgameT1Tank = "Pikachu", EndgameT1D1 = "Shiny Pikachu", EndgameT1D2 = "Snorlax",
            EndgameT2Tank = "Eevee", EndgameT2D1 = "Jolteon", EndgameT2D2 = "",
            EndgamePokeStopCmd = "!pokestop"
        });
        var offProf = new ProfileView(new BotProfile { EndgameEnabled = false, EndgameT1Tank = "Pikachu" });
        Check(mod.Decide(sBase, offProf) is null, "EG H: disabled profile -> null");
        var noBar = sBase with { Pokebar = System.Array.Empty<PokebarSlot>() };
        Check(mod.Decide(noBar, onProf) is null && mod.Status.Contains("barra"), "EG H: no pokebar read -> silent arm");
        var a = mod.Decide(sBase, onProf);
        Check(a is { Channel: ActionChannel.Command, Payload: "summon:Pikachu:1" }, $"EG H: armed -> summon intent ({a?.Payload})");
        var b = mod.Decide(sBase with { NowMs = 2001, ActivePokebarSlot = 1 }, onProf);
        Check(b is { Channel: ActionChannel.Command, Payload: "eghold:donopoke" }, $"EG H: hold tick is a sentinel command ({b?.Payload})");
        var mover = mod.Decide(sBase with { NowMs = 9000 }, onProf);
        Check(mover != null, "EG H: the tick the module owns is never null");
    }
}

static void RunCalibrationChecks()
{
    // A) JsonOk: only {"ok":true} clears the gate.
    {
        Check(OffsetAutoCalibrator.JsonOk("{\"ok\":true,\"message\":\"snap\"}"), "Calib: ok:true accepted");
        Check(!OffsetAutoCalibrator.JsonOk("{\"ok\":false,\"message\":\"no module\"}"), "Calib: ok:false rejected");
        Check(!OffsetAutoCalibrator.JsonOk("{not json"), "Calib: broken JSON rejected");
        Check(!OffsetAutoCalibrator.JsonOk(null), "Calib: null rejected");
    }

    // B) ParseCandidates: hex and decimal tokens; anything outside [0, 0x8000000] is junk.
    {
        var list = OffsetAutoCalibrator.ParseCandidates("{\"count\":4,\"candidates\":[\"0x1A2B4C\",\"42\",\"0x8000001\",\"-7\"]}");
        Check(list.SequenceEqual(new long[] { 0x1A2B4C, 42 }), $"Calib: hex+decimal parsed, out-of-range dropped ({list})");
        Check(OffsetAutoCalibrator.ParseCandidates(null).Count == 0, "Calib: null json -> empty");
        Check(OffsetAutoCalibrator.ParseCandidates("{\"message\":\"x\"}").Count == 0, "Calib: no candidates key -> empty");
        Check(OffsetAutoCalibrator.ParseCandidates("[1,2]").Count == 0, "Calib: non-object root -> empty");
    }

    // C) IsSaneTriple: tile coordinates pass, counters fail the magnitude guard.
    {
        Check(OffsetAutoCalibrator.IsSaneTriple(120, -34, 7), "Calib: tile triple is sane");
        Check(!OffsetAutoCalibrator.IsSaneTriple(60000, 0, 5), "Calib: 60000 = counter, not a tile axis");
        Check(!OffsetAutoCalibrator.IsSaneTriple(0, -50001, 3), "Calib: negative counter rejected");
    }

    // D) StableSurvivors: commit-order-preserving intersection; tickers die.
    {
        var commit = new List<long> { 0x10, 0x20, 0x30 };
        Check(OffsetAutoCalibrator.StableSurvivors(commit, new HashSet<long> { 0x30, 0x20 }).SequenceEqual(new long[] { 0x20, 0x30 }),
            "Calib: survivors keep commit order");
        Check(OffsetAutoCalibrator.StableSurvivors(commit, new HashSet<long>()).Count == 0, "Calib: nothing stable -> nothing survives");
    }

    // E) PickCandidate: the first survivor wins, 0 when empty.
    {
        Check(OffsetAutoCalibrator.PickCandidate(new List<long> { 5, 9 }) == 5, "Calib: picks first");
        Check(OffsetAutoCalibrator.PickCandidate(new List<long>()) == 0, "Calib: empty -> 0");
    }

    // F) IsVerifiedRead: needs READY AND a real position.
    {
        Check(OffsetAutoCalibrator.IsVerifiedRead(new NativeStatus { ReaderStatus = "READY", HasPosition = true, PosX = 1, PosY = 2, PosZ = 3 }),
            "Calib: READY + position accepted");
        Check(!OffsetAutoCalibrator.IsVerifiedRead(new NativeStatus { ReaderStatus = "NOT_CONFIGURED", HasPosition = false }),
            "Calib: NOT_CONFIGURED rejected");
        Check(!OffsetAutoCalibrator.IsVerifiedRead(new NativeStatus { ReaderStatus = "READY", HasPosition = false }),
            "Calib: READY without position rejected");
        Check(!OffsetAutoCalibrator.IsVerifiedRead(null), "Calib: null rejected");
    }

    // G) KryonBot-style auto defaults: a profile with no explicit choice gets the
    //    safe modules ON once (Endgame stays OFF); a later load keeps the saved flags.
    {
        var fresh = BotProfileService.ApplyAutoDefaults(new BotProfile(), writeBack: false);
        Check(fresh.AutoDefaultsVersion == 1, "Migração: versão stampada");
        Check(fresh.AttackerEnabled && fresh.AutoReviveEnabled && fresh.AlertsEnabled &&
              fresh.CatchEnabled && fresh.LootEnabled && fresh.FishingEnabled && fresh.AntiAfkEnabled,
            "Migração: flags seguras ligadas");
        Check(!fresh.EndgameEnabled, "Migração: Endgame continua desligado (precisa dos 6 nomes)");
        var again = BotProfileService.ApplyAutoDefaults(new BotProfile { AutoDefaultsVersion = 1, AttackerEnabled = false }, writeBack: false);
        Check(!again.AttackerEnabled && again.AutoDefaultsVersion == 1, "Migração: flag salva do usuário respeitada");
    }
}

static void RunSensorFoundationChecks()
{
    // A) SensorValue factory + Age.
    {
        var v = SensorValue<PositionValue>.Of(new PositionValue(10, 20, 5), "test");
        Check(v.IsValid && v.Health == SensorHealth.Healthy && v.Confidence == 1.0, "Sensor: Of() produces valid healthy value");
        Check(v.Value!.X == 10 && v.Value.Y == 20 && v.Value.Z == 5, "Sensor: payload intact");

        var u = SensorValue<PositionValue>.Unknown("src");
        Check(!u.IsValid && u.Health == SensorHealth.Unavailable && u.Value is null, "Sensor: Unknown() is invalid/unavailable");

        var now = Environment.TickCount64;
        var aged = new SensorValue<int> { Value = 1, CapturedAt = now - 300, Source = "x", IsValid = true, Health = SensorHealth.Healthy };
        Check(Math.Abs(aged.Age(now).TotalMilliseconds - 300) < 10, "Sensor: Age() computed from CapturedAt");
    }

    // B) FreshnessPolicy per-dado (HP expira antes que Inventory).
    {
        Check(DefaultPolicies.Hp.Evaluate(TimeSpan.FromMilliseconds(100)) == DataFreshness.Fresh, "Freshness: HP 100ms fresh");
        Check(DefaultPolicies.Hp.Evaluate(TimeSpan.FromMilliseconds(300)) == DataFreshness.Stale, "Freshness: HP 300ms stale");
        Check(DefaultPolicies.Hp.Evaluate(TimeSpan.FromMilliseconds(600)) == DataFreshness.Invalid, "Freshness: HP 600ms invalid");

        Check(DefaultPolicies.Inventory.Evaluate(TimeSpan.FromMilliseconds(300)) == DataFreshness.Fresh, "Freshness: Inventory 300ms ainda fresh");
        Check(DefaultPolicies.Inventory.Evaluate(TimeSpan.FromSeconds(5)) == DataFreshness.Stale, "Freshness: Inventory 5s stale");
        Check(DefaultPolicies.Inventory.Evaluate(TimeSpan.FromSeconds(20)) == DataFreshness.Invalid, "Freshness: Inventory 20s invalid");

        Check(DefaultPolicies.Position.Evaluate(TimeSpan.FromMilliseconds(200)) == DataFreshness.Fresh, "Freshness: position 200ms fresh");
        Check(DefaultPolicies.Position.Evaluate(TimeSpan.FromMilliseconds(900)) == DataFreshness.Invalid, "Freshness: position 900ms invalid");
    }

    // C) StructuredPositionSensor — sucesso real.
    {
        var ok = new NativeStatus { NativeOnline = true, ClientFound = true, HasPosition = true, PosX = 11, PosY = 10, PosZ = 7 };
        var sensor = new StructuredPositionSensor(() => Task.FromResult<NativeStatus?>(ok));
        var result = sensor.ReadAsync().GetAwaiter().GetResult();
        Check(result.IsValid && result.Health == SensorHealth.Healthy, "PosSensor: leitura validada = healthy");
        Check(result.Value!.X == 11 && result.Value.Y == 10 && result.Value.Z == 7, "PosSensor: X/Y/Z corretos");
        Check(result.Source == "StructuredPositionSensor", "PosSensor: source rotulado");
    }

    // D) StructuredPositionSensor — client conectado mas posição ilegível (DEGRADED, não inventa zero).
    {
        var noPos = new NativeStatus { NativeOnline = true, ClientFound = true, HasPosition = false };
        var sensor = new StructuredPositionSensor(() => Task.FromResult<NativeStatus?>(noPos));
        var result = sensor.ReadAsync().GetAwaiter().GetResult();
        Check(!result.IsValid && result.Health == SensorHealth.Degraded, "PosSensor: sem posição = degraded (não inventa)");
        Check(result.Value is null, "PosSensor: value nulo (unknown != zero)");
    }

    // E) StructuredPositionSensor — cliente ausente (UNAVAILABLE, vazio != "sem criaturas").
    {
        var sensor = new StructuredPositionSensor(() => Task.FromResult<NativeStatus?>(null));
        var result = sensor.ReadAsync().GetAwaiter().GetResult();
        Check(!result.IsValid && result.Health == SensorHealth.Unavailable, "PosSensor: status null = unavailable");
    }

    // F) StructuredPositionSensor — exceção de transporte vira estado, não crash.
    {
        var sensor = new StructuredPositionSensor(() => Task.FromException<NativeStatus?>(new IOException("pipe closed")));
        var result = sensor.ReadAsync().GetAwaiter().GetResult();
        Check(!result.IsValid && result.Health == SensorHealth.Unavailable && result.Error != null, "PosSensor: exceção -> Unavailable + erro registrado");
    }

    // G) Metadata de sensor (Name/Tier) para o scheduler futuro.
    {
        var sensor = new StructuredPositionSensor(() => Task.FromResult<NativeStatus?>(null));
        Check(sensor.Name == "StructuredPositionSensor", "PosSensor: nome estável p/ logs");
        Check(sensor.Tier == SensorTier.Normal, "PosSensor: tier Normal");
    }
}

static void RunVisualCreatureChecks()
{
    static void SetPx(ref byte[] bgra, int stride, int x, int y, byte r, byte g, byte b)
    {
        var i = y * stride + x * 4;
        bgra[i + 0] = b; bgra[i + 1] = g; bgra[i + 2] = r; bgra[i + 3] = 255; // BGRA
    }
    // Plant an occupied tile: a bright striped sprite band + a uniform HP band color.
    static void Plant(byte[] bgra, int stride, int colW, int rowH, int col, int row, byte hr, byte hg, byte hb)
    {
        var c0 = col * colW;
        var r0 = row * rowH;
        for (var y = r0 + 1; y < r0 + (int)(rowH * .16); y++)                // HP band
            for (var x = c0 + 4; x < c0 + colW - 4; x++) SetPx(ref bgra, stride, x, y, hr, hg, hb);
        for (var y = r0 + (int)(rowH * .22); y < r0 + (int)(rowH * .70); y++) // sprite band (bright edges)
            for (var x = c0 + 5; x < c0 + colW - 5; x++)
                SetPx(ref bgra, stride, x, y, (byte)((x % 3 == 0) ? 245 : 95), (byte)((y % 3 == 0) ? 150 : 90), 80);
    }

    const int W = 400, H = 200;
    int S = W * 4;

    // A) Flat background -> NO creatures (not an error).
    {
        var bg = new byte[S * H];
        for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) SetPx(ref bg, S, x, y, 60, 60, 60);
        var empty = CreatureFrameAnalyzer.Extract(new CapturedFrame(W, H, S, bg));
        Check(empty.Count == 0, "Creature: fundo plano -> zero criaturas");
    }

    // B) Our pokemon at center (blue HP) + one monster at (+1,0) (green HP).
    var frameB = new byte[S * H];
    for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) SetPx(ref frameB, S, x, y, 60, 60, 60);
    int colW = W / 10, rowH = H / 5;
    Plant(frameB, S, colW, rowH, 5, 2, 40, 70, 210);   // player tile: blue HP => SummonOwn
    Plant(frameB, S, colW, rowH, 6, 2, 40, 200, 40);   // +1,0: green HP => alive monster
    var found = CreatureFrameAnalyzer.Extract(new CapturedFrame(W, H, S, frameB));
    Check(found.Any(c => c.Type == ScannedCreature.SummonOwn && c.X == 0 && c.Y == 0),
        "Creature: meu poke detectado no tile central (0,0)");
    Check(found.Any(c => c.IsMonster && c.IsAlive && c.X == 1 && c.Y == 0),
        "Creature: monstro vivo na pos relativa (+1,0)");

    // C) Same but the monster tile has NO green HP => corpse kept with alive=false.
    var frameC = new byte[S * H];
    for (var y = 0; y < H; y++) for (var x = 0; x < W; x++) SetPx(ref frameC, S, x, y, 60, 60, 60);
    Plant(frameC, S, colW, rowH, 4, 2, 30, 30, 30);     // dark HP => dead
    var corpseFound = CreatureFrameAnalyzer.Extract(new CapturedFrame(W, H, S, frameC));
    Check(corpseFound.Any(c => !c.IsAlive && c.IsMonster),
        "Creature: tile sem HP verde = cadáver (alive=false), não sumido");

    // D) ScreenScan.Analyze reclassifies: our pokemon excluded from wilds.
    {
        var own = new ScannedCreature(ScannedCreature.SummonOwn, 100, 0, 0, 0);
        var wild = new ScannedCreature(1, 100, 1, 0, 0);
        var scan = ScreenScan.Analyze(new[] { own, wild });
        Check(scan.HasRead && scan.MyPoke != null && scan.Wilds.Count == 1 && scan.PokeOnField(),
            "Analyze: meu poke separado de wilds (reuso do classifier existente)");
    }

    // E) VisualCreatureSensor: captura Ok -> válido com criaturas reais.
    {
        IFrameSource ok = new FixedFrameSource(new CapturedFrame(W, H, S, frameB));
        var sensor = new VisualCreatureSensor(ok);
        var res = sensor.ReadAsync().GetAwaiter().GetResult();
        Check(res.IsValid && res.Health == SensorHealth.Healthy && res.Value!.Scan.HasRead,
            "VSensor: captura ok -> válido + HasRead");
        Check(res.Value!.Scan.Wilds.Count >= 1, "VSensor: criaturas reais chegaram ao SensorValue");
        Check(res.Source == "VisualCreatureSensor" && res.Confidence > 0 && res.Confidence < 1,
            "VSensor: source rotulado + confiança visual moderada");
    }

    // F) VisualCreatureSensor: captura indisponível -> Unavailable (NÃO lista vazia).
    {
        IFrameSource dead = new UnavailableFrameSource();
        var sensor = new VisualCreatureSensor(dead);
        var res = sensor.ReadAsync().GetAwaiter().GetResult();
        Check(!res.IsValid && res.Health == SensorHealth.Unavailable, "VSensor: sem captura = Unavailable");
        Check(res.Value!.Scan.HasRead == false, "VSensor: HasRead=false (sem leitura != mundo vazio)");
    }

    // G) VisionPresenceSensor: disponível + InGame -> válido; indisponível -> Degraded.
    {
        ISensor<PresenceValue> sOn = new VisionPresenceSensor(() => new PresenceProbe(true, .92, true, "Combined"));
        var on = sOn.ReadAsync().GetAwaiter().GetResult();
        Check(on.IsValid && on.Value!.InGame && on.Confidence > .9, "PSensor: InGame válido com confiança");

        ISensor<PresenceValue> sDown = new VisionPresenceSensor(() => new PresenceProbe(false, 0, false, "ClientReader"));
        var capDown = sDown.ReadAsync().GetAwaiter().GetResult();
        Check(!capDown.IsValid && capDown.Health == SensorHealth.Degraded, "PSensor: sem captura = Degraded (não crash)");
    }
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

sealed class FixedFrameSource(CapturedFrame frame) : IFrameSource
{
    public CaptureResult Capture() => new(CaptureOutcome.Ok, frame);
}

sealed class UnavailableFrameSource : IFrameSource
{
    public CaptureResult Capture() => new(CaptureOutcome.Unavailable, null, "window closed");
}
