namespace BossMod.Dawntrail.Ultimate.FRU;

sealed class P2AxeKick(BossModule module) : Components.SimpleAOEs(module, (uint)AID.AxeKick, 16f);
sealed class P2ScytheKick(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ScytheKick, new AOEShapeDonut(4f, 20f));

sealed class P2IcicleImpact(BossModule module) : Components.GenericAOEs(module, (uint)AID.IcicleImpact)
{
    public readonly List<AOEInstance> AOEs = []; // note: we don't remove finished aoes, since we use them in other components to detect safespots

    private readonly AOEShapeCircle _shape = new(10f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(AOEs)[NumCasts..];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            // initially all aoes start as non-risky
            AOEs.Add(new(_shape, spell.LocXZ, default, Module.CastFinishAt(spell), risky: false));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.IcicleImpact:
                ++NumCasts;
                break;
            case (uint)AID.AxeKick:
                // axe kick -> first circles is only ~2.7s, proteans resolve 0.8s in: players standing under the first circles must start
                // running in as soon as the kick is out (moving in keeps them among the 4 closest anyway)
                MarkAsRisky(0, Math.Min(2, AOEs.Count));
                break;
            case (uint)AID.HouseOfLight:
                // after proteans are baited, first two aoes become risky; remaining are still not - stones are supposed to be baited into them
                MarkAsRisky(0, Math.Min(2, AOEs.Count));
                break;
            case (uint)AID.FrigidStone:
                // after stones are baited, all aoes should be marked as risky
                MarkAsRisky(2, AOEs.Count);
                break;
        }
    }

    private void MarkAsRisky(int start, int end)
    {
        for (var i = start; i < end; ++i)
            AOEs.Ref(i).Risky = true;
    }
}

sealed class P2FrigidNeedleCircle(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FrigidNeedleCircle, 5f);
sealed class P2FrigidNeedleCross(BossModule module) : Components.SimpleAOEs(module, (uint)AID.FrigidNeedleCross, new AOEShapeCross(40f, 2.5f));

sealed class P2FrigidStone : Components.BaitAwayIcon
{
    public P2FrigidStone(BossModule module) : base(module, 5f, (uint)IconID.FrigidStone, (uint)AID.FrigidStone, 8.1f)
    {
        EnableHints = false;
        IgnoreOtherBaits = true;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { }
}

sealed class P2DiamondDustHouseOfLight(BossModule module) : Components.GenericBaitAway(module, (uint)AID.HouseOfLight)
{
    private Actor? _source;
    private DateTime _activation;

    private readonly AOEShapeCone _shape = new(60f, 15f.Degrees());

    public override void Update()
    {
        CurrentBaits.Clear();

        if (_source == null || ForbiddenPlayers == default)
        {
            return;
        }
        var party = Raid.WithoutSlot(false, true, true);
        var len = party.Length;

        (Actor actor, float distSq)[] distances = new (Actor, float)[len];
        var sourcePos = _source.Position;

        for (var i = 0; i < len; ++i)
        {
            var p = party[i];
            var distSq = (p.Position - sourcePos).LengthSq();
            distances[i] = (p, distSq);
        }

        var targets = Math.Min(4, len);
        for (var i = 0; i < targets; ++i)
        {
            var selIdx = i;
            for (var j = i + 1; j < len; ++j)
            {
                if (distances[j].distSq < distances[selIdx].distSq)
                    selIdx = j;
            }

            if (selIdx != i)
            {
                (distances[selIdx], distances[i]) = (distances[i], distances[selIdx]);
            }

            CurrentBaits.Add(new(_source, distances[i].actor, _shape, _activation));
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (CurrentBaits.Count == 0)
            return;

        var baitIndex = CurrentBaits.FindIndex(b => b.Target == actor);
        if (ForbiddenPlayers[slot])
        {
            if (baitIndex >= 0)
                hints.Add("Stay farther away!");
        }
        else
        {
            if (baitIndex < 0)
                hints.Add("Stay closer to bait!");
            else if (PlayersClippedBy(ref CurrentBaits.Ref(baitIndex)).Count != 0)
                hints.Add("Bait cone away from raid!");
        }

        if (ActiveBaitsNotOn(actor).Any(b => IsClippedBy(actor, ref b)))
            hints.Add("GTFO from baited cone!");
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.AxeKick or (uint)AID.ScytheKick)
        {
            _source = caster;
            _activation = Module.CastFinishAt(spell, 0.8d);
        }
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.FrigidStone)
            ForbiddenPlayers[Raid.FindSlot(actor.InstanceID)] = true;
    }
}

