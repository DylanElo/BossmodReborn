namespace BossMod.Dawntrail.Ultimate.FRU;

sealed class P4CrystallizeTime(BossModule module) : BossComponent(module)
{
    public enum Mechanic { None, FangEruption, FangWater, FangDarkness, FangBlizzard, ClawAir, ClawBlizzard }

    public readonly Mechanic[] PlayerMechanics = new Mechanic[PartyState.MaxPartySize];
    public readonly int[] ClawSides = new int[PartyState.MaxPartySize]; // 0 if not assigned (bad config or no claw), +/-1 otherwise for E/W
    public WDir NorthSlowHourglass;
    public BitMask Cleansed;
    private int _numClaws;

    public Actor? FindPlayerByAssignment(Mechanic mechanic, int side)
    {
        var len = PlayerMechanics.Length;
        for (var i = 0; i < len; ++i)
            if (PlayerMechanics[i] == mechanic && ClawSides[i] == side)
                return Raid[i];
        return null;
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        switch (status.ID)
        {
            case (uint)SID.SpellInWaitingDarkEruption:
                AssignMechanic(actor, Mechanic.FangEruption); // always paired with fang
                break;
            case (uint)SID.SpellInWaitingDarkWater:
                AssignMechanic(actor, Mechanic.FangWater); // always paired with fang
                break;
            case (uint)SID.SpellInWaitingUnholyDarkness:
                AssignMechanic(actor, Mechanic.FangDarkness); // always paired with fang
                break;
            case (uint)SID.SpellInWaitingDarkBlizzard:
                AssignMechanic(actor, Mechanic.FangBlizzard, higherPrio: Mechanic.ClawBlizzard); // paired with either, we'll reassign to claw when reacting to claw buff
                break;
            case (uint)SID.SpellInWaitingDarkAero:
                AssignMechanic(actor, Mechanic.ClawAir); // always paired with claw
                break;
            case (uint)SID.Wyrmfang:
                break; // don't react
            case (uint)SID.Wyrmclaw:
                var duration = (status.ExpireAt - WorldState.CurrentTime).TotalSeconds; // 40s for aero, 17s for claw
                if (duration > 25d)
                    AssignMechanic(actor, Mechanic.ClawAir);
                else
                    AssignMechanic(actor, Mechanic.ClawBlizzard, Mechanic.FangBlizzard);
                if (++_numClaws == 4)
                    AssignClawSides();
                break;
        }
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        if (status.ID is (uint)SID.Wyrmclaw or (uint)SID.Wyrmfang
            && (!Service.Config.Get<FRUConfig>().P4ManaStrategy || !actor.IsDeadOrDestroyed && actor.HPMP.CurHP > 0 && status.ExpireAt > WorldState.CurrentTime))
            Cleansed.Set(Raid.FindSlot(actor.InstanceID));
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.UltimateRelativitySlow && source.PosRot.Z < Arena.Center.Z)
            NorthSlowHourglass = source.Position - Arena.Center;
    }

    private void AssignMechanic(Actor player, Mechanic mechanic, Mechanic lowerPrio = Mechanic.None, Mechanic higherPrio = Mechanic.None)
    {
        var slot = Raid.FindSlot(player.InstanceID);
        if (slot < 0)
            return;
        ref var mech = ref PlayerMechanics[slot];
        if (mech == Mechanic.None || mech == lowerPrio)
            mech = mechanic;
        else if (mech != higherPrio && mech != mechanic)
            ReportError($"Trying to assing {mechanic} to {player} who already has {mech}");
    }

    private void AssignClawSides()
    {
        void assign(int slot, int prio, ref (int slot, int prio) prev)
        {
            if (prev.prio < 0)
            {
                prev = (slot, prio);
            }
            else
            {
                var prevWest = prev.prio < prio;
                ClawSides[prev.slot] = prevWest ? -1 : 1;
                ClawSides[slot] = prevWest ? 1 : -1;
            }
        }
        Span<(int slot, int prio)> prios = [(-1, -1), (-1, -1), (-1, -1), (-1, -1), (-1, -1), (-1, -1), (-1, -1)];
        foreach (var (slot, group) in Service.Config.Get<FRUConfig>().P4CrystallizeTimeAssignments.Resolve(Raid))
            assign(slot, group, ref prios[(int)PlayerMechanics[slot]]);
    }
}

sealed class P4CrystallizeTimeDragonHead(BossModule module) : BossComponent(module)
{
    public readonly List<(Actor head, int side)> Heads = [];
    private readonly P4CrystallizeTime? _ct = module.FindComponent<P4CrystallizeTime>();
    private readonly List<(Actor puddle, P4CrystallizeTime.Mechanic soaker)> _puddles = [];
    private readonly HashSet<ulong> _resolvedMaelstroms = [];
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private bool ManaPuddlesReady => _resolvedMaelstroms.Count >= 6;

    public Actor? FindHead(int side)
    {
        var count = Heads.Count;
        for (var i = 0; i < count; ++i)
        {
            var h = Heads[i];
            if (h.side == side)
                return h.head;
        }
        return null;
    }

