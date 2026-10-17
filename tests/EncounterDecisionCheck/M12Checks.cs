using BossMod;
using BossMod.Autorotation;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static partial class EncounterChecks
{
    static partial void CheckM12()
    {
        CheckM12PlanMigration();
        // Independent event fixtures from cactbot r12s.txt; no production predicate calls.
        foreach (var variant in new[] { 0, 1, 2, 3 })
        {
            using var f = new Fixture("M12SLindwurm");
            f.AddPlayer(0);
            var helper = f.AddActor(0x200, 0x233C, ActorType.Enemy, Class.None, new(100, 100));
            f.EnterState(0, 0);
            var start = f.Now;
            void At(double time) => f.Advance(time + 0.001 - (f.Now - start).TotalSeconds);
            void Cast(double time, uint action)
            {
                At(time);
                var caster = action is 0xB4B7 or 0xB4A4 or 0xB4A5 or 0xB9BC or 0xB9C7 ? helper : f.Boss;
                f.EventCast(caster, action);
                f.Advance(0);
            }
            At(15.5);
            f.EventCast(f.Boss, 0xFFFF);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0, $"M12 variant {variant}: unrelated cast cannot trigger Fixer");
            Cast(15.6, 0xB4D7);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x10, $"M12 variant {variant}: real Fixer event advances state");
            Cast(40.8, 0xB495);
            foreach (var time in new[] { 40.9, 43.9, 46.9, 49.9 })
            {
                Cast(time, 0xB496);
                f.EventCast(f.Boss, time < 47 ? 0xB496u : 0xB498u);
                f.Advance(0);
            }
            Cast(70, 0xBEBD);
            Cast(79.2, 0xB49E);
            Cast(81.2, 0xB49E);
            Cast(83.2, 0xB49E);
            Cast(85.2, 0xB49E);
            Cast(87.7, 0xB49D);
            Cast(88, 0xB4AE);
            Cast(96.7, 0xB49F);
            Cast(97.3, 0xB9B9);
            Cast(107.3, 0xB4D7);
            Cast(119.5, 0xBEBE);
            Cast(128.7, 0xB4B6);
            for (var duplicate = 0; duplicate < 8; ++duplicate) f.EventCast(helper, 0xB4B6);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x120, $"M12 variant {variant}: clustered spotlight helpers cannot skip final spotlight");
            Cast(130.7, 0xB4B6);
            Cast(132.7, 0xB4B6);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x120, $"M12 variant {variant}: earlier spotlight waves cannot resolve final wave");
            Cast(134.7, 0xB4B6);
            Cast(140.7, 0xB4BC);
            Cast(145.7, 0xB4BC);
            Cast(150.7, 0xB4BC);
            Cast(152.7, 0xB4B7);
            Cast(155.7, 0xB4BC);
            f.EventCast(helper, 0xB4B7);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x180, $"M12 variant {variant}: late previous Roiling Mass cannot skip next explosion");
            Cast(157.7, 0xB4B7);
            Cast(160.7, 0xB4BC);
            Cast(162.7, 0xB4B7);
            Cast(165.7, 0xB4BC);
            Cast(167.7, 0xB4B7);
            Cast(170.7, 0xB4BC);
            Cast(176.7, 0xB4C2);
            Cast(188.2, 0xB9C6);
            Cast(205.9, 0xBEBF);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x210, $"M12 variant {variant}: Act 3 waits for actual downtime");
            At(209);
            f.Targetable(f.Boss, false);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x220, $"M12 variant {variant}: targetability starts downtime");
            Cast(211, 0xBE09);
            Cast(215.6, (variant & 1) == 0 ? 0xB4A4u : 0xB4A5u);
            Cast(218, 0xB4B0);
            At(220.6);
            f.Targetable(f.Boss, true);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x260, $"M12 variant {variant}: targetability ends downtime");
            Cast(228, 0xB4AB);
            Cast(230.4, 0xB4A8);
            Cast(239.6, 0xB4D7);
            Cast(251.7, 0xBEC0);
            Cast(266.5, 0xB49D);
            Cast(279.4, 0xB49F);
            Cast(288.5, 0xB9C6);
            Cast(314.3, 0xB495);
            foreach (var time in new[] { 314.4, 317.4, 320.4, 323.4 })
            {
                Cast(time, 0xB496);
                f.EventCast(f.Boss, time < 321 ? 0xB496u : 0xB498u);
                f.Advance(0);
            }
            Cast(338.5, (variant & 1) == 0 ? 0xB4C3u : 0xB4C6u);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x10000, $"M12 variant {variant}: preserves old Slaughtershed activation anchor");
            for (var cycle = 0; cycle < 3; ++cycle)
            {
                var shift = cycle * 28.9;
                var action = ((variant + cycle) & 1) == 0 ? 0xB9BCu : 0xB9C7u;
                Cast(340.9 + shift, 0xADC9);
                Cast(349.6 + shift, 0xB4D5);
                Cast(350 + shift, 0xB49F);
                Cast(356.1 + shift, action);
                f.EventCast(helper, action);
                f.Advance(0);
                Check(f.Module.StateMachine.ActiveState?.ID == (uint)((cycle + 1) * 0x10000 + 0x40),
                    $"M12 variant {variant}: duplicate first cleave/knockback cannot skip second hit");
                Cast(360.8 + shift, action);
                var expected = cycle < 2 ? (uint)((cycle + 1) * 0x10000 + 0x100) : 0x40000u;
                Check(f.Module.StateMachine.ActiveState?.ID == expected, $"M12 variant {variant}: Slaughtershed cycle {cycle + 1} follows actual cleave/knockback events");
                if (cycle < 2) Cast(367.4 + shift, (variant & 2) == 0 ? 0xB4C3u : 0xB4C6u);
            }
            At(440);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x40000, $"M12 variant {variant}: ending does not invent a fixed HP transition");
            Cast(445, (variant & 1) == 0 ? 0xB539u : 0xB538u);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x40010, $"M12 variant {variant}: transition/enrage event accepted");
            f.Targetable(f.Boss, false);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x40020, $"M12 variant {variant}: resolution follows actual loss of targetability");
            Check(f.States().Count(x => (x.EndHint & StateMachine.StateHint.Raidwide) != 0) == 8,
                $"M12 variant {variant}: raidwide forecast flags cover Fixers, Splattersheds and Slaughtersheds");
            Check(f.States().Count(x => (x.EndHint & StateMachine.StateHint.DowntimeStart) != 0) == 2,
                $"M12 variant {variant}: both actual downtime transitions are forecast");
            Check(f.Errors.Count == 0, $"M12 variant {variant}: complete event sequence has no missing components or overdue transitions: {string.Join("; ", f.Errors)}");
        }
    }

    private static void CheckM12PlanMigration()
    {
        using var f = new Fixture("M12SLindwurm");
        f.AddPlayer(0);
        f.EnterState(0, 0);
        var planRoot = Environment.GetEnvironmentVariable("BMR_PLAN_ROOT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "pluginConfigs", "BossModReborn", "autorot", "plans");
        var path = Path.Combine(planRoot, "e799812d-bc3f-425d-96d2-c6ca03aa1211.json");
        var payload = JsonNode.Parse(File.ReadAllText(path))!["payload"]!;
        foreach (var track in payload["Modules"]!["BossMod.Autorotation.ClassMCHUtility"]!.AsObject())
        {
            if (track.Value is not JsonArray entries) continue;
            foreach (var entry in entries)
            {
                var key = (entry!["StateID"]!.GetValue<string>(), entry["TimeSinceActivation"]!.GetValue<float>());
                (uint state, float offset)? replacement = key switch
                {
                    ("0x00000000", 38) => (0x10, 22.4f),
                    ("0x00000000", 85) => (0x50, 5.8f),
                    ("0x00000000", 147) => (0x150, 1.3f),
                    ("0x00000000", 225) => (0x260, 4.4f),
                    ("0x00000000", 313) => (0x2D0, 24.5f),
                    ("0x00010000", 3) => (0x10010, 0.6f),
                    ("0x00010000", 61) => (0x30010, 0.8f),
                    _ => null
                };
                if (replacement is { } mapped)
                {
                    entry["StateID"] = $"0x{mapped.state:X8}";
                    entry["TimeSinceActivation"] = mapped.offset;
                }
            }
        }
        var plan = JsonSerializer.Deserialize<Plan>(payload.ToJsonString())!;
        var execution = new PlanExecution(f.Module, plan);
        var actions = execution.GetUpcomingPlannedActions(f.World, 0, 500);
        var expected = new[] { 38f, 85f, 147f, 225f, 313f, 341.5f, 399.5f };
        Check(actions.Count == expected.Length, "M12 native parser and PlanExecution retain all seven saved mitigation entries");
        for (var i = 0; i < expected.Length; ++i)
            Check(Math.Abs(actions[i].ActivationIn - expected[i]) < 0.01f,
                $"M12 native PlanExecution preserves mitigation at {expected[i]:F1}s (actual {actions[i].ActivationIn:F3})");
        foreach (var (id, time) in new[] { (0u, 15.6f), (0x90u, 107.3f), (0x1F0u, 188.2f), (0x280u, 239.6f), (0x2C0u, 288.5f), (0x10000u, 340.9f), (0x20000u, 369.8f), (0x30000u, 398.7f) })
        {
            f.EnterState(0, id);
            var state = execution.FindCurrentStateData();
            Check((f.State(id).EndHint & StateMachine.StateHint.Raidwide) != 0 && Math.Abs(state.EnterTime + state.Duration - time) < 0.01f,
                $"M12 native damage forecast flags the raidwide at {time:F1}s");
        }
    }
}
