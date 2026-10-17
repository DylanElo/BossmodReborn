using BossMod;
using BossMod.Autorotation;
using System.Text.Json;
using System.Text.Json.Nodes;
using M11 = BossMod.Dawntrail.Savage.M11STheTyrant;

internal static partial class EncounterChecks
{
    static partial void CheckM11()
    {
        // Independent source: cactbot r11s.txt ability effects, with virtual branch offsets removed.
        // These are source-derived synthetic traces, not locally recorded savage replays.
        foreach (var axeFirst in new[] { false, true })
        foreach (var cometFirst in new[] { false, true })
            M11Trace(axeFirst, cometFirst);
        CheckM11StagedPlan();
    }

    private static void CheckM11StagedPlan()
    {
        var planRoot = Environment.GetEnvironmentVariable("BMR_PLAN_ROOT");
        if (string.IsNullOrEmpty(planRoot)) return;
        using var f = new Fixture("M11STheTyrant");
        f.AddPlayer(0);
        f.EnterState(0, 0);
        var path = Path.Combine(planRoot, "3e8b815e-28ea-47da-afae-a8fedfb31b11.json");
        var payload = JsonNode.Parse(File.ReadAllText(path))!["payload"]!;
        var plan = JsonSerializer.Deserialize<Plan>(payload.ToJsonString())!;
        var execution = new PlanExecution(f.Module, plan);
        var actions = execution.GetUpcomingPlannedActions(f.World, 0, 700).OrderBy(a => a.ActivationIn).ToArray();
        var expected = new[] { 7.6f, 86.6f, 119.6f, 206.6f, 297.6f, 326.6f, 392.9f, 482.9f, 559.1f, 572.1f };
        Check(actions.Length == expected.Length, "M11 staged JSON retains all ten actual saved mitigation actions");
        for (var i = 0; i < expected.Length; ++i)
            Check(Math.Abs(actions[i].ActivationIn - expected[i]) < 0.01f,
                $"M11 native PlanExecution preserves staged mitigation at {expected[i]:F1}s");
        for (var mi = 0; mi < plan.Modules.Count; ++mi)
        for (var ti = 0; ti < plan.Modules[mi].Tracks.Count; ++ti)
        foreach (var entry in plan.Modules[mi].Tracks[ti])
        {
            f.EnterState(0, entry.StateID);
            if (entry.StateID == 1) f.StartCast(f.Boss, (uint)M11.AID.CrownOfArcadia, 5);
            f.Advance(entry.TimeSinceActivation + 0.01f);
            var actual = execution.ActiveStrategyOverrides(mi, f.World, 0).Values[ti];
            Check(actual is StrategyValueTrack actualTrack && entry.Value is StrategyValueTrack expectedTrack
                && actualTrack.Option == expectedTrack.Option && Math.Abs(actualTrack.ExpireIn - 2.99f) < 0.02f,
                $"M11 staged plan actually activates native strategy at {entry.StateID:X}+{entry.TimeSinceActivation:F1}s (actual {actual}, expected {entry.Value})");
            f.FinishCast(f.Boss);
        }
    }

    private static void M11Trace(bool axeFirst, bool cometFirst)
    {
        using var f = new Fixture("M11STheTyrant");
        var jobs = new[] { Class.PLD, Class.WAR, Class.WHM, Class.SCH, Class.MNK, Class.DRG, Class.MCH, Class.BLM };
        for (var i = 0; i < jobs.Length; ++i) f.AddPlayer(i, jobs[i]);
        f.EnterState(0, 0);
        var start = f.Now;
        void At(double time) => f.Advance(time - (f.Now - start).TotalSeconds);
        void CastStart(double time, uint state, M11.AID aid, float duration)
        {
            At(time);
            Check(f.Module.StateMachine.ActiveState?.ID == state, $"M11 cast anchor {state:X}");
            f.StartCast(f.Boss, (uint)aid, duration);
            f.Advance(0);
        }
        void CastEnd(double time, uint state)
        {
            At(time);
            Check(f.Module.StateMachine.ActiveState?.ID == state, $"M11 cast end {state:X}");
            f.FinishCast(f.Boss);
            f.Advance(0);
        }
        void Effect(double time, uint state, M11.AID aid)
        {
            At(time);
            Check(f.Module.StateMachine.ActiveState?.ID == state, $"M11 event anchor {state:X}");
            f.EventCast(f.Boss, (uint)aid);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID != state, $"M11 observed effect advances {state:X}");
        }
        bool Active(string name) => f.Module.Components.Any(c => c.GetType().Name == name);

