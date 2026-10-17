namespace BossMod.Dawntrail.Ultimate.DMU;

sealed class UltimaRepeater(BossModule module) : Components.RaidwideCast(module, (uint)AID.UltimaRepeaterCast)
{
    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.UltimaRepeaterRaidwide)
        {
            ++NumCasts;
        }
    }
}

sealed class FellForces(DMU module) : Components.GenericBaitStack(module)
{
    public bool active = false;
    private bool setup = false;
    public int expectedCasts = 9;
    private readonly Actor boss = module.KefkaP5()!;

    public override void Update()
    {
        if (!active || setup)
        {
            return;
        }

        SetupBaits();
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.FellForces) or ((uint)AID.FellForces1) or ((uint)AID.FellForces2))
        {
            ++NumCasts;

            if (NumCasts == expectedCasts)
            {
                CurrentBaits.Clear();
            }
        }
    }

    private void SetupBaits()
    {
        var party = Raid.WithSlot(true, true, true);
        BitMask allowedTanks = default;
        BitMask allowedHealers = default;
        BitMask allowedDDs = default;

        var len = party.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var p = ref party[i];

            if (p.Item2.Role == Role.Tank)
            {
                allowedTanks.Set(p.Item1);
            }
            else if (p.Item2.Role == Role.Healer)
            {
                allowedHealers.Set(p.Item1);
            }
            else if (p.Item2.Role is Role.Melee or Role.Ranged)
            {
                allowedDDs.Set(p.Item1);
            }
        }

        var addedTank = false;
        var addedHealer = false;
        var addedDD = false;

        for (var i = 0; i < len; ++i)
        {
            ref var player = ref party[i];
            var p = player.Item2;

            if (p.IsDead)
            {
                continue;
            }

            if (p.Role == Role.Tank)
            {
                if (!addedTank)
                {
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(3f), forbidden: ~allowedTanks));
                    addedTank = true;
                }
            }
            else if (p.Role == Role.Healer)
            {
                if (!addedHealer)
                {
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(5f), forbidden: ~allowedHealers));
                    addedHealer = true;
                }
            }
            else if (p.Role is Role.Melee or Role.Ranged)
            {
                if (!addedDD)
                {
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(5f), forbidden: ~allowedDDs));
                    addedDD = true;
                }
            }
        }
        setup = true;
    }
}

sealed class ChaoticFlood(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ChaoticFloodAOE, new AOEShapeRect(40.0f, 5.0f))
{
    // These are stored here since many different strategies use different waymarks, but these should work for all
    private readonly WPos waymarkA = new(100.0f, 88.0f);
    private readonly WPos waymarkB = new(112.0f, 100.0f);
    private readonly WPos waymarkC = new(100.0f, 112.0f);
    private readonly WPos waymarkD = new(88.0f, 100.0f);

    private readonly List<AOEInstance> aoes = [];
    private readonly List<AOEInstance> aoesHints = []; // Used for when the aoes are first cast as hints
    private int aoesHintsDisplayed = 0;

