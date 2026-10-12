namespace BossMod.Dawntrail.Ultimate.FRU;

// tethers & general assignments
sealed class P4DarklitDragonsong(BossModule module) : BossComponent(module)
{
    public BitMask Stacks;
    public BitMask TowerSoakers;
    public BitMask AssignS;
    public BitMask AssignE;
    private readonly List<(Actor from, Actor to)> _tethers = [];

    public override void AddGlobalHints(Actor actor, GlobalHints hints)
    {
        var southTower = TowerSoakers & AssignS;
        if (southTower.Any())
            hints.Add($"Water in {((southTower & Stacks).Any() ? "S" : "N")} tower");
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        foreach (var t in _tethers)
            Arena.AddLine(t.from.Position, t.to.Position, Colors.Safe); // TODO: min/max break distance
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.SpellInWaitingDarkWater)
        {
            Stacks[Raid.FindSlot(actor.InstanceID)] = true;
            if (Service.Config.Get<FRUConfig>().P4ManaStrategy)
                InitAssignments();
        }
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.LightRampantChains)
        {
            TowerSoakers[Raid.FindSlot(source.InstanceID)] = true;
            var target = WorldState.Actors.Find(tether.Target);
            if (target != null)
                _tethers.Add((source, target));
            if (_tethers.Count == 4 || Service.Config.Get<FRUConfig>().P4ManaStrategy)
                InitAssignments();
        }
    }

    private void InitAssignments()
    {
        if (Service.Config.Get<FRUConfig>().P4ManaStrategy)
        {
            InitManaAssignments();
            return;
        }
        Span<int> ccwOrderSlots = [-1, -1, -1, -1, -1, -1, -1, -1];
        int[] playerPrios = [-1, -1, -1, -1, -1, -1, -1, -1];
        foreach (var (slot, group) in Service.Config.Get<FRUConfig>().P4DarklitDragonsongAssignments.Resolve(Raid))
        {
            ccwOrderSlots[group] = slot;
            playerPrios[slot] = group;
        }
        if (ccwOrderSlots.Contains(-1))
            return; // assignments are not valid, bail

        // find the anchor (tethered player with lowest prio), players tethered to him on both sides will take S tower
        var anchorSlot = TowerSoakers.SetBits().MinBy(i => playerPrios[i]);
        var anchorPlayer = Raid[anchorSlot];
        foreach (var t in _tethers)
        {
            if (t.from == anchorPlayer)
                AssignS[Raid.FindSlot(t.to.InstanceID)] = true;
            else if (t.to == anchorPlayer)
                AssignS[Raid.FindSlot(t.from.InstanceID)] = true;
        }

        // E/W assignments for tower soakers
        AssignE.Set(SelectEastTowerSoaker(TowerSoakers & AssignS, playerPrios));
        AssignE.Set(SelectEastTowerSoaker(TowerSoakers & ~AssignS, playerPrios));

        // assignments for baiters in prio order
        var numAssigned = 0;
        foreach (var slot in ccwOrderSlots)
        {
            if (!TowerSoakers[slot])
            {
                // first and last go N, last two go E
                AssignS[slot] = numAssigned is 1 or 2;
                AssignE[slot] = numAssigned >= 2;
                ++numAssigned;
            }
        }

        // finally, if both stacks are on the same N/S side, bait needs to swap with other bait on same E/W side
        if ((AssignS & Stacks).NumSetBits() != 1)
        {
            var flexStack = Stacks & ~TowerSoakers; // mask containing one set bit, corresponding to non-soaker stack - he will need to flex
            var flexE = AssignE[flexStack.LowestSetBit()];
            var flexMask = (AssignE ^ new BitMask(flexE ? 0u : 0xFF)) & ~TowerSoakers; // mask containing both flexers
            if (flexStack.NumSetBits() != 1 || flexMask.NumSetBits() != 2 || (flexStack & flexMask) != flexStack)
            {
                ReportError("Some error with swap logic, investigate");
                return;
            }
            AssignS ^= flexMask;
        }
    }

    private void InitManaAssignments()
    {
        AssignS = AssignE = default;
        var slots = Service.Config.Get<PartyRolesConfig>().SlotsPerAssignment(Raid);
        if (slots.Length != 8 || _tethers.Count != 4 || TowerSoakers.NumSetBits() != 4 || Stacks.NumSetBits() != 2 || (Stacks & TowerSoakers).NumSetBits() != 1)
            return;
        var tankSlots = slots.Take(2).Where(i => TowerSoakers[i]).ToArray();
        var healerSlots = slots.Skip(2).Take(2).Where(i => TowerSoakers[i]).ToArray();
        var dpsSlots = slots.Skip(4).Where(i => TowerSoakers[i]).ToArray();
        if (tankSlots.Length != 1 || healerSlots.Length != 1 || dpsSlots.Length != 2)
            return;
        // Validate the complete directed ring before publishing any assignment.
        var from = _tethers.Select(t => Raid.FindSlot(t.from.InstanceID)).ToArray();
        var to = _tethers.Select(t => Raid.FindSlot(t.to.InstanceID)).ToArray();
        if (from.Any(i => i < 0) || to.Any(i => i < 0) || from.Distinct().Count() != 4 || to.Distinct().Count() != 4 || !from.Order().SequenceEqual(to.Order()))
            return;
        var healer = healerSlots[0];
        var next = healer;
        var visited = new HashSet<int>();
        for (var i = 0; i < 4; ++i)
        {
            if (!visited.Add(next))
                return;
            next = to[Array.IndexOf(from, next)];
        }
        if (next != healer)
            return;
        var south = BitMask.Build(to[Array.IndexOf(from, healer)], from[Array.IndexOf(to, healer)]);
        var east = new BitMask();
        var opposite = (TowerSoakers & ~south).SetBits().Single(i => i != healer);
        east.Set(opposite); // healer NW; opposite tether NE
        // Box/hourglass: tank swaps with the opposite DPS's original horizontal spot.
        east.Set(opposite == tankSlots[0] || opposite == dpsSlots[0] ? dpsSlots[1] : tankSlots[0]);
        var baitHealer = slots.Skip(2).Take(2).Single(i => !TowerSoakers[i]);
        var baitTank = slots.Take(2).Single(i => !TowerSoakers[i]);
        var baitDps = slots.Skip(4).Where(i => !TowerSoakers[i]).ToArray();
        south.Set(baitDps[0]);
        south.Set(baitDps[1]);
        east.Set(baitTank); // H NW, T NE; remaining DPS SW/SE in horizontal priority
        east.Set(baitDps[1]);
        if ((south & Stacks).NumSetBits() != 1)
        {
            var water = (Stacks & ~TowerSoakers).LowestSetBit();
            var partner = new[] { baitHealer, baitTank, baitDps[0], baitDps[1] }.Where(i => east[i] == east[water]).ToArray();
            if (partner.Length != 2)
                return;
            foreach (var slot in partner)
                south.Toggle(slot);
        }
        AssignS = south;
        AssignE = east;
    }

    private int SelectEastTowerSoaker(BitMask soakers, ReadOnlySpan<int> prios)
    {
        if (soakers.NumSetBits() != 2)
            return -1;
        var s1 = soakers.LowestSetBit();
        var s2 = soakers.HighestSetBit();
        var p1 = prios[s1];
        var p2 = prios[s2];
        return p1 < 4 && p2 < 4 ? (p1 < p2 ? s1 : s2) // both 'western' (one is anchor) - lower (more cw) is east
            : p1 >= 4 && p2 >= 4 ? (p1 > p2 ? s1 : s2) // both 'eastern' (neither is anchor) - higher (more cw) is east
            : p1 < 4 ? s2 : s1; // whoever is more eastern
    }
}