    public static int NumHeadHits(Actor? head) => head == null ? 2 : head.HitboxRadius < 2 ? 1 : 0;
    public Actor? FindInterceptor(Actor head, int side) => _ct?.FindPlayerByAssignment(NumHeadHits(head) > 0 ? P4CrystallizeTime.Mechanic.ClawAir : P4CrystallizeTime.Mechanic.ClawBlizzard, side);

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_config.P4ManaStrategy && _ct != null && !_ct.Cleansed[slot] && IsFang(_ct.PlayerMechanics[slot]) && !ManaPuddleAssignmentsReady())
            hints.Add(ManaPuddleOwners() != null ? "Blue marks incomplete: unmarked blues take leftover puddles (east to west)" : "Blue players: mark yourselves attack 1-4 for puddles (east to west)!");
    }

    private static bool IsFang(P4CrystallizeTime.Mechanic mechanic) => mechanic is P4CrystallizeTime.Mechanic.FangEruption or P4CrystallizeTime.Mechanic.FangWater or P4CrystallizeTime.Mechanic.FangDarkness or P4CrystallizeTime.Mechanic.FangBlizzard;

    // fully marked by the party (attack 1-4 on the four blue players)
    private bool ManaPuddleAssignmentsReady() => ManaPuddleOwners(false) != null;

    // puddle owners, east-most first: attack N takes the Nth puddle from the east; with allowFallback,
    // blue players that did not mark take the leftover puddles, east-most unmarked player first
    // (PF often has one blue forget the macro, which would otherwise leave every blue unable to cleanse)
    private ulong[]? ManaPuddleOwners(bool allowFallback = true)
    {
        if (_ct == null || _puddles.Count != 4)
            return null;
        // Near-ties in X (0.9y in logs) keep the plain X order, same as the Splatoon script, so both tools agree;
        // refusing left every blue without a puddle. A human reading it the other way is covered by ManaOwnPuddleTaken.
        var owners = new ulong[4];
        var used = new HashSet<ulong>();
        for (var i = 0; i < 4; ++i)
        {
            var owner = WorldState.Waymarks[(Sign)i];
            var slot = Raid.FindSlot(owner);
            if (slot >= 0 && IsFang(_ct.PlayerMechanics[slot]) && used.Add(owner))
                owners[i] = owner;
            else if (!allowFallback)
                return null;
        }
        var unmarked = Raid.WithSlot(true, true, true).Where(p => IsFang(_ct.PlayerMechanics[p.Item1]) && !used.Contains(p.Item2.InstanceID)).OrderByDescending(p => p.Item2.PosRot.X).ToArray();
        var free = Enumerable.Range(0, 4).Where(i => owners[i] == 0).ToArray();
        if (unmarked.Length != free.Length)
            return null;
        for (var i = 0; i < free.Length; ++i)
            owners[free[i]] = unmarked[i].Item2.InstanceID;
        return owners;
    }

    private ulong ManaPuddleOwner(Actor puddle)
    {
        var owners = ManaPuddleOwners();
        return owners == null ? 0 : owners[_puddles.Count(p => p.puddle.PosRot.X > puddle.PosRot.X)];
    }

    private static bool PuddleUsed(Actor puddle) => puddle.EventState == 7 || puddle.IsDestroyed;

    // Another blue took your numbered puddle (seen in logs: Attack 4 took Attack 3's, 0.9y apart in X):
    // any puddle still free cleanses, so take one rather than die to Wyrmfang at Return.
    // Standing on it means we used it ourselves and Wyrmfang just hasn't dropped yet: don't send us to another blue's puddle.
    private bool ManaOwnPuddleTaken(int slot, Actor actor)
        => ManaPuddlesReady && _ct != null && !_ct.Cleansed[slot] && IsFang(_ct.PlayerMechanics[slot])
            && _puddles.Exists(p => PuddleUsed(p.puddle) && ManaPuddleOwner(p.puddle) == actor.InstanceID && !actor.Position.InCircle(p.puddle.Position, 3f));

    private bool ManaPuddleIsMine(int slot, Actor actor, Actor puddle)
        => ManaPuddlesReady && (ManaPuddleOwner(puddle) == actor.InstanceID || ManaOwnPuddleTaken(slot, actor));

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        // avoid wrong puddles and try to grab desired one
        if (_ct != null)
        {
            var pcAssignment = _ct.PlayerMechanics[slot];
            var count = _puddles.Count;
            for (var i = 0; i < count; ++i)
            {
                var p = _puddles[i];
                if (!PuddleUsed(p.puddle))
                {
                    if (_config.P4ManaStrategy ? !ManaPuddleIsMine(slot, actor, p.puddle) : p.soaker != pcAssignment)
                        hints.AddForbiddenZone(new SDCircle(p.puddle.Position, 2f));
                    else if (ManaPuddlesReady)
                        hints.GoalZones.Add(AIHints.GoalProximity(p.puddle.Position, 15f, 0.5f));
                }
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var count = Heads.Count;
        if (count == 0)
            return;
        for (var i = 0; i < count; ++i)
        {
            var h = Heads[i];
            Arena.Actor(h.head, Colors.Object, true);
            var interceptor = FindInterceptor(h.head, h.side);
            if (interceptor != null)
                Arena.ZoneCircleOutline(interceptor.Position, 12f);
        }
    }

    public override void DrawArenaBackground(int pcSlot, Actor pc)
    {
        if (_ct != null /*&& ShowPuddles && !_ct.Cleansed[pcSlot]*/)
        {
            var pcAssignment = _ct.PlayerMechanics[pcSlot];
            var count = _puddles.Count;
            for (var i = 0; i < count; ++i)
            {
                var p = _puddles[i];
                if (!PuddleUsed(p.puddle))
                    Arena.ZoneCircle(p.puddle.Position, 1f, (_config.P4ManaStrategy ? ManaPuddleIsMine(pcSlot, pc, p.puddle) : p.soaker == pcAssignment) ? Colors.SafeFromAOE : 0);
            }
        }
    }

    public override void OnActorCreated(Actor actor)
    {
        switch (actor.OID)
        {
            case (uint)OID.DrachenWanderer:
                Heads.Add((actor, actor.PosRot.X > Arena.Center.X ? 1 : -1));
                break;
            case (uint)OID.DragonPuddle:
                // TODO: this is very arbitrary
                var mechanic = actor.PosRot.X < Arena.Center.X
                    ? AssignPuddle(P4CrystallizeTime.Mechanic.FangEruption, P4CrystallizeTime.Mechanic.FangBlizzard)
                    : AssignPuddle(P4CrystallizeTime.Mechanic.FangDarkness, P4CrystallizeTime.Mechanic.FangWater);
                _puddles.Add((actor, mechanic));
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.DrachenWandererDisappear:
                var count = Heads.Count;
                for (var i = 0; i < count; ++i)
                {
                    if (Heads[i].head == caster)
                    {
                        Heads.RemoveAt(i);
                        return;
                    }
                }
                break;
            case (uint)AID.CrystallizeTimeMaelstrom:
                _resolvedMaelstroms.Add(caster.InstanceID);
                break;
        }
    }

    private P4CrystallizeTime.Mechanic AssignPuddle(P4CrystallizeTime.Mechanic first, P4CrystallizeTime.Mechanic second)
    {
        var count = _puddles.Count;
        for (var i = 0; i < count; ++i)
        {
            if (_puddles[i].soaker == first)
            {
                return second;
            }
        }
        return first;
    }
}