    private readonly List<WPos> safeWaymarks = [];
    private bool? clockwise = null;
    private bool firstWaveResolved = false;
    private bool secondWaveResolved = false;
    private WDir currentDirection;
    private int castsResolved = 0;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (SafeSpot(slot, actor) is { } spot)
            hints.AddForbiddenZone(new SDInvertedCircle(spot, 1f));
    }

    public WPos? SafeSpot(int slot, Actor actor)
    {
        if (!Service.Config.Get<DMUConfig>().YarnPirenStrategy || !secondWaveResolved || clockwise == null || NumCasts >= 8)
            return null;
        var hazards = ActiveAOEs(slot, actor).ToArray().Select(a => a.Shape.Distance(a.Origin, a.Rotation)).ToArray();
        if (hazards.Length == 0)
            return null;
        // The small central marker indicates direction; find a full destination circle clear of the observed wave.
        for (var radius = 2f; radius <= 12f; radius += 0.5f)
        {
            var spot = Module.Center + currentDirection * radius;
            if (hazards.All(h => h.Distance(spot) > 1f))
                return spot;
        }
        return null;
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFloodAOEDisplay)
        {
            aoes.Add(new(new AOEShapeRect(20.0f, 5.0f, 20.0f), caster.Position, caster.Rotation, actorID: caster.InstanceID));
            aoesHints.Add(new(new AOEShapeRect(20.0f, 5.0f, 20.0f), caster.Position, caster.Rotation, color: Colors.Enemy, actorID: caster.InstanceID));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFloodAOEDisplay)
        {
            aoesHintsDisplayed++;
            aoesHints.RemoveAt(0);
        }

        if (spell.Action.ID == (uint)AID.ChaoticFloodAOE)
        {
            if (++castsResolved == 2)
            {
                castsResolved = 0;
                if (clockwise != null)
                {
                    currentDirection = clockwise.Value ? new WDir(-currentDirection.Z, currentDirection.X) : new WDir(currentDirection.Z, -currentDirection.X);
                }
            }

            NumCasts++;
            aoes.RemoveAt(0);
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (!secondWaveResolved || clockwise == null || NumCasts == 8)
        {
            return;
        }

        if (!Service.Config.Get<DMUConfig>().YarnPirenStrategy)
            Arena.ZoneCircleOutline(Module.Center + currentDirection * 2.0f, 1.0f, Colors.Safe, 2.0f);
        else if (SafeSpot(pcSlot, pc) is { } spot)
            Arena.ZoneCircleOutline(spot, 1f, Colors.Safe, 2f);

        if (NumCasts <= 4)
        {
            var nextDirection = clockwise.Value ? new WDir(-currentDirection.Z, currentDirection.X) : new WDir(currentDirection.Z, -currentDirection.X);
            Arena.ZoneCircleOutline(Module.Center + nextDirection * 2.0f, 1.0f, Colors.Danger, 2.0f);
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        if (aoesHintsDisplayed < 8)
        {
            return CollectionsMarshal.AsSpan(aoesHints.Take(2).ToList());
        }

        return CollectionsMarshal.AsSpan(aoes.Take(4).ToList());
    }

    public override void Update()
    {
        if (!firstWaveResolved && aoes.Count >= 2)
        {
            List<WPos> waymarks = [waymarkA, waymarkB, waymarkC, waymarkD];
            var aoesFirstWave = aoes.Take(2).ToList();

            foreach (var waymark in waymarks)
            {
                if (!aoesFirstWave.Any(aoe => aoe.Check(waymark)))
                {
                    safeWaymarks.Add(waymark);
                }
            }

            firstWaveResolved = true;
        }

        if (firstWaveResolved && !secondWaveResolved && aoes.Count >= 4)
        {
            var aoesSecondWave = aoes.Skip(2).Take(2).ToList();
            foreach (var waymark in safeWaymarks)
            {
                if (!aoesSecondWave.Any(aoe => aoe.Check(waymark)))
                {
                    var otherWaymark = safeWaymarks.First(point => point != waymark) - Module.Center;
                    clockwise = otherWaymark.X * (waymark - Module.Center).Z - otherWaymark.Z * (waymark - Module.Center).X > 0;
                    currentDirection = (waymark - Module.Center).Normalized();
                    break;
                }
            }
            secondWaveResolved = true;
        }
    }
}

sealed class ChaoticFloodStack(BossModule module) : Components.GenericStackSpread(module)
{
    public int NumCasts = 0;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        var firstZone = hints.ForbiddenZones.Count;
        base.AddAIHints(slot, actor, assignment, hints);
        if (Module.FindComponent<ChaoticFlood>()?.SafeSpot(slot, actor) != null)
            hints.ForbiddenZones.RemoveRange(firstZone, hints.ForbiddenZones.Count - firstZone);
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFlood)
        {
            var target = WorldState.Actors.Find(caster.TargetID);
            if (target == null)
            {
                return;
            }
            Stacks.Add(new(target, 6.0f, 8, 8));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFloodStack)
        {
            NumCasts++;

            if (NumCasts == 4)
            {
                Stacks.Clear();
            }
        }
    }
}

