namespace BossMod.Dawntrail.Ultimate.FRU;

sealed class P2AbsoluteZero(BossModule module) : Components.CastCounter(module, (uint)AID.AbsoluteZeroAOE);

sealed class P2SwellingFrost(BossModule module) : Components.GenericKnockback(module, (uint)AID.SwellingFrost)
{
    private readonly DateTime _activation = module.WorldState.FutureTime(3.2d);

    public override ReadOnlySpan<Knockback> ActiveKnockbacks(int slot, Actor actor)
    {
        return new Knockback[1] { new(Arena.Center, 10f, _activation, ignoreImmunes: true) };
    }
}

sealed class P2SinboundBlizzard(BossModule module) : Components.SimpleAOEs(module, (uint)AID.SinboundBlizzardAOE, new AOEShapeCone(50f, 10f.Degrees()));

sealed class P2HiemalStorm(BossModule module) : Components.SimpleAOEs(module, (uint)AID.HiemalStormAOE, 7f)
{
    private bool _slowDodges;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        // storms are cast every 3s, ray voidzones appear every 2s; to place voidzones more tightly, we pretend radius is smaller during first half of cast
        // there's no point doing it before first voidzone appears, however
        var deadline = _slowDodges ? WorldState.FutureTime(1.5d) : DateTime.MaxValue;
        foreach (var c in Casters)
        {
            var activation = c.Activation;
            hints.AddForbiddenZone(new SDCircle(c.Origin, activation > deadline ? 4f : 7f), activation);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        base.OnEventCast(caster, spell);
        if (spell.Action.ID == (uint)AID.HiemalRay)
            _slowDodges = true;
    }
}

sealed class P2HiemalRay(BossModule module) : Components.VoidzoneAtCastTarget(module, 4f, (uint)AID.HiemalRay, GetVoidzones, 0.7d)
{
    private static Actor[] GetVoidzones(BossModule module)
    {
        var enemies = module.Enemies((uint)OID.HiemalRayVoidzone);
        var count = enemies.Count;
        if (count == 0)
            return [];

        var voidzones = new Actor[count];
        var index = 0;
        for (var i = 0; i < count; ++i)
        {
            var z = enemies[i];
            if (z.EventState != 7)
            {
                voidzones[index++] = z;
            }
        }
        return voidzones[..index];
    }
}

// TODO: show hint if ice veil is clipped
sealed class P2Intermission(BossModule module) : Components.GenericBaitAway(module)
{
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private readonly P2SinboundBlizzard? _cones = module.FindComponent<P2SinboundBlizzard>();
    private readonly List<Actor> _crystalsOfLight = module.Enemies((uint)OID.CrystalOfLight);
    private readonly List<Actor> _crystalsOfDarkness = module.Enemies((uint)OID.CrystalOfDarkness);
    private readonly List<Actor> _iceVeil = module.Enemies((uint)OID.IceVeil);
    private bool _iceVeilInvincible = true;
    private bool _gaiaHammer;
    private readonly Dictionary<ulong, int> _coneBaits = [];
    private readonly Dictionary<ulong, int> _lightBaits = [];
    private readonly Dictionary<ulong, ulong> _puddleSources = [];
    private readonly Dictionary<ulong, int> _puddleBaits = [];
    private bool _manaLightRouteValid = true;

    internal static Actor? ManaLightSource(Actor[] crystals, WPos location)
    {
        Actor? source = null;
        foreach (var crystal in crystals)
        {
            if (crystal.IsDeadOrDestroyed || !crystal.Position.InCircle(location, 7f))
                continue;
            if (source != null)
                return null; // no unique source: never infer a round from another camp
            source = crystal;
        }
        return source;
    }