sealed class P4CrystallizeTimeMaelstrom(BossModule module) : Components.GenericAOEs(module, (uint)AID.CrystallizeTimeMaelstrom)
{
    public readonly List<AOEInstance> AOEs = [];
    private readonly HashSet<ulong> _resolvedSources = [];

    private static readonly AOEShapeCircle _shape = new(12f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        var count = AOEs.Count;
        if (count == 0)
            return [];
        var max = count > 2 ? 2 : count;
        return CollectionsMarshal.AsSpan(AOEs)[..max];
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { } // handled by other components

    // assuming that this component is activated when speed cast starts - all hourglasses should be already created, and tethers should have appeared few frames ago
    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID == (uint)OID.SorrowsHourglass)
        {
            AOEs.Add(new(_shape, actor.Position.Quantized(), actor.Rotation, WorldState.FutureTime(13.2d)));
            SortHelpers.SortAOEByActivation(AOEs);
        }
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        var delay = tether.ID switch
        {
            (uint)TetherID.UltimateRelativitySlow => 18.3d,
            (uint)TetherID.UltimateRelativityQuicken => 7.7d,
            _ => 0
        };
        if (delay != 0)
        {
            var count = AOEs.Count;
            var pos = source.Position;
            for (var i = 0; i < count; ++i)
            {
                var aoe = AOEs[i];
                if (aoe.Origin.AlmostEqual(pos, 1f))
                {
                    AOEs.Ref(i).Activation = WorldState.FutureTime(delay);
                    SortHelpers.SortAOEByActivation(AOEs);
                    return;
                }
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction && _resolvedSources.Add(caster.InstanceID))
        {
            ++NumCasts;
            var count = AOEs.Count;
            var pos = caster.Position;
            for (var i = 0; i < count; ++i)
            {
                var aoe = AOEs[i];
                if (aoe.Origin.AlmostEqual(pos, 1f))
                {
                    AOEs.RemoveAt(i);
                    return;
                }
            }
        }
    }
}

sealed class P4CrystallizeTimeDarkWater(BossModule module) : Components.UniformStackSpread(module, 6f, default, 4, 4)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { } // handled by other components

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.SpellInWaitingDarkWater)
        {
            BitMask forbidden = default;
            if (Module.FindComponent<P4CrystallizeTime>() is var ct && ct != null)
            {
                var len = ct.PlayerMechanics.Length;
                for (var i = 0; i < len; ++i)
                {
                    // should not be shared by eruption and all claws except air on slow side
                    forbidden[i] = ct.PlayerMechanics[i] switch
                    {
                        P4CrystallizeTime.Mechanic.FangEruption => true,
                        P4CrystallizeTime.Mechanic.ClawBlizzard => true,
                        P4CrystallizeTime.Mechanic.ClawAir => ct.ClawSides[i] * ct.NorthSlowHourglass.X > 0f,
                        _ => false
                    };
                }
            }
            AddStack(actor, status.ExpireAt, forbidden);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.DarkWater)
            Stacks.Clear();
    }
}

sealed class P4CrystallizeTimeDarkEruption(BossModule module) : Components.GenericBaitAway(module, (uint)AID.DarkEruption, centerAtTarget: true)
{
    private static readonly AOEShapeCircle _shape = new(6f);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { } // handled by other components

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.SpellInWaitingDarkEruption)
        {
            CurrentBaits.Add(new(Module.PrimaryActor, actor, _shape, status.ExpireAt));
        }
    }
}

sealed class P4CrystallizeTimeDarkAero(BossModule module) : Components.GenericKnockback(module, (uint)AID.CrystallizeTimeDarkAero) // TODO: not sure whether it actually ignores immunes, if so need to warn about immunity
{
    private readonly List<Actor> _sources = [];
    private DateTime _activation;

    private static readonly AOEShapeCircle _shape = new(15f);

    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        var count = _sources.Count;
        if (count == 0)
            return [];
        List<Knockback> sources = [];
        for (var i = 0; i < count; ++i)
        {
            var s = _sources[i];
            if (s != actor)
                sources.Add(new(s.Position, 30f, _activation, _shape));
        }
        return CollectionsMarshal.AsSpan(sources);
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.SpellInWaitingDarkAero)
        {
            _sources.Add(actor);
            _activation = status.ExpireAt;
        }
    }
}

