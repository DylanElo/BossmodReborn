using System.Collections;
using System.Numerics;
using BossMod;
using BossMod.Components;
using M12P2 = BossMod.Dawntrail.Savage.M12S2Lindwurm;

internal static partial class EncounterChecks
{
    static partial void CheckM12P2()
    {
        RunGroup("M12P2 Doom", CheckM12P2Doom);
        RunGroup("M12P2 stored replay", CheckM12P2StoredReplay);
        RunGroup("M12P2 counters", CheckM12P2Counts);
        RunGroup("M12P2 clone resolution", CheckM12P2StackSpreadOrder);
        RunGroup("M12P2 north seam", CheckM12P2Clockspot);
        RunGroup("M12P2 sparse cone", CheckM12P2SparseCone);
        RunGroup("M12P2 Replication1 guidance", CheckM12P2Guidance);
        RunGroup("M12P2 Portent", CheckM12P2Portent);
        RunGroup("M12P2 BloodMana", CheckM12P2BloodMana);
        RunGroup("M12P2 player replay risk", CheckM12P2ReplayRiskLifecycle);
        RunGroup("M12P2 meteor ordering", CheckM12P2MeteorOrdering);
        RunGroup("M12P2 clone cast activation", CheckM12P2CloneCastActivation);
    }

    private const string M12P2Namespace = "BossMod.Dawntrail.Savage.M12S2Lindwurm.";
    private static void P2Field(BossComponent component, string name, object value)
        => component.GetType().GetField(name)!.SetValue(component, value);

