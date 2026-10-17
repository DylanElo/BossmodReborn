namespace BossMod.Autorotation;

public sealed class ClassBRDUtility(RotationModuleManager manager, Actor player) : RoleRangedUtility(manager, player)
{
    public enum Track { WardensPaean = SharedTrack.Count, Troubadour, NaturesMinne, Burst }
    public enum TroubOption { None, Use87, Use87IfNotActive, Use88, Use88IfNotActive }
    public enum BurstOption { None, Window, WindowWithPotion }

    public static readonly ActionID IDLimitBreak3 = ActionID.MakeSpell(BRD.AID.SagittariusArrow);

    public static RotationModuleDefinition Definition()
    {
        var res = new RotationModuleDefinition("Utility: BRD", "Cooldown Planner support for Utility Actions.\nNOTE: This is NOT a rotation preset! All Utility modules are STRICTLY for cooldown-planning usage.", "Utility for planner", "veyn", RotationModuleQuality.Excellent, BitMask.Build((int)Class.BRD), 100);
        DefineShared(res, IDLimitBreak3);

        DefineSimpleConfig(res, Track.WardensPaean, "WardensPaean", "Dispel", -10, BRD.AID.WardensPaean, 30);

        res.Define(Track.Troubadour).As<TroubOption>("Troubadour", "Troub", 500)
            .AddOption(TroubOption.None, "Do not use automatically")
            .AddOption(TroubOption.Use87, "Use Troubadour (120s CD), regardless if equivalent ranged buff is already active", 120, 15, ActionTargets.Self, 62, 87)
            .AddOption(TroubOption.Use87IfNotActive, "Use Troubadour (120s CD), unless equivalent ranged buff is already active", 90, 15, ActionTargets.Self, 62, 87)
            .AddOption(TroubOption.Use88, "Use Troubadour (90s CD), regardless if equivalent ranged buff is already active", 90, 15, ActionTargets.Self, 88)
            .AddOption(TroubOption.Use88IfNotActive, "Use Troubadour (90s CD), unless equivalent ranged buff is already active", 90, 15, ActionTargets.Self, 88)
            .AddAssociatedActions(BRD.AID.Troubadour);

        DefineSimpleConfig(res, Track.NaturesMinne, "NaturesMinne", "Minne", 400, BRD.AID.NaturesMinne, 15);

        // Marker only: read by Rotation Solver's "BRD Plan" rotation, never executed here.
        res.Define(Track.Burst).As<BurstOption>("Burst", "Burst", 300)
            .AddOption(BurstOption.None, "No burst window")
            .AddOption(BurstOption.Window, "Two-minute burst window (Rotation Solver BRD Plan)", 0, 20)
            .AddOption(BurstOption.WindowWithPotion, "Two-minute burst window with potion (Rotation Solver BRD Plan)", 0, 20);

        return res;
    }

    public override void Execute(StrategyValues strategy, Actor? primaryTarget, float estimatedAnimLockDelay, bool isMoving)
    {
        ExecuteShared(strategy, IDLimitBreak3, primaryTarget);
        var wardens = strategy.Option(Track.WardensPaean);
        if (wardens.As<SimpleOption>() == SimpleOption.Use)
            PushPlannedUtility(wardens, BRD.AID.WardensPaean, ResolveTarget(wardens.Value) ?? primaryTarget ?? Player);
        var minne = strategy.Option(Track.NaturesMinne);
        if (minne.As<SimpleOption>() == SimpleOption.Use)
            PushPlannedUtility(minne, BRD.AID.NaturesMinne, ResolveTarget(minne.Value) ?? Player);

        // TODO: for 'if-not-active' strategy, add configurable min-time-left
        // TODO: combine 87/88 options
        var troub = strategy.Option(Track.Troubadour);
        var wantTroub = troub.As<TroubOption>() switch
        {
            TroubOption.Use87 or TroubOption.Use88 => true,
            TroubOption.Use87IfNotActive or TroubOption.Use88IfNotActive => Player.FindStatus(BRD.SID.Troubadour) == null && Player.FindStatus(MCH.SID.Tactician) == null && Player.FindStatus(DNC.SID.ShieldSamba) == null,
            _ => false
        };
        if (wantTroub)
            PushPlannedUtility(troub, BRD.AID.Troubadour, Player);
    }

    private void PushPlannedUtility(in StrategyValues.OptionRef option, BRD.AID action, Actor? target)
    {
        // Share the existing RSR handoff; presets and defaults retain native execution.
        var planned = Manager.Preset == null && Manager.Planner?.Plan != null && float.IsFinite(option.Value.ExpireIn) && option.Value.ExpireIn < float.MaxValue;
        Hints.ActionsToExecute.Push(ActionID.MakeSpell(action), target, option.Priority(), option.Value.ExpireIn, plannedMchUtility: planned);
    }
}