sealed class P4DarklitDragonsongBrightHunger(BossModule module) : Components.GenericTowers(module, (uint)AID.BrightHunger)
{
    private readonly P4DarklitDragonsong? _darklit = module.FindComponent<P4DarklitDragonsong>();
    private int _numTethers;

    private const float TowerOffset = 8f;

    public override void Update()
    {
        // Water can arrive after the fourth tether, when these towers were created.
        if (Service.Config.Get<FRUConfig>().P4ManaStrategy && _darklit != null)
            foreach (ref var tower in CollectionsMarshal.AsSpan(Towers))
                tower.ForbiddenSoakers = _darklit.AssignS.None() ? new(0xFF) : new BitMask(0xFF) ^ (_darklit.TowerSoakers & (tower.Position.Z > Arena.Center.Z ? _darklit.AssignS : ~_darklit.AssignS));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_darklit == null || Towers.Count == 0 || _darklit.AssignS.None() || !_darklit.TowerSoakers[slot])
            return; // we only provide hints for soakers

        // stay on the far side of assigned tower, on the correct E/W side
        var towerPos = Arena.Center + new WDir(0, _darklit.AssignS[slot] ? +TowerOffset : -TowerOffset);
        hints.AddForbiddenZone(new SDInvertedCircle(towerPos, 3f), Towers[0].Activation);
        hints.AddForbiddenZone(new SDCircle(Arena.Center, TowerOffset), Towers[0].Activation);
        hints.AddForbiddenZone(new SDHalfPlane(Arena.Center, new(_darklit.AssignE[slot] ? +1 : -1, default)), Towers[0].Activation);
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.LightRampantChains && ++_numTethers == 4 && _darklit != null)
        {
            var allowedN = _darklit.TowerSoakers & ~_darklit.AssignS;
            var allowedS = _darklit.TowerSoakers & _darklit.AssignS;
            if (_darklit.AssignS.None())
                allowedN = allowedS = Service.Config.Get<FRUConfig>().P4ManaStrategy ? default : _darklit.TowerSoakers;

            var towerOffset = new WDir(default, TowerOffset);
            var act = WorldState.FutureTime(10.4d);
            Towers.Add(new(Arena.Center - towerOffset, 4f, 2, 2, new BitMask(0xFF) ^ allowedN, act));
            Towers.Add(new(Arena.Center + towerOffset, 4f, 2, 2, new BitMask(0xFF) ^ allowedS, act));
        }
    }
}