    private static void CheckM12P2Doom()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        f.AddPlayer(0, Class.WHM);
        var victim = f.AddPlayer(5);
        var doom = f.Activate(M12P2Namespace + "IdyllicDreamDoom");
        Check(f.Hints(doom, 0).ShouldCleanse.None(), "M12P2 Doom: no cleanse before actual status gain");
        f.Status(victim, 0, new((uint)M12P2.SID.Doom, 0, f.Now.AddSeconds(8), 0));
        Check(f.Hints(doom, 0).ShouldCleanse[5], "M12P2 Doom: actual status gain selects affected party slot for Esuna");
        f.Status(victim, 0, default);
        Check(f.Hints(doom, 0).ShouldCleanse.None(), "M12P2 Doom: actual status loss releases cleanse request");
    }

    private static void CheckM12P2StoredReplay()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var player = f.AddPlayer(0);
        var stored = (GenericAOEs)f.Activate(M12P2Namespace + "IdyllicDreamPowerGusherSnakingKick");
        var clone = f.AddActor(0x210, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(86, 100));
        var later = f.AddActor(0x211, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(114, 100));
        f.StartCast(clone, (uint)M12P2.AID.SnakingKickCastFirst, 5);
        f.StartCast(later, (uint)M12P2.AID.SnakingKickCastFirst, 5);
        P2Field(stored, "Visible", true);
        Check(f.Hints(stored, 0).ForbiddenZones.Count == 0, "M12P2 stored AOEs: preview risk gate does not force premature movement");
        P2Field(stored, "Risky", true);
        Check(f.Hints(stored, 0).ForbiddenZones.Count == 2, "M12P2 stored AOEs: locked clone prediction reaches real AI avoidance");
        P2Field(stored, "Risky", false);
        Check(f.Hints(stored, 0).ForbiddenZones.Count == 0, "M12P2 stored AOEs: disabling risk clears previously dangerous preview");
        P2Field(stored, "Risky", true);
        P2Field(stored, "WatchTeleport", true);
        f.Advance(140);
        stored.GetType().GetMethod("Reset")!.Invoke(stored, [8.5d]);
        var hints = f.Hints(stored, 0);
        Check(hints.ForbiddenZones.All(z => Math.Abs((z.activation - f.Now).TotalSeconds - 8.5) < 0.001),
            "M12P2 stored AOEs: replay reschedules AI deadlines instead of retaining first-preview timestamp");
        f.EventCast(clone, (uint)M12P2.AID.IdyllicDreamSnakingKick);
        Check(stored.ActiveAOEs(0, player).Length == 1 && stored.ActiveAOEs(0, player)[0].Origin == later.Position,
            "M12P2 stored AOEs: one clone resolution preserves the other stored clone for final replay");
        stored.GetType().GetMethod("Reset")!.Invoke(stored, [4.8d]);
        Check(f.Hints(stored, 0).ForbiddenZones.All(z => Math.Abs((z.activation - f.Now).TotalSeconds - 4.8) < 0.001),
            "M12P2 stored AOEs: final replay has a fresh final-wave deadline");
        f.EventCast(later, (uint)M12P2.AID.IdyllicDreamSnakingKick);
        Check(f.Hints(stored, 0).ForbiddenZones.Count == 0, "M12P2 stored AOEs: actual final resolution clears all avoidance");
    }

    private static void CheckM12P2Counts()
    {
        using (var f = new Fixture("M12S2TheLindwurm"))
        {
            f.AddPlayer(0);
            f.EnterState(0, 0x30220);
            f.Activate(M12P2Namespace + "IdyllicDreamStaging");
            f.Activate(M12P2Namespace + "IdyllicDreamWurmStackSpread");
            f.Advance(5.2);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x30220, "M12P2 clone timeline: timer alone cannot resolve first pair");
            for (var i = 0; i < 3; ++i) f.EventCast(f.Boss, (uint)M12P2.AID.IdyllicDreamHeavySlam);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x30221, "M12P2 clone timeline: batched third event does not stall exact-pair predicate");
            for (var i = 0; i < 6; ++i) f.EventCast(f.Boss, (uint)M12P2.AID.IdyllicDreamHeavySlam);
            f.Advance(15);
            Check(f.Module.StateMachine.ActiveState?.ID == 0x30222, "M12P2 clone timeline: batched ninth event does not stall end-of-clones predicate");
        }
        foreach (var (state, count, wait, expected, deadline) in new[]
        {
            (0x30322u, 5, 3.6, 0x30330u, 8.5), (0x30351u, 9, 6.6, 0x30352u, 4.8)
        })
        {
            using var f = new Fixture("M12S2TheLindwurm");
            f.AddPlayer(0);
            f.EnterState(0, state);
            f.Activate(M12P2Namespace + "IdyllicDreamPlayerCastCounter");
            f.Activate(M12P2Namespace + "IdyllicDreamArena");
            var stored = f.Activate(M12P2Namespace + "IdyllicDreamPowerGusherSnakingKick");
            P2Field(stored, "Risky", true);
            var clone = f.AddActor(0x220, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(86, 100));
            f.StartCast(clone, (uint)M12P2.AID.SnakingKickCastFirst, 5);
            f.Advance(wait);
            Check(f.Module.StateMachine.ActiveState?.ID == state, $"M12P2 replay {state:X}: timer alone waits for actual replay effects");
            for (var i = 0; i < count; ++i) f.EventCast(f.Boss, (uint)M12P2.AID.HeavySlamReplay);
            f.Advance(0);
            Check(f.Module.StateMachine.ActiveState?.ID == expected, $"M12P2 replay {state:X}: production event counter tolerates batched wave overshoot");
            if (state == 0x30322)
            {
                f.Advance(1.5);
                f.StartCast(f.Boss, (uint)M12P2.AID.TwistedVision, 4);
                f.Advance(0);
                Check(f.Module.StateMachine.ActiveState?.ID == 0x30331,
                    "M12P2 stored replay: next Twisted Vision cast actually starts before replay deadline is predicted");
            }
            Check(f.Hints(stored, 0).ForbiddenZones.Count == 1 && f.Hints(stored, 0).ForbiddenZones.All(z => Math.Abs((z.activation - f.Now).TotalSeconds - deadline) < 0.001),
                $"M12P2 replay {state:X}: real state callback supplies next-wave AI deadline");
        }
    }

    private static void CheckM12P2StackSpreadOrder()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var spreadTarget = f.AddPlayer(0, position: new(86, 100));
        var stackTarget = f.AddPlayer(1, position: new(114, 100));
        f.Activate(M12P2Namespace + "IdyllicDreamStaging");
        var spreadClone = f.AddActor(0x250, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(100, 85));
        var stackClone = f.AddActor(0x251, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(110, 85));
        foreach (var clone in new[] { spreadClone, stackClone })
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clone.InstanceID, 0x1E46));
        f.World.Execute(new ActorState.OpTether(spreadClone.InstanceID, new((uint)M12P2.TetherID.RepSpread, spreadTarget.InstanceID)));
        f.World.Execute(new ActorState.OpTether(stackClone.InstanceID, new((uint)M12P2.TetherID.RepStack, stackTarget.InstanceID)));
        var mechanic = (UniformStackSpread)f.Activate(M12P2Namespace + "IdyllicDreamWurmStackSpread");
        mechanic.Update();
        Check(mechanic.Stacks.Count == 1 && mechanic.Spreads.Count == 1,
            "M12P2 clone mechanics: real shape tethers predict both mixed pair effects");
        f.EventCast(stackClone, (uint)M12P2.AID.IdyllicDreamHeavySlam, spreadTarget.InstanceID);
        mechanic.Update();
        Check(mechanic.Stacks.Count == 0 && mechanic.Spreads.Count == 1 && mechanic.Spreads[0].Target == spreadTarget,
            "M12P2 clone mechanics: earlier stack effect clears its own prediction and preserves later defamation");
        f.EventCast(f.Boss, (uint)M12P2.AID.IdyllicDreamManaBurst, stackTarget.InstanceID);
        mechanic.Update();
        Check(mechanic.Stacks.Count == 0 && mechanic.Spreads.Count == 0,
            "M12P2 clone mechanics: actual later defamation clears remaining prediction despite a different hit-player MainTargetID");
    }

    private static void CheckM12P2Clockspot()
    {
        foreach (var degrees in new[] { -179f, 179f, -180f, 180f })
        {
            using var f = new Fixture("M12S2TheLindwurm");
            var player = f.AddPlayer(0);
            var staging = f.Activate(M12P2Namespace + "IdyllicDreamStaging");
            var clone = f.AddActor(0x230, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None,
                f.Module.Center + 15 * degrees.Degrees().ToDirection());
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clone.InstanceID, 0x1E46));
            f.World.Execute(new ActorState.OpTether(clone.InstanceID, new((uint)M12P2.TetherID.RepStack, player.InstanceID)));
            var clones = (IList)staging.GetType().GetField("WurmClones")!.GetValue(staging)!;
            var assignedRole = clones[0]!.GetType().GetField("AssignedRole")!.GetValue(clones[0]);
            Check(assignedRole?.ToString() == "Stack1", $"M12P2 actual staging: north clone at {degrees} degrees selects north role across angle seam");
        }
    }

    private static void CheckM12P2SparseCone()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        f.AddPlayer(0);
        var target = f.AddPlayer(5, Class.MCH, new(110, 100));
        var staging = f.Activate(M12P2Namespace + "Replication2Staging");
        var clone = f.AddActor(0x240, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(115, 100));
        f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clone.InstanceID, 0x1E46));
        f.World.Execute(new ActorState.OpTether(clone.InstanceID, new((uint)M12P2.TetherID.RepCone, target.InstanceID)));
        f.World.Execute(new ActorState.OpTether(f.Boss.InstanceID, new((uint)M12P2.TetherID.RepBoss, f.World.Party[0]!.InstanceID)));
        f.World.Execute(new ActorState.OpTether(clone.InstanceID, new((uint)M12P2.TetherID.Fixed, target.InstanceID)));
        var cones = (GenericBaitAway)f.Activate(M12P2Namespace + "Replication2HemorrhagicProjection");
        cones.Update();
        Check(cones.CurrentBaits.Count == 1 && cones.CurrentBaits[0].Target == target,
            "M12P2 sparse party: actual tethered slot5 receives cone even with missing slots1-4");
    }
    private static void CheckM12P2Guidance()
    {
        var config = Service.Config.Get<M12P2.M12S2LindwurmConfig>();
        var saved = config.Rep1Strategy;
        try
        {
            foreach (var strategy in Enum.GetValues<M12P2.M12S2LindwurmConfig.Replication1Strategy>())
            {
                using var f = new Fixture("M12S2TheLindwurm");
                AssignNavigationParty(f);
                config.Rep1Strategy = strategy;
                f.Activate(M12P2Namespace + "Replication1SecondBait");
                var guidance = f.Activate(M12P2Namespace + "Replication1Guidance");
                for (var slot = 0; slot < 8; ++slot)
                {
                    var hints = f.Hints(guidance, slot);
                    var isStatic = strategy == M12P2.M12S2LindwurmConfig.Replication1Strategy.Static;
                    float[] degrees = isStatic ? [-135, -135, -45, -45, 45, 45, 135, 135] : [135, -45, -135, 45, 135, -45, -135, 45];
                    float[] signs = isStatic ? [0, 0, -1, 1, 0, 0, 1, -1] : [-1, 1, 1, 1, 1, -1, -1, -1];
                    var radius = isStatic && slot is 2 or 3 or 6 or 7 ? 12.5f : 7.5f;
                    var direction = degrees[slot].Degrees().ToDirection();
                    var expected = f.Module.Center + radius * direction + signs[slot] * 2.25f * new WDir(-direction.Z, direction.X);
                    Check(hints.GoalZones.Count == 1 && NavigationGoal(hints, expected) > NavigationGoal(hints, f.Module.Center),
                        $"M12P2 Replication1 {strategy}: real role {slot} preposition reaches native AI goals");
                }
                f.Status(f.World.Party[0]!, 0, new((uint)M12P2.SID.FireResistanceDownII, 0, f.Now.AddSeconds(20), 0));
                Check(f.Hints(guidance, 0).GoalZones.Count == 0,
                    $"M12P2 Replication1 {strategy}: missing clone formation cannot emit invented final route");
                WPos[] formation = [new(110, 110), new(114, 114), new(90, 90), new(86, 86)];
                for (var index = 0; index < formation.Length; ++index)
                {
                    var clone = f.AddActor((ulong)(0x280 + index), (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, formation[index]);
                    f.StartCast(clone, index < 2 ? (uint)M12P2.AID.TopTierSlamCast : (uint)M12P2.AID.MightyMagicCast, 5);
                    f.World.Execute(new ActorState.OpCastEvent(clone.InstanceID, new(new(ActionType.Spell, (uint)M12P2.AID.Dash),
                        0, 0, 1, new Vector3(formation[index].X, 0, formation[index].Z), 0, 0, default)));
                }
                foreach (var fire in new[] { true, false })
                {
                    for (var slot = 0; slot < 8; ++slot)
                        f.Status(f.World.Party[slot]!, 0, new(fire ? (uint)M12P2.SID.DarkResistanceDownII : (uint)M12P2.SID.FireResistanceDownII, 0, f.Now.AddSeconds(20), 0));
                    for (var slot = 0; slot < 8; ++slot)
                    {
                        var inner = slot is 0 or 1 or 4 or 5;
                        WPos expected;
                        if (strategy == M12P2.M12S2LindwurmConfig.Replication1Strategy.Static)
                            expected = fire
                                ? inner ? new(98.93934f, 98.93934f) : new(100, 113.65f)
                                : inner ? slot is 0 or 4 ? new(85, 90) : new(90, 85)
                                    : slot is 2 or 6 ? new(86.35f, 100) : new(100, 86.35f);
                        else if (fire)
                            expected = inner ? new(101.06066f, 101.06066f) : new(114, 100);
                        else
                        {
                            var support = slot < 4;
                            var north = strategy == M12P2.M12S2LindwurmConfig.Replication1Strategy.DN ? support : !support;
                            expected = inner ? north ? new(101, 93) : new(93, 101)
                                : north ? new(100, 86) : new(86, 100);
                        }
                        var hints = f.Hints(guidance, slot);
                        Check(hints.GoalZones.Count == 1 && NavigationGoal(hints, expected) > NavigationGoal(hints, f.Module.Center),
                            $"M12P2 Replication1 {strategy}: actual clone dash and {(fire ? "dark" : "fire")} debuff select final role {slot} destination");
                        Check(f.Module.Arena.InBounds(expected), $"M12P2 Replication1 {strategy}: final role {slot} destination remains on arena");
                    }
                }
            }
        }
        finally { config.Rep1Strategy = saved; }
    }

    private static void CheckM12P2Portent()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        WPos[] players = [new(86, 100), new(114, 100), new(86, 100), new(114, 100), new(86, 100), new(114, 100), new(86, 100), new(114, 100)];
        AssignNavigationParty(f, players);
        var config = Service.Config.Get<M12P2.M12S2LindwurmConfig>();
        var saved = config.ShowLindwurmsPortentHints;
        config.ShowLindwurmsPortentHints = true;
        try
        {
            var shared = f.Activate(M12P2Namespace + "IdyllicDreamSharedState");
            var portent = f.Activate(M12P2Namespace + "LindwurmsPortent");
            M12P2.Element[] elements = [M12P2.Element.Wind, M12P2.Element.Wind, M12P2.Element.Dark, M12P2.Element.Dark,
                M12P2.Element.Earth, M12P2.Element.Fire, M12P2.Element.Fire, M12P2.Element.Earth];
            WPos[] towers = [new(91, 100), new(109, 100), new(93, 94), new(107, 94), new(94, 105), new(106, 105), new(80, 105), new(120, 105)];
            WPos[] destinations = [new(91, 100), new(109, 100), new(93, 94), new(107, 94), new(89, 95), new(110, 97), new(86, 109), new(114, 109)];
            Check(f.Hints(portent, 0).GoalZones.Count == 0, "M12P2 Portent: no role goal before live portent status");
            for (var slot = 0; slot < 8; ++slot)
                shared.GetType().GetMethod("RecordTowerSoak")!.Invoke(shared, [slot, slot, elements[slot], towers[slot]]);
            f.Status(f.World.Party[0]!, 0, new((uint)M12P2.SID.FarawayPortent, 0, f.Now.AddSeconds(5), 0));
            portent.Update();
            for (var slot = 0; slot < 8; ++slot)
            {
                var hints = f.Hints(portent, slot);
                Check(hints.GoalZones.Count == 1 && NavigationGoal(hints, destinations[slot]) > NavigationGoal(hints, players[slot]),
                    $"M12P2 Portent: actual status and recorded tower soak route role {slot} to existing assigned position");
            }
            config.ShowLindwurmsPortentHints = false;
            Check(f.Hints(portent, 0).GoalZones.Count == 0, "M12P2 Portent: disabled positional setting preserves generic safety without role goal");
            config.ShowLindwurmsPortentHints = true;
            f.EventCast(f.Boss, (uint)M12P2.AID.LindwurmsThunderII);
            Check(f.Hints(portent, 0).GoalZones.Count == 0, "M12P2 Portent: actual cone resolution releases assigned navigation");
        }
        finally { config.ShowLindwurmsPortentHints = saved; }
    }

    private static void CheckM12P2BloodMana()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var beta = f.AddPlayer(0);
        var alpha = f.AddPlayer(1);
        var mana = f.Activate(M12P2Namespace + "ManaSphere");
        f.Status(beta, 0, new((uint)M12P2.SID.MutationB, 0, f.Now.AddSeconds(30), 0));
        f.Status(alpha, 0, new((uint)M12P2.SID.MutationA, 0, f.Now.AddSeconds(30), 0));
        Check(f.Hints(mana, 0).GoalZones.Count == 0, "M12P2 BloodMana: beta cannot select soak before observed sphere pattern");
        WPos[] positions = [new(90, 95), new(90, 105), new(80, 88), new(80, 112), new(118, 86), new(118, 114), new(118, 90), new(118, 110)];
        uint[] types = [(uint)M12P2.OID.ManaSphereBlueSphere, (uint)M12P2.OID.ManaSphereGreenDonut,
            (uint)M12P2.OID.ManaSpherePurpleBowtie, (uint)M12P2.OID.ManaSphereOrangeBowtie];
        for (var index = 0; index < positions.Length; ++index)
        {
            var sphere = f.AddActor((ulong)(0x290 + index), types[index % 4], ActorType.Enemy, Class.None, positions[index]);
            f.World.Execute(new ActorState.OpCastEvent(sphere.InstanceID, new(new(ActionType.Spell, (uint)M12P2.AID.ManaSphereSpawn),
                0, 0, 1, new Vector3(positions[index].X, 0, positions[index].Z), 0, 0, default)));
        }
        var hints = f.Hints(mana, 0);
        Check(hints.GoalZones.Count == 2 && NavigationGoal(hints, positions[4]) > NavigationGoal(hints, positions[0]) &&
            NavigationGoal(hints, positions[5]) > NavigationGoal(hints, positions[6]),
            "M12P2 BloodMana: actual beta status and eight sphere events prefer opposite-side matching close shapes");
        Check(f.Hints(mana, 1).GoalZones.Count == 0, "M12P2 BloodMana: alpha status never receives beta soak destination");
        f.World.Execute(new ActorState.OpMove(beta.InstanceID, new Vector4(positions[4].X, 0, positions[4].Z, 0)));
        f.EventCast(f.Boss, (uint)M12P2.AID.BloodyBurst, beta.InstanceID);
        hints = f.Hints(mana, 0);
        Check(hints.GoalZones.Count == 1 && NavigationGoal(hints, positions[4]) == 0 && NavigationGoal(hints, positions[5]) > 0,
            "M12P2 BloodMana: actual soak effect releases consumed sphere while preserving remaining eligible shape");
        f.Status(beta, 0, new((uint)M12P2.SID.MutatingCells, 0, f.Now.AddSeconds(5), 0));
        Check(f.Hints(mana, 0).GoalZones.Count == 0, "M12P2 BloodMana: observed debuff swap releases pre-swap soak routing");
    }

    private static void CheckM12P2ReplayRiskLifecycle()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var player = f.AddPlayer(0);
        var partner = f.AddPlayer(1);
        f.Activate(M12P2Namespace + "IdyllicDreamStaging");
        var understudies = new Actor[8];
        for (var index = 0; index < understudies.Length; ++index)
        {
            understudies[index] = f.AddActor((ulong)(0x310 + index), (uint)M12P2.OID.Understudy, ActorType.Enemy, Class.None, new(90 + index, 90));
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(understudies[index].InstanceID, 0x11D2));
        }
        for (var slot = 0; slot < 2; ++slot)
            f.World.Execute(new ActorState.OpTether(understudies[slot].InstanceID, new((uint)M12P2.TetherID.Fixed, f.World.Party[slot]!.InstanceID)));
        var clones = new Actor[2];
        for (var slot = 0; slot < 2; ++slot)
        {
            clones[slot] = f.AddActor((ulong)(0x330 + slot), (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(100 + slot, 85));
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clones[slot].InstanceID, 0x1E46));
        }
        for (var slot = 0; slot < 2; ++slot)
        {
            f.World.Execute(new ActorState.OpTether(clones[slot].InstanceID, new(slot == 0 ? (uint)M12P2.TetherID.RepSpread : (uint)M12P2.TetherID.RepStack, f.World.Party[slot]!.InstanceID)));
            f.World.Execute(new ActorState.OpTether(clones[slot].InstanceID, new((uint)M12P2.TetherID.Fixed, f.World.Party[slot]!.InstanceID)));
        }
        var replay = (GenericAOEs)f.Activate(M12P2Namespace + "IdyllicDreamManaBurstPlayer");
        var predict = replay.GetType().GetMethod("Predict")!;
        predict.Invoke(replay, [0]);
        Check(replay.ActiveAOEs(0, player).Length == 1 && f.Hints(replay, 1).ForbiddenZones.Count == 0,
            "M12P2 player clone replay: actual first-wave assignments preview without premature avoidance");
        P2Field(replay, "Risky", true);
        Check(f.Hints(replay, 1).ForbiddenZones.Count == 1, "M12P2 player clone replay: enabled first-wave risk reaches AI avoidance");
        var helper = f.AddActor(0x340, 0x233C, ActorType.Enemy, Class.None, understudies[0].Position);
        f.EventCast(helper, (uint)M12P2.AID.ManaBurstReplay, player.InstanceID);
        Check(replay.ActiveAOEs(0, player).Length == 0, "M12P2 player clone replay: actual first effect clears its prediction");
        for (var slot = 0; slot < 2; ++slot)
            f.World.Execute(new ActorState.OpTether(understudies[4 + slot].InstanceID, new((uint)M12P2.TetherID.Fixed, f.World.Party[slot]!.InstanceID)));
        predict.Invoke(replay, [1]);
        Check(replay.ActiveAOEs(0, player).Length == 1 && f.Hints(replay, 1).ForbiddenZones.Count == 0,
            "M12P2 player clone replay: new second-wave assignments reset inherited risk while platforms remain split");
        P2Field(replay, "Risky", true);
        Check(f.Hints(replay, 1).ForbiddenZones.Count == 1, "M12P2 player clone replay: second platform-disappear risk gate re-enables avoidance");
    }

    private static void CheckM12P2MeteorOrdering()
    {
        var config = Service.Config.Get<M12P2.M12S2LindwurmConfig>();
        var saved = config.IdyllicDreamAdjustToMistakes;
        config.IdyllicDreamAdjustToMistakes = false;
        try
        {
            foreach (var order in new[] { new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new[] { 3, 2, 1, 0, 7, 6, 5, 4 } })
            {
                using var f = new Fixture("M12S2TheLindwurm");
                AssignNavigationParty(f);
                var meteor = (GenericTowers)f.Activate(M12P2Namespace + "IdyllicDreamElementalMeteor");
                foreach (var (_, player) in f.Module.Raid.WithSlot())
                    f.Status(player, 0, new((uint)M12P2.SID.LightResistanceDownII, 0, f.Now.AddSeconds(30), 0));
                // Both rows lie within10y of center X: exercises the existing geometric rank fallback.
                WPos[] spots = [new(94, 94), new(94, 106), new(92, 94), new(92, 106), new(106, 94), new(106, 106), new(108, 94), new(108, 106)];
                foreach (var index in order)
                {
                    var actor = f.AddActor((ulong)(0x350 + index), (uint)M12P2.OID.MeteorEarth, ActorType.EventObj, Class.None, spots[index]);
                    f.World.Execute(new ActorState.OpEventObjectAnimation(actor.InstanceID, 1, 2));
                }
                meteor.GetType().GetMethod("CreateTowers")!.Invoke(meteor, null);
                int[] roleSpots = [0, 5, 2, 7, 1, 4, 3, 6];
                for (var slot = 0; slot < 8; ++slot)
                {
                    var allowed = meteor.ActiveTowers(slot, f.World.Party[slot]!).ToArray().Where(tower => !tower.ForbiddenSoakers[slot]).ToArray();
                    Check(allowed.Length == 1 && allowed[0].Position == spots[roleSpots[slot]],
                        $"M12P2 meteors order{order[0]}: observed animation order cannot swap native close/far role{slot} tower assignment");
                }
                Check(f.Errors.Count == 0, "M12P2 meteor geometry fallback: complete actual events assign without errors");
            }
        }
        finally { config.IdyllicDreamAdjustToMistakes = saved; }
    }

    private static void CheckM12P2CloneCastActivation()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var stackTarget = f.AddPlayer(0);
        var spreadTarget = f.AddPlayer(1);
        f.EnterState(0, 0x30210);
        f.Activate(M12P2Namespace + "IdyllicDreamStaging");
        var arena = f.Activate(M12P2Namespace + "IdyllicDreamArena");
        P2Field(arena, "State", 1);
        var stackClone = f.AddActor(0x380, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(100, 85));
        var spreadClone = f.AddActor(0x381, (uint)M12P2.OID.Lindschrat, ActorType.Enemy, Class.None, new(110, 85));
        foreach (var clone in new[] { stackClone, spreadClone })
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clone.InstanceID, 0x1E46));
        f.World.Execute(new ActorState.OpTether(stackClone.InstanceID, new((uint)M12P2.TetherID.RepStack, stackTarget.InstanceID)));
        f.World.Execute(new ActorState.OpTether(spreadClone.InstanceID, new((uint)M12P2.TetherID.RepSpread, spreadTarget.InstanceID)));
        f.Advance(2.8);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x30210 && !f.Module.Components.Any(c => c.GetType().Name == "IdyllicDreamWurmStackSpread"),
            "M12P2 clone forecast: waiting for actual TwistedVision cast cannot start clone deadlines early");
        f.StartCast(f.Boss, (uint)M12P2.AID.TwistedVision, 4);
        f.Advance(0);
        Check(f.Module.StateMachine.ActiveState?.ID == 0x30211,
            "M12P2 clone forecast: actual TwistedVision starts platform disappearance state");
        var mechanic = (UniformStackSpread)f.Module.Components.Single(c => c.GetType().Name == "IdyllicDreamWurmStackSpread");
        mechanic.Update();
        Check(mechanic.Stacks.Count == 1 && Math.Abs((mechanic.Stacks[0].Activation - f.Now).TotalSeconds - 10.5) < 0.001,
            "M12P2 clone forecast: actual cast start anchors stack deadline10.5s later");
        Check(mechanic.Spreads.Count == 1 && Math.Abs((mechanic.Spreads[0].Activation - f.Now).TotalSeconds - 11.7) < 0.001,
            "M12P2 clone forecast: actual cast start anchors defamation deadline11.7s later");
        Check(f.Hints(mechanic, 0).ForbiddenZones.Any(zone => Math.Abs((zone.activation - f.Now).TotalSeconds - 11.7) < 0.001),
            "M12P2 clone forecast: native AI uses actual cast anchored defamation deadline");
    }

}
