using System.Numerics;
using System.Reflection;
using BossMod;

internal sealed class Fixture : IDisposable
{
    internal readonly WorldState World;
    internal readonly BossModule Module;
    internal readonly List<string> Errors = [];
    internal Actor Boss => Module.PrimaryActor;
    internal DateTime Now => World.CurrentTime;

    internal Fixture(string moduleName)
    {
        var info = BossModuleRegistry.RegisteredModules.Values.Single(x => x.ModuleType.Name == moduleName);
        World = new(TimeSpan.TicksPerSecond, "offline-encounter-decisions");
        World.Frame.Timestamp = new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc);
        var boss = AddActor(0x100, info.PrimaryActorOID, ActorType.Enemy, Class.None, new(100, 100));
        Module = info.ModuleFactory(World, boss);
        Module.Error.Subscribe((_, _, message) => Errors.Add(message));
    }

    internal Actor AddActor(ulong id, uint oid, ActorType type, Class job, WPos position)
    {
        World.Execute(new ActorState.OpCreate(id, oid, -1, 0, "test", 0, type, job, 100,
            new Vector4(position.X, 0, position.Z, 0), 1, new(100, 100, 0, 10000, 10000), true, type == ActorType.Player, 0, 0, 0));
        return World.Actors.Find(id)!;
    }

    internal Actor AddPlayer(int slot, Class job = Class.MCH, WPos? position = null)
    {
        var actor = AddActor((ulong)(0x1000 + slot), 0, ActorType.Player, job, position ?? new(100, 100));
        World.Execute(new PartyState.OpModify(slot, new((ulong)(slot + 1), actor.InstanceID, false)));
        return actor;
    }

    internal BossComponent Activate(string typeName)
    {
        var type = typeof(BossModule).Assembly.GetType(typeName, true)!;
        typeof(BossModule).GetMethod(nameof(BossModule.ActivateComponent))!.MakeGenericMethod(type).Invoke(Module, null);
        return Module.Components.Single(x => x.GetType() == type);
    }

    internal void EventCast(Actor caster, uint action, ulong target = 0)
        => World.Execute(new ActorState.OpCastEvent(caster.InstanceID, new(new(ActionType.Spell, action), target, 0, 1, default, 0, 0, default)));

    internal void StartCast(Actor caster, uint action, float duration, ulong target = 0, WPos? location = null)
    {
        var position = location ?? caster.Position;
        World.Execute(new ActorState.OpCastInfo(caster.InstanceID, new()
        {
            Action = new(ActionType.Spell, action), TotalTime = duration, TargetID = target,
            Location = new(position.X, caster.PosRot.Y, position.Z), Rotation = caster.Rotation
        }));
    }

    internal void FinishCast(Actor caster) => World.Execute(new ActorState.OpCastInfo(caster.InstanceID, null));
    internal void Targetable(Actor actor, bool value) => World.Execute(new ActorState.OpTargetable(actor.InstanceID, value));
    internal void Status(Actor actor, int index, ActorStatus value) => World.Execute(new ActorState.OpStatus(actor.InstanceID, index, value));

    internal void Advance(double seconds)
    {
        World.Frame.Timestamp = World.Frame.Timestamp.AddSeconds(seconds);
        Module.StateMachine.Update(Now);
    }

    internal void Tick(double seconds = 0)
    {
        World.Frame.Timestamp = World.Frame.Timestamp.AddSeconds(seconds);
        Module.Update();
    }

    internal StateMachine.State State(uint id) => States().Single(x => x.ID == id);
    internal IEnumerable<StateMachine.State> States()
    {
        HashSet<StateMachine.State> seen = [];
        var pending = new Stack<StateMachine.State>(Module.StateMachine.Phases.Select(x => x.InitialState));
        while (pending.TryPop(out var state))
            if (seen.Add(state))
            {
                yield return state;
                foreach (var next in state.NextStates ?? []) pending.Push(next);
            }
    }

    // Seek is test setup; all later transitions execute the production state callbacks and event subscriptions.
    internal void EnterState(int phase, uint id)
    {
        var sm = Module.StateMachine;
        sm.Reset();
        sm.Start(Now);
        if (phase != 0)
            typeof(StateMachine).GetMethod("TransitionToPhase", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sm, [phase]);
        if (sm.ActiveState?.ID != id)
            typeof(StateMachine).GetMethod("TransitionToState", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sm, [State(id)]);
    }

    internal AIHints Hints(BossComponent component, int slot, PartyRolesConfig.Assignment assignment = default)
    {
        var hints = new AIHints();
        component.AddAIHints(slot, World.Party[slot]!, assignment, hints);
        return hints;
    }

    internal AIHints ModuleHints(int slot, PartyRolesConfig.Assignment assignment = default)
    {
        var hints = new AIHints();
        Module.CalculateAIHints(slot, World.Party[slot]!, assignment, hints);
        return hints;
    }

    public void Dispose() => Module.Dispose();
}