sealed class P4DarklitDragonsongPathOfLight(BossModule module) : Components.GenericBaitAway(module, (uint)AID.PathOfLightAOE)
{
    private readonly P4DarklitDragonsong? _darklit = module.FindComponent<P4DarklitDragonsong>();
    private Actor? _source;
    private DateTime _activation;

    private static readonly AOEShapeCone _shape = new(60f, 30f.Degrees());

    public override void Update()
    {
        CurrentBaits.Clear();
        if (_source != null && ForbiddenPlayers.Any())
            foreach (var p in Raid.WithoutSlot(false, true, true).SortedByRange(_source.Position).Take(4))
                CurrentBaits.Add(new(_source, p, _shape, _activation));
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

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (_darklit == null || _source == null || _darklit.AssignS.None() || ForbiddenPlayers[slot])
            return; // we only provide hints for baiters

        // do not clip either tower (it's visible at half-angle = asin(4/8) = 30) or each other
        var dir = (_darklit.AssignE[slot] ? +1 : -1) * (_darklit.AssignS[slot] ? 60f : 120f).Degrees();
        hints.AddForbiddenZone(new SDPrecisePosition(Arena.Center + 4f * dir.ToDirection(), new(default, 1f), Arena.Bounds.MapResolution, actor.Position, 0.1f), _activation);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.DarklitDragonsongUsurper)
        {
            _source = caster;
            _activation = Module.CastFinishAt(spell, 12f);
        }
    }

    public override void OnTethered(Actor source, in ActorTetherInfo tether)
    {
        if (tether.ID == (uint)TetherID.LightRampantChains)
            ForbiddenPlayers[Raid.FindSlot(source.InstanceID)] = true;
    }
}

