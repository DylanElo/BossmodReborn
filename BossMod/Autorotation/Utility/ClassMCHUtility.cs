namespace BossMod.Autorotation;

public sealed class ClassMCHUtility(RotationModuleManager manager, Actor player) : RoleRangedUtility(manager, player)
{
    public enum Track { Tactician = SharedTrack.Count, Dismantle, Burst }
    public enum TactOption { None, Use87, Use87IfNotActive, Use88, Use88IfNotActive }
    public enum BurstOption { None, Window, WindowWithPotion }

    public static readonly ActionID IDLimitBreak3 = ActionID.MakeSpell(MCH.AID.SatelliteBeam);

    public static RotationModuleDefinition Definition()
    {
        var res = new RotationModuleDefinition("Utility: MCH", "Cooldown Planner support for Utility Actions.\nNOTE: This is NOT a rotation preset! All Utility modules are STRICTLY for cooldown-planning usage.", "Utility for planner", "Aimsucks", RotationModuleQuality.Excellent, BitMask.Build((int)Class.MCH), 100);
        DefineShared(res, IDLimitBreak3);

        res.Define(Track.Tactician).As<TactOption>("Tactician", "Tact", 400)
            .AddOption(TactOption.None, "Do not use automatically")
            .AddOption(TactOption.Use87, "Use Tactician (120s CD), regardless if equivalent ranged buff is already active", 120, 15, ActionTargets.Self, 56, 87)
            .AddOption(TactOption.Use87IfNotActive, "Use Tactician (120s CD), unless equivalent ranged buff is already active", 90, 15, ActionTargets.Self, 56, 87)
            .AddOption(TactOption.Use88, "Use Tactician (90s CD), regardless if equivalent ranged buff is already active", 90, 15, ActionTargets.Self, 88)
            .AddOption(TactOption.Use88IfNotActive, "Use Tactician (90s CD), unless equivalent ranged buff is already active", 90, 15, ActionTargets.Self, 88)
            .AddAssociatedActions(MCH.AID.Tactician);

        DefineSimpleConfig(res, Track.Dismantle, "Dismantle", "Dism", 500, MCH.AID.Dismantle, 10);

        // Marker only: read by Rotation Solver's "MCH Plan" rotation, never executed here.
        res.Define(Track.Burst).As<BurstOption>("Burst", "Burst", 300)
            .AddOption(BurstOption.None, "No burst window")
            .AddOption(BurstOption.Window, "Two-minute burst window (Rotation Solver MCH Plan)", 0, 20)
            .AddOption(BurstOption.WindowWithPotion, "Two-minute burst window with potion (Rotation Solver MCH Plan)", 0, 20);

        return res;
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        ExecuteShared(strategy, IDLimitBreak3, primaryTarget);
        var dismantle = strategy.Option(Track.Dismantle);
        if (dismantle.As<SimpleOption>() == SimpleOption.Use)
            PushPlannedUtility(dismantle, MCH.AID.Dismantle, ResolveTarget(dismantle.Value) ?? primaryTarget);

        // TODO: for 'if-not-active' strategy, add configurable min-time-left
        // TODO: combine 87/88 options
        var tact = strategy.Option(Track.Tactician);
        var wantTact = tact.As<TactOption>() switch
        {
            TactOption.Use87 or TactOption.Use88 => true,
            TactOption.Use87IfNotActive or TactOption.Use88IfNotActive => Player.FindStatus(BRD.SID.Troubadour) == null && Player.FindStatus(MCH.SID.Tactician) == null && Player.FindStatus(DNC.SID.ShieldSamba) == null,
            _ => false
        };
        if (wantTact)
            PushPlannedUtility(tact, MCH.AID.Tactician, Player);
    }

    private void PushPlannedUtility(in StrategyValues.OptionRef option, MCH.AID action, Actor? target)
    {
        // Only native plan entries have a finite window; presets and defaults retain normal execution.
        var planned = Manager.Preset == null && Manager.Planner?.Plan != null && float.IsFinite(option.Value.ExpireIn) && option.Value.ExpireIn < float.MaxValue;
        Hints.ActionsToExecute.Push(ActionID.MakeSpell(action), target, option.Priority(), option.Value.ExpireIn, plannedMchUtility: planned);
    }
}
