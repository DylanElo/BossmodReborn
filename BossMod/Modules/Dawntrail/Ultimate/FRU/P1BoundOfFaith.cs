namespace BossMod.Dawntrail.Ultimate.FRU;

abstract class BurntStrike(BossModule module, uint aid) : Components.SimpleAOEs(module, aid, new AOEShapeRect(80f, 5f));
sealed class P1TurnOfHeavensBurntStrikeFire(BossModule module) : BurntStrike(module, (uint)AID.TurnOfHeavensBurntStrikeFire);
sealed class P1TurnOfHeavensBurntStrikeLightning(BossModule module) : BurntStrike(module, (uint)AID.TurnOfHeavensBurntStrikeLightning);

abstract class BurntOut(BossModule module, uint aid) : Components.SimpleAOEs(module, aid, new AOEShapeRect(80f, 10f));
sealed class P1TurnOfHeavensBurnout(BossModule module) : BurntOut(module, (uint)AID.TurnOfHeavensBurnout);

sealed class P1BrightfireSmall(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BrightfireSmall, 5f);
sealed class P1BrightfireLarge(BossModule module) : Components.SimpleAOEs(module, (uint)AID.BrightfireLarge, 10f);

// TODO: fixed tethers strat variant (tether target with clone on safe side goes S, other goes N, if any group has 5 players prio1 adjusts)
sealed class P1BoundOfFaith(BossModule module) : Components.UniformStackSpread(module, 6f, default, 4, 4)
{
    public WDir SafeSide;
    public DateTime Activation;
    public readonly int[] AssignedGroups = new int[PartyState.MaxPartySize];
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private uint _safeHalo;

    public WDir AssignedLane(int slot) => new(0, AssignedGroups[slot] * 5.4f);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints) { } // we have dedicated components for this

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);
        if (AssignedGroups[pcSlot] != 0 && SafeSide.X != 0)
        {
            Arena.ZoneCircleOutline(Arena.Center + SafeSide * 18.2f + AssignedLane(pcSlot), 1, Colors.Safe);
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        _safeHalo = spell.Action.ID switch
        {
            (uint)AID.TurnOfHeavensFire => (uint)OID.HaloOfLevin,
            (uint)AID.TurnOfHeavensLightning => (uint)OID.HaloOfFlame,
            _ => _safeHalo
        };
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.Fire && WorldState.Actors.Find(tether.Target) is var target && target != null)
        {
            Activation = WorldState.FutureTime(10.6d);
            AddStack(target, Activation);
            if (Stacks.Count == 2)
                InitAssignments();
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.BoundOfFaithSinsmoke)
            Stacks.Clear();
    }

    private void InitAssignments()
    {
        var manaSlots = _config.P123ManaStrategy ? Service.Config.Get<PartyRolesConfig>().SlotsPerAssignment(Raid) : [];
        if (_config.P123ManaStrategy && manaSlots.Length == 0)
            return;
        if (_safeHalo != default)
        {
            WDir averageOffset = default;
            foreach (var aoe in Module.Enemies(_safeHalo))
                averageOffset += aoe.Position - Arena.Center;
            var safeSideX = averageOffset.X > 0 ? 1 : -1;
            SafeSide = new(safeSideX, SafeSide.Z);
        }

        // initial assignments
        Span<int> tetherSlots = [-1, -1];
        Span<int> prio = [0, 0, 0, 0, 0, 0, 0, 0];
        foreach (var (slot, group) in _config.P1BoundOfFaithAssignment.Resolve(Raid))
        {
            AssignedGroups[slot] = group < 4 ? -1 : 1;
            prio[slot] = group & 3;
            if (IsStackTarget(Raid[slot]))
                tetherSlots[tetherSlots[0] < 0 ? 0 : 1] = slot;
        }

        // swaps
        if (tetherSlots[0] >= 0 && tetherSlots[1] >= 0 && AssignedGroups[tetherSlots[0]] == AssignedGroups[tetherSlots[1]])
        {
            // Mana keeps the higher-priority prey north; legacy flexes the lower priority on either side.
            var flexLower = !_config.P123ManaStrategy || AssignedGroups[tetherSlots[0]] > 0;
            var tetherFlexSlot = (prio[tetherSlots[0]] < prio[tetherSlots[1]]) == flexLower ? tetherSlots[0] : tetherSlots[1];
            AssignedGroups[tetherFlexSlot] = -AssignedGroups[tetherFlexSlot];

            if (_config.P123ManaStrategy)
            {
                var normalFlexSlot = manaSlots[(int)(AssignedGroups[tetherFlexSlot] < 0 ? PartyRolesConfig.Assignment.MT : PartyRolesConfig.Assignment.M1)];
                AssignedGroups[normalFlexSlot] = -AssignedGroups[normalFlexSlot];
                return;
            }

            // now the group where we've moved flex slot has 5 people, find untethered with lowest prio
            for (var normalFlexSlot = 0; normalFlexSlot < PartyState.MaxPartySize; ++normalFlexSlot)
            {
                if (normalFlexSlot != tetherFlexSlot && prio[normalFlexSlot] == 0 && AssignedGroups[normalFlexSlot] == AssignedGroups[tetherFlexSlot])
                {
                    AssignedGroups[normalFlexSlot] = -AssignedGroups[normalFlexSlot];
                    break;
                }
            }
        }
    }
}