sealed class P2DiamondDustSafespots(BossModule module) : BossComponent(module)
{
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private bool? _out;
    private bool? _supportsBaitCones;
    private bool? _conesAtCardinals;
    private readonly WDir[] _safeOffs = new WDir[PartyState.MaxPartySize];

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_safeOffs[slot] != default)
        {
            hints.PathfindMapBounds = FRU.PathfindHugBorderBounds;
            hints.AddForbiddenZone(new SDPrecisePosition(Arena.Center + _safeOffs[slot], new WDir(default, 1f), Arena.Bounds.MapResolution, actor.Position, 0.1f));
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (_safeOffs[pcSlot] != default)
            Arena.ZoneCircleOutline(Arena.Center + _safeOffs[pcSlot], 1f, Colors.Safe);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.IcicleImpact:
                if (_conesAtCardinals == null)
                {
                    _conesAtCardinals = IsCardinal(caster.Position - Arena.Center);
                    InitIfReady();
                }
                break;
            case (uint)AID.AxeKick:
                _out = true;
                InitIfReady();
                break;
            case (uint)AID.ScytheKick:
                _out = false;
                InitIfReady();
                break;
        }
    }

    // the kick's damage event arrives ~0.8s before the cast is reported finished; the first circles land 2.7s after axe kick,
    // so players under them need every bit of that (a 0.2y miss in logs) - react to whichever comes first (both are idempotent)
    public override void OnEventCast(Actor caster, ActorCastEvent spell) => OnKickDone(spell.Action.ID);
    public override void OnCastFinished(Actor caster, ActorCastInfo spell) => OnKickDone(spell.Action.ID);

    private void OnKickDone(uint aid)
    {
        switch (aid)
        {
            case (uint)AID.AxeKick:
                // out done => cone baiters go in, ice baiters stay
                for (var i = 0; i < _safeOffs.Length; ++i)
                    if (_safeOffs[i] != default && Raid[i]?.Class.IsSupport() == _supportsBaitCones)
                        _safeOffs[i] = 4f * _safeOffs[i].Normalized();
                break;
            case (uint)AID.ScytheKick:
                // in done => cone baiters stay, ice baiters go out
                for (var i = 0; i < _safeOffs.Length; ++i)
                    if (_safeOffs[i] != default && Raid[i]?.Class.IsSupport() != _supportsBaitCones)
                        _safeOffs[i] = 8f * _safeOffs[i].Normalized();
                break;
        }
    }

    public override void OnEventIcon(Actor actor, uint iconID, ulong targetID)
    {
        if (iconID == (uint)IconID.FrigidStone && _supportsBaitCones == null)
        {
            _supportsBaitCones = actor.Class.IsDD();
            InitIfReady();
        }
    }

    private void InitIfReady()
    {
        if (_out == null || _supportsBaitCones == null || _conesAtCardinals == null)
            return;
        var supportsAtCardinals = _supportsBaitCones == _conesAtCardinals;
        var offsetTH = supportsAtCardinals ? default : _config.P2DiamondDustSupportsCCW ? 45f.Degrees() : -45f.Degrees();
        var offsetDD = !supportsAtCardinals ? default : _config.P2DiamondDustDDCCW ? 45f.Degrees() : -45f.Degrees();
        foreach (var (slot, group) in _config.P2DiamondDustCardinals.Resolve(Raid))
        {
            var support = group < 4;
            var baitCone = _supportsBaitCones == support;
            var dir = 180f.Degrees() - (group & 3) * 90f.Degrees();
            dir += support ? offsetTH : offsetDD;
            var radius = (_out.Value ? 16f : default) + (baitCone ? 1f : 3f);
            _safeOffs[slot] = radius * dir.ToDirection();
        }
    }

    private static bool IsCardinal(WDir off) => Math.Abs(off.X) < 1 || Math.Abs(off.Z) < 1;
}

