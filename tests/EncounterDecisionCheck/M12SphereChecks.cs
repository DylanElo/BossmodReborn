using System.Numerics;
using BossMod;
using M12P2 = BossMod.Dawntrail.Savage.M12S2Lindwurm;

internal static partial class EncounterChecks
{
    static partial void CheckM12Spheres()
    {
        for (var pattern = 0; pattern < 2; ++pattern)
            for (var mirror = 0; mirror < 2; ++mirror)
                for (var reverse = 0; reverse < 2; ++reverse)
                    for (var betaGroup = 0; betaGroup < 2; ++betaGroup)
                        for (var seed = 0; seed < 3; ++seed)
                            CheckM12SphereRouting(pattern, mirror != 0, reverse != 0, betaGroup, seed);
        CheckM12SphereMissingData();
    }

    private static Actor[] SphereParty(Fixture f, int betaGroup)
    {
        Class[] jobs = [Class.PLD, Class.WAR, Class.WHM, Class.SCH, Class.VPR, Class.DRG, Class.MCH, Class.BLM];
        var players = jobs.Select((job, slot) => f.AddPlayer(slot, job)).ToArray();
        for (var slot = 0; slot < 8; ++slot)
            f.Status(players[slot], 0, new((uint)(slot % 2 == betaGroup ? M12P2.SID.MutationB : M12P2.SID.MutationA), 0, f.Now.AddSeconds(20), 0));
        return players;
    }

    private static (Actor[] spheres, WPos north, WPos south) SpherePattern(Fixture f, int pattern, bool mirror, bool reverse, int seed, int count = 8)
    {
        uint[] pair = pattern == -1
            ? [(uint)M12P2.OID.ManaSphereBlueSphere, (uint)M12P2.OID.ManaSphereGreenDonut,
                (uint)M12P2.OID.ManaSphereOrangeBowtie, (uint)M12P2.OID.ManaSpherePurpleBowtie]
            : pattern == 0
            ? [(uint)M12P2.OID.ManaSphereGreenDonut, (uint)M12P2.OID.ManaSphereOrangeBowtie,
                (uint)M12P2.OID.ManaSphereBlueSphere, (uint)M12P2.OID.ManaSpherePurpleBowtie]
            : [(uint)M12P2.OID.ManaSphereBlueSphere, (uint)M12P2.OID.ManaSpherePurpleBowtie,
                (uint)M12P2.OID.ManaSphereGreenDonut, (uint)M12P2.OID.ManaSphereOrangeBowtie];
        if (reverse) (pair[0], pair[1]) = (pair[1], pair[0]);
        WPos[] positions = [new(90, 95), new(90, 105), new(80, 88), new(80, 112),
            new(118, 86), new(118, 114), new(118, 90), new(118, 110)];
        if (mirror)
            positions = positions.Select(p => new WPos(200 - p.X, p.Z)).ToArray();
        var spheres = positions.Select((p, i) => f.AddActor((ulong)(0x890 + i), pair[i % 4], ActorType.Enemy, Class.None, p)).ToArray();
        var random = new Random(seed);
        foreach (var i in Enumerable.Range(0, count).OrderBy(_ => random.Next()))
            SphereSpawn(f, spheres[i], positions[i]);
        // The event order must not affect geometric north/south assignments.
        return (spheres, positions[4], positions[5]);
    }

    private static void SphereSpawn(Fixture f, Actor sphere, WPos origin)
        => f.World.Execute(new ActorState.OpCastEvent(sphere.InstanceID, new(new(ActionType.Spell, (uint)M12P2.AID.ManaSphereSpawn),
            0, 0, 1, new Vector3(origin.X, 0, origin.Z), 0, 0, default)));

