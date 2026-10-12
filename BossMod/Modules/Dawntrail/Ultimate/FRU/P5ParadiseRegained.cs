namespace BossMod.Dawntrail.Ultimate.FRU;

sealed class P5ParadiseRegainedTowers(BossModule module) : Components.GenericTowers(module, (uint)AID.WingsDarkAndLightExplosion)
{
    public WDir RelativeSouth;
    private bool? _lightFirst;
    private Actor? _firstTank;
    private readonly PartyRolesConfig _roles = Service.Config.Get<PartyRolesConfig>();

    public override void Update()
    {
        var roles = _roles.AssignmentsPerSlot(Raid);
        EnableHints = roles.Length == 8 && RelativeSouth != default;
        var baits = Module.FindComponent<P5ParadiseRegainedBaits>();
        _lightFirst ??= baits?.LightFirst;
        _firstTank ??= baits?.FirstTank;
        for (var i = 0; i < Towers.Count; ++i)
        {
            var tower = Towers[i];
            tower.ForbiddenSoakers = default;
            for (var slot = 0; slot < 8; ++slot)
            {
                var role = roles.Length == 8 ? roles[slot] : PartyRolesConfig.Assignment.Unassigned;
                var near = _lightFirst is bool light && ((tower.Position - Arena.Center).Dot(RelativeSouth.OrthoL()) > 0) == light;
                var allowed = NumCasts + i == 0
                    ? role is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2
                    : _lightFirst != null && (near ? role is PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.R2 : role is PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2);
                tower.ForbiddenSoakers[slot] = !allowed;
            }
            Towers[i] = tower;
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);
        // The bait component ends with the second cleave; retain the original MT
        // so the final tower still provides the Mana aggro/facing handoff reminder.
        if (NumCasts == 2 && Towers.Count != 0 && _firstTank != null && actor.Role == Role.Tank)
            hints.Add(actor == _firstTank
                ? "After last tower: Provoke and face the boss North"
                : "After last tower: return aggro to the original MT", false);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        // DPS wait on the safe side until the healer tower/first cleave resolves.
        if (assignment is not (PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2 or PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.R2) || NumCasts > 0)
            base.AddAIHints(slot, actor, assignment, hints);
        else if (EnableHints)
        {
            // ...but still avoid towers they may not soak (logs: a DPS stood in the healer tower)
            var towers = ActiveTowers(slot, actor);
            for (var i = 0; i < towers.Length; ++i)
                if (towers[i].ForbiddenSoakers[slot])
                    hints.AddForbiddenZone(towers[i].ShapeDistance ?? towers[i].Shape.Distance(towers[i].Position, towers[i].Rotation), towers[i].Activation);
        }
        if (EnableHints && NumCasts > 0 && assignment is PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2)
            hints.AddForbiddenZone(new SDRect(Arena.Center, RelativeSouth.Normalized(), 20f, 0f, 20f));
    }

    public override void OnMapEffect(byte index, uint state)
    {
        if (index is >= 51 and <= 53 && state == 0x00020001u)
        {
            var dir = index switch
            {
                51 => -120f.Degrees(),
                52 => 120f.Degrees(),
                _ => default
            };
            if (RelativeSouth == default && NumCasts == 0 && Towers.Count == 0)
                RelativeSouth = dir.ToDirection();
            var forbidden = Raid.WithSlot(true, true, true).WhereActor(p => p.Role == Role.Tank).Mask();
            Towers.Add(new(Arena.Center + 7f * dir.ToDirection(), 3f, 2, 2, forbidden, WorldState.FutureTime(9.5d)));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            ++NumCasts;
            if (Towers.Count == 0)
            {
                ReportError($"Unexpected expolosion @ {caster.Position}");
            }
            else
            {
                if (!Towers[0].Position.AlmostEqual(caster.Position, 1))
                    ReportError($"Unexpected position: {caster.Position} instead of {Towers[0].Position}");
                Towers.RemoveAt(0);
            }
        }
    }
}