sealed class P2HeavenlyStrike(BossModule module) : Components.GenericKnockback(module, (uint)AID.HeavenlyStrike)
{
    private readonly WDir[] _safeDirs = BuildSafeDirs(module);
    private readonly DateTime _activation = module.WorldState.FutureTime(3.9d);

    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        return new Knockback[1] { new(Arena.Center, 12, _activation) };
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_safeDirs[slot] != default)
            hints.AddForbiddenZone(new SDPrecisePosition(Arena.Center + 6f * _safeDirs[slot], new(1f, default), Arena.Bounds.MapResolution, actor.Position, 0.25f), _activation);
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (_safeDirs[pcSlot] != default)
            Arena.ZoneCircleOutline(Arena.Center + 18f * _safeDirs[pcSlot], 1f, Colors.Safe);
    }

    internal static WDir[] BuildSafeDirs(BossModule module)
    {
        var res = new WDir[PartyState.MaxPartySize];
        var icicle = module.FindComponent<P2IcicleImpact>();
        if (icicle?.AOEs.Count > 0)
        {
            var safeDir = (icicle.AOEs.Ref(0).Origin - module.Center).Normalized();
            if (Service.Config.Get<FRUConfig>().P123ManaStrategy ? safeDir.X > 0.8f || safeDir.Z > 0.5f : safeDir.X > 0.5f || safeDir.Z > 0.8f)
                safeDir = -safeDir; // G1
            foreach (var (slot, group) in Service.Config.Get<FRUConfig>().P2DiamondDustKnockbacks.Resolve(module.Raid))
                res[slot] = group == 1 ? -safeDir : safeDir;
        }
        return res;
    }
}

sealed class P2SinboundHoly(BossModule module) : Components.UniformStackSpread(module, 6f, default, 4, 4)
{
    public int NumCasts;
    private DateTime _nextExplosion;
    private readonly WDir _destinationDir = CalculateDestination(module);
    private readonly WPos[] _initialSpots = new WPos[PartyState.MaxPartySize];
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private readonly WDir[] _manaStartDirs = P2HeavenlyStrike.BuildSafeDirs(module);
    private readonly P2FrigidNeedleCross? _manaStars = module.FindComponent<P2FrigidNeedleCross>();

    private static WDir CalculateDestination(BossModule module)
    {
        // if oracle jumps directly to one of the initial safespots, both groups run opposite in one (arbitrary, CW) direction, and the one that ends up behind boss slides across - in that case we return zero destination
        // note: we assume that when this is called oracle is already at position
        var icicles = module.FindComponent<P2IcicleImpact>();
        var oracles = module.Enemies((uint)OID.OraclesReflection);
        var oracle = oracles.Count != 0 ? oracles[0] : null;
        if (icicles == null || icicles.AOEs.Count == 0 || oracle == null)
            return default;

        var idealDir = (module.Center - oracle.Position).Normalized(); // ideally we wanna stay as close as possible to across the oracle
        var destDir = (icicles.AOEs[0].Origin - module.Center).Normalized().OrthoL(); // actual destination is one of the last icicles
        return destDir.Dot(idealDir) switch
        {
            > 0.5f => destDir,
            < -0.5f => -destDir,
            _ => default, // fast movement mode
        };
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_config.P123ManaStrategy)
        {
            AddManaAIHints(slot, actor, assignment, hints);
            return;
        }
        var master = actor.Role != Role.Healer ? Stacks.MinBy(s => (s.Target.Position - actor.Position).LengthSq()).Target : null;
        if (master != null && ((master.Position - actor.Position).LengthSq() > 100f || (master.Position - Arena.Center).LengthSq() < 196f))
            master = null; // our closest healer is too far away or too close to center, something is wrong (maybe kb didn't finish yet, or healer fucked up)

        // determine movement speed and direction
        // baseline is towards safety (opposite boss), or CW (arbitrary) if there's no obvious safe direction
        // however, if we're non-healer, it is overridden by healer's decision (we can slide over later)
        var moveQuickly = _destinationDir == default;
        var preferredDir = !moveQuickly ? _destinationDir : (actor.Position - Arena.Center).Normalized().OrthoR();
        moveQuickly &= NumCasts > 0; // don't start moving while waiting for first cast