    internal static float ManaLightBaitGoal(WPos point, WPos source, WPos center, int circles, int puddles)
    {
        var offset = point - source;
        var outward = (source - center).Normalized();
        var radiusSq = offset.LengthSq();
        if (outward == default || radiusSq < 16f || radiusSq > 25f)
            return 0f;
        // Circle baits stay outside and alternate CW/CCW. After the first Ray,
        // circle the source: outer CW, inner CW, inner CCW, outer CCW.
        var stage = puddles < 0 ? -1 : puddles & 3;
        var clockwise = puddles <= 0 ? (circles & 1) == 0 : stage is 1 or 2;
        var outside = stage < 0 || stage is 0 or 1;
        var radial = offset.Dot(outward) * (outside ? 1f : -1f);
        var lateral = offset.Dot(outward.OrthoR()) * (clockwise ? 1f : -1f);
        return radial > 0f && lateral > 0f ? 100f + Math.Min(radial, lateral) : 0f;
    }

    internal static int ManaLightClock(int clockSpot) => clockSpot is >= 0 and < 8 ? (clockSpot + (clockSpot & 1)) & 7 : -1;

    internal static WDir? ManaConeBaitDirection(Actor source, Actor[] obstacles, WPos center, bool clockwise, AOEShapeCone cone)
    {
        var outward = (source.Position - center).Normalized();
        if (outward == default)
            return null;
        var outwardAngle = Angle.FromDirection(outward);
        var arcs = new ArcList(source.Position, 1f);
        arcs.ForbidArcByLength((outwardAngle + 180f.Degrees()).Normalized(), 90f.Degrees()); // never aim inward
        arcs.ForbidArcByLength((outwardAngle + (clockwise ? 90f : -90f).Degrees()).Normalized(), 90f.Degrees());
        foreach (var target in obstacles)
        {
            if (target == source || target.IsDead || target.IsDestroyed)
                continue;
            var offset = target.Position - source.Position;
            var distance = offset.Length();
            if (distance <= target.HitboxRadius)
                return null;
            if (distance <= cone.Radius + target.HitboxRadius)
                arcs.ForbidArcByLength(Angle.FromDirection(offset), cone.HalfAngle + Angle.Asin(Math.Min(1f, target.HitboxRadius / distance)));
        }
        var preferred = (outwardAngle + (clockwise ? -cone.HalfAngle : cone.HalfAngle)).Normalized();
        // The position hint allows 0.1 yalm at distance 1: reserve its full angular tolerance plus a margin.
        var best = arcs.Allowed(Angle.Asin(0.1f) + 1f.Degrees()).MinBy(r => preferred.DistanceToRange(r.min, r.max).Abs().Rad);
        return best.max > best.min ? preferred.ClosestInRange(best.min, best.max).ToDirection() : null;
    }

    internal static ShapeDistance[] ManaConeBaitSafety(Actor source, Actor[] obstacles, WPos center, bool clockwise, AOEShapeCone cone)
    {
        var outward = (source.Position - center).Normalized();
        List<ShapeDistance> forbidden = [new SDCircle(source.Position, 1f), new SDHalfPlane(source.Position, outward), new SDHalfPlane(source.Position, clockwise ? outward.OrthoR() : outward.OrthoL())];
        foreach (var target in obstacles)
        {
            if (target == source || target.IsDead || target.IsDestroyed)
                continue;
            var offset = target.Position - source.Position;
            var distance = offset.Length();
            if (distance <= target.HitboxRadius)
                return [new SDCircle(source.Position, cone.Radius)]; // overlapping target: no aim is safe
            if (distance <= cone.Radius + target.HitboxRadius)
                forbidden.Add(new SDCone(source.Position, cone.Radius, Angle.FromDirection(offset), cone.HalfAngle + Angle.Asin(Math.Min(1f, target.HitboxRadius / distance)) + 1f.Degrees()));
        }
        return forbidden.ToArray();
    }

    public bool CrystalsActive => CrystalsOfLight.Count != 0;

    public override bool KeepOnPhaseChange => true; // collision might not be removed if component gets unloaded too early