sealed class MaddeningOrchestra(DMU module) : Components.GenericBaitAway(module, centerAtTarget: true)
{
    private bool active = false;
    private readonly DateTime[] magicVulnerability = new DateTime[PartyState.MaxPartySize];
    private bool firstWave = false;
    private readonly Actor boss = module.KefkaP5()!;

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID == (uint)SID.MagicVulnerabilityUp)
        {
            var slot = Raid.FindSlot(actor.InstanceID);
            if (slot >= 0)
            {
                magicVulnerability[slot] = status.ExpireAt;
            }
        }
    }

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.MaddeningOrchestra)
        {
            active = true;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.Holy) or ((uint)AID.Flare))
        {
            ++NumCasts;

            if (NumCasts >= 5)
            {
                firstWave = true;
            }
            if (NumCasts >= 8)
            {
                active = false;
                CurrentBaits.Clear();
            }
        }
    }

    public override void Update()
    {
        CurrentBaits.Clear();

        if (!active)
        {
            return;
        }

        var targets = Raid.WithoutSlot(true, true, true).SortedByRange(boss.Position).ToList();
        if (!firstWave)
        {
            foreach (var player in targets)
            { // Tanks & rest of party are hit with different spells, but same size AOEs
                CurrentBaits.Add(new(boss.Position, player, new AOEShapeCircle(5.0f)));
            }

            return;
        }

        targets = [.. targets.Take(3)];
        BitMask forbiddenPlayers = Raid.WithSlot(true, true, true).Where(p => magicVulnerability[p.Item1] > WorldState.CurrentTime || p.Item2.Role == Role.Tank).Mask();
        foreach (var target in targets)
        {
            CurrentBaits.Add(new(boss.Position, target, new AOEShapeCircle(5.0f), forbidden: forbiddenPlayers));
        }

        ForbiddenPlayers = forbiddenPlayers;
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);

        if (!ForbiddenPlayers[slot] && firstWave)
        {
            if (IsBaitTarget(actor))
            {
                hints.Add("Bait!", false);
            }
            else
            {
                hints.Add("Bait!");
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        if (!active || NumCasts >= 8 || !Service.Config.Get<DMUConfig>().YarnPirenStrategy)
            return;
        // Yarn diagrams 224-225: fixed role scatter, then the three unhit players bait closer.
        WDir offset = assignment switch
        {
            PartyRolesConfig.Assignment.MT => new(-4, -10),
            PartyRolesConfig.Assignment.OT => new(4, -10),
            PartyRolesConfig.Assignment.H1 => new(-10, 4),
            PartyRolesConfig.Assignment.H2 => new(10, 4),
            PartyRolesConfig.Assignment.M1 => new(-4, 10),
            PartyRolesConfig.Assignment.M2 => new(4, 10),
            PartyRolesConfig.Assignment.R1 => new(-10, -4),
            PartyRolesConfig.Assignment.R2 => new(10, -4),
            _ => default
        };
        if (offset == default)
            return;
        if (firstWave)
        {
            // Do not guess the random first-wave membership while status updates are incomplete.
            var eligible = 0;
            foreach (var (memberSlot, member) in Raid.WithSlot(true, true, true))
                if (member.Role != Role.Tank && magicVulnerability[memberSlot] <= WorldState.CurrentTime)
                    ++eligible;
            if (eligible != 3)
                return;
            offset = actor.Role == Role.Tank ? new(0, -10)
                : offset / offset.Length() * (magicVulnerability[slot] > WorldState.CurrentTime ? 16f : 8f);
        }
        hints.AddForbiddenZone(new SDInvertedCircle(Module.Center + offset, 0.6f));
    }
}