sealed class P4DarklitDragonsongSpiritTaker(BossModule module) : SpiritTaker(module)
{
    //private readonly P4DarklitDragonsong? _darklit = module.FindComponent<P4DarklitDragonsong>();

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        //if (_darklit != null && _darklit.AssignS.Any() && Spreads.Count > 0 && Spreads[0].Activation > WorldState.FutureTime(1))
        //{
        //    // preposition
        //    hints.AddForbiddenZone(new SDInvertedCircle(Arena.Center + PrepositionOffset(_darklit, slot, actor), 1)), Spreads[0].Activation);
        //}
        //else
        {
            // just adjust and dodge...
            base.AddAIHints(slot, actor, assignment, hints);
        }
    }

    // TODO: this is very arbitrary...
    //private WDir PrepositionOffset(P4DarklitDragonsong darklit, int slot, Actor actor)
    //{
    //    if (darklit.TowerSoakers[slot])
    //    {
    //        // tower soakers go to fixed spots, skewed not to hit the fragment
    //        var dir = (darklit.AssignE[slot] ? 1 : -1) * (darklit.AssignS[slot] ? 25 : 135).Degrees();
    //        return 9 * dir.ToDirection();
    //    }
    //    else
    //    {
    //        // baiting healer goes far west, tank goes near west, northern dd goes center, southern dd goes near east
    //        var off = actor.Role switch
    //        {
    //            Role.Healer => -12,
    //            Role.Tank => -6,
    //            _ => darklit.AssignS[slot] ? 6 : 0
    //        };
    //        return new(off, 0);
    //    }
    //}
}

sealed class P4DarklitDragonsongDarkWater(BossModule module) : Components.UniformStackSpread(module, 6f, default, 4, includeDeadTargets: true)
{
    private readonly P4DarklitDragonsong? _assignments = module.FindComponent<P4DarklitDragonsong>();
    private bool _resolveImminent;

    public void Show()
    {
        _resolveImminent = true;
        Update();
    }

    public override void Update()
    {
        if (!_resolveImminent)
            return;
        if (_assignments != null && _assignments.AssignS.Any())
        {
            foreach (ref var s in Stacks.AsSpan())
            {
                var isSouth = _assignments.AssignS[Raid.FindSlot(s.Target.InstanceID)];
                s.ForbiddenPlayers = _assignments.AssignS ^ new BitMask(isSouth ? 0xFF : 0u);
            }
        }
        else if (Service.Config.Get<FRUConfig>().P4ManaStrategy)
            foreach (ref var s in CollectionsMarshal.AsSpan(Stacks))
                s.ForbiddenPlayers = new(0xFF);
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        if (_resolveImminent)
            base.AddHints(slot, actor, hints);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!_resolveImminent || _assignments == null || _assignments.AssignS.None())
            return;

        var stacks = CollectionsMarshal.AsSpan(Stacks);
        var len = stacks.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var s = ref stacks[i];
            if (!s.ForbiddenPlayers[slot])
            {
                if (s.Target == actor || s.Activation > WorldState.FutureTime(1.5d))
                {
                    // preposition
                    var off = 9 * (_assignments.AssignS[slot] ? 20f : 130f).Degrees().ToDirection();
                    var center = Arena.Center;
                    hints.AddForbiddenZone(new SDInvertedUnion([new SDCircle(center + off, 1f), new SDCircle(center + new WDir(-off.X, off.Z), 1f)]), s.Activation);
                }
                else
                {
                    // otherwise just stack tightly with our target, and avoid other
                    for (var j = 0; j < len; ++j)
                    {
                        ref var st = ref stacks[j];
                        hints.AddForbiddenZone(st.ForbiddenPlayers[slot] ? new SDCircle(st.Target.Position, st.Radius) : new SDInvertedCircle(st.Target.Position, 2f), st.Activation);
                    }
                }
            }
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.SpellInWaitingDarkWater)
            AddStack(actor, status.ExpireAt);
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.DarkWater)
            Stacks.Clear();
    }
}

