using System.Collections;
using BossMod;
using M12H = BossMod.Dawntrail.Savage.M12S2Lindwurm;

internal static partial class EncounterChecks
{
    static partial void CheckM12Handoff()
    {
        var config = Service.Config.Get<M12H.M12S2LindwurmConfig>();
        RunGroup("M12 early fixed packets", CheckEarlyFixedHandoff);
        var saved2 = config.Rep2Strategy;
        var saved3 = config.Rep3Roles;
        try
        {
            foreach (var strategy in Enum.GetValues<M12H.Replication2Strategy>())
                RunGroup($"M12 handoff {strategy}", () =>
                {
                    config.Rep2Strategy = strategy;
                    CheckHandoffCase(false);
                    CheckHandoffCase(false, bossFirst: true);
                });
            foreach (var roles in new[]
            {
                new[] { M12H.Replication3Role.Stack1, M12H.Replication3Role.Stack2, M12H.Replication3Role.Stack3, M12H.Replication3Role.Stack4,
                    M12H.Replication3Role.Defam1, M12H.Replication3Role.Defam2, M12H.Replication3Role.Defam3, M12H.Replication3Role.Defam4 },
                new[] { M12H.Replication3Role.Stack1, M12H.Replication3Role.Defam1, M12H.Replication3Role.Defam4, M12H.Replication3Role.Defam2,
                    M12H.Replication3Role.Defam3, M12H.Replication3Role.Stack3, M12H.Replication3Role.Stack4, M12H.Replication3Role.Stack2 }
            })
                RunGroup("M12 Idyllic handoff " + roles[1], () =>
                {
                    config.Rep3Roles = roles;
                    CheckHandoffCase(true);
                });
        }
        finally
        {
            config.Rep2Strategy = saved2;
            config.Rep3Roles = saved3;
        }
    }

