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
        MonstersToAttack = { "Mimikyu", "Pikachu" }
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
    Check(dst.MonstersToAttack.SequenceEqual(new[] { "Mimikyu", "Pikachu" }), "Share import carries monster list");
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

static string ConfigShareServiceChecksumHelper(string body)
{
    var bytes = System.Text.Encoding.UTF8.GetBytes(body);
    uint a = 1, bsum = 0;
    foreach (var x in bytes) { a = (a + x) % 65521; bsum = (bsum + a) % 65521; }
    return ((bsum << 16) | a).ToString("X");
}

RunPmChecks();
RunSocorroChecks();

static void RunSocorroChecks()
{
    static GameState GS(long t, bool? field, int wilds, int? activeSlot,
        System.Collections.Generic.IReadOnlyList<PokebarSlot>? bar = null) => new()
    {
        ClientConnected = true, InGame = true, HasPosition = true, NowMs = t,
        FieldHasPoke = field, WildsNearby = wilds, ActivePokebarSlot = activeSlot,
        ActiveAlive = field ?? false,
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