sealed class P5ParadiseRegainedBaits(BossModule module) : Components.GenericBaitAway(module)
{
    public bool? LightFirst;
    private Actor? _source;
    private Actor? _firstTarget;
    public Actor? FirstTank => _firstTarget;
    private AOEShapeCone? _curCleave;
    private DateTime _activation;
    private bool _tetherClosest;
    // the tether that goes with a cleave resolves ~0.8s after it (logged: dark cleave at +1.4s, closest tether at +2.2s), so its avoidance
    // must outlive the cleave; cleared by the tether event, or after 1.5s if that is missed
    private bool? _pendingTetherClosest;
    private DateTime _pendingTetherUntil;
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();

    private readonly AOEShapeCone _shapeCleaveL = new(100f, 120f.Degrees(), 60f.Degrees());
    private readonly AOEShapeCone _shapeCleaveD = new(100f, 120f.Degrees(), -60f.Degrees());
    private readonly AOEShapeCircle _shapeTether = new(4);

    // both cleaves are done; the component stays alive only to protect from the second tether (~0.8s after cleave two)
    private bool CleavesDone => NumCasts >= 2;
    private bool TetherPending => _pendingTetherClosest != null && WorldState.CurrentTime < _pendingTetherUntil;

    public override void Update()
    {
        CurrentBaits.Clear();
        if (_source != null && _curCleave != null && !CleavesDone)
        {
            // Mana: the second cleave always lands on the other tank (the first tether's bait), however late they Provoke;
            // following the boss's live target left DPS on the wrong side until 2.2s before it (e.g. OT Provoked 1.5s after cleave 1)
            var cleaveTarget = NumCasts == 0 ? _firstTarget
                : _config.P4ManaStrategy && Raid.WithoutSlot(false, true, true).FirstOrDefault(p => p.Role == Role.Tank && p != _firstTarget) is { } otherTank ? otherTank
                : WorldState.Actors.Find(_source.TargetID);
            if (cleaveTarget != null)
                CurrentBaits.Add(new(_source, cleaveTarget, _curCleave, _activation));
            // The first cleave stays locked to its original tank after the cast-time aggro swap.
            var excludedTarget = _config.P4ManaStrategy ? cleaveTarget?.InstanceID ?? _source.TargetID : _source.TargetID;
            var eligible = Raid.WithoutSlot(false, true, true).Where(p => p.InstanceID != excludedTarget);
            var tetherTarget = _tetherClosest ? eligible.Closest(_source.Position) : eligible.Farthest(_source.Position);
            if (tetherTarget != null)
                CurrentBaits.Add(new(tetherTarget, tetherTarget, _shapeTether, _activation)); // +0.7s?
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (!ForbiddenPlayers[slot] && CurrentBaits.Count == 2 && _source != null && _firstTarget != null)
        {
            var isFirstTank = actor == _firstTarget;
            if (_source.TargetID == _firstTarget.InstanceID)
                hints.Add(isFirstTank ? "Pass aggro!" : "Taunt!"); // TODO: can a tank bait both cleaves with invuln?

            var firstTankShouldBaitTether = NumCasts > 0; // TODO: can a tank bait cleave+tether with invuln?
            var shouldBaitTether = isFirstTank == firstTankShouldBaitTether;
            if (shouldBaitTether && CurrentBaits[1].Target != actor)
                hints.Add(_tetherClosest ? "Go closer!" : "Go farther!");
        }

        base.AddHints(slot, actor, hints);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (_config.P4ManaStrategy && _source != null && _firstTarget != null && _curCleave != null)
        {
            var tetherTank = NumCasts == 0 ? Raid.WithoutSlot(false, true, true).FirstOrDefault(p => p.Role == Role.Tank && p != _firstTarget) : _firstTarget;
            if (_pendingTetherClosest is bool pendingClosest && TetherPending)
            {
                // tether still pending after its cleave: the first is baited by the second tank (logs: a DPS was pulled to 2.5y off it), the second by the first
                var pendingBait = NumCasts == 1 ? Raid.WithoutSlot(false, true, true).FirstOrDefault(p => p.Role == Role.Tank && p != _firstTarget) : _firstTarget;
                if (pendingBait != null && actor != pendingBait)
                    hints.AddForbiddenZone(pendingClosest ? new SDCircle(pendingBait.Position, 5f) : new SDInvertedCircle(_source.Position, 8f), _pendingTetherUntil);
            }
            else if (!CleavesDone && tetherTank != null && actor != tetherTank)
            {
                // Tank goals are at radius 2/10 with radius-1 tolerance: leave a full yalm of distance priority.
                hints.AddForbiddenZone(_tetherClosest ? new SDCircle(_source.Position, 4f) : new SDInvertedCircle(_source.Position, 8f), _activation);
                if (_tetherClosest)
                    hints.AddForbiddenZone(new SDCircle(tetherTank.Position, 5f), _activation); // the 4y circle lands on the tank, not on the boss
            }
        }
        if (!ForbiddenPlayers[slot] && SafeOffset(slot, actor) is var offset && offset != default)
            hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + offset, 1f));
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);

        var safeOffset = SafeOffset(pcSlot, pc);
        if (safeOffset != default)
        {
            Arena.ZoneCircleOutline(Arena.Center + safeOffset, 1f, Colors.Safe);
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        (var shape, var closest) = spell.Action.ID switch
        {
            (uint)AID.WingsDarkAndLightDL => (_shapeCleaveD, true),
            (uint)AID.WingsDarkAndLightLD => (_shapeCleaveL, false),
            _ => (null, false)
        };
        if (shape != null)
        {
            ForbiddenPlayers = Raid.WithSlot(true, true, true).WhereActor(p => p.Role != Role.Tank).Mask();
            _source = caster;
            _firstTarget = WorldState.Actors.Find(caster.TargetID);
            _curCleave = shape;
            _activation = Module.CastFinishAt(spell, 0.3d);
            _tetherClosest = closest;
            LightFirst = !closest;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        var nextShape = spell.Action.ID switch
        {
            (uint)AID.WingsDarkAndLightCleaveLight => _shapeCleaveD,
            (uint)AID.WingsDarkAndLightCleaveDark => _shapeCleaveL,
            _ => null
        };
        if (spell.Action.ID is (uint)AID.WingsDarkAndLightTetherLight or (uint)AID.WingsDarkAndLightTetherDark)
            _pendingTetherClosest = null;
        if (nextShape != null)
        {
            ++NumCasts;
            _curCleave = nextShape;
            _activation = WorldState.FutureTime(3.7d);
            _pendingTetherClosest = _tetherClosest;
            _pendingTetherUntil = WorldState.FutureTime(1.5d);
            _tetherClosest = !_tetherClosest;
        }
    }

    private WDir SafeOffset(int slot, Actor player)
    {
        var south = Module.FindComponent<P5ParadiseRegainedTowers>()?.RelativeSouth ?? default;
        if (south == default || _curCleave == null)
            return default; // not initialized properly
        if (CleavesDone && (player != _firstTarget || !TetherPending))
            return default; // only the second tether's bait (first tank) holds its spot until it resolves

        var southDir = Angle.FromDirection(south);
        if (ForbiddenPlayers[slot])
        {
            return default; // tower component handles non-tank movement
        }
        else if (player == _firstTarget)
        {
            if (NumCasts == 0)
            {
                // bait cleave, so that south is safe
                return _config.P4ManaStrategy ? -7f * south : 7f * (southDir + 2f * _curCleave.DirectionOffset).ToDirection();
            }
            else
            {
                // bait tether across south
                return (_tetherClosest ? 2f : 10f) * (southDir + 180f.Degrees()).ToDirection();
            }
        }
        else
        {
            if (NumCasts > 0)
            {
                // bait cleave, so that north is safe
                return 7f * (southDir - _curCleave.DirectionOffset).ToDirection();
            }
            else if (_tetherClosest)
            {
                // bait tether at south
                return 2f * southDir.ToDirection();
            }
            else
            {
                // bait tether at max melee at 45 degrees
                return 10f * (southDir + 0.75f * _curCleave.DirectionOffset).ToDirection();
            }
        }
    }
}
