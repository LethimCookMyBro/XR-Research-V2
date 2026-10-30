#if RESEARCH_S1_A_INPUT
using System;
using System.Collections.Generic;
using System.Reflection;
using VRMaintenanceResearch.ResearchFlow;
using Oculus.Interaction;
namespace UnityEngine
{
    public class MonoBehaviour { public bool enabled = true; public T GetComponent<T>() where T : class => null; public static implicit operator bool(MonoBehaviour value) => value != null; }
    public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }
}
namespace Oculus.Interaction
{
    public enum PointerEventType { Select, Unselect, Cancel, Move }
    public readonly struct PointerEvent { public readonly int Identifier; public readonly PointerEventType Type; public PointerEvent(int id, PointerEventType type) { Identifier = id; Type = type; } }
    public class PointableElement : UnityEngine.MonoBehaviour
    {
        public bool AddNewPointsToFront, TransferOnSecondSelection;
        private readonly List<int> selecting = new List<int>();
        public int SelectingPointsCount => selecting.Count;
        public event Action<PointerEvent> WhenPointerEventRaised;
        public void Emit(int id, PointerEventType type)
        {
            if (type == PointerEventType.Select)
            {
                if (selecting.Count == 1 && TransferOnSecondSelection) Emit(selecting[0], PointerEventType.Cancel);
                if (AddNewPointsToFront) selecting.Insert(0, id); else selecting.Add(id);
            }
            else if (type == PointerEventType.Cancel || type == PointerEventType.Unselect) selecting.Remove(id);
            WhenPointerEventRaised?.Invoke(new PointerEvent(id, type));
        }
    }
}
namespace Oculus.Interaction.HandGrab { public sealed class HandGrabInteractable : UnityEngine.MonoBehaviour { } }
namespace VRMaintenanceResearch.AssetLibraryQA
{
    public sealed class QAPlug : UnityEngine.MonoBehaviour { public PointableElement grabbable = new PointableElement(); }
    public sealed class QALatch : UnityEngine.MonoBehaviour { public bool Closed; public bool Toggle() { Closed = !Closed; return true; } }
}
namespace VRMaintenanceResearch.ResearchFlow
{
    public sealed class ResearchS1FreeBody { public void Refresh() { } }
    public enum PartOperation { Held, Manipulate, Move, Turn, Insert, Remove, Open, Close, Connect, Disconnect }
    public enum PartBlockReason { None, Unspecified, ClipClosed, GuardNotSeated, Retained, Alignment, EndStop, Contact, CableLimit, OtherHeld, FrontGuardPresent, BladeCapPresent, BladePresent, RearRingPresent, LatchClosed, MountNotReady }
    public sealed class S1TaskAState { public int Sequence; }
    public sealed class ResearchSessionFlowController { }
    public sealed class ResearchS1TaskAMechanics : UnityEngine.MonoBehaviour
    {
        int sequence;
        public ResearchTaskPort port = new ResearchTaskPort();
        public Dictionary<int, string> sources = new Dictionary<int, string> { [1] = "ControllerRight", [2] = "ControllerLeft", [3] = "None" };
        public string InputSource(int pointer) => port.Allowed && sources.TryGetValue(pointer, out var source) ? source : "None";
        public S1TaskAState Capture() => new S1TaskAState { Sequence = ++sequence };
    }
    public sealed class ResearchTaskPort : UnityEngine.MonoBehaviour
    {
        public sealed class Attempt { public string InputSource; public bool Active = true; }
        public bool Allowed = true;
        public List<string> events = new List<string>();
        public Attempt Current;
        public PartOperation LastOperation;
        public PartBlockReason LastReason;
        public string ObservationSource;
        public int Observations;
        public bool UpdateInteraction(Attempt value, PartOperation operation, PartBlockReason reason)
        {
            if (!IsAttemptActive(value)) return false;
            LastOperation = operation; LastReason = reason; ObservationSource = value.InputSource; Observations++; return true;
        }
        public Attempt BeginAttempt(string id, string source) { Current = new Attempt { InputSource = source }; events.Add("GRAB:" + source); return Current; }
        public bool IsAttemptActive(Attempt value) => value != null && value.Active && Allowed;
        public bool UpdateAttempt(Attempt value, string action, object before, object after, bool blocked,
            PartOperation operation = PartOperation.Manipulate, PartBlockReason reason = PartBlockReason.Unspecified)
        {
            if (!IsAttemptActive(value)) throw new Exception("Closed attempt updated");
            UpdateInteraction(value, operation, blocked ? (reason == PartBlockReason.None ? PartBlockReason.Unspecified : reason) : PartBlockReason.None);
            events.Add(action + ":" + value.InputSource); return true;
        }
        public bool EndAttempt(Attempt value, bool system = false) { if (!value.Active) return false; value.Active = false; events.Add((system ? "CANCEL:" : "RELEASE:") + value.InputSource); return true; }
    }
}
internal static class ResearchS1TaskAInputChecks
{
    static int checks;
    static void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
    static void Call(ResearchS1TaskAInput input, string method) => typeof(ResearchS1TaskAInput).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(input, null);
    static ResearchS1TaskAInput Fixture(bool front = false)
    {
        var value = new ResearchS1TaskAInput { owner = new ResearchS1TaskAMechanics(), stableId = "S01", pointable = new PointableElement { AddNewPointsToFront = front }, interactable = new Oculus.Interaction.HandGrab.HandGrabInteractable() };
        Call(value, "Start"); return value;
    }
    static void TypedObservations()
    {
        var input = Fixture(); var port = input.owner.port;
        input.Observe(PartOperation.Turn, PartBlockReason.EndStop);
        Check(port.Observations == 0, "Observation before selection cannot create interaction.");
        input.pointable.Emit(1, PointerEventType.Select);
        int events = port.events.Count;
        input.Observe(PartOperation.Turn, PartBlockReason.EndStop);
        Check(port.LastOperation == PartOperation.Turn && port.LastReason == PartBlockReason.EndStop && port.ObservationSource == "ControllerRight", "Observe preserves typed operation, reason and live input source.");
        Check(port.events.Count == events, "Observe does not fabricate a logged assembly operation.");
        input.Motion("INSTALL", input.owner.Capture(), true, PartBlockReason.CableLimit);
        Check(port.LastOperation == PartOperation.Insert && port.LastReason == PartBlockReason.CableLimit, "Blocked install carries typed insert and cable reason.");
        input.Motion("INSTALL", input.owner.Capture(), false, PartBlockReason.CableLimit);
        Check(port.LastReason == PartBlockReason.None, "Accepted motion clears the old blocked reason.");
        foreach (var pair in new[] { ("REMOVE", PartOperation.Remove), ("CONNECT", PartOperation.Connect), ("DISCONNECT", PartOperation.Disconnect), ("FASTEN", PartOperation.Turn), ("ROTATE_WORKPIECE", PartOperation.Turn), ("MOVE", PartOperation.Move) })
        {
            input.Motion(pair.Item1, input.owner.Capture(), false);
            Check(port.LastOperation == pair.Item2, "Motion category maps to typed operation " + pair.Item1);
        }
        int observations = port.Observations; port.Allowed = false;
        input.Observe(PartOperation.Move, PartBlockReason.Contact);
        Check(port.Observations == observations, "Paused Observe cannot update stale interaction.");
        var latch = Fixture(); latch.latch = new VRMaintenanceResearch.AssetLibraryQA.QALatch();
        latch.pointable.Emit(1, PointerEventType.Select);
        Check(latch.owner.port.LastOperation == PartOperation.Close, "Latch selection reports close instead of generic turn.");
        latch.Motion("UNFASTEN", latch.owner.Capture(), false);
        Check(latch.owner.port.LastOperation == PartOperation.Open, "Latch opening reports typed open.");
    }

