using BossMod;
using M9 = BossMod.Dawntrail.Savage.M09SVampFatale;

internal static partial class EncounterChecks
{
    static partial void CheckM09Ultrasonic()
    {
        for (var turn = 0; turn < 8; ++turn)
            foreach (var spread in new[] { true, false })
                foreach (var wave in new[] { 0, 1 })
                    foreach (var mirror in new[] { false, true })
                {
                    var rotation = turn * 45f;
                    RunGroup($"M9 Ultrasonic {rotation} spread={spread} wave={wave} mirror={mirror}", () => CheckUltrasonicWave(rotation, spread, wave, mirror));
                }
        RunGroup("M9 Ultrasonic incomplete observations", CheckUltrasonicIncomplete);
        RunGroup("M9 Ultrasonic dead players", CheckUltrasonicDeaths);
    }

    private static WPos UltrasonicPosition(float degrees, float radius = 12f) => new WPos(100, 100) + radius * degrees.Degrees().ToDirection();

    private static void CheckUltrasonicWave(float rotation, bool spread, int wave, bool mirror)
    {
        using var f = new Fixture("M09SVampFatale");
        AssignNavigationParty(f);
        var config = Service.Config.Get<M9.M09SVampFataleConfig>();
        config.Assignments = M9.M09SVampFataleConfig.AssignmentStrategy.Game8;
        config.EnableTowerOrder = true;
        var tank = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.UltrasonicSpreadTank");
        var rest = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.UltrasonicSpreadRest");
        var amp = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.UltrasonicAmp");
        var cellAngles = new[] { 180f, -45f, 90f, 45f };
        var cells = new Actor[4];
        var targets = new WPos[8];
        for (var role = 0; role < 4; ++role)
        {
            var inside = role * 2 + wave;
            var outside = role * 2 + (1 - wave);
            cells[role] = f.AddActor((ulong)(0x600 + role), (uint)(role == 0 ? M9.OID.CharnelCell2 : role == 1 ? M9.OID.CharnelCell : M9.OID.CharnelCell1), ActorType.Enemy, Class.None, UltrasonicPosition(cellAngles[role] * (mirror ? -1 : 1) + rotation));
            targets[inside] = cells[role].Position;
            var direction = !spread || role == 0 ? -112.5f : role == 1 ? 0f : 135f;
            if (mirror)
                direction = !spread || role == 0 ? 112.5f : role == 1 ? -135f : 0f;
            targets[outside] = UltrasonicPosition(direction + rotation, 7f);
            f.Status(f.World.Party[inside]!, 0, new((uint)M9.SID.HellAwaits, 0, f.Now.AddSeconds(20), 0));
            if (wave == 1)
                f.Status(f.World.Party[outside]!, 0, new((uint)M9.SID.HellAwaits, 0, f.Now.AddSeconds(0.5), 0));
        }
        for (var slot = 0; slot < 8; ++slot)
            f.World.Execute(new ActorState.OpMove(f.World.Party[slot]!.InstanceID, targets[slot].ToVec4()));
        f.StartCast(f.Boss, (uint)(spread ? M9.AID.UltrasonicSpreadCast : M9.AID.UltrasonicAmpCast), 5);
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot, (PartyRolesConfig.Assignment)slot);
            Check(!NavigationForbidden(hints, targets[slot]), $"M9 actual combined cone/cage decisions allow assigned slot {slot}");
            Check(hints.ForbiddenZones.All(z => Math.Abs((z.activation - f.Now).TotalSeconds - 5 - ActorCastInfo.NPCFinishDelay) < 0.001), $"M9 slot {slot} uses observed cast finish");
            if (slot % 2 != wave)
            {
                var wrong = UltrasonicPosition((!spread || slot / 2 == 0 ? (mirror ? -135f : 0f) : mirror ? 112.5f : -112.5f) + rotation, 7f);
                Check(NavigationForbidden(hints, wrong), $"M9 outside slot {slot} rejects another role lane");
                foreach (var cell in cells)
                    Check(NavigationForbidden(hints, cell.Position), $"M9 outside slot {slot} avoids actual cage");
            }
        }
        config.EnableTowerOrder = false;
        Check(!f.ModuleHints(1 - wave).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), "M9 existing AI opt-out preserves avoidance without authored route");
        config.EnableTowerOrder = true;
        config.Assignments = M9.M09SVampFataleConfig.AssignmentStrategy.Hector;
        Check(!f.ModuleHints(1 - wave).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), "M9 Game8 lane rules do not override Hector choice");
        config.Assignments = M9.M09SVampFataleConfig.AssignmentStrategy.Game8;
        // Releasing/destroying cages during the cast must not rotate a solved assignment.
        foreach (var cell in cells)
            f.World.Execute(new ActorState.OpDead(cell.InstanceID, true));
        for (var slot = 0; slot < 8; ++slot)
            Check(!NavigationForbidden(f.ModuleHints(slot), targets[slot]), $"M9 stable route survives cell death for slot {slot}");
        f.EventCast(f.Boss, (uint)(spread ? M9.AID.UltrasonicSpreadTank : M9.AID.UltrasonicAmp));
        if (spread)
            f.EventCast(f.Boss, (uint)M9.AID.UltrasonicSpreadRest);
        Check(f.ModuleHints(1 - wave).ForbiddenZones.Count == 0, "M9 real effect releases all route constraints");
        config.EnableTowerOrder = false;
    }

    private static void CheckUltrasonicIncomplete()
    {
        using var f = new Fixture("M09SVampFatale");
        AssignNavigationParty(f);
        var config = Service.Config.Get<M9.M09SVampFataleConfig>();
        config.Assignments = M9.M09SVampFataleConfig.AssignmentStrategy.Game8;
        config.EnableTowerOrder = true;
        var amp = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.UltrasonicAmp");
        f.StartCast(f.Boss, (uint)M9.AID.UltrasonicAmpCast, 5);
        Check(f.Hints(amp, 1).ForbiddenZones.Count > 0, "M9 absent statuses do not exempt unverified aggregate allies from cone");
        Check(!f.Hints(amp, 0).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), "M9 absent statuses/cells do not invent a route");
        f.FinishCast(f.Boss);
        for (var slot = 0; slot < 8; slot += 2)
            f.Status(f.World.Party[slot]!, 0, new((uint)M9.SID.HellAwaits, 0, f.Now.AddSeconds(20), 0));
        f.AddActor(0x700, (uint)M9.OID.CharnelCell, ActorType.Enemy, Class.None, UltrasonicPosition(180));
        f.StartCast(f.Boss, (uint)M9.AID.UltrasonicAmpCast, 5);
        Check(!f.Hints(amp, 1).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), "M9 partial cages retain avoidance without invented complete assignment");
        Check(NavigationForbidden(f.Hints(amp, 1), UltrasonicPosition(180)), "M9 partial cage still creates actual cage hazard");
        for (var i = 0; i < 3; ++i)
            f.AddActor((ulong)(0x701 + i), (uint)M9.OID.CharnelCell1, ActorType.Enemy, Class.None, UltrasonicPosition(new[] { 90f, 45f, -45f }[i]));
        Check(f.Hints(amp, 1).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), "M9 late native cage spawns complete the observed route");
        config.EnableTowerOrder = false;
    }

    private static void CheckUltrasonicDeaths()
    {
        foreach (var change in new[] { "dead", "destroyed", "disconnected" })
        {
            using var f = new Fixture("M09SVampFatale");
            AssignNavigationParty(f);
            var config = Service.Config.Get<M9.M09SVampFataleConfig>();
            config.Assignments = M9.M09SVampFataleConfig.AssignmentStrategy.Game8;
            config.EnableTowerOrder = true;
            var rest = f.Activate("BossMod.Dawntrail.Savage.M09SVampFatale.UltrasonicSpreadRest");
            for (var slot = 0; slot < 8; slot += 2)
                f.Status(f.World.Party[slot]!, 0, new((uint)M9.SID.HellAwaits, 0, f.Now.AddSeconds(20), 0));
            for (var i = 0; i < 4; ++i)
                f.AddActor((ulong)(0x720 + i), (uint)M9.OID.CharnelCell1, ActorType.Enemy, Class.None, UltrasonicPosition(new[] { 180f, 90f, 45f, -45f }[i]));
            f.StartCast(f.Boss, (uint)M9.AID.UltrasonicSpreadCast, 5);
            var baits = (BossMod.Components.GenericBaitAway)rest;
            var missing = f.World.Party[5]!;
            Check(baits.CurrentBaits.Count == 2 && baits.CurrentBaits.Any(b => b.Target == missing), $"M9 {change}: native roles select live healer and DPS anchors");
            switch (change)
            {
                case "dead": f.World.Execute(new ActorState.OpDead(missing.InstanceID, true)); break;
                case "destroyed": f.World.Execute(new ActorState.OpDestroy(missing.InstanceID)); break;
                case "disconnected": f.World.Execute(new PartyState.OpModify(5, default)); break;
            }
            var survivor = f.Hints(rest, 7);
            Check(baits.CurrentBaits.Any(b => b.Target == f.World.Party[7]) && baits.CurrentBaits.All(b => b.Target != missing), $"M9 {change}: surviving original DPS becomes bait anchor");
            Check(survivor.ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), $"M9 {change}: known role lane remains fixed for surviving DPS");
            var absentHints = new AIHints();
            rest.AddAIHints(5, missing, default, absentHints);
            Check(absentHints.ForbiddenZones.Count == 0 && absentHints.GoalZones.Count == 0, $"M9 {change}: no movement output for absent/dead actor");
            if (change == "disconnected")
            {
                var replacement = f.AddActor(0x2005, 0, ActorType.Player, Class.MCH, new(100, 100));
                f.World.Execute(new PartyState.OpModify(5, new(99, replacement.InstanceID, false)));
                Check(!f.Hints(rest, 5).ForbiddenZones.Any(z => z.shapeDistance is SDInvertedCone), "M9 replacement in reused party slot does not inherit original actor assignment");
            }
            f.World.Execute(new ActorState.OpDead(f.World.Party[7]!.InstanceID, true));
            f.Hints(rest, 3);
            Check(baits.CurrentBaits.Count == 1 && baits.CurrentBaits[0].Target == f.World.Party[3], $"M9 {change}: no original living DPS removes stale DPS cone and retains healer");
            config.EnableTowerOrder = false;
        }
    }
}