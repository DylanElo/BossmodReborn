namespace BossMod.Dawntrail.Savage.M09SVampFatale;

// draw hints based on hector guide? config file for different strats? let user handle it?
// if soaked 1st tower, hell awaits status lasts enough to prevent soaking 2nd
// need to set up party config roles to determine 1st set of towers
sealed class BloodyBondage(BossModule module) : Components.CastTowers(module, (uint)AID.BloodyBondage, 4f, 1, 1)
{
    private static readonly M09SVampFataleConfig _config = Service.Config.Get<M09SVampFataleConfig>();
    private static readonly PartyRolesConfig _partyConfig = Service.Config.Get<PartyRolesConfig>();
    private BitMask _soaked;
    private string[] TowerLabels => _config.Assignments == M09SVampFataleConfig.AssignmentStrategy.Game8 ? ["T", "M", "R", "H"] : ["T", "H", "M", "R"];

    // (towers[i].Position - Arena.Center).ToAngle().Deg, Util/Angle says S=0, W=-90, E=90, N=-/+180
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == WatchedAction)
        {
            Towers.Add(new(spell.LocXZ, Radius, MinSoakers, MaxSoakers, _soaked, Module.CastFinishAt(spell), caster.InstanceID));

            if (Towers.Count == 4)
            {
                Towers.Sort(delegate (Tower x, Tower y)
                {
                    var north = Angle.AnglesCardinals[2];
                    var xAngle = (x.Position - Arena.Center).ToAngle();
                    var yAngle = (y.Position - Arena.Center).ToAngle();

                    var xDeg = xAngle.AlmostEqual(north, 0.01f) ? 180f : xAngle.Deg;
                    var yDeg = yAngle.AlmostEqual(north, 0.01f) ? 180f : yAngle.Deg;

                    return xDeg < yDeg ? 1 : -1;
                });
            }
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.HellAwaits)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            _soaked.Set(slot);
        }
    }
    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (!_config.ShowTowerOrder)
        {
            base.DrawArenaForeground(pcSlot, pc);
            return;
        }

        if (Towers.Count != 4)
            return;

        var index = GetTowerIndex(pcSlot);
        var towers = CollectionsMarshal.AsSpan(Towers);
        var len = towers.Length;

        // couldn't get tower index (party not configured or index error)
        if (index == -1)
        {
            //base.DrawArenaForeground(pcSlot, pc);
            for (var i = 0; i < len; ++i)
            {
                var isInside = towers[i].IsInside(pc);
                var numInside = towers[i].NumInside(Module);

                if (isInside && numInside == 1)
                {
                    towers[i].Shape.Outline(Arena, towers[i].Position, towers[i].Rotation, Colors.Safe);
                }
                else if (isInside && numInside > 1)
                {
                    towers[i].Shape.Draw(Arena, towers[i].Position, towers[i].Rotation, default);
                }
                else if (numInside == 0)
                {
                    towers[i].Shape.Outline(Arena, towers[i].Position, towers[i].Rotation, default, 2f);
                    Arena.TextWorld(towers[i].Position, TowerLabels[i], Colors.Safe);
                }
            }
        }
        else
        {
            var actorParty = (NumCasts / 4 + 1) == GetLightParty(pcSlot);

            // not in light party that's doing towers
            if (!actorParty)
            {
                for (var i = 0; i < len; ++i)
                {
                    towers[i].Shape.Draw(Arena, towers[i].Position, towers[i].Rotation, default);
                }
                return;
            }

            ref readonly var mytower = ref towers[index];
            var isInside = mytower.IsInside(pc);
            mytower.Shape.Outline(Arena, mytower.Position, mytower.Rotation, isInside ? Colors.Safe : default, 2f);
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (!_config.ShowTowerOrder)
        {
            base.AddHints(slot, actor, hints);
            return;
        }

        if (Towers.Count != 4)
            return;

        var index = GetTowerIndex(slot);

        // couldn't get tower index (party not configured or index error)
        if (index == -1)
        {
            base.AddHints(slot, actor, hints);
            return;
        }
        else
        {
            var actorParty = (NumCasts / 4 + 1) == GetLightParty(slot);

            // not in light party that's doing towers
            if (!actorParty)
                return;

            base.AddHints(slot, actor, hints);
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!_config.EnableTowerOrder)
        {
            base.AddAIHints(slot, actor, assignment, hints);
            return;
        }

        if (Towers.Count != 4)
            return;

        var index = GetTowerIndex(slot);
        var towers = CollectionsMarshal.AsSpan(Towers);

        // couldn't get tower index (party not configured or index error)
        if (index == -1)
        {
            base.AddAIHints(slot, actor, assignment, hints);
            return;
        }
        else
        {
            var actorParty = (NumCasts / 4 + 1) == GetLightParty(slot);

            // not in light party that's doing towers
            if (!actorParty)
            {
                var len = towers.Length;
                for (var i = 0; i < len; ++i)
                {
                    hints.AddForbiddenZone(towers[i].Shape, towers[i].Position, default, towers[i].Activation);
                }
            }
            else
            {
                ref readonly var t = ref towers[index];
                if (_config.Assignments == M09SVampFataleConfig.AssignmentStrategy.Game8)
                {
                    // Arrive for the cast finish; retain melee uptime while there is enough travel time.
                    hints.AddForbiddenZone(new SDInvertedCircle(t.Position, Radius), t.Activation);
                    for (var i = 0; i < towers.Length; ++i)
                        if (i != index)
                            hints.AddForbiddenZone(towers[i].Shape, towers[i].Position, default, towers[i].Activation);
                }
                else
                    hints.GoalZones.Add(AIHints.GoalSingleTarget(t.Position, 3f, 9f));
            }
        }
    }

    private int GetTowerIndex(int pcSlot)
    {
        if (_partyConfig.SlotsPerAssignment(Raid).Length == 0)
            return -1;

        var assignment = _partyConfig[Raid.Members[pcSlot].ContentId];
        var index = assignment switch
        {
            PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT => 0,
            PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2 => 1,
            PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.M2 => 2,
            PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.R2 => 3,
            _ => -1
        };

        return _config.Assignments == M09SVampFataleConfig.AssignmentStrategy.Game8 ? index switch { 1 => 3, 2 => 1, 3 => 2, _ => index } : index;
    }

    private int GetLightParty(int pcSlot)
    {
        if (_partyConfig.SlotsPerAssignment(Raid).Length == 0)
            return 0;

        var assignment = _partyConfig[Raid.Members[pcSlot].ContentId];
        var lp = assignment switch
        {
            PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.H1 => 1,
            PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.M2 or PartyRolesConfig.Assignment.R2 or PartyRolesConfig.Assignment.H2 => 2,
            _ => 0
        };

        return lp;
    }
}
// TODO: only draw adds if sharing the same status; set priority based on status; any difference between 3 adds?
// draw circle around cell that player is in; if player isn't inside cell, draw circles around all cells to avoid
sealed class CharnelCells(BossModule module) : Components.AddsMulti(module, [(uint)OID.CharnelCell, (uint)OID.CharnelCell1, (uint)OID.CharnelCell2]);
sealed class BloodyBondageUndeadDeathmatch(BossModule module) : Components.CastTowers(module, (uint)AID.BloodyBondageUndeadDeathmatch, 6f, 4, 4)
{
    private static readonly M09SVampFataleConfig _config = Service.Config.Get<M09SVampFataleConfig>();
    private static readonly PartyRolesConfig _partyConfig = Service.Config.Get<PartyRolesConfig>();

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!_config.EnableDeathmatch)
        {
            base.AddAIHints(slot, actor, assignment, hints);
            return;
        }

        if (Towers.Count != 2)
            return;

        var lightparty = GetLightParty(slot);
        if (lightparty == 0)
            return;

        Tower[] lpTowers = new Tower[2];
        var towers = CollectionsMarshal.AsSpan(Towers);
        var len = towers.Length;

        for (var i = 0; i < len; ++i)
        {
            var angle = (towers[i].Position - Arena.Center).ToAngle();
            if (angle.AlmostEqual(Angle.AnglesCardinals[0], 0.01f) || angle.AlmostEqual(Angle.AnglesCardinals[2], 0.01f))
            {
                lpTowers[0] = towers[i];
            }
            else if (angle.AlmostEqual(Angle.AnglesCardinals[1], 0.01f) || angle.AlmostEqual(Angle.AnglesCardinals[3], 0.01f))
            {
                lpTowers[1] = towers[i];
            }
        }

        hints.GoalZones.Add(AIHints.GoalSingleTarget(lpTowers[lightparty - 1].Position, 5f, 9f));
    }

    private int GetLightParty(int pcSlot)
    {
        if (_partyConfig.SlotsPerAssignment(Raid).Length == 0)
            return 0;

        var assignment = _partyConfig[Raid.Members[pcSlot].ContentId];
        var lp = assignment switch
        {
            PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.M1 or PartyRolesConfig.Assignment.R1 or PartyRolesConfig.Assignment.H1 => 1,
            PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.M2 or PartyRolesConfig.Assignment.R2 or PartyRolesConfig.Assignment.H2 => 2,
            _ => 0
        };

        return lp;
    }
}
// Game8: use the widest cage gap for tanks, then usable gaps clockwise for DPS and healers.
abstract class UltrasonicBaits(BossModule module, AID action, bool spread, bool tanks) : Components.GenericBaitAway(module, (uint)action)
{
    private readonly List<WPos> _cells = [];
    private readonly HashSet<ulong> _participants = [];
    private DateTime _activation;
    private Actor? _source;
    private bool _groupResolved;
    private static readonly M09SVampFataleConfig _config = Service.Config.Get<M09SVampFataleConfig>();

