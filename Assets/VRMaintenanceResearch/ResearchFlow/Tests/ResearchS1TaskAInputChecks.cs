#if RESEARCH_S1_A_INPUT
using System;
using System.Collections.Generic;
using System.Reflection;
using VRMaintenanceResearch.ResearchFlow;
using Oculus.Interaction;
namespace UnityEngine
{
    public class MonoBehaviour { public bool enabled = true; public static implicit operator bool(MonoBehaviour value) => value != null; }
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
        public Attempt BeginAttempt(string id, string source) { Current = new Attempt { InputSource = source }; events.Add("GRAB:" + source); return Current; }
        public bool IsAttemptActive(Attempt value) => value != null && value.Active && Allowed;
        public bool UpdateAttempt(Attempt value, string action, object before, object after, bool blocked) { if (!IsAttemptActive(value)) throw new Exception("Closed attempt updated"); events.Add(action + ":" + value.InputSource); return true; }
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
        Console.WriteLine("S1_TASK_A_INPUT_SOURCE_FIXTURE_PASS checks=" + checks + "; actual_sdk_runtime=false");
    }
}
#endif