    static void Main()
    {
        foreach (bool front in new[] { false, true })
        {
            var input = Fixture(front); var events = input.owner.port.events;
            input.pointable.Emit(1, PointerEventType.Select);
            var first = input.owner.port.Current;
            Check(input.Allowed && events.Count == 1, "Real primary selection opens neutral effort.");
            Call(input, "LateUpdate");
            Check(events.Count == 1, "Idle held object cannot be assembly attempt.");
            input.pointable.Emit(2, PointerEventType.Select);
            Check(input.Allowed, "Second actual selection keeps an active primary.");
            Check(!first.Active && events.Count == 3, "SDK transfer cancels original primary and opens new token.");
            input.Motion("INSTALL", input.owner.Capture(), false);
            Check(events[events.Count - 1] == "INSTALL:ControllerLeft", "Motion attributed to actual new primary.");
            input.pointable.Emit(1, PointerEventType.Unselect);
            Check(input.Allowed && events.Count == 4, "Late old-primary release cannot close new effort.");
            input.pointable.Emit(2, PointerEventType.Unselect);
            Check(!input.Allowed, "Last release immediately disallows mutation before LateUpdate.");
            Call(input, "LateUpdate");
            Check(events[events.Count - 1] == "RELEASE:ControllerLeft", "Genuine final release not hidden.");
        }
        var gated = Fixture(); gated.pointable.Emit(1, PointerEventType.Select);
        gated.owner.port.Allowed = false; gated.Gate(false);
        gated.owner.port.Allowed = true; gated.Gate(true);
        gated.pointable.Emit(1, PointerEventType.Move);
        Check(!gated.Allowed && gated.owner.port.events.Count == 2, "Resume move without fresh select cannot revive cancelled effort.");
        gated.pointable.Emit(1, PointerEventType.Select);
        Check(gated.Allowed, "Fresh physical selection after pause works.");
        gated.owner.port.Current.Active = false;
        Check(!gated.Allowed, "Port membership independently gates stale token.");
        var unknown = Fixture(); unknown.pointable.Emit(1, PointerEventType.Select); unknown.pointable.Emit(3, PointerEventType.Select);
        unknown.pointable.Emit(1, PointerEventType.Unselect);
        Check(!unknown.Allowed, "Unproven promoted pointer cannot inherit trusted input.");
        var changed = Fixture(); changed.pointable.Emit(1, PointerEventType.Select); changed.owner.sources[1] = "ControllerLeft";
        Check(!changed.Allowed, "Changed source cannot inherit old effort.");
        var cancelled = Fixture(); cancelled.pointable.Emit(1, PointerEventType.Select); cancelled.pointable.Emit(1, PointerEventType.Cancel); Call(cancelled, "LateUpdate");
        Check(cancelled.owner.port.events[1] == "CANCEL:ControllerRight", "Actual cancellation retained.");
        TypedObservations();
        Console.WriteLine("S1_TASK_A_INPUT_SOURCE_FIXTURE_PASS checks=" + checks + "; actual_sdk_runtime=false");
    }
}
#endif
