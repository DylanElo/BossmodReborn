namespace BossMod.Dawntrail.Ultimate.DMU;

// Used for displaying the casts after they have locked in
sealed class AllThingsEndingCasts(BossModule module) : Components.SimpleAOEGroups(module, [(uint)AID.AllThingsEnding, (uint)AID.AllThingsEnding1], new AOEShapeCone(100f, 90f.Degrees()));

// Used for displaying where the baits for the casts currently are before they lock in
// Baits are guessed by closest player to the clone
sealed class AllThingsEnding(BossModule module) : Components.GenericBaitAway(module, onlyShowOutlines: true)
{
    private enum BaitType { None, Far, Close }
    private BaitType currentBait = BaitType.None;
    private readonly List<Actor> clones = [with(4)]; // Also includes the boss since he will cast the same spell
    private readonly List<Actor> baiters = [with(4)]; // players currently baiting
    private WPos? lastKnownTowerMidPoint = null;
    public bool aoesLocked = true; // Used to prevent the aoes baits constantly showing
    private readonly AOEShapeCone cone = new(25f, 90f.Degrees());

    private readonly PathOfLight? towers = module.FindComponent<PathOfLight>();
    private readonly ForsakenShapes? shapes = module.FindComponent<ForsakenShapes>();
    private static readonly DMUConfig dmuConfig = Service.Config.Get<DMUConfig>();

    public override void OnCastStarted(Actor caster, ActorCastInfo spell)
    {
        switch (spell.Action.ID)
        {
            case (uint)AID.FuturesEndCast:
                if (dmuConfig.YarnPirenStrategy)
                    clones.Clear();
                currentBait = BaitType.Far;
                break;
            case (uint)AID.PastsEndCast:
                if (dmuConfig.YarnPirenStrategy)
                    clones.Clear();
                currentBait = BaitType.Close;
                break;
            case (uint)AID.AllThingsEnding:
            case (uint)AID.AllThingsEnding1:
                aoesLocked = true;
                break;
        }
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (spell.Action.ID is (uint)AID.PastsEndSpread or (uint)AID.PastsEndSpread1 or (uint)AID.FuturesEndSpread or (uint)AID.FuturesEndSpread1)
        {
            clones.Add(caster);
        }
    }

    public override void Update()
    {
        if (towers?.Towers.Count == 2)
            lastKnownTowerMidPoint = towers.Towers[0].Position + (towers.Towers[1].Position - towers.Towers[0].Position) * 0.5f;
        CurrentBaits.Clear();
        baiters.Clear();

        if (aoesLocked)
        {
            return;
        }

        var raid = Raid.WithoutSlot(false, true, true);
        var len = raid.Length;
        var countC = clones.Count;
        for (var i = 0; i < countC; ++i)
        {
            var clone = clones[i];
            Actor? baiter = null;
            var bestDistance = float.MaxValue;
            var clonePos = clone.Position;
            for (var j = 0; j < len; ++j)
            {
                var player = raid[j];
                if (baiters.Contains(player))
                {
                    continue;
                }

                var distance = (clonePos - player.Position).LengthSq();
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    baiter = player;
                }
            }

            if (baiter == null)
            {
                continue;
            }

            baiters.Add(baiter);

            var direction = clone.AngleTo(baiter);
            if (currentBait == BaitType.Close)
            {
                direction += 180f.Degrees();
            }

            CurrentBaits.Add(new(clonePos, baiter, cone, customRotation: direction));
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);

        if (aoesLocked)
        {
            return;
        }

        if (towers == null || shapes == null)
        {
            return;
        }

        if ((shapes.currentTowerSet & 1) == 0 && shapes.currentTowerSet != 8)
        {
            return;
        }

        // Final set displays safe spots differently compared to every other tower set
        if (shapes.currentTowerSet == 9)
        {
            if (dmuConfig.P2Forsaken is DMUConfig.P2ForsakenStrategy.Meow_Markerless or DMUConfig.P2ForsakenStrategy.Meow_DN_ZENITH_Markers)
            {
                var waymark = WorldState.Waymarks.GetFieldMark((int)Waymark.A);
                if (waymark is Vector3 way)
                {
                    Arena.ZoneCircleOutline(new WPos(way), 1.0f, Colors.Safe, 2.0f);
                }
                return;
            }

            if (dmuConfig.P2Forsaken == DMUConfig.P2ForsakenStrategy.Kroxy_Rinon_Melee_Flex)
            {
                if (lastKnownTowerMidPoint != null)
                {
                    BaitSafeSpot(lastKnownTowerMidPoint.Value);
                }
            }
            return;
        }

        if (towers.Towers.Count != 2)
        {
            return;
        }
        var towersSpan = CollectionsMarshal.AsSpan(towers.Towers);
        var posSW = towersSpan[0].Position;
        var posSE = towersSpan[1].Position;
        var midpoint = new WPos((posSW.X + posSE.X) * 0.5f, (posSW.Z + posSE.Z) * 0.5f);
        lastKnownTowerMidPoint = midpoint;

        if (shapes.currentTowerSet == 8)
        { // Just to collect the lastKnownTowerMidPoint otherwise it will be null
            return;
        }

        BaitSafeSpot(midpoint);
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        // Unsnapped kicks must be baited toward the waiting group; locked casts have their own AOE component.
        if (!dmuConfig.YarnPirenStrategy || aoesLocked)
            base.AddAIHints(slot, actor, assignment, hints);
        if (!dmuConfig.YarnPirenStrategy || aoesLocked || clones.Count == 0 || shapes == null || assignment == PartyRolesConfig.Assignment.Unassigned)
            return;
        WPos? destination = null;
        if (shapes.currentTowerSet == 9 && WorldState.Waymarks.GetFieldMark((int)Waymark.A) is Vector3 a)
            destination = new WPos(a);
        else if (shapes.currentTowerSet < 9 && (shapes.currentTowerSet & 1) != 0 && towers?.Towers.Count == 2 && lastKnownTowerMidPoint is WPos midpoint)
        {
            var south = (midpoint - Arena.Center).Normalized();
            destination = currentBait == BaitType.Close ? midpoint + south * 2 : midpoint - south * 13;
        }
        if (destination is WPos position)
            hints.AddForbiddenZone(new SDInvertedCircle(position, 1f), default);
    }

    private void BaitSafeSpot(WPos midPoint)
    {
        var newSouth = (midPoint - Arena.Center).Normalized();
        if (currentBait == BaitType.Close)
        {
            Arena.ZoneCircleOutline(midPoint + newSouth * 2.0f, 1.0f, Colors.Safe, 2.0f);
        }
        else if (currentBait == BaitType.Far)
        {
            Arena.ZoneCircleOutline(midPoint - newSouth * 13.0f, 1.0f, Colors.Safe, 2.0f);
        }
    }
}
