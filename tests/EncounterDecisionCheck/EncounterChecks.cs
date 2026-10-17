using BossMod;

internal static partial class EncounterChecks
{
    private static int _passed;
    private static int _failedGroups;
    public static int Run()
    {
        // Initialize real generated config and module registries; no plugin/game startup or user config files.
        Service.Config.Initialize();
        Service.LuminaGameData = new Lumina.GameData(Environment.GetEnvironmentVariable("BMR_SQPACK")
            ?? "E:/SteamLibrary/steamapps/common/FINAL FANTASY XIV Online/game/sqpack");
        List<string> constructionLogs = [];
        Service.LogHandlerDebug = constructionLogs.Add;
        var names = new[] { "M09SVampFatale", "M10STheXtremes", "M11STheTyrant", "M12SLindwurm", "M12S2TheLindwurm", "DMU" };
        foreach (var name in names)
        {
            RunGroup(name + " lifecycle", () =>
            {
            var info = BossModuleRegistry.RegisteredModules.Values.SingleOrDefault(x => x.ModuleType.Name == name);
            Check(info != null, $"registered {name}");
            using var module = BossModuleRegistry.CreateModuleForTimeline(info!.PrimaryActorOID)!;
            Check(module.StateMachine.Phases.Count > 0, $"real {name} states constructed");
            var pending = new Stack<StateMachine.State>(module.StateMachine.Phases.Select(p => p.InitialState));
            HashSet<StateMachine.State> seen = [];
            while (pending.TryPop(out var state))
                if (seen.Add(state))
                    foreach (var next in state.NextStates ?? []) pending.Push(next);
            var structure = string.Join("\n", seen.OrderBy(s => s.ID).Select(s =>
                $"{s.ID:X8}:{s.Duration:R}:{s.EndHint}:{string.Join(',', (s.NextStates ?? []).Select(n => n.ID.ToString("X8")))}"));
            Console.WriteLine($"TIMELINE_STRUCTURE {name} {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(structure)))}");
            var duplicates = constructionLogs.Where(x => x.Contains("Duplicate state id", StringComparison.Ordinal)).ToArray();
            if (name is "M09SVampFatale" or "M10STheXtremes")
            {
                // Preserve existing effective IDs used by plans; detect any new authored collisions.
                var expected = name == "M09SVampFatale"
                    ? new[] { "B0010", "D0010", "F0010", "F0020", "F0030", "F0031", "F0040", "F0080", "F0081" }
                    : new[] { "10051", "400A1", "70000", "70001", "70001", "70002" };
                Check(duplicates.SequenceEqual(expected.Select(id => $"Duplicate state id {id}, incrementing by 1")),
                    $"{name} has only the documented pre-existing state-ID collisions");
                foreach (var duplicate in duplicates) Console.WriteLine($"KNOWN {name} timeline: " + duplicate);
            }
            else
                Check(duplicates.Length == 0, $"{name} authored state IDs need no automatic renumbering: {string.Join("; ", duplicates)}");
            module.StateMachine.Start(new DateTime(2026, 10, 17));
            module.StateMachine.Reset();
            Check(module.StateMachine.ActiveState == null && module.StateMachine.ActivePhaseIndex == -1, $"{name} native wipe reset stops timeline");
            Check(module.Components.All(x => x.KeepOnPhaseChange), $"{name} native wipe reset removes transient components");
            constructionLogs.Clear();
            });
            constructionLogs.Clear();
        }
        RunGroup("required decision coverage", () =>
        {
            foreach (var method in new[] { nameof(CheckM11), nameof(CheckM12), nameof(CheckDMU), nameof(CheckSavageNavigation),
                nameof(CheckDMUNavigation), nameof(CheckDMUBlackHole), nameof(CheckDMUWind), nameof(CheckDMUPiren), nameof(CheckDMUOrchestra), nameof(CheckFloodNavigation),
                nameof(CheckM12P2), nameof(CheckDMURemaining) })
                Check(typeof(EncounterChecks).GetMethod(method, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) != null,
                    $"required production decision checks implemented: {method}");
        });
        RunGroup("M11 decisions", () => CheckM11());
        RunGroup("M12 decisions", () => CheckM12());
        RunGroup("M12 phase 2 decisions", () => CheckM12P2());
        RunGroup("DMU decisions", () => CheckDMU());
        RunGroup("Savage navigation", () => CheckSavageNavigation());
        RunGroup("DMU navigation", () => CheckDMUNavigation());
        RunGroup("DMU black hole", () => CheckDMUBlackHole());
        RunGroup("DMU elemental navigation", () => CheckDMUWind());
        RunGroup("DMU Piren navigation", () => CheckDMUPiren());
        RunGroup("DMU orchestra navigation", () => CheckDMUOrchestra());
        RunGroup("DMU remaining decisions", () => CheckDMURemaining());
        var assembly = typeof(BossModule).Assembly.Location;
        Console.WriteLine($"PASS {_passed} assertions against production encounter code; assembly {assembly}");
        Console.WriteLine($"SHA256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly)))}");
        Console.WriteLine($"FAILED GROUPS {_failedGroups}");
        return _failedGroups == 0 ? 0 : 1;
    }

    static partial void CheckM11();
    static partial void CheckM12();
    static partial void CheckM12P2();
    static partial void CheckDMU();
    static partial void CheckSavageNavigation();
    static partial void CheckDMUNavigation();
    static partial void CheckDMUBlackHole();
    static partial void CheckDMUWind();
    static partial void CheckDMUPiren();
    static partial void CheckDMUOrchestra();
    static partial void CheckDMURemaining();

    private static void RunGroup(string name, Action run)
    {
        try { run(); }
        catch (Exception e)
        {
            ++_failedGroups;
            Console.Error.WriteLine($"GROUP FAILURE {name}: {e}");
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("FAIL " + name);
        ++_passed;
        Console.WriteLine("PASS " + name);
    }
}
