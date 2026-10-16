namespace BossMod.Dawntrail.Savage.M01SBlackCat;

[ConfigDisplay(Order = 0x110, Parent = typeof(DawntrailConfig))]
public sealed class M01SBlackCatConfig() : ConfigNode()
{
    [PropertyDisplay("M1S: Game8 strategy (AI positions)", tooltip: "Quadruple Crossing cross baits (TH first), Raining Cats MT NW / ST NE with H1D1D2 near and H2D3D4 far south, Splintering Nails T-D-H, Tempestuous Tear MT group front. Requires correct party roles.")]
    public bool Game8Strategy = true;
}

// Game8 (game8.jp/ff14/630292) positions, AI only: goal zones that pull each role to its spot; mechanic AOEs stay forbidden zones, so they always win.
// ponytail: Mouser/Copycat tiles, shockwave knockback and swipe pair spots are left to plain pathfinding; add when a replay shows the AI picking wrong tiles.
sealed class Game8Strategy(BossModule module) : BossComponent(module)
{
    private readonly M01SBlackCatConfig _config = Service.Config.Get<M01SBlackCatConfig>();
    private Angle _nailsFacing, _tearFacing;
    private bool _nails, _tear, _cats;
    private int _catsTethers;
    private bool _supportsBaitFirst = true; // false when Nailchipper marks the supports first

    public override void Update()
    {
        var qc = Module.FindComponent<QuadrupleCrossingProtean>();
        if (qc?.Origin == null)
            return;
        // the first marker set sits on one role group; the unmarked group baits set 1, the marked group set 2
        if (Module.FindComponent<Nailchipper>() is var chipper && chipper != null && chipper.Spreads.Count != 0 && qc.NumCasts % 8 < 4)
            _supportsBaitFirst = !chipper.Spreads.Exists(s => s.Target.Role is Role.Tank or Role.Healer);
        else if (qc.NumCasts % 8 == 0)
            _supportsBaitFirst = true;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.SplinteringNails:
                _nails = true;
                _nailsFacing = spell.Rotation;
                break;
            case (uint)AID.TempestuousTear:
                _tear = true;
                _tearFacing = spell.Rotation;
                break;
            case (uint)AID.RainingCatsFirst:
                _cats = true;
                _catsTethers = 0;
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.SplinteringNailsAOE:
                _nails = false;
                break;
            case (uint)AID.TempestuousTearAOE:
                _tear = false;
                break;
            case (uint)AID.RainingCatsTether:
                if (++_catsTethers >= 8)
                    _cats = false;
                break;
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!_config.Game8Strategy || assignment == PartyRolesConfig.Assignment.Unassigned)
            return;
        if (Destination(actor, assignment) is WPos dest)
        {
            hints.GoalZones.Add(AIHints.GoalProximity(dest, 15f, 1f));
            hints.GoalZones.Add(AIHints.GoalProximity(dest, 1.5f, 2f));
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (_config.Game8Strategy && Service.Config.Get<PartyRolesConfig>()[Raid.Members[pcSlot].ContentId] is var a && a != PartyRolesConfig.Assignment.Unassigned
            && Destination(pc, a) is WPos dest)
            Arena.ZoneCircleOutline(dest, 1f, Colors.Safe);
    }

    private WPos? Destination(Actor actor, PartyRolesConfig.Assignment a)
    {
        var boss = Module.PrimaryActor;
        var support = a is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2;

        // Quadruple Crossing: cross fixed relative to the caster's facing (MT/D3 front, H1/D1 left, H2/D4 right, ST/D2 back).
        // The baiting group stands close on its cardinal, the other waits on the X (45 deg CCW). TH bait first, then DPS;
        // with Nailchipper markers out, whoever has no marker baits.
        if (Module.FindComponent<QuadrupleCrossingProtean>() is var qc && qc?.Origin is WPos origin)
        {
            var cardinal = a switch
            {
                PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.R1 => 0f,
                PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 => 90f,
                PartyRolesConfig.Assignment.H2 or PartyRolesConfig.Assignment.R2 => -90f,
                _ => 180f
            };
            var baits = support == (_supportsBaitFirst == qc.NumCasts % 8 < 4);
            var dir = qc.Facing + (baits ? cardinal : cardinal + 45f).Degrees();
            return origin + (baits ? 3f : 10f) * dir.ToDirection();
        }

        // Splintering Nails: looking at the boss from its front, tanks left, DPS middle, healers right.
        if (_nails)
        {
            var off = a switch
            {
                PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT => -55f,
                PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2 => 55f,
                _ => 0f
            };
            return boss.Position + 6f * (_nailsFacing + off.Degrees()).ToDirection();
        }

        // Tempestuous Tear: MT group (MT H1 D1 D3) in front of the boss, ST group behind; the cleave AOE picks the side.
        if (_tear)
        {
            var front = a is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1;
            return boss.Position + 6f * (front ? _tearFacing : _tearFacing + 180f.Degrees()).ToDirection();
        }

        // Raining Cats: MT NW and ST NE take tethers 1-3, D1/D2 take the 4th (they swap with their tank);
        // H1 D1 D2 stack inside the south target circle, H2 D3 D4 on the south edge.
        if (_cats)
        {
            var lastTether = _catsTethers >= 6;
            var nw = new WDir(-0.7071f, -0.7071f);
            var ne = new WDir(0.7071f, -0.7071f);
            var near = boss.Position + new WDir(0f, 2f);
            var far = new WPos(boss.Position.X, Arena.Center.Z + 17f);
            return a switch
            {
                PartyRolesConfig.Assignment.MT => lastTether ? near : boss.Position + 8f * nw,
                PartyRolesConfig.Assignment.OT => lastTether ? near : boss.Position + 8f * ne,
                PartyRolesConfig.Assignment.M1 => lastTether ? boss.Position + 8f * nw : near,
                PartyRolesConfig.Assignment.M2 => lastTether ? boss.Position + 8f * ne : near,
                PartyRolesConfig.Assignment.H1 => near,
                _ => far
            };
        }
        return null;
    }
}