    public override void Update()
    {
        IgnoreOtherBaits = true;
        CurrentBaits.Clear();
        if (_cones == null)
            return;
        foreach (var c in _crystalsOfDarkness)
        {
            var baiter = Raid.WithoutSlot(false, true, true).Closest(c.Position);
            if (baiter != null)
                CurrentBaits.Add(new(c, baiter, _cones.Shape));
        }
    }

    public override void OnMapEffect(byte index, uint state)
    {
        if (index == 0x18)
        {
            switch (state)
            {
                case 0x00020001u: // crystal appears
                    Arena.Bounds = new ArenaBoundsCustom([new Polygon(Arena.Center, 20f, 64)], [new Polygon(new(100.5f, 100f), 6f, 16)]); // crystal collision is slightly off center for some reason
                    break;
                case 0x00080004u: // crystal destroyed
                    Arena.Bounds = FRU.BuildArena().arena;
                    break;
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        // enemy priorities
        var clockSpot = _config.P2IntermissionClockSpots[assignment];
        var count = hints.PotentialTargets.Count;
        for (var i = 0; i < count; ++i)
        {
            var e = hints.PotentialTargets[i];
            e.Priority = e.Actor.OID switch
            {
                (uint)OID.CrystalOfLight => CrystalPriority(e.Actor, clockSpot),
                (uint)OID.CrystalOfDarkness => AIHints.Enemy.PriorityPointless,
                (uint)OID.IceVeil => _iceVeilInvincible
                    ? AIHints.Enemy.PriorityInvincible
                    : e.Actor.PendingHPRatio < (_gaiaHammer ? 0 : 0.5f)
                        ? AIHints.Enemy.PriorityPointless
                        : 1,
                _ => 0
            };
        }

        // don't stand inside light crystals, to avoid bad puddle baits
        foreach (var c in CrystalsOfLight)
            hints.AddForbiddenZone(new SDCircle(c.Position, 4f), WorldState.FutureTime(30d));

        // mechanic resolution
        if (clockSpot < 0)
        {
            // no assignment, oh well...
        }
        else if ((clockSpot & 1) == 0)
        {
            // cardinals - bait puddles accurately
            var assignedDir = (180f - 45f * clockSpot).Degrees();
            var assignedPosition = Arena.Center + 15f * assignedDir.ToDirection(); // crystal is at R=15
            var assignedCrystal = CrystalsOfLight.FirstOrDefault(c => c.Position.AlmostEqual(assignedPosition, 2f));
            if (assignedCrystal != null)
            {
                hints.AddForbiddenZone(new SDInvertedCircle(assignedPosition, 5f), WorldState.FutureTime(60d));
                if (_config.P123ManaStrategy)
                {
                    if (_manaLightRouteValid)
                    {
                        var circles = _lightBaits.GetValueOrDefault(assignedCrystal.InstanceID);
                        var puddles = _puddleSources.GetValueOrDefault(actor.InstanceID) == assignedCrystal.InstanceID
                            ? _puddleBaits.GetValueOrDefault(actor.InstanceID) : -1;
                        // Native AoE/voidzone avoidance takes precedence over this route goal,
                        // and can cross the inner side while dodging a locked outer circle.
                        hints.GoalZones.Add(p => ManaLightBaitGoal(p, assignedCrystal.Position, Arena.Center, circles, puddles));
                    }
                }
                else
                    hints.AddForbiddenZone(new SDCircle(Arena.Center, 17f), DateTime.MaxValue); // prefer to stay near border
            }
            else
            {
                // go to the ice veil
                // TODO: consider helping other melees with their crystals? a bit dangerous, can misbait
                // TODO: consider helping nearby ranged to bait their cones?
                hints.AddForbiddenZone(new SDInvertedCone(Arena.Center, 7f, assignedDir, 10f.Degrees()), DateTime.MaxValue);
            }
        }
        else
        {
            // intercardinals - bait cones
            if (_cones?.Casters.Count == 0)
            {
                var assignedPosition = Arena.Center + 9f * (180f - 45f * clockSpot).Degrees().ToDirection(); // crystal is at R=8
                var assignedCrystal = CrystalsOfDarkness.FirstOrDefault(c => c.Position.AlmostEqual(assignedPosition, 2f));
                if (assignedCrystal != null)
                {
                    if (_config.P123ManaStrategy && _cones.Shape is AOEShapeCone cone)
                    {
                        var obstacles = CrystalsOfLight.Concat(CrystalsOfDarkness).Concat(BossModule.GetActiveActors(_iceVeil)).ToArray();
                        var clockwise = (_coneBaits.GetValueOrDefault(assignedCrystal.InstanceID) & 1) == 0;
                        var baitDir = ManaConeBaitDirection(assignedCrystal, obstacles, Arena.Center, clockwise, cone);
                        if (baitDir == null)
                            return; // cannot find a crystal-safe bait; do not choose arbitrary geometry
                        // Precision map cells are larger than the requested final tolerance: constrain every allowed aim.
                        foreach (var forbidden in ManaConeBaitSafety(assignedCrystal, obstacles, Arena.Center, clockwise, cone))
                            hints.AddForbiddenZone(forbidden, DateTime.MaxValue);
                        assignedPosition = assignedCrystal.Position + baitDir.Value; // retain native 1-yalm bait distance
                    }
                    hints.AddForbiddenZone(new SDPrecisePosition(assignedPosition, new WDir(default, 1f), Arena.Bounds.MapResolution, actor.Position, 0.1f));
                }
            }
            // else: just dodge cones etc...
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        Arena.Actors(CrystalsOfLight);
        Arena.Actors(CrystalsOfDarkness, Colors.Object);
        Arena.Actor(IceVeil, _iceVeilInvincible ? Colors.Object : default);
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.Invincibility)
        {
            _iceVeilInvincible = false;
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (_config.P123ManaStrategy && spell.Action.ID == (uint)AID.HiemalStormAOE)
        {
            // The helper locks its location before the source visual arrives; switch
            // to the next bait side at the actual lock, not the late source visual.
            var source = ManaLightSource(CrystalsOfLight.ToArray(), spell.LocXZ);
            if (source == null)
                _manaLightRouteValid = false;
            else
                _lightBaits[source.InstanceID] = _lightBaits.GetValueOrDefault(source.InstanceID) + 1;
        }
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (source.OID == (uint)OID.CrystalOfLight && tether.ID == (uint)TetherID.HiemalRay)
        {
            _puddleSources[tether.Target] = source.InstanceID;
            _puddleBaits[tether.Target] = 0;
        }
    }

    public override void OnCastFinished(Actor caster, ActorCastInfo spell)
    {
        if (caster.OID == (uint)OID.CrystalOfDarkness && spell.Action.ID == (uint)AID.SinboundBlizzard)
            _coneBaits[caster.InstanceID] = _coneBaits.GetValueOrDefault(caster.InstanceID) + 1;
    }

    private List<Actor> CrystalsOfLight => BossModule.GetActiveActors(_crystalsOfLight);
    private List<Actor> CrystalsOfDarkness => BossModule.GetActiveActors(_crystalsOfDarkness);
    private Actor? IceVeil => BossModule.GetActiveActor(_iceVeil);

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.HiemalRay && _puddleSources.ContainsKey(spell.MainTargetID))
            _puddleBaits[spell.MainTargetID] = _puddleBaits.GetValueOrDefault(spell.MainTargetID) + 1;
        if (spell.Action.ID == (uint)AID.Icecrusher)
        {
            _gaiaHammer = true;
        }
    }

    private int CrystalPriority(Actor crystal, int clockSpot)
    {
        if (_config.P123ManaStrategy)
            clockSpot = ManaLightClock(clockSpot);
        var offset = crystal.Position - Arena.Center;
        var priority = clockSpot switch
        {
            0 => offset.Z < -10f,
            2 => offset.X > +10f,
            4 => offset.Z > +10f,
            6 => offset.X < -10f,
            _ => false
        };
        return priority ? 2 : 1;
    }
}