sealed class P4CrystallizeTimeUnholyDarkness(BossModule module) : Components.UniformStackSpread(module, 6f, default, 5, 5)
{
    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { } // handled by other components

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.SpellInWaitingUnholyDarkness)
        {
            BitMask forbidden = default;
            if (Module.FindComponent<P4CrystallizeTime>() is var ct && ct != null)
            {
                var len = ct.PlayerMechanics.Length;
                for (var i = 0; i < len; ++i)
                {
                    // should not be shared by all claws except blizzard on slow side
                    forbidden[i] = ct.PlayerMechanics[i] switch
                    {
                        P4CrystallizeTime.Mechanic.ClawBlizzard => ct.ClawSides[i] * ct.NorthSlowHourglass.X < 0f,
                        P4CrystallizeTime.Mechanic.ClawAir => true,
                        _ => false
                    };
                }
            }
            AddStack(actor, status.ExpireAt, forbidden);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UltimateRelativityUnholyDarkness)
            Stacks.Clear();
    }
}

sealed class P4CrystallizeTimeTidalLight : Components.Exaflare
{
    public List<(WPos pos, Angle dir)> StartingPositions = [];
    public WDir StartingOffsetSum;

    public P4CrystallizeTimeTidalLight(BossModule module) : base(module, new AOEShapeRect(10f, 20f))
    {
        ImminentColor = Colors.AOE;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.TidalLightAOEFirst)
        {
            Lines.Add(new(caster.Position, 10f * spell.Rotation.ToDirection(), Module.CastFinishAt(spell), 2.1d, 4, 1, spell.Rotation));
            StartingPositions.Add((caster.Position, spell.Rotation));
            StartingOffsetSum += caster.Position - Arena.Center;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.TidalLightAOEFirst or (uint)AID.TidalLightAOERest)
        {
            ++NumCasts;
            var count = Lines.Count;
            var pos = caster.Position;
            for (var i = 0; i < count; ++i)
            {
                var line = Lines[i];
                if (line.Next.AlmostEqual(pos, 1f))
                {
                    AdvanceLine(line, pos);
                    if (line.ExplosionsLeft == 0)
                        Lines.RemoveAt(i);
                    return;
                }
            }
            ReportError($"Failed to find entry for {caster.InstanceID:X}");
        }
    }
}

sealed class P4CrystallizeTimeQuietus(BossModule module) : Components.CastCounter(module, (uint)AID.Quietus);

sealed class P4CrystallizeTimeHints(BossModule module) : BossComponent(module)
{
    public enum Hint
    {
        None = 0,
        SafespotRough = 1 << 0, // position roughly around safespot
        SafespotPrecise = 1 << 1, // position exactly at safespot
        Maelstrom = 1 << 2, // avoid maelstroms
        Heads = 1 << 3, // avoid head interceptors
        Knockback = 1 << 4, // position to knock back across
        KnockbackFrom = 1 << 5, // position to be a knockback source
        Mid = 1 << 6, // position closer to center if possible
    }

    private readonly P4CrystallizeTime? _ct = module.FindComponent<P4CrystallizeTime>();
    private readonly P4CrystallizeTimeDragonHead? _heads = module.FindComponent<P4CrystallizeTimeDragonHead>();
    private readonly P4CrystallizeTimeMaelstrom? _hourglass = module.FindComponent<P4CrystallizeTimeMaelstrom>();
    private DateTime KnockbacksResolve; // default before knockbacks are done, set to estimated resolve time after they are done
    private bool DarknessDone;
    private bool _eruptionResolved;
    private bool _eruptionHeadResolved;
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();