sealed class ChaoticFlareTB(DMU module) : Components.GenericBaitStack(module, (uint)AID.ChaoticFlareTB)
{
    public bool active = false;
    private readonly Actor boss = module.KefkaP5()!;

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ChaoticFlareTB)
        {
            NumCasts++;
            active = false;
        }
    }

    public override void Update()
    {
        if (!active)
        {
            return;
        }

        CurrentBaits.Clear();

        var party = Raid.WithSlot(true, true, true);
        BitMask allowedTanks = default;
        var len = party.Length;
        for (var i = 0; i < len; ++i)
        {
            ref var p = ref party[i];

            if (p.Item2.Role == Role.Tank)
            {
                allowedTanks.Set(p.Item1);
            }
        }

        var addedTank = false;

        for (var i = 0; i < len; ++i)
        {
            ref var player = ref party[i];
            var p = player.Item2;

            if (p.IsDead)
            {
                continue;
            }

            if (p.Role == Role.Tank)
            {
                if (!addedTank)
                {
                    CurrentBaits.Add(new(p, boss, new AOEShapeCircle(5.0f), forbidden: ~allowedTanks));
                    addedTank = true;
                }
            }
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!active)
            return;
        base.AddAIHints(slot, actor, assignment, hints);
        if (Service.Config.Get<DMUConfig>().YarnPirenStrategy && assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT)
            hints.AddForbiddenZone(new SDInvertedCircle(Module.Center + new WDir(0, -10), 0.6f));
    }
}

sealed class ChaoticHolyFlareDiffusion(BossModule module) : Components.GenericBaitAway(module, centerAtTarget: true, onlyShowOutlines: true)
{
    private static bool InvulnerableUntil(Actor actor, DateTime activation)
        => actor.Statuses.Any(s => s.ID is 82 or 409 or 810 or 811 or 1836 or 3255 && s.ExpireAt >= activation);

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!Service.Config.Get<DMUConfig>().YarnPirenStrategy)
        {
            base.AddAIHints(slot, actor, assignment, hints);
            return;
        }
        var flare = CurrentBaits.FirstOrDefault(b => b.Shape is AOEShapeCircle { Radius: 25f });
        var holy = CurrentBaits.FirstOrDefault(b => b.Shape is AOEShapeCircle { Radius: 6f });
        var hasPair = flare.Target != null && holy.Target != null;
        var overlapActivation = !hasPair || holy.Activation >= flare.Activation ? holy.Activation : flare.Activation;
        var protectedHoly = hasPair && actor == holy.Target && InvulnerableUntil(actor, overlapActivation);
        foreach (var bait in CurrentBaits)
        {
            if (bait.Target == actor || protectedHoly && bait.Target == flare.Target)
                continue;
            hints.AddForbiddenZone(bait.Shape, bait.Target.Position, bait.Rotation, bait.Activation);
        }
        if (!hasPair || assignment == PartyRolesConfig.Assignment.Unassigned)
            return;
        // Yarn diagram 226: status holders decide positions, including the tank swap.
        WDir? offset = actor == flare.Target ? new(0, -19)
            : actor == holy.Target ? protectedHoly ? new WDir(0, -10) : null
            : actor.Role != Role.Tank ? new WDir(0, 15) : null;
        if (offset is { } position)
            hints.AddForbiddenZone(new SDInvertedCircle(Module.Center + position, 0.6f));
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);
        var activation = CurrentBaits.Count > 0 ? CurrentBaits.Max(b => b.Activation) : default;
        if (Service.Config.Get<DMUConfig>().YarnPirenStrategy
            && CurrentBaits.Any(b => b.Target == actor && b.Shape is AOEShapeCircle { Radius: 6f } && !InvulnerableUntil(actor, activation)))
            hints.Add("Swap and use tank invulnerability for Holy overlapping Flare");
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        if (status.ID is var id && id == (uint)SID.SurpriseHoly)
        {
            CurrentBaits.RemoveAll(b => b.Target == actor);
            CurrentBaits.Add(new(actor, actor, new AOEShapeCircle(6.0f), status.ExpireAt));
        }
        else if (id == (uint)SID.SurpriseFlare)
        {
            CurrentBaits.RemoveAll(b => b.Target == actor);
            CurrentBaits.Add(new(actor, actor, new AOEShapeCircle(25.0f), status.ExpireAt));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.FlareDiffusion) or ((uint)AID.ChaoticHoly))
        {
            NumCasts++;

            if (NumCasts == 2)
            {
                CurrentBaits.Clear();
            }
        }
    }
}