    private static void CheckM12SphereRouting(int pattern, bool mirror, bool reverse, int betaGroup, int seed)
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var mana = f.Activate(M12P2Namespace + "ManaSphere");
        var players = SphereParty(f, betaGroup);
        var (spheres, north, south) = SpherePattern(f, pattern, mirror, reverse, seed);
        var tag = $"M12 spheres {pattern}/{mirror}/{reverse}/{betaGroup}/{seed}";
        for (var slot = 0; slot < 8; ++slot)
        {
            var hints = f.ModuleHints(slot);
            var beta = slot % 2 == betaGroup;
            var mine = slot < 2 ? north : south;
            var other = slot < 2 ? south : north;
            Check(hints.GoalZones.Count == (beta ? 1 : 0), $"{tag}: slot{slot} gets exactly its own beta allocation");
            if (beta)
                Check(NavigationGoal(hints, mine) > NavigationGoal(hints, other) && !NavigationForbidden(hints, mine) && NavigationForbidden(hints, other),
                    $"{tag}: beta tank north and other three south through aggregate AI");
            else
                Check(spheres.All(s => NavigationForbidden(hints, s.Position)), $"{tag}: alpha never touches any sphere");
        }
        f.World.Execute(new ActorState.OpMove(spheres[4].InstanceID, new Vector4(north.X - 1, 0, north.Z + 1, 0)));
        Check(NavigationGoal(f.ModuleHints(betaGroup), spheres[4].Position) > NavigationGoal(f.ModuleHints(betaGroup), south),
            $"{tag}: assignment follows actual moving sphere");
        f.Advance(18);
        Check(f.ModuleHints(betaGroup).GoalZones.Count == 1, $"{tag}: observed final two seconds retains soak route");
        f.World.Execute(new ActorState.OpMove(players[betaGroup].InstanceID, spheres[4].PosRot));
        f.EventCast(f.Boss, (uint)M12P2.AID.BloodyBurst, players[betaGroup].InstanceID);
        Check(f.ModuleHints(betaGroup).GoalZones.Count == 0, $"{tag}: tank does not switch to southern sphere after actual northern soak");
        Check(f.ModuleHints(2 + betaGroup).GoalZones.Count == 1 && NavigationGoal(f.ModuleHints(2 + betaGroup), south) > 0,
            $"{tag}: north soak preserves three-player southern route");
        f.EventCast(spheres[4], (uint)M12P2.AID.BlackHoleAbsorb);
        SphereSpawn(f, spheres[4], north);
        Check(f.ModuleHints(betaGroup).GoalZones.Count == 0 && f.ModuleHints(2 + betaGroup).GoalZones.Count == 1,
            $"{tag}: late duplicate spawn after absorption cannot resurrect consumed northern allocation");
        f.World.Execute(new ActorState.OpMove(players[2 + betaGroup].InstanceID, spheres[5].PosRot));
        f.EventCast(f.Boss, (uint)M12P2.AID.BloodyBurst, players[2 + betaGroup].InstanceID);
        Check(Enumerable.Range(0, 8).All(slot => f.ModuleHints(slot).GoalZones.Count == 0), $"{tag}: both actual soak events release all allocations");
    }

    private static void CheckM12SphereMissingData()
    {
        foreach (var missing in new[] { "expiry", "status loss", "swap", "wrong letter", "partial party", "dead beta", "wrong elements", "partial pattern", "absorb" })
        {
            using var f = new Fixture("M12S2TheLindwurm");
            var mana = f.Activate(M12P2Namespace + "ManaSphere");
            var players = SphereParty(f, 0);
            var (spheres, _, _) = SpherePattern(f, missing == "wrong elements" ? -1 : 0, false, false, 12, missing == "partial pattern" ? 7 : 8);
            switch (missing)
            {
                case "expiry": f.Advance(20); break;
                case "status loss": f.Status(players[2], 0, default); break;
                case "swap": f.Status(players[0], 1, new((uint)M12P2.SID.MutatingCells, 0, f.Now.AddSeconds(5), 0)); break;
                case "wrong letter": f.Status(players[2], 0, new((uint)M12P2.SID.MutationA, 0, f.Now.AddSeconds(20), 0)); break;
                case "partial party": f.World.Execute(new PartyState.OpModify(7, new(0, 0, false))); break;
                case "dead beta": f.World.Execute(new ActorState.OpDead(players[0].InstanceID, true)); break;
                case "absorb": f.EventCast(spheres[4], (uint)M12P2.AID.BlackHoleAbsorb); break;
            }
            Check(f.ModuleHints(0).GoalZones.Count == 0, $"M12 spheres {missing}: actual missing/consumed data cannot direct tank to another sphere");
            if (missing == "partial pattern")
            {
                SphereSpawn(f, spheres[0], spheres[0].Position);
                Check(f.ModuleHints(0).GoalZones.Count == 0, "M12 spheres: duplicate spawn does not complete a seven-sphere pattern");
                SphereSpawn(f, spheres[7], spheres[7].Position);
                Check(f.ModuleHints(0).GoalZones.Count == 1, "M12 spheres: actual eighth distinct spawn unlocks complete observed allocation");
            }
        }
    }
}
