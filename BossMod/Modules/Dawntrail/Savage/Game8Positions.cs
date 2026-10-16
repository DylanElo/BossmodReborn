namespace BossMod.Dawntrail.Savage;

[ConfigDisplay(Order = 0x111, Parent = typeof(DawntrailConfig))]
public sealed class ArcadionGame8Config() : ConfigNode()
{
    [PropertyDisplay("M2S: Game8 strategy (AI positions)", tooltip: "Venom spread/pairs, poison drop TH NW / DPS SE. Requires correct party roles.")]
    public bool M2S = true;

    [PropertyDisplay("M3S: Game8 strategy (AI positions)", tooltip: "Bombarian Special spread / pair X, Lariat 8-way / 4-way, Infernal Spin attach spots, first knockback tower melee NW / ranged SE. Requires correct party roles.")]
    public bool M3S = true;

    [PropertyDisplay("M4S: Game8 strategy (AI positions)", tooltip: "Wicked Blaze MT group W / ST group E, Midnight Sabbath spread octagon, Sword Quiver pairs. Requires correct party roles.")]
    public bool M4S = true;
}

// Game8 macro positions, AI only: goal zones pull each role to its spot; mechanic AOEs stay forbidden zones, so they always win.
public abstract class Game8Positions(BossModule module, Func<ArcadionGame8Config, bool> enabled) : BossComponent(module)
{
    // BMR angles: S = 0, E = 90, N = 180, W = -90
    public const float N = 180f, NE = 135f, E = 90f, SE = 45f, S = 0f, SW = -45f, W = -90f, NW = -135f;

    private readonly ArcadionGame8Config _config = Service.Config.Get<ArcadionGame8Config>();

    protected abstract WPos? Destination(int slot, Actor actor, PartyRolesConfig.Assignment a);

    public static bool IsSupport(PartyRolesConfig.Assignment a)
        => a is PartyRolesConfig.Assignment.MT or PartyRolesConfig.Assignment.OT or PartyRolesConfig.Assignment.H1 or PartyRolesConfig.Assignment.H2;

    // 基本散開: D3 MT D4 / H1 ☆ H2 / D1 ST D2
    public static float BaseSpread(PartyRolesConfig.Assignment a) => a switch
    {
        PartyRolesConfig.Assignment.MT => N,
        PartyRolesConfig.Assignment.R1 => NW,
        PartyRolesConfig.Assignment.R2 => NE,
        PartyRolesConfig.Assignment.H1 => W,
        PartyRolesConfig.Assignment.H2 => E,
        PartyRolesConfig.Assignment.M1 => SW,
        PartyRolesConfig.Assignment.M2 => SE,
        _ => S
    };

    public static WPos At(WPos center, float deg, float radius) => center + radius * deg.Degrees().ToDirection();

    public static bool Active(Components.GenericAOEs? comp, int slot, Actor actor) => comp != null && comp.ActiveAOEs(slot, actor).Length != 0;

    public override void AddAIHints(int slot, Actor actor, PartyRolesConfig.Assignment assignment, AIHints hints)
    {
        if (!enabled(_config) || assignment == PartyRolesConfig.Assignment.Unassigned)
            return;
        if (Destination(slot, actor, assignment) is WPos dest)
        {
            hints.GoalZones.Add(AIHints.GoalProximity(dest, 15f, 1f));
            hints.GoalZones.Add(AIHints.GoalProximity(dest, 1.5f, 2f));
        }
    }

    public override void DrawArenaForeground(int pcSlot, Actor pc)
    {
        if (enabled(_config) && Service.Config.Get<PartyRolesConfig>()[Raid.Members[pcSlot].ContentId] is var a && a != PartyRolesConfig.Assignment.Unassigned
            && Destination(pcSlot, pc, a) is WPos dest)
            Arena.ZoneCircleOutline(dest, 1f, Colors.Safe);
    }
}