        if (master != null)
        {
            var masterSlot = Raid.FindSlot(master.InstanceID);
            if (masterSlot >= 0 && NumCasts > 0)
            {
                var masterMovement = preferredDir.Dot(master.Position - _initialSpots[masterSlot]);
                if (masterMovement < -2)
                    preferredDir = -preferredDir; // swap movement direction to follow healer
            }

            moveQuickly &= (actor.Position - master.Position).LengthSq() < 25f; // don't move too quickly if healer can't catch up

            // non-healers should just stack with whatever closest healer is
            // before first cast, ignore master's movements
            var moveDir = NumCasts > 0 ? master.LastFrameMovement.Normalized() : default;
            hints.AddForbiddenZone(new SDInvertedCapsule(master.Position + 2f * moveDir, moveDir, 4f, 1.5f), DateTime.MaxValue);
        }

        // note: other hints have to be 'later' than immediate (to make getting out of voidzones higher prio), but 'earlier' than stack-with-healer:
        // healer's position is often overlapped by new voidzones, if healer is moving slowly - in that case we still need to dodge in correct direction
        var hintTime = WorldState.FutureTime(50d);

        // stay near border
        hints.AddForbiddenZone(new SDCircle(Arena.Center, 16f), hintTime);

        // prefer moving towards safety (CW is arbitrary)
        var planeOffset = moveQuickly ? 2 : -2; // if we're moving quickly, mark our current spot as forbidden
        hints.AddForbiddenZone(new SDHalfPlane(Arena.Center + planeOffset * preferredDir, preferredDir), hintTime);
    }

    // The four Mana cases are relative to the original knockback camps, not the player's current position.
    internal static (WDir destination, bool clockwise, bool fast, WDir meeting) ManaRoute(WDir start, WDir oracle, int numCasts, bool starsResolved = true)
    {
        if (!starsResolved)
            return (start, true, false, default);
        var aligned = Math.Abs(start.Dot(oracle));
        if (aligned > 0.9f) // case 1: both camps run CW 180 degrees
            return (start.Rotate(-180f.Degrees()), true, true, default);
        if (aligned < 0.1f) // case 2: both camps run slowly CW 90 degrees
            return (start.Rotate((-30f * Math.Min(numCasts, 3)).Degrees()), true, false, default);
        if (Math.Abs(start.Rotate(45f.Degrees()).Dot(oracle)) > 0.9f) // case 3: both camps run quickly CW 135 degrees
            return (start.Rotate(-135f.Degrees()), true, true, default);

        // Case 4: near camp CCW, far camp CW. Keep the final stacks separated before the slanted slide.
        var clockwise = start.Dot(oracle) < 0;
        var travel = 70f * Math.Min(numCasts, 3) / 3f;
        var meeting = start.Rotate((clockwise ? -90f : 90f).Degrees());
        return (numCasts >= 4 ? meeting : start.Rotate((clockwise ? -travel : travel).Degrees()), clockwise, false, meeting);
    }

    internal static bool ManaFastArrived(WDir start, WDir current, WDir destination)
    {
        var progress = (Angle.FromDirection(start) - Angle.FromDirection(current)).Normalized().Deg;
        if (progress < -15f)
            progress += 360f; // retain progress past 180 degrees rather than orbiting after a late frame
        var travel = Math.Abs((Angle.FromDirection(start) - Angle.FromDirection(destination)).Normalized().Deg);
        return progress >= travel - 3f;
    }

    private void AddManaAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var group = _config.P2DiamondDustKnockbacks[assignment];
        var oracle = Module.Enemies((uint)OID.OraclesReflection).FirstOrDefault();
        if (group < 0 || _manaStartDirs[slot] == default || oracle == null)
            return; // incomplete inputs: do not guess a camp or route
        var oracleOffset = oracle.Position - Arena.Center;
        if (oracleOffset.LengthSq() < 100f)
            return; // reflection has not teleported yet
        var oracleDir = oracleOffset.Normalized();

        var healerRole = _config.P2DiamondDustKnockbacks[PartyRolesConfig.Assignment.H1] == group ? PartyRolesConfig.Assignment.H1 : PartyRolesConfig.Assignment.H2;
        if (_config.P2DiamondDustKnockbacks[healerRole] != group)
            return;
        var healerSlot = Service.Config.Get<PartyRolesConfig>().SlotsPerAssignment(Raid);
        var master = healerSlot.Length > (int)healerRole ? Raid[healerSlot[(int)healerRole]] : null;
        if (master == null || master.IsDead || !Stacks.Any(s => s.Target == master))
            return; // never substitute the other camp's healer

        // Holy activates while the Stars state is still waiting; do not depart or request Sprint early.
        var starsResolved = _manaStars is { NumCasts: > 0 };
        var route = ManaRoute(_manaStartDirs[slot], oracleDir, NumCasts, starsResolved);
        if (!starsResolved)
        {
            hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + 18f * route.destination, 1f), WorldState.FutureTime(50d));
            return;
        }
        if (actor != master)
        {
            var moveDir = master.LastFrameMovement.Normalized();
            hints.AddForbiddenZone(new SDInvertedCapsule(master.Position + 2f * moveDir, moveDir, 4f, 1.5f), DateTime.MaxValue);
        }

        var currentDir = (actor.Position - Arena.Center).Normalized();
        hints.AddForbiddenZone(new SDCircle(Arena.Center, 16f), WorldState.FutureTime(50d));
        if (route.fast && !ManaFastArrived(_manaStartDirs[slot], currentDir, route.destination))
        {
            var tangent = currentDir.OrthoR();
            hints.AddForbiddenZone(new SDHalfPlane(actor.Position + 2f * tangent, tangent), WorldState.FutureTime(50d));
            hints.ActionsToExecute.Push(ActionID.MakeSpell(ClassShared.AID.Sprint), actor, ActionQueue.Priority.High);
        }
        else
        {
            // Slow cases advance once per real stack pulse, preventing both camps from rushing together.
            hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + 18f * route.destination, 1f), WorldState.FutureTime(50d));
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.SinboundHoly)
        {
            AddStacks(Raid.WithoutSlot(false, true, true).Where(p => p.Role == Role.Healer), Module.CastFinishAt(spell, 0.9d));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.SinboundHolyAOE && WorldState.CurrentTime > _nextExplosion)
        {
            if (NumCasts == 0)
            {
                foreach (var (i, p) in Raid.WithSlot(false, true, true))
                {
                    _initialSpots[i] = p.Position;
                }
            }

            ++NumCasts;
            if (_config.P123ManaStrategy && NumCasts == 4 && Module.Enemies((uint)OID.OraclesReflection).FirstOrDefault() is { } oracle)
            {
                var start = _manaStartDirs.FirstOrDefault(d => d != default);
                var offset = oracle.Position - Arena.Center;
                if (start != default && offset.LengthSq() >= 100f && Module.FindComponent<P2TwinStillnessSilence>() is { } twin)
                    twin.ManaHolyMeetingDirection = ManaRoute(start, offset.Normalized(), NumCasts).meeting;
            }
            _nextExplosion = WorldState.FutureTime(0.5d);
        }
    }
}

