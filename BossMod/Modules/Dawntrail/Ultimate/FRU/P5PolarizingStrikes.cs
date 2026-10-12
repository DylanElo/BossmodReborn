namespace BossMod.Dawntrail.Ultimate.FRU;

sealed class P5PolarizingStrikes(BossModule module) : Components.GenericAOEs(module)
{
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private readonly PartyRolesConfig _roles = Service.Config.Get<PartyRolesConfig>();
    private const float StepInSeconds = 1f; // FRU sim (Mana): step out of the gap 1.6s before the hit, ~1s after the lingering lines go off
    private readonly List<AOEInstance> _aoes = []; // 'afterglow'
    private readonly Actor?[] _baiters = [null, null]; // light/left, dark/right
    private readonly BitMask[] _forbidden = [default, default];
    private Actor? _source;
    private bool _baitsActive;

    private static readonly AOEShapeRect _shape = new(100f, 3f);

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor) => CollectionsMarshal.AsSpan(_aoes);

    public override void Update()
    {
        _baiters[0] = _baiters[1] = null;
        if (_source != null && _aoes.Count == 0 && _baitsActive)
        {
            var left = _source.Rotation.ToDirection().OrthoL();
            float distL = float.MaxValue, distR = float.MaxValue;
            foreach (var p in Raid.WithoutSlot(false, true, true))
            {
                var off = p.Position - _source.Position;
                var side = left.Dot(off) > 0;
                ref var target = ref _baiters[side ? 0 : 1];
                ref var dist = ref side ? ref distL : ref distR;
                var d = off.LengthSq();
                if (d < dist)
                {
                    dist = d;
                    target = p;
                }
            }
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_source != null && _aoes.Count == 0 && _baitsActive)
        {
            if (_baiters.Contains(actor) && (_forbidden[0] | _forbidden[1])[slot])
                hints.Add("Hide behind party!");

            var inLight = InAOE(_source, _baiters[0], actor);
            var inDark = InAOE(_source, _baiters[1], actor);
            if (inLight == inDark)
                hints.Add("Stay in group!");
            else if (_forbidden[inLight ? 0 : 1][slot])
                hints.Add("Go to correct group!");
        }
        else
        {
            base.AddHints(slot, actor, hints);
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_source == null)
            return;

        if (_aoes.Count > 0)
            base.AddAIHints(slot, actor, assignment, hints); // lingering lines
        if (WaitSpot(assignment) is WPos spot)
            hints.AddForbiddenZone(new SDInvertedCircle(spot, 1f), _aoes.Count > 0 ? _aoes[0].Activation : default);
    }

    // FRU sim (Mana) PSPos: between hits wait in the gap behind the boss, leaning toward your next side (OUT);
    // step to the 45-degree bait spot (IN) ~1.6s before the hit. The first bait goes straight IN.
    private WPos? WaitSpot(PartyRolesConfig.Assignment assignment)
    {
        if (_source == null || NumCasts >= 16 || assignment == PartyRolesConfig.Assignment.Unassigned)
            return null;
        var role = _config.P5PolarizingStrikesAssignments[assignment];
        if (role < 0)
            return null;
        var next = (NumCasts + 2) >> 2; // bait being set up: stays the current one until its lingering line has gone off
        var left = role < 4;
        var order = role & 3;
        var front = next == order;
        if (next > order)
            left ^= true;
        var stepIn = NumCasts == 0 || _aoes.Count == 0 && !(_source.CastInfo is { } cast && cast.NPCRemainingTime > StepInSeconds);
        var (distance, fromRear) = stepIn ? (front ? 5.6f : 8.41f, 45f) : front ? (6.32f, 8f) : (8.42f, 14.5f);
        var dir = _source.Rotation + (left ? 180f - fromRear : fromRear - 180f).Degrees();
        return _source.Position + distance * dir.ToDirection();
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (_source == null)
            return;
        if (_baitsActive)
            foreach (var (baiter, forbidden) in _baiters.Zip(_forbidden))
                if (baiter != null)
                    _shape.Outline(Arena, _source.Position, Angle.FromDirection(baiter.Position - _source.Position), forbidden[pcSlot] ? default : Colors.Safe);
        // where to stand now: the gap while lines linger and until ~1.6s before the next hit, then your bait spot
        var roles = _roles.AssignmentsPerSlot(WorldState.Party);
        var spot = pcSlot < roles.Length ? WaitSpot(roles[pcSlot]) : null;
        if (spot != null)
            Arena.ZoneCircleOutline(spot.Value, 1f, Colors.Safe);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.PolarizingStrikes:
            case (uint)AID.PolarizingPaths:
                _source = caster;
                _baitsActive = true;
                break;
            case (uint)AID.CruelPathOfLightBait:
            case (uint)AID.CruelPathOfDarknessBait:
                _baitsActive = false;
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.CruelPathOfLightBait:
            case (uint)AID.CruelPathOfDarknessBait:
                ++NumCasts;
                _aoes.RemoveAll(a => a.Activation < WorldState.CurrentTime); // a missed afterglow event must not leave a line behind
                _aoes.Add(new(_shape, caster.Position, caster.Rotation, WorldState.FutureTime(2d)));
                break;
            case (uint)AID.CruelPathOfLightAOE:
            case (uint)AID.CruelPathOfDarknessAOE:
                ++NumCasts;
                // each afterglow resolves its own line; clearing all dropped the partner line if its effect came a frame later
                if (_aoes.Count > 0)
                {
                    var best = 0;
                    for (var i = 1; i < _aoes.Count; ++i)
                        if ((_aoes[i].Origin - caster.Position).LengthSq() + (_aoes[i].Rotation - caster.Rotation).Normalized().Abs().Rad
                            < (_aoes[best].Origin - caster.Position).LengthSq() + (_aoes[best].Rotation - caster.Rotation).Normalized().Abs().Rad)
                            best = i;
                    _aoes.RemoveAt(best);
                }
                break;
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        switch (status.ID)
        {
            case (uint)SID.LightResistanceDown:
                _forbidden[0].Set(Raid.FindSlot(actor.InstanceID));
                break;
            case (uint)SID.DarkResistanceDown:
                _forbidden[1].Set(Raid.FindSlot(actor.InstanceID));
                break;
        }
    }

    private bool InAOE(Actor source, Actor? target, Actor player) => target != null && (target == player || _shape.Check(player.Position, source.Position, Angle.FromDirection(target.Position - source.Position)));
}
