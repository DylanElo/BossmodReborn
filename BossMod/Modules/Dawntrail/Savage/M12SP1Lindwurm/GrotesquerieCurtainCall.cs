namespace BossMod.Dawntrail.Savage.M12SLindwurm;

sealed class GrotesquerieCurtainCall(BossModule module) : Components.UniformStackSpread(module, default, 6f)
{
    private const float ChainGroupUpWindow = 4f;

    private readonly bool[] _hasBurstingGrotesquerie = new bool[8];
    private readonly bool[] _hasRottingFlesh = new bool[8];
    private readonly bool[] _hasBondsAlpha = new bool[8];
    private readonly bool[] _hasUnbreakableAlpha = new bool[8];
    private readonly DateTime[] _rottingFleshExpiry = new DateTime[8];
    private DateTime _chainActivationTime;

    public override void OnStatusGain(Actor actor, ref ActorStatus status)
    {
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0 || slot >= 8)
            return;

        if (status.ID == (uint)SID.BurstingGrotesquerie)
        {
            _hasBurstingGrotesquerie[slot] = true;
            AddSpread(actor, status.ExpireAt);
        }
        else if (status.ID == (uint)SID.RottingFlesh)
        {
            _hasRottingFlesh[slot] = true;
            _rottingFleshExpiry[slot] = status.ExpireAt;
        }
        else if (status.ID is (uint)SID.BondsOfFleshAlpha or (uint)SID.BondsOfFleshBeta)
        {
            _hasBondsAlpha[slot] = true;
            // Set chain activation time when first bond is detected
            if (_chainActivationTime == default)
                _chainActivationTime = status.ExpireAt;
        }
        else if (status.ID is (uint)SID.UnbreakableFlesh0 or (uint)SID.UnbreakableFlesh1)
        {
            _hasUnbreakableAlpha[slot] = true;
        }
    }

    public override void OnStatusLose(Actor actor, ref ActorStatus status)
    {
        var slot = Raid.FindSlot(actor.InstanceID);
        if (slot < 0 || slot >= 8)
            return;

        if (status.ID == (uint)SID.BurstingGrotesquerie)
        {
            _hasBurstingGrotesquerie[slot] = false;
            Spreads.RemoveAll(s => s.Target == actor);
        }
        else if (status.ID == (uint)SID.RottingFlesh)
        {
            _hasRottingFlesh[slot] = false;
            _rottingFleshExpiry[slot] = default;
        }
        else if (status.ID is (uint)SID.BondsOfFleshAlpha or (uint)SID.BondsOfFleshBeta)
        {
            _hasBondsAlpha[slot] = false;
            // Reset chain activation time when all bonds are gone
            var anyBondsRemaining = false;
            for (var i = 0; i < 8; ++i)
            {
                if (i != slot && _hasBondsAlpha[i])
                {
                    anyBondsRemaining = true;
                    break;
                }
            }
            if (!anyBondsRemaining)
                _chainActivationTime = default;
        }
        else if (status.ID is (uint)SID.UnbreakableFlesh0 or (uint)SID.UnbreakableFlesh1)
        {
            _hasUnbreakableAlpha[slot] = false;
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        base.DrawArenaForeground(pcSlot, pc);

        // Draw center grouping indicator before chains activate
        if (_chainActivationTime != default && !_hasUnbreakableAlpha[pcSlot])
        {
            var timeUntilChains = (_chainActivationTime - WorldState.CurrentTime).TotalSeconds;
            if (timeUntilChains > 0 && timeUntilChains <= ChainGroupUpWindow)
            {
                Arena.ZoneCircleOutline(Module.Center, 2f, Colors.Safe);
            }
        }

        if (_hasUnbreakableAlpha[pcSlot])
        {
            DrawChainBreakIndicator(pcSlot, pc);
        }
    }

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        base.AddAIHints(slot, actor, assignment, hints);
        // Regroup only after spreads/cleanses: chains remember their length when the debuff transforms.
        if (_hasBondsAlpha[slot] && !_hasBurstingGrotesquerie[slot] && !_hasRottingFlesh[slot]
            && _chainActivationTime > WorldState.CurrentTime && _chainActivationTime <= WorldState.FutureTime(ChainGroupUpWindow))
            hints.AddForbiddenZone(new SDInvertedCircle(Module.Center, 2f), _chainActivationTime);

        if (_hasUnbreakableAlpha[slot] && ChainDestination(assignment) is WPos destination)
            hints.GoalZones.Add(AIHints.GoalProximity(destination, 1f, 10f));
    }

    private WPos? ChainDestination(PartyRolesConfig.Assignment assignment)
    {
        // Idyllshire: TH west, DPS east, in separate rows; partners are random, so never guess a tether partner.
        var row = assignment switch
        {
            PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.M1 => 0,
            PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.M2 => 1,
            PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.R1 => 2,
            PartyRolesConfig.Assignment.H2 or PartyRolesConfig.Assignment.R2 => 3,
            _ => -1
        };
        return row >= 0 ? Module.Center + new WDir(assignment is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2 ? -18f : 18f, -12f + 8f * row) : null;
    }

    private void DrawChainBreakIndicator(int pcSlot, Actor pc)
    {
        var assignment = Service.Config.Get<PartyRolesConfig>()[Raid.Members[pcSlot].ContentId];
        if (ChainDestination(assignment) is WPos destination)
        {
            Arena.AddLine(pc.Position, destination, Colors.Safe);
            Arena.ZoneCircleOutline(destination, 1f, Colors.Safe);
        }
    }

    public override void AddHints(int slot, Actor actor, TextHints hints)
    {
        base.AddHints(slot, actor, hints);

        if (_hasBurstingGrotesquerie[slot])
            hints.Add("Spread and avoid cone!", false);
        else if (_hasRottingFlesh[slot])
            hints.Add("Spread and get hit by cone!", false);

        if (_hasUnbreakableAlpha[slot])
            hints.Add("Break chains!", false);
    }
}