// Towers sets ~7.5 seconds apart, towers explosions and tower glows happen at the same time, so remove the tower instance from the list
// No clue why I decided to make a list and everything and check the angle of towers to get an order, you can just get the WPos of towers since it always the same
// and just set the order like that instead, but oh well
sealed class Celestriad(BossModule module) : Components.GenericTowers(module)
{
    private readonly List<(Actor actor, Elements element)> allTowers = []; // Used for the initial setup of the component
    private enum Elements { NONE, ICE, FIRE, THUNDER, NO_ELEMENT }
    private readonly List<Elements> towerOrder = [Elements.NONE, Elements.NONE, Elements.NONE];
    // Starting debuff is the one we care about; it will disappear by the time we get to final tower set so we will just save it forever as it's only necessary for towers
    private readonly Elements[] debuffs = Utils.MakeArray(PartyState.MaxPartySize, Elements.NONE);

    public override void OnActorCreated(Actor actor)
    {
        if (actor.OID is var id && id == (uint)OID.IceTower)
        {
            allTowers.Add((actor, Elements.ICE));
        }
        else if (id == (uint)OID.FireTower)
        {
            allTowers.Add((actor, Elements.FIRE));
        }
        else if (id == (uint)OID.ThunderTower)
        {
            allTowers.Add((actor, Elements.THUNDER));
        }

        if (allTowers.Count == 9)
        {
            allTowers.Sort(delegate ((Actor actor, Elements element) a, (Actor actor, Elements element) b)
            {
                var north = Angle.AnglesCardinals[2];
                var xAngle = (a.actor.Position - Arena.Center).ToAngle();
                var yAngle = (b.actor.Position - Arena.Center).ToAngle();

                var xDeg = xAngle.AlmostEqual(north, 0.01f) ? 180f : xAngle.Deg;
                var yDeg = yAngle.AlmostEqual(north, 0.01f) ? 180f : yAngle.Deg;

                return xDeg < yDeg ? 1 : -1;
            });

            // We can use simple logic to solve the order, since elements spawn together in 3s
            towerOrder[0] = allTowers[0].element;
            towerOrder[1] = allTowers[3].element;
            towerOrder[2] = allTowers[6].element;
        }
    }