    public override void Update()
    {
        // Observe the actual matching head's first contact; absence is not success.
        if (_config.P4ManaStrategy && _ct != null && _ct.NorthSlowHourglass.X != 0f
            && _heads?.FindHead(_ct.NorthSlowHourglass.X > 0f ? 1 : -1) is { HitboxRadius: < 2f })
            _eruptionHeadResolved = true;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (actor.PendingKnockbacks.Count > 0)
        {
            return; // don't move while waiting for kb to resolve...
        }

        // near red ice heads for the darkness stack right next to the eruption cubby: don't arrive before eruption resolves
        if (!_eruptionResolved && _ct?.FindPlayerByAssignment(P4CrystallizeTime.Mechanic.FangEruption, 0) is { } erupter && erupter != actor)
            hints.AddForbiddenZone(new SDCircle(erupter.Position, 6.5f), Module.FindComponent<P4CrystallizeTimeDarkEruption>() is { CurrentBaits.Count: > 0 } er ? er.CurrentBaits[0].Activation : default);

        var hint = CalculateHint(slot);
        // lone Ice starts inside the second hourglass (5.3y off it) and the run north needs ~17.8y to clear it: 3.0s on foot misses the
        // 27.4s explosion when leaving at the 24.8s Blizzard, 2.4s with Sprint makes it (FRU sim Sprints here too)
        if (_config.P4ManaStrategy && hint.offset == SafeOffsetManaLoneIceNorth(0))
            hints.ActionsToExecute.Push(ActionID.MakeSpell(ClassShared.AID.Sprint), actor, ActionQueue.Priority.High);
        if (hint.offset != default)
        {
            // we want to stay really close to border
            if (hint.offset.LengthSq() > 324f)
            {
                hint.offset *= 1.02632f;
            }

            var hint_ = hint.hint;
            if ((hint_ & Hint.KnockbackFrom) != 0)
            {
                var party = Raid.WithoutSlot(false, true, true);
                var len = party.Length;
                for (var i = 0; i < len; ++i)
                {
                    if (party[i].PendingKnockbacks.Count != 0)
                    {
                        return; // don't even try moving until all knockbacks are resolved, that can fuck up others...
                    }
                }
            }
            if ((hint_ & Hint.SafespotRough) != 0)
            {
                hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + hint.offset, 1f), DateTime.MaxValue);
            }
            if ((hint_ & Hint.SafespotPrecise) != 0)
            {
                hints.PathfindMapBounds = FRU.PathfindHugBorderBounds;
                hints.AddForbiddenZone(new SDPrecisePosition(Arena.Center + hint.offset, new(default, 1f), Arena.Bounds.MapResolution, actor.Position, 0.1f));
            }
            if ((hint_ & Hint.Maelstrom) != 0 && _hourglass != null)
            {
                var count = _hourglass.AOEs.Count;
                var aoes = CollectionsMarshal.AsSpan(_hourglass.AOEs);
                var max = count > 2 ? 2 : count;
                for (var i = 0; i < max; ++i)
                {
                    ref var aoe = ref aoes[i];
                    hints.AddForbiddenZone(aoe.Shape.Distance(aoe.Origin, aoe.Rotation), aoe.Activation);
                }
            }
            if ((hint_ & Hint.Heads) != 0 && _heads != null)
            {
                var count = _heads.Heads.Count;
                for (var i = 0; i < count; ++i)
                {
                    var h = _heads.Heads[i];
                    if (_heads.FindInterceptor(h.head, h.side) is var interceptor && interceptor != null && interceptor != actor)
                        hints.AddForbiddenZone(new SDCircle(interceptor.Position, 12f));
                }
            }
            if ((hint_ & Hint.Knockback) != 0 && _ct != null)
            {
                var source = _ct.FindPlayerByAssignment(P4CrystallizeTime.Mechanic.ClawAir, _ct.NorthSlowHourglass.X > 0f ? -1 : 1);
                var dest = Arena.Center + SafeOffsetDarknessStack(_ct.NorthSlowHourglass.X > 0 ? 1 : -1);
                var pos = source != null ? source.Position + 2 * (dest - source.Position).Normalized() : Arena.Center + hint.offset;
                hints.AddForbiddenZone(new SDPrecisePosition(pos, new(default, 1f), Arena.Bounds.MapResolution, actor.Position, 0.1f));
            }
            if ((hint_ & Hint.Mid) != 0 && _hourglass != null)
            {
                var count = _hourglass.AOEs.Count;
                var max = count > 2 ? 2 : count;
                var aoes = CollectionsMarshal.AsSpan(_hourglass.AOEs);
                for (var i = 0; i < max; ++i)
                {
                    if (aoes[i].Check(actor.Position))
                    {
                        return;
                    }
                }
                // stay on correct side
                var dest = Arena.Center + new WDir(default, hint.offset.Z > 0f ? 18f : -18f);
                hints.GoalZones.Add(AIHints.GoalSingleTarget(dest, 2f, 0.5f));
            }
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        var safeOffset = CalculateHint(pcSlot).offset;
        if (safeOffset != default)
            Arena.ZoneCircleOutline(Arena.Center + safeOffset, 1f, Colors.Safe);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.DarkEruption:
                _eruptionResolved = true;
                break;
            case (uint)AID.CrystallizeTimeDarkAero:
                KnockbacksResolve = WorldState.FutureTime(1d); // it takes ~0.8s to resolve knockbacks
                break;
            case (uint)AID.UltimateRelativityUnholyDarkness:
                DarknessDone = true;
                break;
        }
    }

    // these are all possible 'raw' safespot offsets; they expect valid arguments
    private static readonly Angle IdealSecondHeadBaitAngle = 33f.Degrees();
    private static WDir SafeOffsetDodgeFirstHourglassSouth(int side) => 19f * (side * 40f).Degrees().ToDirection();
    private static WDir SafeOffsetPreKnockbackSouth(int side, float radius) => radius * (side * 30f).Degrees().ToDirection();
    private static WDir SafeOffsetDarknessStack(int side) => 19f * (side * 140f).Degrees().ToDirection();
    private static WDir SafeOffsetDodgeSecondHourglassSouth(int side) => 19f * (side * 20f).Degrees().ToDirection();
    private static WDir SafeOffsetDodgeSecondHourglassEW(int side) => 19f * (side * 80f).Degrees().ToDirection(); // for ice that doesn't share unholy darkness
    private static WDir SafeOffsetManaLoneIceNorth(int side) => new(0f, -19f); // Mana lone Ice: FRU sim (Mana) Sprints to north center (sim 43,0) at head contact, clearing the second hourglass
    private static readonly WDir SafeOffsetManaIceNorth = new(0f, -17f); // Mana Ices after hg2/Darkness: FRU sim NS_EXA_DODGE, (98-102, 83) depending on the first Tidal Light
    private static WDir SafeOffsetFirstHeadBait(int side) => 13f * (side * 90f).Degrees().ToDirection();
    private static WDir SafeOffsetSecondHeadBait(int side) => 13f * (side * IdealSecondHeadBaitAngle).ToDirection();
    private static WDir SafeOffsetChillNorth(int side) => 6f * (side * 150f).Degrees().ToDirection(); // final for non-airs
    private static WDir SafeOffsetChillSouth(int side) => 6f * (side * 30f).Degrees().ToDirection(); // final for 2 airs

    // these determine rough safespot offset (depending on player state and mechanic progress) for drawing on arena or adding ai hints
    private (WDir offset, Hint hint) CalculateHint(int slot)
    {
        if (_ct == null || _heads == null || _hourglass == null || _ct.NorthSlowHourglass.X == 0)
            return default;
        var clawSide = _ct.ClawSides[slot];
        var northSlowSide = _ct.NorthSlowHourglass.X > 0f ? 1 : -1;
        var result = CalculateRawHint(slot, clawSide, northSlowSide);
        // Unholy needs 5: stack on the actual holder rather than the ideal spot (PF holders rarely stand exactly on it)
        if (_config.P4ManaStrategy && result.offset == SafeOffsetDarknessStack(northSlowSide)
            && Module.FindComponent<P4CrystallizeTimeUnholyDarkness>() is { Stacks.Count: > 0 } unholy
            && unholy.Stacks[0] is var stack && stack.Target != Raid[slot] && !stack.ForbiddenPlayers[slot] && !stack.Target.IsDeadOrDestroyed)
            result.offset = stack.Target.Position - Arena.Center;
        return result;
    }

    private (WDir offset, Hint hint) CalculateRawHint(int slot, int clawSide, int northSlowSide)
    {
        return _ct!.PlayerMechanics[slot] switch
        {
            P4CrystallizeTime.Mechanic.ClawAir => clawSide != 0 ? HintClawAir(clawSide, _hourglass.NumCasts, northSlowSide) : default,
            P4CrystallizeTime.Mechanic.ClawBlizzard => clawSide != 0 ? HintClawBlizzard(slot, clawSide, _hourglass.NumCasts, northSlowSide) : default,
            P4CrystallizeTime.Mechanic.FangEruption => _config.P4ManaStrategy ? HintManaFangEruption(northSlowSide, _hourglass.NumCasts) : HintFangEruption(northSlowSide, _hourglass.NumCasts),
            _ => HintFangOther(_hourglass.NumCasts, northSlowSide)
        };
    }

    private (WDir offset, Hint hint) HintClawAir(int clawSide, int numHourglassesDone, int northSlowSide)
    {
        if (numHourglassesDone < 2)
            return (SafeOffsetDodgeFirstHourglassSouth(clawSide), Hint.SafespotRough | Hint.Maelstrom); // dodge first hourglass by the south side
        if (KnockbacksResolve == default)
            return (_config.P4ManaStrategy && clawSide != northSlowSide ? new WDir(12.5f * clawSide, 16f) : SafeOffsetPreKnockbackSouth(clawSide, 19f), Hint.SafespotPrecise); // preposition to knock party across; Mana (FRU sim Aero plant): the knocking Aero stays at its cubby
        if (numHourglassesDone < 4 && clawSide == northSlowSide)
            return (SafeOffsetDodgeSecondHourglassSouth(clawSide), Hint.SafespotRough | Hint.Maelstrom); // dodge second hourglass; note that player on the slow side can already go intercept the head
        // by now, blizzards have booped their heads, so now it's our turn
        var head = _heads?.FindHead(clawSide);
        if (head != null)
        {
            var headOff = head.Position - Arena.Center;
            var headDir = Angle.FromDirection(headOff) * clawSide; // always decreases as head moves
            var hint = clawSide != northSlowSide && WorldState.CurrentTime < KnockbacksResolve ? Hint.None : Hint.SafespotPrecise; // Hint.KnockbackFrom?.. depends on how new pending knockbacks work for others
            return (headDir.Rad > IdealSecondHeadBaitAngle.Rad ? SafeOffsetSecondHeadBait(clawSide) : headOff, hint);
        }
        // head is done, so dodge between last two hourglasses; Mana: south center (FRU sim POST_EARLY_SOAK)
        return (_config.P4ManaStrategy ? new WDir(0f, 17f) : SafeOffsetChillSouth(northSlowSide), Hint.Maelstrom | Hint.Heads | Hint.Mid);
    }

    private (WDir offset, Hint hint) HintClawBlizzard(int slot, int clawSide, int numHourglassesDone, int northSlowSide)
    {
        // Dark Blizzard III goes off 0.5-1s after head contact; its 12y donut clips the Fragment of Fate from ~16y, so leaving the bait spot
        // early cracks it (in logs: 0.7s/4.9y toward north centre -> -88k, Fragment broke at Tidal Light). Hold until it resolves.
        if (P4CrystallizeTimeDragonHead.NumHeadHits(_heads?.FindHead(clawSide)) == 0 || Raid[slot]?.FindStatus((uint)SID.SpellInWaitingDarkBlizzard) != null)
            return (_config.P4ManaStrategy && clawSide != northSlowSide ? SafeOffsetFirstHeadBait(clawSide) + new WDir(0f, -1.5f) : SafeOffsetFirstHeadBait(clawSide), Hint.SafespotPrecise); // intercept first head at E/W cardinal; Mana lone Ice 1.5y north (FRU sim)
        if (clawSide == northSlowSide)
            return _config.P4ManaStrategy && DarknessDone
                ? (SafeOffsetManaIceNorth, Hint.Maelstrom | Hint.Heads | Hint.Mid) // Mana: both Ices gather north center after Unholy Darkness (FRU sim)
                : HintFangEruption(northSlowSide, numHourglassesDone); // go stack with eruption after intercepting head
        // non-eruption side - dodge second hourglass, but then immediately dodge head boop
        if (numHourglassesDone < 4)
            return _config.P4ManaStrategy
                ? (SafeOffsetManaLoneIceNorth(clawSide), Hint.SafespotRough | Hint.Maelstrom) // Mana: Sprint north past the second hourglass before it explodes
                : (SafeOffsetDodgeSecondHourglassEW(clawSide), Hint.SafespotRough | Hint.Maelstrom); // dodge second hourglass
        // dodge last hourglass and head
        return (_config.P4ManaStrategy ? SafeOffsetManaIceNorth : SafeOffsetChillNorth(-northSlowSide), Hint.Maelstrom | Hint.Heads | Hint.Mid);
    }

    private (WDir offset, Hint hint) HintManaFangEruption(int northSlowSide, int numHourglassesDone)
    {
        if (!_eruptionResolved || !_eruptionHeadResolved)
            return (new WDir(12f * northSlowSide, -15f), Hint.SafespotRough | Hint.Heads | Hint.Maelstrom);
        return HintFangEruption(northSlowSide, numHourglassesDone);
    }

    private (WDir offset, Hint hint) HintFangEruption(int northSlowSide, int numHourglassesDone)
    {
        if (!DarknessDone)
            return (SafeOffsetDarknessStack(northSlowSide), Hint.SafespotRough | Hint.Heads | (numHourglassesDone < 4 ? Hint.Maelstrom : Hint.None));
        // Mana: the stack moves north after Unholy Darkness (FRU sim POST_UD, ~82 north, before the puddles)
        return (_config.P4ManaStrategy ? SafeOffsetManaIceNorth : SafeOffsetChillNorth(-northSlowSide), Hint.Maelstrom | Hint.Mid);
    }

    private (WDir offset, Hint hint) HintFangOther(int numHourglassesDone, int northSlowSide)
    {
        if (numHourglassesDone < 2)
            return (SafeOffsetDodgeFirstHourglassSouth(-northSlowSide), Hint.SafespotRough | Hint.Maelstrom); // dodge first hourglass by the south side
        if (KnockbacksResolve == default)
            return (_config.P4ManaStrategy ? new WDir(-11.4f * northSlowSide, 14.4f) : SafeOffsetPreKnockbackSouth(-northSlowSide, 17f), Hint.Knockback); // preposition to knockback across arena; Mana: next to the planted Aero (FRU sim)
        // from now on move together with eruption
        return HintFangEruption(northSlowSide, numHourglassesDone);
    }
}

