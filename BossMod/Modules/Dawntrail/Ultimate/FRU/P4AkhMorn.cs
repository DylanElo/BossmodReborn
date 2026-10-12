namespace BossMod.Dawntrail.Ultimate.FRU;

// TODO: can target change if boss is provoked mid cast?
sealed class P4AkhMorn(BossModule module) : Components.UniformStackSpread(module, 4f, default, 4)
{
    public int NumCasts;
    private bool _soloOT;
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private Actor? _soloSource;

    public void SetSoloOT(bool soloOT)
    {
        _soloOT = soloOT;
        if (!_config.P4ManaStrategy)
            return;
        // Activation replays in-progress casts before the state configures the solo tank.
        Stacks.Clear();
        Spreads.Clear();
        _soloSource = null;
        foreach (var caster in WorldState.Actors)
            if (caster.CastInfo is { } spell)
                OnCastStarted(caster, spell);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (_config.P4ManaStrategy && Stacks.Count == 1 && Spreads.Count == 1)
        {
            var solo = Spreads[0].Target == actor;
            var offset = _soloSource != null ? _soloSource.Position - Arena.Center : default;
            if (!solo || offset.LengthSq() > 1f)
                hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + (solo ? 12f * offset.Normalized() : default), 1f), Stacks[0].Activation);
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID is (uint)AID.AkhMornOracle or (uint)AID.AkhMornUsurper && WorldState.Actors.Find(caster.TargetID) is var target && target != null)
        {
            var activation = Module.CastFinishAt(spell, 0.9d);
            if (!_config.P4ManaStrategy)
                AddStack(target, activation);
            else if ((spell.Action.ID == (uint)AID.AkhMornOracle) == _soloOT)
            {
                _soloSource = caster;
                Spreads.Add(new(target, StackRadius, activation));
            }
            else
                Stacks.Add(new(target, StackRadius, 7, 7, activation));
            if (_config.P4ManaStrategy && Stacks.Count == 1 && Spreads.Count == 1)
            {
                var soloSlot = Raid.FindSlot(Spreads[0].Target.InstanceID);
                if (soloSlot >= 0)
                    Stacks.Ref(0).ForbiddenPlayers.Set(soloSlot);
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.AkhMornAOEOracle)
            ++NumCasts;
    }
}