sealed class P2SinboundHolyVoidzone(BossModule module) : Components.Voidzone(module, 6f, GetVoidzones)
{
    public bool AIHintsEnabled = true;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (AIHintsEnabled)
        {
            base.AddAIHints(slot, actor, assignment, hints);
        }
    }

    private static Actor[] GetVoidzones(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.SinboundHolyVoidzone);
        var count = enemies.Count;
        if (count == 0)
            return [];

        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (z.EventState != 7)
                voidzones[index++] = z;
        }
        return voidzones[..index];
    }
}

sealed class P2ShiningArmor(BossModule module) : Components.GenericGaze(module, (uint)AID.ShiningArmor)
{
    private Eye[] _eye = [];

    public override ReadOnlySpan<Eye> ActiveEyes(int slot, Actor actor) => _eye;

    public override void OnActorPlayActionTimelineEvent(Actor actor, ushort id)
    {
        if (actor.OID == (uint)OID.BossP2 && id == 0x1E43)
        {
            var loc = actor.Position.Quantized();
            _eye = [new(loc, WorldState.FutureTime(7.2d), eyeCenter: loc)];
        }
    }
}

sealed class P2TwinStillnessSilence(BossModule module) : Components.GenericAOEs(module)
{
    public readonly List<AOEInstance> AOEs = [];
    public WDir ManaHolyMeetingDirection;
    private readonly Actor? _source = module.GetActor((uint)OID.OraclesReflection);
    private BitMask _thinIce;
    private readonly WPos[] _slideBackPos = new WPos[PartyState.MaxPartySize]; // used for hints only
    private P2SinboundHolyVoidzone? _voidzones; // used for hints only
    private const float SlideDistance = 32f;