sealed class P4CrystallizeTimeRewind(BossModule module) : Components.GenericKnockback(module)
{
    public bool RewindDone;
    public bool ReturnDone;
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();

    public WDir RelativeNorth
    {
        get
        {
            if (_exalines != null)
                foreach (var (pos, _) in _exalines.StartingPositions)
                {
                    var offset = pos - Arena.Center;
                    if (MathF.Abs(offset.Z) > MathF.Abs(offset.X))
                        return new(default, offset.Z > 0f ? 1f : -1f);
                }
            return default;
        }
    }
    private readonly P4CrystallizeTime? _ct = module.FindComponent<P4CrystallizeTime>();
    private readonly P4CrystallizeTimeTidalLight? _exalines = module.FindComponent<P4CrystallizeTimeTidalLight>();

    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        if (!_config.P4ManaStrategy && !RewindDone && _ct != null && _exalines != null && _ct.Cleansed[slot])
        {
            var exas = _exalines.StartingPositions;
            var count = exas.Count;
            var sources = new List<Knockback>();
            for (var i = 0; i < count; ++i)
            {
                var s = exas[i];
                sources.Add(new(s.pos, 20f, direction: s.dir, kind: Kind.DirForward));
            }
            return CollectionsMarshal.AsSpan(sources);
        }
        return [];
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_config.P4ManaStrategy)
        {
            if (!RewindDone)
            {
                if (Service.Config.Get<PartyRolesConfig>()[Raid.Members[slot].ContentId] == PartyRolesConfig.Assignment.Unassigned)
                    hints.Add("Assign your party role for Mana Y positions!");
                if (RelativeNorth == default)
                    hints.Add("Wait for the second Tidal Light: its origin is relative north.", false);
            }
            else if (!ReturnDone)
                hints.Add("Use Arm's Length / Surecast before Hallowed Wings!", !IsImmune(slot, WorldState.FutureTime(1d)));
            return;
        }
        base.AddHints(slot, actor, hints);
        if (!RewindDone && _ct != null && _exalines != null && _ct.Cleansed[slot])
        {
            var players = Raid.WithoutSlot(false, true, true);
            players.Sort(static (a, b) => a.PosRot.X.CompareTo(b.PosRot.X));
            var xOrder = Array.IndexOf(players, actor);
            players.Sort(static (a, b) => a.PosRot.Z.CompareTo(b.PosRot.Z));
            var zOrder = Array.IndexOf(players, actor);
            if (xOrder >= 0 && zOrder >= 0)
            {
                if (_exalines.StartingOffsetSum.X > 0f)
                    xOrder = players.Length - 1 - xOrder;
                if (_exalines.StartingOffsetSum.Z > 0f)
                    zOrder = players.Length - 1 - zOrder;

                var isFirst = xOrder == 0 || zOrder == 0;
                var isTank = actor.Role == Role.Tank;
                if (isFirst != isTank)
                    hints.Add(isTank ? "Stay in front of the group!" : "Hide behind tank!");
                var isFirstX = xOrder < 4;
                var isFirstZ = zOrder < 4;
                if (isFirstX == isFirstZ)
                    hints.Add("Position in group properly!");
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_config.P4ManaStrategy && RewindDone && !ReturnDone && !actor.IsDeadOrDestroyed && ((FRU)Module).BossP4Usurper()?.CastInfo is { } cast && cast.Action.ID == (uint)AID.CrystallizeTimeHallowedWings1)
        {
            var resolve = Module.CastFinishAt(cast, 1.3d);
            if ((resolve - WorldState.CurrentTime).TotalSeconds < 5d && !IsImmune(slot, resolve))
            {
                var category = actor.Class.GetClassCategory();
                if (category is ClassCategory.Healer or ClassCategory.Caster)
                    hints.ActionsToExecute.Push(ActionDefinitions.Surecast, actor, ActionQueue.Priority.High);
                else if (category is ClassCategory.Tank or ClassCategory.Melee or ClassCategory.PhysRanged)
                    hints.ActionsToExecute.Push(ActionDefinitions.Armslength, actor, ActionQueue.Priority.High);
            }
        }
        if (!RewindDone && _ct != null && _exalines != null && _ct.Cleansed[slot] && (!_config.P4ManaStrategy || (RelativeNorth != default && assignment != PartyRolesConfig.Assignment.Unassigned)))
        {
            var midpoint = SafeCorner();
            hints.GoalZones.Add(AIHints.GoalProximity(midpoint, 15f, 1f));
            var destPoint = midpoint + AssignedPositionOffset(actor, assignment);
            hints.GoalZones.Add(AIHints.GoalProximity(destPoint, 1f, 1.5f));
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (!RewindDone && _exalines != null && (!_config.P4ManaStrategy || RelativeNorth != default))
        {
            var midpoint = SafeCorner();
            Arena.ZoneCircleOutline(midpoint, 1f);
            var offset = AssignedPositionOffset(pc, Service.Config.Get<PartyRolesConfig>()[Raid.Members[pcSlot].ContentId]);
            if (offset != default)
                Arena.ZoneCircleOutline(midpoint + offset, 1, Colors.Safe);
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        base.OnStatusGain(actor, ref status);
        switch (status.ID)
        {
            case (uint)SID.Return:
                RewindDone = true;
                break;
            case (uint)SID.Stun:
                ReturnDone = true;
                break;
        }
    }

    private WPos SafeCorner() => _config.P4ManaStrategy ? Arena.Center : _exalines != null ? Arena.Center + 0.5f * _exalines.StartingOffsetSum : default;

    private WDir AssignedPositionOffset(Actor actor, PartyRolesConfig.Assignment assignment)
    {
        if (_exalines == null || assignment == PartyRolesConfig.Assignment.Unassigned)
            return default;
        if (_config.P4ManaStrategy)
            return ManaRecordOffset(RelativeNorth, assignment);
        // TODO: make configurable?..
        var isLeft = assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1;
        var offDir = (Angle.FromDirection(_exalines.StartingOffsetSum) + (isLeft ? 45 : -45).Degrees()).ToDirection();
        var normDir = isLeft ? offDir.OrthoL() : offDir.OrthoR();
        var (offX, offY) = actor.Role == Role.Tank ? (4, 1) : (1, 2);
        return offX * offDir + offY * normDir;
    }

    // Mana Y: tanks on the two northern arms, everyone else stacked near center.
    internal static WDir ManaRecordOffset(WDir north, PartyRolesConfig.Assignment assignment) => assignment switch
    {
        PartyRolesConfig.Assignment.MT => 14f * north + 11f * north.OrthoL(),
        PartyRolesConfig.Assignment.OT => 14f * north + 11f * north.OrthoR(),
        PartyRolesConfig.Assignment.Unassigned => default,
        _ => 3f * north
    };
}

sealed class P4CrystallizeTimeSpiritTaker(BossModule module) : SpiritTaker(module)
{
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private readonly P4CrystallizeTimeRewind? _rewind = module.FindComponent<P4CrystallizeTimeRewind>();

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (_config.P4ManaStrategy && _rewind is { RelativeNorth: var north } && north != default && ManaSpreadOffset(north, assignment) is var offset && offset != default)
            hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + offset, 0.75f));
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);
        if (_config.P4ManaStrategy)
        {
            if (Service.Config.Get<PartyRolesConfig>()[Raid.Members[slot].ContentId] == PartyRolesConfig.Assignment.Unassigned)
                hints.Add("Assign your party role for Mana Spirit Taker clocks!");
            if (_rewind == null || _rewind.RelativeNorth == default)
                hints.Add("Second Tidal Light origin unavailable: choose your Mana clock manually!");
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        var assignment = Service.Config.Get<PartyRolesConfig>()[Raid.Members[pcSlot].ContentId];
        if (_config.P4ManaStrategy && _rewind is { RelativeNorth: var north } && north != default && ManaSpreadOffset(north, assignment) is var offset && offset != default)
            Arena.ZoneCircleOutline(Arena.Center + offset, 0.75f, Colors.Safe);
    }

    // JP waymarks are 10 yalms from center; intercardinal components are 10/sqrt(2).
    internal static WDir ManaSpreadOffset(WDir north, PartyRolesConfig.Assignment assignment)
    {
        var east = north.OrthoR();
        return assignment switch
        {
            PartyRolesConfig.Assignment.MT => 14f * north - 11f * east,
            PartyRolesConfig.Assignment.OT => 14f * north + 11f * east,
            PartyRolesConfig.Assignment.H1 => -10f * east,
            PartyRolesConfig.Assignment.H2 => 10f * east,
            PartyRolesConfig.Assignment.M1 => -7.071f * north - 7.071f * east,
            PartyRolesConfig.Assignment.M2 => -7.071f * north + 7.071f * east,
            PartyRolesConfig.Assignment.R1 => 7.071f * north - 7.071f * east,
            PartyRolesConfig.Assignment.R2 => 7.071f * north + 7.071f * east,
            _ => default
        };
    }
}