    private static void CheckHandoffCase(bool idyllic, bool bossFirst = false)
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var config = Service.Config.Get<M12H.M12S2LindwurmConfig>();
        Actor[] players = new Actor[8];
        Actor[] understudies = new Actor[8];
        for (var i = 0; i < 8; ++i)
            players[i] = f.AddPlayer(i, position: f.Module.Center + 9 * (180 - 45f * i).Degrees().ToDirection());
        var staging = f.Activate(M12P2Namespace + (idyllic ? "IdyllicDreamStaging" : "Replication2Staging"));
        Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "handoff: inactive spawn data gives no speculative route");
        for (var i = 0; i < 8; ++i)
        {
            understudies[i] = f.AddActor((ulong)(0x400 + i), (uint)M12H.OID.Understudy, ActorType.Enemy, Class.None,
                f.Module.Center + 16 * (180 - 45f * i).Degrees().ToDirection());
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(understudies[i].InstanceID, 0x11D2));
            Tether(understudies[i], M12H.TetherID.Fixed, players[i]);
            if (i < 7)
                Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "handoff: missing player clone assignment suppresses speculative navigation");
        }
        Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "handoff: all player tethers but no clone shapes give no speculative route");
        if (bossFirst)
            Tether(f.Boss, M12H.TetherID.RepBoss, players[7]);
        Dictionary<string, (Actor Source, M12H.TetherID Shape)> sources = [];
        Actor[] clones = new Actor[idyllic ? 8 : 6];
        int[] clock = idyllic ? [0, 1, 2, 3, 4, 5, 6, 7] : [1, 2, 3, 5, 6, 7];
        string[] roles = idyllic
            ? ["Stack1", "Defam1", "Stack2", "Defam2", "Stack3", "Defam3", "Stack4", "Defam4"]
            : config.Rep2Strategy == M12H.Replication2Strategy.DN
                ? ["Cone1", "Defam1", "Stack1", "Stack2", "Defam2", "Cone2"]
                : ["Cone2", "Defam2", "Stack1", "Stack2", "Defam1", "Cone1"];
        M12H.TetherID[] shapes = idyllic
            ? [M12H.TetherID.RepStack, M12H.TetherID.RepSpread, M12H.TetherID.RepStack, M12H.TetherID.RepSpread,
               M12H.TetherID.RepStack, M12H.TetherID.RepSpread, M12H.TetherID.RepStack, M12H.TetherID.RepSpread]
            : [M12H.TetherID.RepCone, M12H.TetherID.RepSpread, M12H.TetherID.RepStack,
               M12H.TetherID.RepStack, M12H.TetherID.RepSpread, M12H.TetherID.RepCone];
        for (var i = 0; i < clones.Length; ++i)
        {
            clones[i] = f.AddActor((ulong)(0x500 + i), (uint)M12H.OID.Lindschrat, ActorType.Enemy, Class.None,
                f.Module.Center + 18 * (180 - 45f * clock[i]).Degrees().ToDirection());
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clones[i].InstanceID, 0x1E46));
            sources.Add(roles[i], (clones[i], shapes[i]));
        }
        for (var i = 0; i < clones.Length; ++i)
        {
            Tether(clones[i], shapes[i], players[(i + 1) % 8]);
            if (i < clones.Length - 1)
                Check(f.ModuleHints(0).ForbiddenZones.Count == 0, "handoff: partial shape packets cannot drive provisional roles");
        }
        if (!idyllic)
        {
            sources.Add("Boss", (f.Boss, M12H.TetherID.RepBoss));
            if (!bossFirst)
                Tether(f.Boss, M12H.TetherID.RepBoss, players[7]);
        }

        string Role(int slot) => idyllic ? config.Rep3Roles[slot].ToString()
            : config.GetReplication2().Roles[(slot + (config.Rep2Strategy == M12H.Replication2Strategy.BananaCodex ? 2 : 0)) % 8].ToString();
        for (var slot = 0; slot < 8; ++slot)
        {
            var player = players[slot];
            var role = Role(slot);
            var hints = f.ModuleHints(slot);
            Check(hints.ForbiddenZones.Any(z => z.shapeDistance is SDRect), "handoff: live unrelated tether segments reach aggregate AI avoidance");
            var destinations = hints.ForbiddenZones.Where(z => z.shapeDistance is SDInvertedCircle).ToArray();
            if (!sources.TryGetValue(role, out var source))
            {
                Check(destinations.Length == 1 && !destinations[0].shapeDistance.Contains(understudies[slot].Position),
                    "handoff: no-tether role waits at assigned clone instead of taking a random line");
                Check(!NavigationForbidden(hints, understudies[slot].Position), "handoff: no-tether wait remains feasible with unrelated live lines");
                continue;
            }
            var held = source.Source.Tether.Target == player.InstanceID;
            Check(destinations.Length == (held ? 0 : 1), $"handoff {role}: actual holder controls whether interception is requested");
            if (held)
                continue;
            var currentHolder = f.World.Actors.Find(source.Source.Tether.Target)!;
            var delta = currentHolder.Position - source.Source.Position;
            Check(Enumerable.Range(1, (int)(delta.Length() * 4)).Any(i => !NavigationForbidden(hints, source.Source.Position + delta.Normalized() * (i * 0.25f)))
                && destinations[0].shapeDistance.Contains(f.Module.Center + new WDir(30, 30)),
                $"handoff {role}: native AI chooses the live source-to-holder interception segment");
            Tether(source.Source, source.Shape, player);
            Check(f.ModuleHints(slot).ForbiddenZones.All(z => z.shapeDistance is not SDInvertedCircle),
                $"handoff {role}: real transferred tether stops pursuit immediately");
            Tether(source.Source, source.Shape, currentHolder);
            f.World.Execute(new ActorState.OpDead(player.InstanceID, true));
            Check(f.ModuleHints(slot).ForbiddenZones.Count == 0, $"handoff {role}: dead player receives no movement request");
            f.World.Execute(new ActorState.OpDead(player.InstanceID, false));
            f.World.Execute(new ActorState.OpTether(source.Source.InstanceID, default));
            Check(f.ModuleHints(slot).ForbiddenZones.Count == 0, $"handoff {role}: actual tether removal clears stale interception");
            Tether(source.Source, source.Shape, currentHolder);
        }

        if (!idyllic && !bossFirst && config.Rep2Strategy == M12H.Replication2Strategy.DN)
        {
            var holder = players[7];
            var foreign = clones[0];
            var foreignHolder = players[1];
            var originalSource = foreign.Position;
            var originalHolder = foreignHolder.Position;
            var direction = (holder.Position - f.Boss.Position).Normalized();
            var crossed = f.Boss.Position + 2 * direction;
            Move(foreignHolder, crossed + (crossed - foreign.Position).Normalized() * 8);
            var hints = f.ModuleHints(0);
            Check(hints.ForbiddenZones.Any(z => z.shapeDistance is SDRect && z.shapeDistance.Contains(crossed)),
                "handoff: observed foreign line actually crosses preferred source-side intercept");
            Check(hints.ForbiddenZones.Count(z => z.shapeDistance is SDInvertedCircle) == 1
                && Enumerable.Range(1, 30).Any(i => !NavigationForbidden(hints, f.Boss.Position + direction * (0.25f * i))),
                "handoff: crossed preferred intercept moves to another feasible point on observed desired segment");
            Move(foreign, f.Boss.Position);
            Move(foreignHolder, holder.Position);
            hints = f.ModuleHints(0);
            Check(hints.ForbiddenZones.All(z => z.shapeDistance is not SDInvertedCircle),
                "handoff: fully overlapping unrelated line suppresses impossible mandatory destination");
            Move(foreign, originalSource);
            Move(foreignHolder, originalHolder);
        }

        // Fixed target intentionally differs from the last movable shape target.
        for (var slot = 0; slot < 8; ++slot)
            if (sources.TryGetValue(Role(slot), out var source))
                Tether(source.Source, M12H.TetherID.Fixed, players[slot]);
        Check((bool)staging.GetType().GetProperty("WurmsAssigned")!.GetValue(staging)!, "handoff: actual fixed tethers complete assignment");
        var bySlot = (Array)staging.GetType().GetField("WurmsBySlot")!.GetValue(staging)!;
        for (var slot = 0; slot < 8; ++slot)
        {
            Check(f.ModuleHints(slot).ForbiddenZones.Count == 0, "handoff: completed mechanic releases staging navigation");
            var wurm = bySlot.GetValue(slot);
            if (sources.TryGetValue(Role(slot), out var source))
            {
                Check(wurm != null && wurm.GetType().GetField("Actor")!.GetValue(wurm) == source.Source,
                    "handoff: downstream mechanic sees authoritative final holder rather than stale original target");
                Check(wurm!.GetType().GetField("Target")!.GetValue(wurm) == players[slot],
                    "handoff: fixed tether updates the actual target consumed by spread/cone replay decisions");
            }
            else
                Check(wurm == null, "handoff: no-tether slot retains no stale locked clone");
        }

        void Move(Actor actor, WPos point) => f.World.Execute(new ActorState.OpMove(actor.InstanceID, new(point.X, 0, point.Z, 0)));

        void Tether(Actor source, M12H.TetherID shape, Actor target)
            => f.World.Execute(new ActorState.OpTether(source.InstanceID, new((uint)shape, target.InstanceID)));
    }
    private static void CheckEarlyFixedHandoff()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var staging = f.Activate(M12P2Namespace + "IdyllicDreamStaging");
        for (var i = 0; i < 8; ++i)
        {
            var player = f.AddPlayer(i);
            var clone = f.AddActor((ulong)(0x700 + i), (uint)M12H.OID.Lindschrat, ActorType.Enemy, Class.None,
                f.Module.Center + 18 * (180 - 45f * i).Degrees().ToDirection());
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clone.InstanceID, 0x1E46));
            f.World.Execute(new ActorState.OpTether(clone.InstanceID, new((uint)M12H.TetherID.RepStack, player.InstanceID)));
            f.World.Execute(new ActorState.OpTether(clone.InstanceID, new((uint)M12H.TetherID.Fixed, player.InstanceID)));
            Check((bool)staging.GetType().GetProperty("WurmsAssigned")!.GetValue(staging)! == (i == 7),
                "handoff: early fixed packets cannot freeze assignment before the full observed clone set");
            Check(((IList)staging.GetType().GetField("WurmClones")!.GetValue(staging)!).Count == i + 1,
                "handoff: later clone spawn is retained after earlier fixed tether packets");
        }
    }

}