    private static int Group(Actor actor) => actor.Role == Role.Tank ? 0 : actor.Role == Role.Healer ? 2 : 1;
    private bool Present(Actor actor) => !actor.IsDead && !actor.IsDestroyed && Raid.FindSlot(actor.InstanceID) >= 0;
    private bool Participant(Actor actor) => Present(actor) && _participants.Contains(actor.InstanceID);
    private bool SameGroup(Actor a, Actor b) => a == b || _groupResolved && Participant(a) && Participant(b) && (!spread || Group(a) == Group(b));

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID != (uint)(spread ? AID.UltrasonicSpreadCast : AID.UltrasonicAmpCast))
            return;
        CurrentBaits.Clear();
        _cells.Clear();
        _participants.Clear();
        _source = caster;
        _activation = Module.CastFinishAt(spell);
        foreach (var actor in Raid.WithoutSlot(false, true, true))
        {
            // The released party retains roughly half a second of Hell Awaits at the second cast.
            var status = actor.FindStatus((uint)SID.HellAwaits);
            if (status == null || status.Value.ExpireAt.AddSeconds(-1) < WorldState.CurrentTime)
                _participants.Add(actor.InstanceID);
        }
        var participants = Raid.WithoutSlot(false, true, true).Where(Participant).ToArray();
        _groupResolved = participants.Length == 4 && participants.Count(p => p.Role == Role.Tank) == 1
            && participants.Count(p => p.Role == Role.Healer) == 1 && participants.Count(p => p.Role is Role.Melee or Role.Ranged) == 2;
        // Once observed, role lanes remain fixed after a death; only surviving original participants may share/re-anchor a bait.
        foreach (var actor in Raid.WithoutSlot(false, true, true))
        {
            if (!Participant(actor) || (tanks ? actor.Role != Role.Tank : actor.Role == Role.Tank))
                continue;
            if (CurrentBaits.Any(b => !spread || Group(b.Target) == Group(actor)))
                continue;
            CurrentBaits.Add(new(caster, actor, new AOEShapeCone(40f, (tanks ? 50f : 22.5f).Degrees()), _activation));
        }
        ObserveCells();
    }

    private void ObserveCells()
    {
        if (_cells.Count == 4)
            return;
        _cells.Clear();
        foreach (var oid in new[] { OID.CharnelCell, OID.CharnelCell1, OID.CharnelCell2 })
            foreach (var cell in Module.Enemies((uint)oid))
                if (!cell.IsDead && !cell.IsDestroyed)
                    _cells.Add(cell.Position);
    }

    private Angle? Lane(int group)
    {
        if (_source == null || _cells.Count != 4)
            return null;
        var angles = _cells.Select(p => (p - _source.Position).ToAngle().Rad).OrderDescending().ToArray();
        var gaps = new float[4];
        var widest = 0;
        for (var i = 0; i < 4; ++i)
        {
            gaps[i] = angles[i] - angles[(i + 1) % 4] + (i == 3 ? 2f * MathF.PI : 0f);
            if (gaps[i] > gaps[widest])
                widest = i;
        }
        if (gaps[widest] < 104f.Degrees().Rad)
            return null;
        for (var offset = 0; offset < 4; ++offset)
        {
            var i = (widest + offset) % 4;
            // A 45-degree gap cannot safely hold a 45-degree cone next to a caged player.
            if (offset != 0 && gaps[i] < 49f.Degrees().Rad)
                continue;
            if (group-- == 0)
                return new Angle(angles[i] - gaps[i] * 0.5f);
        }
        return null;
    }

    private void RefreshBaits()
    {
        for (var i = CurrentBaits.Count - 1; i >= 0; --i)
        {
            var bait = CurrentBaits[i];
            if (Participant(bait.Target))
                continue;
            var replacement = Raid.WithoutSlot(false, true, true).FirstOrDefault(a => Participant(a) && Group(a) == Group(bait.Target));
            if (replacement == null)
                CurrentBaits.RemoveAt(i);
            else
            {
                bait.Target = replacement;
                CurrentBaits[i] = bait;
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_source == null || !Present(actor))
            return;
        RefreshBaits();
        ObserveCells();
        var mine = CurrentBaits.Any(b => SameGroup(actor, b.Target));
        foreach (var bait in CurrentBaits)
        {
            if (SameGroup(actor, bait.Target))
            {
                var halfAngle = ((AOEShapeCone)bait.Shape).HalfAngle;
                foreach (var other in Raid.WithoutSlot(false, true, true))
                    if (!SameGroup(actor, other) && (other.Position - _source.Position).LengthSq() > 1)
                        hints.AddForbiddenZone(new SDCone(_source.Position, 40f, (other.Position - _source.Position).ToAngle(), halfAngle), _activation);
                foreach (var cell in _cells)
                    hints.AddForbiddenZone(new SDCone(_source.Position, 40f, (cell - _source.Position).ToAngle(), halfAngle), _activation);
            }
            else
                hints.AddForbiddenZone(bait.Shape, bait.Source.Position, bait.Rotation, _activation);
        }
        if (!Participant(actor))
            return;
        foreach (var cell in _cells)
            hints.AddForbiddenZone(new SDCircle(cell, 4f), _activation);
        if (mine && _groupResolved && _config.EnableTowerOrder && _config.Assignments == M09SVampFataleConfig.AssignmentStrategy.Game8 && Lane(spread ? Group(actor) : 0) is { } lane)
        {
            hints.AddForbiddenZone(new SDInvertedCone(_source.Position, 40f, lane, 2f.Degrees()), _activation);
            hints.AddForbiddenZone(new SDCircle(_source.Position, 3f), _activation);
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (!Present(actor))
            return;
        RefreshBaits();
        foreach (var bait in CurrentBaits)
            if (SameGroup(actor, bait.Target))
            {
                if (Raid.WithoutSlot(false, true, true).Any(other => !SameGroup(actor, other) && bait.Shape.Check(other.Position, bait.Source.Position, bait.Rotation)))
                    hints.Add(BaitAwayHint);
            }
            else if (bait.Shape.Check(actor.Position, bait.Source.Position, bait.Rotation))
                hints.Add("GTFO from baited aoe!");
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID != WatchedAction)
            return;
        ++NumCasts;
        CurrentBaits.Clear();
        _source = null;
        _cells.Clear();
        _participants.Clear();
    }
}

sealed class UltrasonicSpreadTank(BossModule module) : UltrasonicBaits(module, AID.UltrasonicSpreadTank, true, true);
sealed class UltrasonicSpreadRest(BossModule module) : UltrasonicBaits(module, AID.UltrasonicSpreadRest, true, false);
sealed class UltrasonicAmp(BossModule module) : UltrasonicBaits(module, AID.UltrasonicAmp, false, true);
