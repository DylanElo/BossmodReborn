using BossMod;

internal static partial class EncounterChecks
{
    private static void CheckP4WaterStageNavigation()
    {
        using var f = new Fixture("DMU");
        DMUParty(f);
        var first = f.States().Single(s => s.Name == "Antilight + Edge of Death");
        f.EnterState(3, first.ID);
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.GrandCrossOrder");
        f.Activate("BossMod.Dawntrail.Ultimate.DMU.AntiLight");
        foreach (var action in new[] { 50068u, 50069u, 50081u, 50070u })
        {
            f.StartCast(f.Boss, action, 1);
            f.FinishCast(f.Boss);
        }
        f.Advance(12.6);
        var firstWater = f.Module.Components.Single(c => c.GetType().Name == "ForkedWater");
        Check((bool)firstWater.GetType().GetField("FirstSet")!.GetValue(firstWater)!, "DMU actual anti-light transition selects first beam-relative water stage");
        f.EnterState(3, first.ID + 0x40u);
        var secondWater = f.Module.Components.Single(c => c.GetType().Name == "ForkedWater");
        Check(!(bool)secondWater.GetType().GetField("FirstSet")!.GetValue(secondWater)!, "DMU actual Ultima Upsurge state selects second fixed water stage");
        Check(!(bool)secondWater.GetType().GetField("active")!.GetValue(secondWater)!, "DMU actual second water state waits for overlapping bait snapshot");
    }

    private static void CheckAntiLightNavigation()
    {
        foreach (var death in new[] { false, true })
        foreach (var blackWound in new[] { false, true })
        foreach (var truth in new[] { false, true })
        foreach (var swap in new[] { false, true })
        {
            using var f = new Fixture("DMU");
            DMUParty(f);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.GrandCrossOrder");
            var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.AntiLight");
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.EdgeOfDeath");
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU anti-light: unknown colors do not guess");
            var neo = f.AddActor(0x700, 0x4C36, ActorType.Enemy, Class.None, new(100, 80));
            for (var set = 0; set < 3; ++set)
            {
                f.Status(neo, 0, new(2056, truth ? (ushort)0x462 : (ushort)0x461, f.Now.AddSeconds(30), 0));
                f.StartCast(neo, 47892, 9);
                f.FinishCast(neo);
                f.Status(neo, 0, default);
            }
            var player = f.World.Party[0]!;
            f.Status(player, 0, new(death ? 1382u : 454u, 0, f.Now.AddSeconds(6), 0));
            f.Status(player, 1, new(blackWound ? 4888u : 4887u, 0, f.Now.AddSeconds(6), 0));
            var white = f.AddActor(0x701, 0, ActorType.Enemy, Class.None, new(89.5f, 80));
            var black = f.AddActor(0x702, 0, ActorType.Enemy, Class.None, new(110.5f, 80));
            var edge = f.AddActor(0x703, 0, ActorType.Enemy, Class.None, new(100, 80));
            f.StartCast(white, 50068, 5.5f);
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU anti-light: one missing beam does not guess");
            f.StartCast(black, 50069, 5.5f);
            f.StartCast(neo, swap ? 50066u : 50081u, 5);
            f.StartCast(edge, 50070, 5.5f);
            component.Update();
            var requireWhite = (death == blackWound) ^ !truth ^ swap;
            var selected = new WPos(requireWhite ? 90 : 110, 100);
            var wrong = new WPos(requireWhite ? 110 : 90, 100);
            var hints = f.ModuleHints(0, PartyRolesConfig.Assignment.MT);
            Check(hints.ForbiddenZones.All(z => z.shapeDistance.Distance(selected) >= 0),
                $"DMU anti-light: death={death} woundBlack={blackWound} truth={truth} swap={swap} required hit survives edge avoidance");
            Check(hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(wrong) < 0), "DMU anti-light: opposite-color lane rejected");
            Check(hints.ForbiddenZones.Any(z => z.shapeDistance.Distance(new(100, 100)) < 0), "DMU anti-light: simultaneous lethal edge retained");
            var deadline = f.Hints(component, 0).ForbiddenZones[0].activation;
            Check(Math.Abs((deadline - f.Now).TotalSeconds - (5.5f + ActorCastInfo.NPCFinishDelay)) < 0.001, "DMU anti-light: real beam cast deadline");
            f.FinishCast(white);
            f.FinishCast(black);
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU anti-light: actual cast completion releases required lane");
        }
    }

    private static void CheckP4BaitSnapshotNavigation()
    {
        foreach (var inferno in new[] { false, true })
        foreach (var truth in new[] { false, true })
        {
            using var f = new Fixture("DMU");
            DMUParty(f);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.TsunamiInfernoOrder");
            var name = inferno ? "Inferno" : "Tsunami";
            var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU." + name);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU." + name + "Baits");
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, $"DMU {name}: unknown debuffs do not move");
            var chaos = f.AddActor(0x800, 0x4C33, ActorType.Enemy, Class.None, new(100, 80));
            f.Status(chaos, 0, new(2056, truth ? (ushort)0x460 : (ushort)0x45F, f.Now.AddSeconds(20), 0));
            for (var i = 0; i < 8; ++i)
                f.Status(f.World.Party[i]!, 0, new(inferno ? 5547u : 5548u, 0, f.Now.AddSeconds(8), 0));
            component.Update();
            for (var i = 0; i < 8; ++i)
            {
                var zones = f.ModuleHints(i, (PartyRolesConfig.Assignment)i).ForbiddenZones;
                Check(zones.Count > 0 && zones.All(z => z.shapeDistance.Distance(f.Module.Center) >= 0) && zones[^1].shapeDistance.Distance(new(110, 100)) < 0,
                    $"DMU {name}: actual {(truth ? "truth" : "lie")} slot{i} centers before snapshot");
                Check(zones[^1].activation == f.Now.AddSeconds(8), $"DMU {name}: tracked debuff snapshot deadline");
            }
            var circle = inferno == truth;
            var action = inferno ? (circle ? 47906u : 47907u) : (circle ? 47909u : 47908u);
            var helper = f.AddActor(0x801, 0, ActorType.Enemy, Class.None, f.Module.Center);
            f.StartCast(helper, action, 5);
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, $"DMU {name}: actual ground cast releases centering");
            var after = f.ModuleHints(0, PartyRolesConfig.Assignment.MT);
            Check(after.ForbiddenZones.Any(z => z.shapeDistance.Distance(circle ? f.Module.Center : new WPos(110, 100)) < 0),
                $"DMU {name}: actual {(circle ? "circle" : "donut")} ground danger takes control after snapshot");
        }
    }
}