    private readonly AOEShapeCone _shapeFront = new(30f, 135f.Degrees());
    private readonly AOEShapeCone _shapeBack = new(30f, 45f.Degrees());

    public void EnableAIHints()
    {
        _voidzones = Module.FindComponent<P2SinboundHolyVoidzone>();
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => AOEs.Count != 0 ? CollectionsMarshal.AsSpan(AOEs)[..1] : [];

    // both cones also hit anyone standing inside the reflection itself (1.0y behind was hit). Logged survivors sat 5.5-6.4y behind
    // (a 4.9y hit was the back cone); at 6.6y the straight slide from the south (lands ~5y behind) was forbidden and the AI froze
    public const float PointBlankRadius = 3f;
    public static bool Hits(in AOEInstance aoe, WPos pos) => aoe.Check(pos) || pos.InCircle(aoe.Origin, PointBlankRadius);

    public override void Update()
    {
        if (AOEs.Count != 2)
            return;
        foreach (var (i, p) in Raid.WithSlot(false, true, true).IncludedInMask(_thinIce))
            if (_slideBackPos[i] == default && p.LastFrameMovement != default)
                _slideBackPos[i] = p.PrevPosition;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_voidzones == null && ManaHolyMeetingDirection != default)
        {
            // After both fourth stacks, converge at Mana's 135-degree meeting point before gaze/ice.
            hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + 18f * ManaHolyMeetingDirection, 1f), WorldState.FutureTime(50d));
            return;
        }
        if (_voidzones == null || _source == null)
            return;

        if (!_thinIce[slot])
        {
            // preposition
            // this is a bit hacky - we need to stay either far away from boss, or close (and slide over at the beginning of the ice)
            // the actual shape is quite complicated ('primary' shape is a set of points at distance X from a cone behind boss, 'secondary' is a set of points at distance X from primary), so we use a rough approximation

            // first, find a set of allowed angles along the border
            var zoneList = new ArcList(Arena.Center, 17f);
            foreach (var z in _voidzones.Sources(Module))
                zoneList.ForbidCircle(z.Position, 6f);

            // now find closest allowed zone
            var actorDir = Angle.FromDirection(actor.Position - Arena.Center);
            var closest = zoneList.Allowed(5f.Degrees()).MinBy(z => actorDir.DistanceToRange(z.min, z.max).Abs().Rad);
            if (closest != default)
            {
                var desiredDir = (closest.min + closest.max) * 0.5f;
                var halfWidth = (closest.max - closest.min) * 0.5f;
                if (halfWidth.Deg > 5)
                {
                    // destination is very wide, narrow it down a bit to be in line with the boss
                    halfWidth = 5.Degrees();
                    var sourceDir = Angle.FromDirection(_source.Position - Arena.Center);
                    var sourceDist = sourceDir.DistanceToRange(closest.min + halfWidth, closest.max - halfWidth);
                    var oppositeDist = (sourceDir + 180.Degrees()).DistanceToRange(closest.min + halfWidth, closest.max - halfWidth);
                    desiredDir = oppositeDist.Abs().Rad < sourceDist.Abs().Rad ? (sourceDir + 180.Degrees() + oppositeDist) : (sourceDir + sourceDist);
                }
                hints.AddForbiddenZone(new SDCircle(Arena.Center, 16f), WorldState.FutureTime(50));
                hints.AddForbiddenZone(new SDInvertedCone(Arena.Center, 100f, desiredDir, halfWidth), DateTime.MaxValue);
            }
        }
        else if (actor.LastFrameMovement == default)
        {
            // at this point, we have thin ice, so we can either stay or move fixed distance
            var sourceOffset = _source.Position - Arena.Center;
            var needToMove = AOEs.Count > 0 ? Hits(AOEs[0], actor.Position) : NumCasts == 0 && sourceOffset.Dot(actor.Position - Arena.Center) > 0;
            if (!needToMove)
                return;

            var zoneList = new ArcList(actor.Position, SlideDistance);
            zoneList.ForbidInverseCircle(Arena.Center, Arena.Bounds.Radius);

            foreach (var z in _voidzones.Sources(Module))
            {
                var offset = z.Position - actor.Position;
                var dist = offset.Length();
                if (dist >= SlideDistance)
                {
                    // voidzone center is outside slide distance => forbid voidzone itself
                    zoneList.ForbidCircle(z.Position, 6f);
                }
                else if (dist >= 6)
                {
                    // forbid the voidzone's shadow
                    zoneList.ForbidArcByLength(Angle.FromDirection(offset), Angle.Asin(6f / dist));
                }
                // else: we're already in voidzone, oh well
            }

            if (AOEs.Count == 0)
            {
                // if we're behind boss, slide over to the safe point as opposite to the boss as possible
                var farthestDir = Angle.FromDirection(-sourceOffset);
                var bestRange = zoneList.Allowed(5f.Degrees()).MinBy(r => farthestDir.DistanceToRange(r.min, r.max).Abs().Rad);
                var dir = farthestDir.ClosestInRange(bestRange.min, bestRange.max);
                hints.AddForbiddenZone(new SDInvertedCircle(actor.Position + SlideDistance * dir.ToDirection(), 1f), DateTime.MaxValue);
            }
            else
            {
                // dodge next aoe
                ref var nextAOE = ref AOEs.Ref(0);
                zoneList.ForbidInfiniteCone(nextAOE.Origin, nextAOE.Rotation, ((AOEShapeCone)nextAOE.Shape).HalfAngle);
                if (!actor.Position.InCircle(nextAOE.Origin, PointBlankRadius))
                    zoneList.ForbidCircle(nextAOE.Origin, PointBlankRadius); // from inside, any 32y slide leaves it

                // prefer to return to the starting spot, for more natural preposition for next mechanic
                if (AOEs.Count == 1 && _slideBackPos[slot] != default && !zoneList.Forbidden.Contains(Angle.FromDirection(_slideBackPos[slot] - actor.Position).Rad))
                {
                    hints.AddForbiddenZone(new SDInvertedCircle(_slideBackPos[slot], 1f), DateTime.MaxValue);
                }
                else if (zoneList.Allowed(1f.Degrees()).MaxBy(r => (r.max - r.min).Rad) is var best && best.max.Rad > best.min.Rad)
                {
                    var dir = 0.5f * (best.min + best.max);
                    hints.AddForbiddenZone(new SDInvertedCircle(actor.Position + SlideDistance * dir.ToDirection(), 1f), DateTime.MaxValue);
                }
                // else: no good direction can be found, wait for a bit, maybe voidzone will disappear
            }
        }
        // else: we are already sliding, nothing to do...
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        Arena.Actor(_source, Colors.Object, true);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        var (shape1, off1, shape2, off2) = spell.Action.ID switch
        {
            (uint)AID.TwinStillnessFirst => (_shapeFront, new Angle(), _shapeBack, 180f.Degrees()),
            (uint)AID.TwinSilenceFirst => (_shapeBack, default, _shapeFront, 180f.Degrees()),
            _ => (null, default, null, default)
        };
        if (shape1 != null && shape2 != null)
        {
            AOEs.Add(new(shape1, spell.LocXZ, spell.Rotation + off1, Module.CastFinishAt(spell)));
            AOEs.Add(new(shape2, spell.LocXZ, spell.Rotation + off2, Module.CastFinishAt(spell, 2.1d)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.TwinStillnessFirst or (uint)AID.TwinStillnessSecond or (uint)AID.TwinSilenceFirst or (uint)AID.TwinSilenceSecond)
        {
            ++NumCasts;
            if (AOEs.Count != 0)
                AOEs.RemoveAt(0);
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.ThinIce)
            _thinIce[Raid.FindSlot(actor.InstanceID)] = true;
    }
}

sealed class P2ThinIce(BossModule module) : Components.ThinIce(module, 32f)
{
    private P2TwinStillnessSilence? _aoe = module.FindComponent<P2TwinStillnessSilence>();

    public override bool DestinationUnsafe(int slot, Actor actor, WPos pos)
    {
        _aoe ??= Module.FindComponent<P2TwinStillnessSilence>();
        if (_aoe != null)
        {
            var aoes = _aoe.ActiveAOEs(slot, actor);
            var len = aoes.Length;
            for (var i = 0; i < len; ++i)
            {
                ref readonly var aoe = ref aoes[i];
                if (P2TwinStillnessSilence.Hits(aoe, pos))
                {
                    return true;
                }
            }
        }
        return !Arena.InBounds(pos);
    }
}
