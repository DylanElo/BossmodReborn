namespace BossMod.Dawntrail.Ultimate.FRU;

sealed class P4MornAfah(BossModule module) : Components.UniformStackSpread(module, 4f, default, 8)
{
    private Actor? _target;
    private DateTime _activation;
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();

    public override void Update()
    {
        if (_target == null)
            return;
        var usurper = Module.Enemies((uint)OID.UsurperOfFrostP4).FirstOrDefault();
        var oracle = Module.Enemies((uint)OID.OracleOfDarknessP4).FirstOrDefault();
        var failed = _config.P4ManaStrategy && usurper != null && oracle != null
            && usurper.HPMP.MaxHP != 0 && oracle.HPMP.MaxHP != 0
            && Math.Abs((decimal)usurper.HPMP.CurHP * oracle.HPMP.MaxHP - (decimal)oracle.HPMP.CurHP * usurper.HPMP.MaxHP) * 20
                > (decimal)usurper.HPMP.MaxHP * oracle.HPMP.MaxHP;
        if (failed && Stacks.Count != 0)
        {
            Stacks.Clear();
            Spreads.Add(new(_target, StackRadius, _activation));
        }
        else if (!failed && Spreads.Count != 0)
        {
            Spreads.Clear();
            AddStack(_target, _activation);
        }
    }

    public override void AddGlobalHints(Actor actor, GlobalHints hints)
    {
        if (Spreads.Count != 0)
            hints.Add("HP check failed: tank sacrifices alone; everyone else stay away!");
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.MornAfahUsurper)
        {
            var target = WorldState.Actors.Find(caster.TargetID);
            if (target != null)
            {
                _target = target;
                _activation = Module.CastFinishAt(spell, 0.9f);
                AddStack(target, _activation);
                Update();
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.MornAfahAOE)
        {
            _target = null;
            Stacks.Clear();
            Spreads.Clear();
        }
    }
}

sealed class P4MornAfahHPCheck(BossModule module) : BossComponent(module)
{
    public override void AddGlobalHints(Actor actor, GlobalHints hints)
    {
        var usurpers = Module.Enemies((uint)OID.UsurperOfFrostP4);
        var oracles = Module.Enemies((uint)OID.OracleOfDarknessP4);
        var usurper = usurpers.Count != 0 ? usurpers[0] : null;
        var oracle = oracles.Count != 0 ? oracles[0] : null;
        if (usurper != null && oracle != null && usurper.HPMP.MaxHP != 0 && oracle.HPMP.MaxHP != 0)
        {
            var diff = 100m * usurper.HPMP.CurHP / usurper.HPMP.MaxHP - 100m * oracle.HPMP.CurHP / oracle.HPMP.MaxHP;
            hints.Add($"Usurper HP: {(diff > 0 ? "+" : "")}{diff:f1}%");
        }
    }
}