sealed class P4SomberDance(BossModule module) : Components.GenericBaitAway(module, centerAtTarget: true)
{
    private readonly FRUConfig _config = Service.Config.Get<FRUConfig>();
    private Actor? _source;
    private DateTime _activation;

    private static readonly AOEShapeCircle _shape = new(8);

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);
        if (_config.P4ManaStrategy && Service.Config.Get<PartyRolesConfig>().SlotsPerAssignment(Raid).Length == 0)
            hints.Add("Assign all party roles for Mana Somber Dance!");
    }

    public override void Update()
    {
        CurrentBaits.Clear();
        if (_source == null)
            return;
        var targets = Raid.WithoutSlot(false, true, true);
        var target = NumCasts == 0 ? targets.Farthest(_source.Position) : targets.Closest(_source.Position);
        if (target != null)
            CurrentBaits.Add(new(_source, target, _shape, _activation));
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var baiter = _config.P4SomberDanceOTBait ? PartyRolesConfig.Assignment.OT : PartyRolesConfig.Assignment.MT;
        if (_config.P4ManaStrategy)
        {
            var mtSlot = Service.Config.Get<PartyRolesConfig>().SlotsPerAssignment(Raid);
            if (mtSlot.Length == 0)
                return;
            baiter = Raid[mtSlot[(int)PartyRolesConfig.Assignment.MT]]?.Class == Class.PLD ? PartyRolesConfig.Assignment.OT : PartyRolesConfig.Assignment.MT;
        }
        if (assignment == baiter)
        {
            // go far east/west
            var pos = _config.P4ManaStrategy && NumCasts > 0 && _source != null
                ? _source.Position
                : _config.P4ManaStrategy && _source != null ? FirstBaitPosition() : Arena.Center + new WDir(actor.PosRot.X > Arena.Center.X ? 19f : -19f, default);
            if (pos is { } destination)
                hints.AddForbiddenZone(new SDInvertedCircle(destination, 1f), _activation);
        }
        else
        {
            base.AddAIHints(slot, actor, assignment, hints);
            if (_config.P4ManaStrategy && NumCasts == 0 && _source != null && FirstBaitPosition() is { } baitPosition)
                hints.AddForbiddenZone(new SDInvertedCircle(_source.Position, MathF.Max(0f, MathF.Sqrt((baitPosition - _source.Position).LengthSq()) - 2f)), _activation);
            if (_config.P4ManaStrategy && NumCasts > 0 && _source != null)
                hints.AddForbiddenZone(new SDCircle(_source.Position, 9f), _activation);
        }
    }

    private WPos? FirstBaitPosition()
    {
        var fragments = Module.Enemies((uint)OID.FragmentOfFate);
        WPos? best = null;
        var bestDistance = -1f;
        // Choose the farther E/W edge, keeping the buster away from the north Fragment.
        foreach (var side in new[] { -1f, 1f })
        {
            var pos = Arena.Center + new WDir(19f * side, 0f);
            if (fragments.Any(f => !f.IsDeadOrDestroyed && (pos - f.Position).LengthSq() <= (10f + f.HitboxRadius) * (10f + f.HitboxRadius)))
                continue; // 8-yalm buster + hitbox + destination tolerance + movement margin
            var distance = (pos - (_source?.Position ?? Arena.Center)).LengthSq();
            if (distance > bestDistance)
            {
                best = pos;
                bestDistance = distance;
            }
        }
        return best;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.SomberDance)
        {
            ForbiddenPlayers = Raid.WithSlot(true, true, true).WhereActor(p => p.Role != Role.Tank).Mask();
            _source = caster;
            _activation = Module.CastFinishAt(spell, 0.4d);
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.SomberDanceAOE1 or (uint)AID.SomberDanceAOE2)
        {
            ++NumCasts;
            _activation = WorldState.FutureTime(3.2d);
            if (!_config.P4ManaStrategy)
                foreach (var t in spell.Targets)
                    ForbiddenPlayers.Set(Raid.FindSlot(t.ID));
        }
    }
}