    public override void OnActorEAnim(Actor actor, uint state)
    {
        if (actor.OID is ((uint)OID.IceTower) or ((uint)OID.FireTower) or ((uint)OID.ThunderTower))
        {
            if (state == (uint)Animations.TowerGlow)
            {
                // A reused actor can glow for the next wave just before its previous explosion.
                if (Towers.Any(t => t.ActorID == actor.InstanceID && t.Activation > WorldState.FutureTime(1)))
                    return;
                // cactbot e79ea7f1: glow at 1455.7, damage at 1461.8; subsequent waves are six seconds apart.
                Towers.Add(new(actor.Position, 3.0f, 2, 2, activation: WorldState.FutureTime(6.1f), actorID: actor.InstanceID));

                if (Towers.Count >= 4)
                {
                    Towers.Sort(delegate (Tower a, Tower b)
                    {
                        var north = Angle.AnglesCardinals[2];
                        var xAngle = (a.Position - Arena.Center).ToAngle();
                        var yAngle = (b.Position - Arena.Center).ToAngle();

                        var xDeg = xAngle.AlmostEqual(north, 0.01f) ? 180f : xAngle.Deg;
                        var yDeg = yAngle.AlmostEqual(north, 0.01f) ? 180f : yAngle.Deg;

                        return xDeg < yDeg ? 1 : -1;
                    });
                }
            }

            if (state == (uint)Animations.TowerExplosion)
            {
                var index = Towers.FindIndex(t => t.ActorID == actor.InstanceID && t.Activation <= WorldState.FutureTime(1));
                if (index >= 0)
                {
                    Towers.RemoveAt(index);
                    ++NumCasts;
                }
            }
        }
    }

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0)
        {
            return;
        }

        if (debuffs[slot] != Elements.NONE)
        {
            return;
        }

        if (status.ID == (uint)SID.IceResistanceDownII)
        {
            debuffs[slot] = Elements.ICE;
        }

        if (status.ID == (uint)SID.FireResistanceDownII)
        {
            debuffs[slot] = Elements.FIRE;
        }

        if (status.ID == (uint)SID.LightningResistanceDownII)
        {
            debuffs[slot] = Elements.THUNDER;
        }

        // If all 6 players have debuffs, we know the final two are the non-debuff players
        if (debuffs.Count(d => d != Elements.NONE) == 6)
        {
            for (var i = 0; i < debuffs.Length; ++i)
            {
                if (debuffs[i] == Elements.NONE)
                {
                    debuffs[i] = Elements.NO_ELEMENT;
                }
            }
        }
    }

    // Used for setting the forbidden soakers to the towers:
    // Example: towerOrder is ICE, FIRE, THUNDER
    // So we know thunder will go to Ice -> Fire -> Thunder
    // So we know fire will go to Thunder -> Ice -> Fire
    // So we know ice will go to Fire -> Thunder -> Ice
    // So every wave of towers is just +1 to their element, and they will end back at their original element
    public override void Update()
    {
        if (!AssignmentsReady)
        {
            return;
        }

        // Assign each tower to an element index
        var towerElements = new Elements[Towers.Count];
        for (var i = 0; i < Towers.Count; ++i)
        {
            var index = allTowers.Find(t => t.actor.InstanceID == Towers[i].ActorID);
            towerElements[i] = index == default ? Elements.NONE : index.element;
        }

        // Find the dupe element
        var dupeElement = Elements.NONE;
        for (var i = 0; i < towerElements.Length && dupeElement == Elements.NONE; ++i)
        {
            for (var k = i + 1; k < towerElements.Length; ++k)
            {
                if (towerElements[i] == towerElements[k])
                {
                    dupeElement = towerElements[i];
                    break;
                }
            }
        }
        var dupeIndex = Array.LastIndexOf(towerElements, dupeElement);

        // Set up the forbidden players for each tower
        var set = NumCasts / 4;
        for (var i = 0; i < Towers.Count; ++i)
        {
            var tower = Towers[i];
            BitMask forbiddenPlayers = default;

            for (var k = 0; k < debuffs.Length; ++k)
            {
                var playerDebuff = debuffs[k];
                if (i == dupeIndex)
                {
                    if (playerDebuff != Elements.NO_ELEMENT)
                    {
                        forbiddenPlayers.Set(k);
                    }
                }
                else
                {
                    if (playerDebuff == Elements.NO_ELEMENT)
                    {
                        forbiddenPlayers.Set(k);
                        continue;
                    }

                    var targetElement = towerOrder[(towerOrder.IndexOf(playerDebuff) + set + 1) % 3];
                    if (targetElement != towerElements[i])
                    {
                        forbiddenPlayers.Set(k);
                    }
                }
            }
            tower.ForbiddenSoakers = forbiddenPlayers;
            Towers[i] = tower;
        }
    }

    // Glow/explosion events interleave between waves; do not route using a partial wave or guessed debuff.
    private bool AssignmentsReady => !towerOrder.Contains(Elements.NONE) && !debuffs.Contains(Elements.NONE) && Towers.Count == 4 && NumCasts % 4 == 0;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (AssignmentsReady)
            base.AddAIHints(slot, actor, assignment, hints);
    }
}

sealed class CatastrophicChoice(BossModule module) : Components.GenericAOEs(module)
{
    private readonly List<AOEInstance> aoes = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.CatastrophicChoiceQuake)
        {
            aoes.Add(new(new AOEShapeCircle(10.0f), caster.Position));
        }

        if (spell.Action.ID == (uint)AID.CatastrophicChoiceTornado)
        {
            aoes.Add(new(new AOEShapeDonut(10.0f, 40.0f), caster.Position));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.Quake) or ((uint)AID.Tornado))
        {
            NumCasts++;
            if (aoes.Count > 0)
            {
                aoes.RemoveAt(0);
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        return CollectionsMarshal.AsSpan(aoes);
    }
}

