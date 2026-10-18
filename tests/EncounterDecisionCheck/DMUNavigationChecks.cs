using BossMod;
using BossMod.Dawntrail.Ultimate.DMU;

internal static partial class EncounterChecks
{
    static partial void CheckDMUNavigation()
    {
        RunGroup("DMU teleport", CheckTeleTrouncingNavigation);
        RunGroup("DMU Yarn teleport strategy", CheckYarnTeleStrategy);
        RunGroup("DMU Yarn melee inner", CheckYarnMeleeInnerNavigation);
        RunGroup("DMU Yarn gravity", CheckYarnGravitasNavigation);
        RunGroup("DMU EU boss split", () => CheckDecisiveBattleNavigation());
        RunGroup("DMU Yarn boss split", () => CheckDecisiveBattleNavigation(true));
        RunGroup("DMU forked water", CheckForkedWaterNavigation);
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            RunGroup($"DMU Yarn beam first {rotation}", () => CheckForkedWaterNavigation(true, true, rotation));
            RunGroup($"DMU Yarn fixed second {rotation}", () => CheckForkedWaterNavigation(true, false, rotation));
        }
        RunGroup("DMU Yarn unknown beam", () => CheckForkedWaterNavigation(true, true, 0, false));
        RunGroup("DMU P4 required color hits", CheckAntiLightNavigation);
        RunGroup("DMU P4 bait snapshot lifecycle", CheckP4BaitSnapshotNavigation);
        RunGroup("DMU P4 state-owned route stages", CheckP4WaterStageNavigation);
        RunGroup("DMU P5 observed flood navigation", CheckFloodNavigation);
    }

    private static void DMUParty(Fixture f)
    {
        var config = Service.Config.Get<PartyRolesConfig>();
        config.Assignments.Clear();
        Class[] jobs = [Class.WAR, Class.PLD, Class.WHM, Class.SCH, Class.VPR, Class.SAM, Class.MCH, Class.BRD];
        for (var i = 0; i < 8; ++i)
        {
            f.AddPlayer(i, jobs[i]);
            config.Assignments[(ulong)i + 1] = (PartyRolesConfig.Assignment)i;
        }
    }

    private static void CheckTeleTrouncingNavigation()
    {
        foreach (var strategy in Enum.GetValues<DMUConfig.P1TeleTrouncingStrategy>().Where(x => x != DMUConfig.P1TeleTrouncingStrategy.TeleTrouncingNone))
        {
            Service.Config.Get<DMUConfig>().P1TeleTrouncing = strategy;
            using var f = new Fixture("DMU");
            DMUParty(f);
            var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.TeleTrouncing");
            var player = f.World.Party[0]!;
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, $"DMU {strategy}: unknown arrows do not move");
            f.Status(player, 0, new(4877, 0, f.Now.AddSeconds(7), 0));
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, $"DMU {strategy}: incomplete arrows do not move");
            f.Status(player, 1, new(5080, 0, f.Now.AddSeconds(10), 0));
            var first = f.Hints(component, 0).ForbiddenZones.Single();
            var firstPoint = strategy == DMUConfig.P1TeleTrouncingStrategy.Modified_Xolo ? new WPos(87.750f, 88.030f) : new WPos(112, 94);
            var secondPoint = strategy == DMUConfig.P1TeleTrouncingStrategy.Modified_Xolo ? new WPos(87.750f, 93.570f) : new WPos(112, 100);
            Check(first.shapeDistance.Distance(firstPoint) > 0 && first.shapeDistance.Distance(secondPoint) < 0, $"DMU {strategy}: actual first arrow destination");
            Check(first.activation == f.Now.AddSeconds(7), $"DMU {strategy}: first arrow snapshot deadline");
            Check(f.Hints(component, 1).ForbiddenZones.Count == 0, $"DMU {strategy}: local arrow route not shared with foreign slot");
            f.Status(player, 0, default);
            var second = f.Hints(component, 0).ForbiddenZones.Single();
            Check(second.shapeDistance.Distance(secondPoint) > 0 && second.shapeDistance.Distance(firstPoint) < 0, $"DMU {strategy}: status loss advances actual route");
            Check(second.activation == f.Now.AddSeconds(10), $"DMU {strategy}: second arrow snapshot deadline");
            f.Status(player, 1, default);
            Check(f.Hints(component, 0).ForbiddenZones.Count == 0, $"DMU {strategy}: finished arrows release navigation");
        }
        Service.Config.Get<DMUConfig>().P1TeleTrouncing = DMUConfig.P1TeleTrouncingStrategy.Modified_Xolo;
    }

    private static void CheckDecisiveBattleNavigation(bool yarn = false)
    {
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = yarn;
        using var f = new Fixture("DMU");
        DMUParty(f);
        var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.TheDecisiveBattle");
        var chaos = f.AddActor(0x201, 0x4C33, ActorType.Enemy, Class.None, new(90, 100));
        var exdeath = f.AddActor(0x202, 0x4C35, ActorType.Enemy, Class.None, new(110, 100));
        f.StartCast(chaos, 49890, 3);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU decisive battle: partial boss observations do not route");
        f.StartCast(exdeath, 49891, 3);
        for (var i = 0; i < 8; ++i)
        {
            var assignment = (PartyRolesConfig.Assignment)i;
            var zone = f.Hints(component, i, assignment).ForbiddenZones.Single();
            var goesChaos = yarn ? i is 1 or 3 or 4 or 5 : i is 0 or 2 or 4 or 5;
            Check(zone.shapeDistance.Distance(goesChaos ? chaos.Position : exdeath.Position) > 0 && zone.shapeDistance.Distance(goesChaos ? exdeath.Position : chaos.Position) < 0,
                $"DMU {(yarn ? "Yarn" : "EU")} decisive battle: actual {assignment} boss selection");
            Check(Math.Abs((zone.activation - f.Now).TotalSeconds - (3f + ActorCastInfo.NPCFinishDelay)) < 0.001, $"DMU decisive battle: {assignment} NPC-adjusted cast deadline");
        }
        Check(f.Hints(component, 0, PartyRolesConfig.Assignment.Unassigned).ForbiddenZones.Count == 0, "DMU decisive battle: unassigned role does not guess");
        Service.Config.Get<PartyRolesConfig>().Assignments.Clear();
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU decisive battle: incomplete party assignments do not guess");
        f.FinishCast(chaos);
        Check(f.Hints(component, 0).ForbiddenZones.Count == 0, "DMU decisive battle: cast finish releases route");
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }

    private static void CheckYarnTeleStrategy()
    {
        var config = Service.Config.Get<DMUConfig>();
        config.P1TeleTrouncing = DMUConfig.P1TeleTrouncingStrategy.Freaky_Arrow;
        config.YarnPirenStrategy = true;
        Check(config.TeleTrouncingStrategy() == DMUConfig.P1TeleTrouncingStrategy.Modified_Xolo, "DMU Yarn: P1 arrows always use Modified Xolo");
        config.P1TeleTrouncing = DMUConfig.P1TeleTrouncingStrategy.TeleTrouncingNone;
        Check(config.TeleTrouncingStrategy() == DMUConfig.P1TeleTrouncingStrategy.TeleTrouncingNone, "DMU Yarn: None still disables the arrow hints");
        config.YarnPirenStrategy = false;
        config.P1TeleTrouncing = DMUConfig.P1TeleTrouncingStrategy.Freaky_Arrow;
        Check(config.TeleTrouncingStrategy() == DMUConfig.P1TeleTrouncingStrategy.Freaky_Arrow, "DMU non-Yarn: picked arrow strategy is kept");
        config.P1TeleTrouncing = DMUConfig.P1TeleTrouncingStrategy.Modified_Xolo;
    }

    private static void CheckYarnMeleeInnerNavigation()
    {
        var expected = new WPos[] { new(95.5f, 95.5f), new(104.5f, 95.5f), new(89.5f, 110.5f), new(110.5f, 110.5f), new(95.5f, 104.5f), new(104.5f, 104.5f), new(89.5f, 89.5f), new(110.5f, 89.5f) };
        foreach (var yarn in new[] { true, false })
        {
            Service.Config.Get<DMUConfig>().YarnPirenStrategy = yarn;
            using var f = new Fixture("DMU");
            DMUParty(f);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.DoubleTroubleTrapStacksTeleTrouncing");
            var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.TeleTrouncingTethers");
            Check(f.Hints(component, 0, PartyRolesConfig.Assignment.MT).ForbiddenZones.Count == 0, "DMU Yarn melee-inner: no route before the sleep tethers are observed");
            // Actual statue tethers on every support sleep target.
            for (var i = 0; i < 4; ++i)
            {
                var statue = f.AddActor((ulong)(0x400 + i), 0x4C31, ActorType.Enemy, Class.None, new(107, 43));
                f.World.Execute(new ActorState.OpTether(statue.InstanceID, new(45, f.World.Party[i]!.InstanceID)));
            }
            for (var i = 0; i < 8; ++i)
            {
                var assignment = (PartyRolesConfig.Assignment)i;
                var zones = f.Hints(component, i, assignment).ForbiddenZones;
                if (yarn)
                {
                    Check(zones.Count == 1 && zones[0].shapeDistance.Distance(expected[i]) >= 0 && zones[0].shapeDistance.Distance(new(100, 100)) < 0,
                        $"DMU Yarn melee-inner: actual {assignment} route is its fixed spread spot");
                    var ring = i is 0 or 1 or 4 or 5 ? 7f : 15f;
                    Check((expected[i] - f.Module.Center).Length() < ring, $"DMU Yarn melee-inner: {assignment} stays in selected ring");
                }
                else
                {
                    Check(zones.Count == 1 && zones[0].shapeDistance.Distance(expected[i]) < 0, $"DMU non-Yarn: {assignment} does not use the Yarn spread spot");
                }
            }
            Check(f.Hints(component, 0, PartyRolesConfig.Assignment.Unassigned).ForbiddenZones.Count == 0, "DMU melee-inner: unassigned role does not guess a spot");
            if (!yarn)
                continue;
            // Sleep landing releases the route.
            for (var i = 0; i < 4; ++i)
                f.Status(f.World.Party[i]!, 0, new(4894, 0, f.Now.AddSeconds(30), 0));
            Check(f.Hints(component, 0, PartyRolesConfig.Assignment.MT).ForbiddenZones.Count == 0, "DMU Yarn melee-inner: sleep debuff releases route");
        }
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }

    private static void CheckForkedWaterNavigation() => CheckForkedWaterNavigation(false);

    private static void CheckForkedWaterNavigation(bool yarn, bool firstSet = false, int rotation = 0, bool observeNeo = true)
    {
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = yarn;
        foreach (var truth in new[] { true, false })
        {
            using var f = new Fixture("DMU");
            DMUParty(f);
            if (yarn)
                f.EnterState(3, f.Module.StateMachine.Phases[3].InitialState.ID);
            f.Activate("BossMod.Dawntrail.Ultimate.DMU.GrandCrossOrder");
            var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.ForkedWater");
            component.GetType().GetField("FirstSet")!.SetValue(component, firstSet);
            var beamNorth = new WDir(0, -1).Rotate(rotation.Degrees());
            var neo = f.AddActor(0x301, 0x4C36, ActorType.Enemy, Class.None, f.Module.Center + beamNorth * 20f);
            if (yarn && observeNeo)
                f.Module.Update();
            f.Status(neo, 0, new(2056, truth ? (ushort)0x462 : (ushort)0x461, f.Now.AddSeconds(20), 0));
            f.Status(f.World.Party[2]!, 0, new(5545, 0, f.Now.AddSeconds(8), 0));
            f.Status(f.World.Party[6]!, 0, new(5544, 0, f.Now.AddSeconds(8), 0));
            component.Update();
            if (yarn && firstSet && !observeNeo)
            {
                var missing = f.Hints(component, 2, PartyRolesConfig.Assignment.H1);
                var native = f.Hints(component, 2, PartyRolesConfig.Assignment.Unassigned);
                Check(missing.ForbiddenZones.Count == native.ForbiddenZones.Count, "DMU Yarn first water: unobserved Neo does not guess a beam route");
                Check(missing.ForbiddenZones.Count > 0, "DMU Yarn first water: unknown beam retains native stack/spread safety");
                continue;
            }
            foreach (var slot in Enumerable.Range(0, 8))
            {
                var zones = f.Hints(component, slot, (PartyRolesConfig.Assignment)slot).ForbiddenZones;
                var expected = (slot, truth) switch { (2, false) => new WPos(88, 100), (6, true) => new WPos(112, 100), _ => slot < 4 ? new WPos(100, 88) : new WPos(100, 112) };
                if (yarn && firstSet)
                    expected = f.Module.Center + (expected - f.Module.Center).Rotate(rotation.Degrees());
                Check(zones.Count > 0 && zones[^1].shapeDistance.Distance(expected) > 0 && zones[^1].shapeDistance.Distance(new(100, 100)) < 0,
                    $"DMU forked water: actual {(truth ? "truth" : "lie")} slot {slot} stack/spread destination");
                Check(f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot).ForbiddenZones.All(z => z.shapeDistance.Distance(expected) >= 0),
                    $"DMU forked water: {(truth ? "truth" : "lie")} slot {slot} selected destination survives combined stack/spread safety");
            }
            component.GetType().GetField("active")!.SetValue(component, false);
            Check(f.Hints(component, 2, PartyRolesConfig.Assignment.H1).ForbiddenZones.Count == 0, "DMU forked water: overlap gate suppresses inherited and route movement");
            component.GetType().GetField("active")!.SetValue(component, true);
            for (var i = 0; i < 4; ++i) f.EventCast(neo, 47896);
            component.Update();
            Check(f.Hints(component, 2, PartyRolesConfig.Assignment.H1).ForbiddenZones.Count == 0, "DMU forked water: actual fourth cast releases route");
        }
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }

    private static void CheckYarnGravitasNavigation()
    {
        foreach (var yarn in new[] { true, false })
        {
            Service.Config.Get<DMUConfig>().YarnPirenStrategy = yarn;
            using var f = new Fixture("DMU");
            DMUParty(f);
            var component = f.Activate("BossMod.Dawntrail.Ultimate.DMU.Gravitas");
            component.GetType().GetField("side")!.SetValue(component, P1GravitasData.Side.NORTH);
            for (var i = 0; i < 4; ++i)
            {
                var statue = f.AddActor((ulong)(0x500 + i), 0x4C31, ActorType.Enemy, Class.None, new(102.5f, 22.5f));
                f.World.Execute(new ActorState.OpTether(statue.InstanceID, new(45, f.World.Party[i]!.InstanceID)));
            }
            // Default plays the first stack on the north set; Yarn starts south.
            WPos mine = yarn ? new(100, 109) : new(100, 91), other = yarn ? new(100, 91) : new(100, 109);
            for (var i = 0; i < 8; ++i)
            {
                var zones = f.Hints(component, i, (PartyRolesConfig.Assignment)i).ForbiddenZones;
                Check(zones.Count > 0 && zones[^1].shapeDistance.Distance(mine) >= 0 && zones[^1].shapeDistance.Distance(other) < 0,
                    $"DMU {(yarn ? "Yarn" : "default")} gravity: first stack route for slot{i}");
            }
        }
        Service.Config.Get<DMUConfig>().YarnPirenStrategy = false;
    }
}