sealed class P1BoundOfFaithAIKnockback(BossModule module) : BossComponent(module)
{
    private readonly P1BoundOfFaith? _comp = module.FindComponent<P1BoundOfFaith>();
    private bool _horizDone;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_comp == null || _comp.SafeSide == default)
            return;

        var sideOffset = _horizDone ? 0f : 7f; // before horizonal aoes are done, we don't show knockback, so adjust the unsafe zone
        hints.AddForbiddenZone(new SDHalfPlane(Arena.Center + sideOffset * _comp.SafeSide, _comp.SafeSide), _comp.Activation);

        var lane = _comp.AssignedLane(slot);
        if (_horizDone && lane.Z != 0f)
        {
            hints.AddForbiddenZone(new SDInvertedRect(Arena.Center + lane, new WDir(1f, default), 20f, 20f, 0.7f), _comp.Activation);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.TurnOfHeavensBurnout)
            _horizDone = true;
    }
}

sealed class P1BoundOfFaithAIStack(BossModule module) : BossComponent(module)
{
    private readonly P1BoundOfFaith? _comp = module.FindComponent<P1BoundOfFaith>();
    private bool _haveFetters;

    internal static int ManaStackIndex(int group, int[] stackGroups)
    {
        if (group == 0 || stackGroups.Length != 2 || stackGroups[0] == 0 || stackGroups[0] != -stackGroups[1])
            return -1;
        var index = -1;
        for (var i = 0; i < stackGroups.Length; ++i)
            if (stackGroups[i] == group)
            {
                if (index >= 0)
                    return -1; // incomplete/duplicate assignments: do not choose by distance
                index = i;
            }
        return index;
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_comp == null)
            return;

        if (_haveFetters)
        {
            var stackWith = _comp.Stacks.MinBy(s => (s.Target.Position - actor.Position).LengthSq());
            if (Service.Config.Get<FRUConfig>().P123ManaStrategy)
            {
                var stackIndex = ManaStackIndex(_comp.AssignedGroups[slot], _comp.Stacks.Select(s => Raid.FindSlot(s.Target.InstanceID) is var targetSlot && targetSlot >= 0 ? _comp.AssignedGroups[targetSlot] : 0).ToArray());
                if (stackIndex < 0 || _comp.Stacks[stackIndex].Target.IsDeadOrDestroyed)
                    return;
                stackWith = _comp.Stacks[stackIndex];
            }
            foreach (var s in _comp.Stacks)
            {
                ShapeDistance zone = s.Target == stackWith.Target
                    ? new SDInvertedCircle(s.Target.Position, 4f) // stay a bit closer to the target to avoid spooking people
                    : new SDCircle(s.Target.Position, 6f);
                hints.AddForbiddenZone(zone, _comp.Activation);
            }

            // all else being equal, try staying closer to center
            hints.GoalZones.Add(AIHints.GoalSingleTarget(Arena.Center, 7.5f, 0.5f));
        }
        else
        {
            // just go to center
            hints.AddForbiddenZone(new SDInvertedRect(Arena.Center, new WDir(1f, default), 1f, 1f, 20f), DateTime.MaxValue);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.FloatingFetters)
            _haveFetters = true;
    }
}
