namespace BossMod.Dawntrail.Savage.M11STheTyrant;

// Resolve on observed effects, including helper casts; the randomized orders have identical timings.
sealed class TimelineEvents(BossModule module) : BossComponent(module)
{
    private AID[] _actions = [];
    private DateTime _acceptAfter;
    public bool Matched;

    public void Watch(AID[] actions, float expectedDelay)
    {
        _actions = actions;
        // Repeated helper effects from the previous volley cannot satisfy the next mechanic.
        _acceptAfter = WorldState.CurrentTime.AddSeconds(Math.Max(0f, expectedDelay - 1f));
        Matched = false;
    }

    public override void OnEventCast(Actor caster, ActorCastEvent spell)
    {
        if (WorldState.CurrentTime >= _acceptAfter && Array.IndexOf(_actions, (AID)spell.Action.ID) >= 0)
            Matched = true;
    }
}