sealed class StrayApocalypse(BossModule module) : Components.Exaflare(module, 6f)
{
    private readonly List<AOEInstance> currentAOEs = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.StrayApocalypseExaFlareCast)
        {
            Lines.Add(new(caster.Position, 7.071f * spell.Rotation.ToDirection(), Module.CastFinishAt(spell), 0.5f, 7, 7));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is ((uint)AID.StrayApocalypseExaFlareCast) or
            ((uint)AID.StrayApocalypseExaFlare))
        {
            var count = Lines.Count;
            var pos = caster.Position;

            for (var i = 0; i < count; ++i)
            {
                var line = Lines[i];
                if (line.Next.AlmostEqual(pos, 1f))
                {
                    ++NumCasts;
                    AdvanceLine(line, pos);
                    if (line.ExplosionsLeft == 0)
                    {
                        Lines.RemoveAt(i);
                    }

                    return;
                }
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        currentAOEs.Clear();

        var exaFlares = Lines;
        for (var i = 0; i < exaFlares.Count; ++i)
        {
            var line = Lines[i];
            var pos = line.Next;
            var time = line.NextExplosion;

            for (var k = 0; k < line.ExplosionsLeft; ++k)
            {
                currentAOEs.Add(new(Shape, pos.Quantized(), line.Rotation, time, k == 0 ? ImminentColor : FutureColor));
                pos += line.Advance;
                time = time.AddSeconds(line.TimeToMove);
            }
        }

        return CollectionsMarshal.AsSpan(currentAOEs);
    }
}

sealed class StrayEntropy(BossModule module) : Components.UniformStackSpread(module, 0f, 5.0f)
{
    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.StrayEntropyCast)
        {
            foreach (var p in Raid.WithoutSlot())
            {
                Spreads.Add(new(p, 5.0f));
            }
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.StrayEntropySpread)
        {
            Spreads.Clear();
        }
    }
}

sealed class P5ForsakenRaidWide(BossModule module) : Components.RaidwideCasts(module, [(uint)AID.ForsakenCast, (uint)AID._Ability_Forsaken2]);

// TODO update to use MapEffects maybe?
sealed class P5ForsakenGround(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ForsakenGround, new AOEShapeCircle(8.0f))
{
    private readonly List<AOEInstance> aoes = [];

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        if (spell.Action.ID == (uint)AID.ForsakenGround)
        {
            aoes.Add(new(new AOEShapeCircle(8.0f), caster.Position, actorID: caster.InstanceID));
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID == (uint)AID.ForsakenGround)
        {
            NumCasts++;
            if (aoes.Count > 0)
            {
                var aoeIndex = aoes.FindIndex(a => a.ActorID == caster.InstanceID && a.Color != Colors.Danger);
                if (aoeIndex >= 0)
                {
                    var aoe = aoes[aoeIndex];
                    aoe.Color = Colors.Danger;
                    aoes[aoeIndex] = aoe;
                }
            }
        }
    }

    public override ReadOnlySpan<AOEInstance> ActiveAOEs(int slot, Actor actor)
    {
        return CollectionsMarshal.AsSpan(aoes);
    }
}

// The random bait target is unknown until the helper cast locks its ground location.
sealed class P5ForsakenBait(BossModule module) : Components.SimpleAOEs(module, (uint)AID.ForsakenAOEBait, 8f);

sealed class P5ForsakenStack(BossModule module) : Components.StackTogether(module, (uint)IconID.StackShare, 5.0f, 6.0f);

/*
    ForsakenAOEBait = 47928, // Helper->self, 5.0s cast, range 8 circle - Bait puddle
    ForsakenBonds = 47929, // Helper->players, no cast, range 6 circle - Stack
 */
