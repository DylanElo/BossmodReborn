using BossMod;
using BossMod.Components;
using P2Timing = BossMod.Dawntrail.Savage.M12S2Lindwurm;

internal static partial class EncounterChecks
{
    static partial void CheckRemainingTiming()
    {
        RunGroup("M12P2 Replication1 observed deadlines", CheckReplication1Deadlines);
        RunGroup("M12P2 player replay event deadlines", CheckPlayerReplayDeadlines);
        RunGroup("M12P2 stored replay cast deadlines", CheckStoredReplayDeadlines);
        RunGroup("DMU tower deadline jitter", CheckTowerDeadlineJitter);
    }

    private static void TimingEvent(Fixture f, Actor caster, uint action, WPos position)
        => f.World.Execute(new ActorState.OpCastEvent(caster.InstanceID,
            new(new(ActionType.Spell, action), 0, 0, 1, new(position.X, 0, position.Z), 0, 0, default)));

    private static void CheckReplication1Deadlines()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var player = f.AddPlayer(0, position: new(90, 90));
        f.AddPlayer(1, position: new(110, 90));
        f.AddPlayer(2, position: new(110, 92));
        f.Activate(M12P2Namespace + "Replication1SecondBait");
        var fire = f.AddActor(0xA01, (uint)P2Timing.OID.Lindschrat, ActorType.Enemy, Class.None, new(90, 90));
        var dark = f.AddActor(0xA02, (uint)P2Timing.OID.Lindschrat, ActorType.Enemy, Class.None, new(110, 90));
        f.StartCast(fire, (uint)P2Timing.AID.TopTierSlamCast, 3);
        f.StartCast(dark, (uint)P2Timing.AID.MightyMagicCast, 3);
        f.FinishCast(fire);
        f.FinishCast(dark);
        var bait = (UniformStackSpread)f.Activate(M12P2Namespace + "MightyMagicTopTierSlamSecondBait");
        TimingEvent(f, fire, (uint)P2Timing.AID.Dash, fire.Position);
        bait.Update();
        Check(bait.Stacks.Single().Activation == DateTime.MaxValue && f.Hints(bait, 0).ForbiddenZones.Count == 0,
            "M12P2 missing cleave leaves dash preview unanchored and does not force AI movement");
        f.Advance(23); // Independent delay before the actual cleave must not shorten the second bait.
        var cleave = f.Now;
        f.EventCast(f.Boss, (uint)P2Timing.AID.SnakingKick);
        f.Advance(8.4);
        TimingEvent(f, fire, (uint)P2Timing.AID.Dash, fire.Position);
        f.Advance(1);
        TimingEvent(f, dark, (uint)P2Timing.AID.Dash, dark.Position);
        bait.Update();
        Check(bait.Stacks.Single().Activation == cleave.AddSeconds(15.7), "M12P2 fire deadline anchors actual cleave rather than either staggered dash");
        Check(bait.Spreads.Count == 2 && bait.Spreads.All(s => s.Activation == cleave.AddSeconds(16.8)), "M12P2 dark deadline includes observed delay after fire");
        var first = bait.Stacks.Single().Activation;
        f.EventCast(f.Boss, (uint)P2Timing.AID.SnakingKick);
        TimingEvent(f, fire, (uint)P2Timing.AID.Dash, fire.Position);
        bait.Update();
        Check(bait.Stacks.Count == 1 && bait.Stacks[0].Activation == first, "M12P2 duplicate cleave and dash do not drift deadline or multiply targets");
        f.StartCast(fire, (uint)P2Timing.AID.TopTierSlamCast, 5);
        bait.Update();
        Check(Math.Abs((bait.Stacks.Single().Activation - f.Now).TotalSeconds - 6.3) < 0.001, "M12P2 repeat cast supplies authoritative fire deadline including native NPC finish correction");
        f.EventCast(fire, (uint)P2Timing.AID.TopTierSlamStack);
        f.EventCast(dark, (uint)P2Timing.AID.MightyMagicSpread);
        bait.Update();
        Check(bait.Stacks.Count == 0 && bait.Spreads.Count == 0, "M12P2 actual second bait effects clear timed AI targets");
    }

    private static void CheckPlayerReplayDeadlines()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        for (var slot = 0; slot < 8; ++slot) f.AddPlayer(slot, position: new(84 + 4 * slot, 100));
        f.Activate(M12P2Namespace + "IdyllicDreamStaging");
        Actor[] understudies = new Actor[8];
        for (var i = 0; i < 8; ++i)
        {
            understudies[i] = f.AddActor((ulong)(0xB00 + i), (uint)P2Timing.OID.Understudy, ActorType.Enemy, Class.None, new(85 + 4 * i, 90));
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(understudies[i].InstanceID, 0x11D2));
            f.World.Execute(new ActorState.OpTether(understudies[i].InstanceID, new((uint)P2Timing.TetherID.Fixed, f.World.Party[i]!.InstanceID)));
        }
        Actor[] clones = new Actor[8];
        for (var i = 0; i < 8; ++i)
        {
            clones[i] = f.AddActor((ulong)(0xB20 + i), (uint)P2Timing.OID.Lindschrat, ActorType.Enemy, Class.None, new(100 + i, 85));
            f.World.Execute(new ActorState.OpPlayActionTimelineEvent(clones[i].InstanceID, 0x1E46));
        }
        for (var i = 0; i < 8; ++i)
            f.World.Execute(new ActorState.OpTether(clones[i].InstanceID, new(i % 2 == 0 ? (uint)P2Timing.TetherID.RepSpread : (uint)P2Timing.TetherID.RepStack, f.World.Party[i]!.InstanceID)));
        for (var i = 0; i < 8; ++i)
            f.World.Execute(new ActorState.OpTether(clones[i].InstanceID, new((uint)P2Timing.TetherID.Fixed, f.World.Party[i]!.InstanceID)));
        var spread = (GenericAOEs)f.Activate(M12P2Namespace + "IdyllicDreamManaBurstPlayer");
        var towers = (GenericTowers)f.Activate(M12P2Namespace + "IdyllicDreamHeavySlamPlayer");
        void Predict(int order)
        {
            spread.GetType().GetMethod("Predict")!.Invoke(spread, [order]);
            towers.GetType().GetMethod("Predict")!.Invoke(towers, [order]);
            P2Field(spread, "Risky", true);
        }
        Predict(0);
        var player = f.World.Party[0]!;
        Check(spread.ActiveAOEs(0, player).Length == 2 && towers.Towers.Count == 2, "M12P2 first actual clone assignment generates two spreads and two towers");
        Check(f.Hints(spread, 1).ForbiddenZones.Count == 0 && f.Hints(towers, 1).ForbiddenZones.Count == 0,
            "M12P2 unanchored replay geometry does not impose AI movement even with risk requested");
        f.Advance(17);
        f.StartCast(f.Boss, (uint)P2Timing.AID.Reenactment, 6);
        var deadline = f.Now;
        Check(spread.ActiveAOEs(0, player).ToArray().All(a => Math.Abs((a.Activation - deadline).TotalSeconds - 9.6) < 0.001), "M12P2 first replay spread follows native NPC finish correction plus observed 3.3-second delay");
        Check(towers.Towers.All(t => Math.Abs((t.Activation - deadline).TotalSeconds - 9.8) < 0.001), "M12P2 first replay tower follows native NPC finish correction plus observed 3.5-second delay");
        Check(f.Hints(spread, 1).ForbiddenZones.Count > 0 && f.Hints(towers, 1).ForbiddenZones.Count > 0,
            "M12P2 observed replay anchor enables native spread and tower AI constraints");
        Predict(0);
        Check(spread.ActiveAOEs(0, player).Length == 2 && towers.Towers.Count == 2, "M12P2 repeated prediction replaces geometry instead of duplicating it");
        Check(towers.Towers.All(t => Math.Abs((t.Activation - deadline).TotalSeconds - 9.8) < 0.001), "M12P2 duplicate prediction preserves an observed deadline instead of dropping back to a guess");
        Predict(1);
        Check(f.Hints(spread, 1).ForbiddenZones.Count == 0 && f.Hints(towers, 1).ForbiddenZones.Count == 0, "M12P2 second replay clears first-wave timing and constraints");
        f.Advance(7);
        var exit = f.Now;
        var portalClone = f.World.Actors.Find(0xB20)!;
        f.EventCast(portalClone, (uint)P2Timing.AID.TemporalTearExit);
        Check(spread.ActiveAOEs(0, player).ToArray().All(a => a.Activation == exit.AddSeconds(1.2f)) && towers.Towers.All(t => t.Activation == exit.AddSeconds(1.4f)),
            "M12P2 second replay follows actual portal exit with distinct damage delays");
        f.Advance(0.4);
        f.EventCast(portalClone, (uint)P2Timing.AID.TemporalTearExit);
        Check(towers.Towers.All(t => t.Activation == exit.AddSeconds(1.4f)), "M12P2 duplicate portal exit cannot postpone replay");
        foreach (var aoe in spread.ActiveAOEs(0, player).ToArray())
        {
            var helper = f.AddActor((ulong)(0xB40 + (int)aoe.Origin.X), 0x233C, ActorType.Enemy, Class.None, aoe.Origin);
            f.EventCast(helper, (uint)P2Timing.AID.ManaBurstReplay);
        }
        foreach (var tower in towers.Towers.ToArray())
        {
            var helper = f.AddActor((ulong)(0xB80 + (int)tower.Position.X), 0x233C, ActorType.Enemy, Class.None, tower.Position);
            f.EventCast(helper, (uint)P2Timing.AID.HeavySlamReplay);
        }
        Check(spread.ActiveAOEs(0, player).Length == 0 && towers.Towers.Count == 0, "M12P2 actual replay effects remove all second-wave predictions");
    }

    private static void CheckStoredReplayDeadlines()
    {
        using var f = new Fixture("M12S2TheLindwurm");
        var player = f.AddPlayer(0);
        var stored = (GenericAOEs)f.Activate(M12P2Namespace + "IdyllicDreamPowerGusherSnakingKick");
        var circle = f.AddActor(0xC01, (uint)P2Timing.OID.Lindschrat, ActorType.Enemy, Class.None, new(90, 100));
        var cone = f.AddActor(0xC02, (uint)P2Timing.OID.Lindschrat, ActorType.Enemy, Class.None, new(110, 100));
        f.Advance(17);
        f.StartCast(circle, (uint)P2Timing.AID.SnakingKickCastFirst, 5);
        f.StartCast(cone, (uint)P2Timing.AID.PowerGusherAOEVisual, 5);
        P2Field(stored, "Visible", true);
        var first = stored.ActiveAOEs(0, player).ToArray();
        Check(Math.Abs((first.Single(a => a.Shape is AOEShapeCircle).Activation - f.Now).TotalSeconds - 34.4) < 0.001,
            "M12P2 initial stored circle follows delayed five-second visual finish plus 29.1 seconds and native NPC correction");
        Check(Math.Abs((first.Single(a => a.Shape is AOEShapeCone).Activation - f.Now).TotalSeconds - 34.6) < 0.001,
            "M12P2 initial stored cone follows visual finish plus 29.3 seconds rather than cast start");
        Check(f.Hints(stored, 0).ForbiddenZones.Count == 0, "M12P2 initial stored visual remains a harmless preview before the risk gate");
        P2Field(stored, "Risky", true);
        P2Field(stored, "WatchTeleport", true);
        var reset = stored.GetType().GetMethod("Reset")!;
        reset.Invoke(stored, [false]);
        Check(f.Hints(stored, 0).ForbiddenZones.Count == 0, "M12P2 absent actual Twisted Vision cast does not invent a replay deadline");
        f.Advance(21);
        f.StartCast(f.Boss, (uint)P2Timing.AID.TwistedVision, 7);
        reset.Invoke(stored, [false]);
        var aoes = stored.ActiveAOEs(0, player).ToArray();
        Check(Math.Abs((aoes.Single(a => a.Shape is AOEShapeCircle).Activation - f.Now).TotalSeconds - 11.7) < 0.001, "M12P2 circle replay follows actual delayed Twisted Vision finish plus 4.4 seconds with native NPC correction");
        Check(Math.Abs((aoes.Single(a => a.Shape is AOEShapeCone).Activation - f.Now).TotalSeconds - 11.9) < 0.001, "M12P2 cone replay retains observed 0.2-second delay after circle");
        f.EventCast(cone, (uint)P2Timing.AID.TemporalTearEnter);
        f.Advance(12);
        var exit = f.Now;
        TimingEvent(f, cone, (uint)P2Timing.AID.TemporalTearExit, new(105, 105));
        f.Advance(2.2);
        reset.Invoke(stored, [true]);
        Check(stored.ActiveAOEs(0, player).ToArray().All(a => a.Activation == exit.AddSeconds(6.2)), "M12P2 final stored replay stays anchored to observed portal exit despite variable intervening cast timing");
    }

    private static void CheckTowerDeadlineJitter()
    {
        using var f = new Fixture("DMU");
        var player = f.AddPlayer(0);
        var towers = (GenericTowers)f.Activate("BossMod.Dawntrail.Ultimate.DMU.Celestriad");
        var actor = f.AddActor(0xD01, (uint)BossMod.Dawntrail.Ultimate.DMU.OID.IceTower, ActorType.EventObj, Class.None, new(100, 86));
        f.World.Execute(new ActorState.OpEventObjectAnimation(actor.InstanceID, 0x10, 0x20));
        Check(towers.Towers.Single().Activation == f.Now.AddSeconds(6.1f), "DMU observed primary-timeline tower glow anchors a 6.1-second deadline");
        f.Advance(5.8);
        f.World.Execute(new ActorState.OpEventObjectAnimation(actor.InstanceID, 0x10, 0x20));
        f.Advance(0.1);
        f.World.Execute(new ActorState.OpEventObjectAnimation(actor.InstanceID, 1, 0x40));
        Check(towers.NumCasts == 1 && towers.Towers.Count == 1, "DMU early tower explosion consumes previous wave and preserves interleaved future wave");
        f.World.Execute(new ActorState.OpEventObjectAnimation(actor.InstanceID, 1, 0x40));
        Check(towers.NumCasts == 1 && towers.Towers.Count == 1, "DMU repeated early explosion cannot consume future wave");
        f.Advance(7);
        f.World.Execute(new ActorState.OpEventObjectAnimation(actor.InstanceID, 1, 0x40));
        Check(towers.NumCasts == 2 && towers.Towers.Count == 0, "DMU late observed explosion releases its wave despite forecast jitter");
    }
}