        Check(f.Module.Info!.PlanLevel == 100, "M11 level 100 planning enabled");
        Check(!f.States().Any(s => s.Name == "Final Phase" || s.Duration >= 300), "M11 no coarse placeholder states");
        var entryTimes = new Dictionary<uint, float>();
        var elapsed = 0f;
        for (var state = f.Module.StateMachine.Phases[0].InitialState; state != null; state = state.NextStates?.SingleOrDefault())
        {
            entryTimes[state.ID] = elapsed;
            elapsed += state.Duration;
        }
        var migrations = new (float OldEntry, float OldOffset, uint State, float Offset)[]
        {
            (5.6f, 2, 1, 2), (10.6f, 76, 0x1050, 6.8f), (10.6f, 109, 0x1080, 3.4f),
            (10.6f, 196, 0x1110, 14.2f), (10.6f, 287, 0x1310, 5.5f), (10.6f, 316, 0x1340, 6.4f),
            (370.9f, 22, 0x1410, 3.9f), (370.9f, 112, 0x1500, 3.5f),
            (529.1f, 30, 0x1720, 1.9f), (529.1f, 43, 0x1740, 1)
        };
        foreach (var migration in migrations)
            Check(Math.Abs(entryTimes[migration.State] + migration.Offset - migration.OldEntry - migration.OldOffset) < 0.01f
                && migration.Offset < f.State(migration.State).Duration,
                $"M11 migrated plan anchor {migration.State:X} preserves observed timing without clamping");
        CastStart(5.6, 0, M11.AID.CrownOfArcadia, 5);
        CastEnd(10.6, 1);
        Check(Active("AssaultWeaponTimeline") && Active("AssaultWeaponSafeSpots"), "M11 original weapon AI active after opener");
        f.EventCast(f.Boss, (uint)M11.AID.AutoAttack);
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x10, "M11 unrelated event does not advance mechanic");
        Effect(17.7, 0x10, axeFirst ? M11.AID.RawSteelTrophyAxe : M11.AID.RawSteelTrophyScythe);
        Effect(25.1, 0x1000, axeFirst ? M11.AID.RawSteel_Impact : M11.AID.RawSteel_HeavyHitter);
        Effect(38.4, 0x1010, M11.AID.TrophyWeapons);
        Effect(48.2, 0x1020, M11.AID.AssaultEvolved);
        Effect(72.7, 0x1030, M11.AID.TrophyWeapons);
        Effect(79.8, 0x1040, M11.AID.VoidStardust);
        Effect(89.9, 0x1050, cometFirst ? M11.AID.Comet : M11.AID.CrushingComet);
        Effect(92.7, 0x1060, M11.AID.AssaultEvolved);
        Effect(116.2, 0x1070, cometFirst ? M11.AID.CrushingComet : M11.AID.Comet);
        Effect(122.2, 0x1080, M11.AID.CrownOfArcadia);
        Effect(134.6, 0x1090, M11.AID.DanceOfDominationTrophy);
        Check(Math.Abs((f.Module.StateMachine.NextTransitionWithFlag(StateMachine.StateHint.Raidwide) - f.Now).TotalSeconds - 6.7) < 0.01,
            "M11 raidwide forecast resolves to observed first hit, not trophy animation");
        Effect(141.3, 0x1098, M11.AID.DanceOfDomination1);
        Effect(145.5, 0x10A0, M11.AID.DanceOfDomination3);
        Effect(150.5, 0x10B0, M11.AID.EyeOfTheHurricane);
        Effect(156.4, 0x10C0, axeFirst ? M11.AID.RawSteelTrophyScythe : M11.AID.RawSteelTrophyAxe);
        Effect(163.8, 0x10D0, axeFirst ? M11.AID.RawSteel_HeavyHitter : M11.AID.RawSteel_Impact);
        Effect(178.9, 0x10E0, M11.AID.Charybdistopia);
        Effect(184.6, 0x10F0, M11.AID.UltimateTrophyWeapons);
        Effect(192.4, 0x1100, M11.AID.AssaultApex);
        Effect(227.3, 0x1110, M11.AID.ImmortalReign);
        Effect(240.4, 0x1120, M11.AID.OneAndOnly1);
        Effect(252.6, 0x1200, M11.AID.GreatWallOfFire);
        Effect(266.1, 0x1210, M11.AID.OrbitalOmen);
        Effect(274.8, 0x1220, M11.AID.FireAndFury);
        Effect(292.1, 0x1300, M11.AID.Meteorain);
        Effect(300.1, 0x1310, M11.AID.FearsomeFireball1);
        f.EventCast(f.Boss, (uint)M11.AID.FearsomeFireball1);
        f.Advance(0.2);
        f.EventCast(f.Boss, (uint)M11.AID.FearsomeFireball1);
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x1320, "M11 clustered previous Fireball effects cannot skip next volley");
        Effect(310.2, 0x1320, M11.AID.FearsomeFireball1);
        Effect(320.2, 0x1330, M11.AID.FearsomeFireball1);
        Effect(330.2, 0x1340, M11.AID.FearsomeFireball1);
        Effect(342.9, 0x1350, M11.AID.TripleTyrannhilation2);
        Effect(344.0, 0x1358, M11.AID.Shockwave);
        At(345.4);
        f.EventCast(f.Boss, (uint)M11.AID.Shockwave);
        At(346.8);
        f.EventCast(f.Boss, (uint)M11.AID.Shockwave);
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x1360, "M11 repeated Shockwaves cannot replace the observed sequence ending");
        Effect(347.6, 0x1360, M11.AID.TripleTyrannhilationEnd);
        Effect(359.8, 0x1370, M11.AID.Flatliner);
        Effect(361.8, 0x1380, M11.AID.Flatliner1);
        CastStart(370.9, 0x1390, M11.AID.MajesticMeteor, 5);
        Check(!Active("AssaultWeaponTimeline") && Active("MeteorMechanicHints"), "M11 weapon AI switches to split meteor AI on observed cast");
        CastEnd(375.9, 0x20);
        Effect(389.0, 0x1400, M11.AID.ExplosionTower);
        Effect(403.9, 0x1410, M11.AID.FireBreath1);
        Effect(421.1, 0x1420, M11.AID.ExplosionTower);
        Effect(436.0, 0x1430, M11.AID.MajesticMeteowrathFirst);
        Effect(445.3, 0x1440, M11.AID.MassiveMeteor);
        Effect(459.1, 0x1450, M11.AID.ArcadionAvalanche_Pick1);
        Effect(468.7, 0x1460, M11.AID.ArcadionAvalanche_Smash1);
        Effect(479.4, 0x1470, M11.AID.CrownOfArcadia);
        Effect(490.5, 0x1500, M11.AID.GreatWallOfFire);
        Effect(504.0, 0x1510, M11.AID.OrbitalOmen);
        Effect(512.7, 0x1520, M11.AID.FireAndFury);
        Effect(521.8, 0x1600, M11.AID.CrownOfArcadia);
        CastStart(529.1, 0x1610, M11.AID.EclipticStampede, 5);
        Check(!Active("MeteorMechanicHints") && Active("AtomicImpactBaitPath"), "M11 meteor AI switches to final-phase AI on observed cast");
        CastEnd(534.1, 0x30);
        Effect(541.2, 0x1700, M11.AID.AtomicImpact);
        Effect(557.2, 0x1710, M11.AID.CosmicKissTower);
        Effect(566.8, 0x1720, M11.AID.MajesticMeteowrathSecond);
        Effect(571.1, 0x1730, axeFirst ? M11.AID.TwoWayFireballEnd : M11.AID.FourWayFireballEnd);
        Effect(579.4, 0x1740, M11.AID.CrownOfArcadia);
        Effect(593.3, 0x1800, M11.AID.HeartbreakKickTower);
        Effect(612.4, 0x1810, M11.AID.HeartbreakKickTower);
        Effect(633.5, 0x1820, M11.AID.HeartbreakKickTower);
        CastStart(651.6, 0x1900, M11.AID.EnrageCast, 8);
        CastEnd(659.6, 0x1901);
        Check(f.Module.StateMachine.ActiveState?.Name == "Heartbreaker enrage", "M11 full source trace reaches enrage");
        Check(f.Errors.Count == 0, "M11 trace has no missing components, unexpected casts or overdue transitions");
        f.Module.StateMachine.Reset();
        Check(f.Module.StateMachine.ActiveState == null && f.Module.Components.Count == 0, "M11 reset clears timeline and mechanic components");
    }
}
