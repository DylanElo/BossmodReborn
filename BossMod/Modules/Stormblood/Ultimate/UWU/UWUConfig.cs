namespace BossMod.Stormblood.Ultimate.UWU;

[ConfigDisplay(Order = 0x210, Parent = typeof(StormbloodConfig))]
public sealed class UWUConfig() : ConfigNode()
{
    [PropertyDisplay("Titan gaols priorities (close < far)")]
    [GroupDetails(["0", "1", "2", "3", "4", "5", "6", "7"])]
    public GroupAssignmentUnique P3GaolPriorities = DefaultGaolPriorities();

    [PropertyDisplay("Show fixed location for Mistral Song 2")]
    public bool P1MistralSongFixedLocation = true;

    [PropertyDisplay("Mistral Song 2 location X")]
    [PropertySlider(80, 120, Speed = 0.1f)]
    public float P1MistralSongX = 107f;

    [PropertyDisplay("Mistral Song 2 location Z")]
    [PropertySlider(80, 120, Speed = 0.1f)]
    public float P1MistralSongZ = 107f;

    // D1 > D2 > ST > D3 > D4 > H1 > H2 (JP macro naming: D1/D2 melee, D3 ranged, D4 caster); MT last
    private static GroupAssignmentUnique DefaultGaolPriorities()
    {
        GroupAssignmentUnique r = new();
        r[PartyRolesConfig.Assignment.M1] = 0;
        r[PartyRolesConfig.Assignment.M2] = 1;
        r[PartyRolesConfig.Assignment.OT] = 2;
        r[PartyRolesConfig.Assignment.R1] = 3;
        r[PartyRolesConfig.Assignment.R2] = 4;
        r[PartyRolesConfig.Assignment.H1] = 5;
        r[PartyRolesConfig.Assignment.H2] = 6;
        r[PartyRolesConfig.Assignment.MT] = 7;
        return r;
    }
}
