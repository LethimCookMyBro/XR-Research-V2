using System;
using System.Collections.Generic;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum S1InspectionCategory { Pass, AssemblyIncomplete, SpecificationMismatch, PreconditionNotReady, SymptomPresent, SystemError }
    public enum S1Placement { NotInstalled, Partial, Seated }
    public enum S1ThreadState { Absent, EngagedLoose, Tight }
    public enum S1RamType { Unknown, Ddr4, Ddr5 }
    public enum S1RamSlot { None, A1, A2, B1, B2 }
    public enum S1SsdInterface { Unknown, Sata, Nvme }
    public enum S1SsdMount { None, RearTray1, RearTray2 }
    public enum S1GpuSlot { None, PrimaryCpu, SecondaryFullLength }
    public enum S1Connector { None, MotherboardSata, PsuSata, PsuPcie8, SsdData, SsdPower, GpuPcie8, Eps8 }

    [Serializable]
    public sealed class S1InspectionResult
    {
        public S1InspectionCategory Category;
        public string ReasonCode;
        // Readiness permits the simulated test; it is not evidence that the test ran.
        public bool CanRunDiagnostic;
        public bool Passed => Category == S1InspectionCategory.Pass;

        public S1InspectionResult(S1InspectionCategory category, string reasonCode, bool canRunDiagnostic = false)
        {
            Category = category;
            ReasonCode = reasonCode;
            CanRunDiagnostic = canRunDiagnostic;
        }
    }

    [Serializable]
    public sealed class S1TaskABaseState
    {
        public string caseId, motherboardId, cpuId, coolerId, psuId;
        public bool caseIntact, motherboardSecured, cpuSeated, coolerSecured, psuSecured;
        public bool atx24Connected, epsConnected, cpuFanConnected, frontPanelConnected, powerIsolated;
    }

    [Serializable]
    public sealed class S1RamState
    {
        public string id, specification;
        public S1RamType memoryType;
        public double capacityGb;
        public S1Placement placement;
        public S1RamSlot slot;
        public bool retained;
    }

    [Serializable]
    public sealed class S1SsdState
    {
        public string id;
        public S1SsdInterface storageInterface;
        public double capacityGb, ratedEnduranceTbw;
        public S1Placement placement;
        public S1SsdMount mount;
        public S1ThreadState trayFastener;
    }

    [Serializable]
    public sealed class S1GpuState
    {
        public string id;
        public double vramGb, declaredBoardPowerW;
        public S1Placement placement;
        public S1GpuSlot slot;
        public bool retained;
        public S1ThreadState bracketFastener;
        public S1Connector auxiliaryPower;
    }

    [Serializable]
    public sealed class S1CableState
    {
        public string id, sourcePartId, targetPartId;
        public S1Connector sourceConnector, targetConnector;
        public bool sourceConnected, targetConnected;
    }

    [Serializable]
    public sealed class S1TaskAState
    {
        public bool systemIntegrityValid;
        public double capturedAtSeconds, workpieceYawDegrees;
        public S1TaskABaseState baseState;
        public S1RamState[] ram;
        public S1SsdState[] ssds;
        public S1GpuState[] gpus;
        public S1CableState[] cables;
        public bool assemblyObstructed;
        // Spec 10.2: every slot's occupancy and latch, plus efforts in progress (held or partly seated parts) at the snapshot.
        public S1SlotState[] slots;
        public S1PendingAttempt[] pending_attempts;
    }

    [Serializable]
    public sealed class S1SlotState
    {
        public string id, occupantId;
        public bool latchClosed;
    }

    [Serializable]
    public sealed class S1PendingAttempt
    {
        public string partId, targetId;
        public bool held;
        public S1Placement placement;
    }

    [Serializable]
    public sealed class S1TaskBState
    {
        public bool systemIntegrityValid;
        public double capturedAtSeconds, workpieceYawDegrees;
        public string baseId, frontGuardId, bladeId, spinnerId, rearGuardId, rearLockId;
        public bool powerIsolated, motorRunning, baseStable, assemblyObstructed;
        public S1Placement frontGuardPlacement, bladePlacement, rearGuardPlacement;
        public bool frontGuardAligned, frontGuardClipped, bladeCompatible, bladeKeyAligned, rearGuardAligned;
        public S1ThreadState spinner, rearLock;
        public string context_version, original_fault_id, context_basis;
        public bool? original_fault_active;
        public string[] user_created_issues, observation_accessible_targets;

        public S1TaskBState WithB04Context()
        {
            context_version = original_fault_id = context_basis = null;
            original_fault_active = null;
            user_created_issues = observation_accessible_targets = null;
            if (ResearchS1TaskEvaluator.Evaluate(this).Category == S1InspectionCategory.SystemError) return this;
            context_version = "research.s1-b04-context.1";
            original_fault_id = "B04.rear_guard_lock_engaged_loose";
            context_basis = "authored_B04_readiness_delta_not_error_or_intent";
            original_fault_active = rearLock != S1ThreadState.Tight;
            var issues = new List<string>();
            if (frontGuardPlacement != S1Placement.Seated) issues.Add("front_guard_not_seated");
            else if (!frontGuardAligned) issues.Add("front_guard_not_aligned");
            if (!frontGuardClipped) issues.Add("front_clip_open");
            if (bladePlacement != S1Placement.Seated) issues.Add("blade_not_seated");
            else if (!bladeKeyAligned) issues.Add("blade_key_not_aligned");
            if (!bladeCompatible) issues.Add("blade_incompatible");
            if (spinner != S1ThreadState.Tight) issues.Add("spinner_not_tight");
            if (rearGuardPlacement != S1Placement.Seated) issues.Add("rear_guard_not_seated");
            else if (!rearGuardAligned) issues.Add("rear_guard_not_aligned");
            if (rearLock == S1ThreadState.Absent) issues.Add("rear_lock_absent");
            user_created_issues = issues.ToArray();
            var accessible = new List<string>();
            if (powerIsolated && !motorRunning)
            {
                accessible.Add("B.front_guard");
                if (frontGuardPlacement == S1Placement.NotInstalled) accessible.Add("B.blade_mount");
                accessible.Add("B.rear_guard_mount");
            }
            observation_accessible_targets = accessible.ToArray();
            return this;
        }
    }

    public static class ResearchS1TaskEvaluator
    {
        public static S1InspectionResult Evaluate(S1TaskAState state)
        {
            if (!Valid(state)) return Result(S1InspectionCategory.SystemError, "invalid_snapshot");
            var basis = state.baseState;
            if (!basis.caseIntact || !basis.motherboardSecured || !basis.cpuSeated || !basis.coolerSecured || !basis.psuSecured ||
                !basis.atx24Connected || !basis.epsConnected || !basis.cpuFanConnected || !basis.frontPanelConnected || !basis.powerIsolated)
                return Result(S1InspectionCategory.AssemblyIncomplete, "base_not_ready");
            if (state.assemblyObstructed) return Result(S1InspectionCategory.AssemblyIncomplete, "assembly_obstructed");

            int ramCount = 0, ssdCount = 0, gpuCount = 0;
            double ramTotal = 0;
            string ramSpecification = null;
            bool specificationMatches = true, a2 = false, b2 = false;
            foreach (var ram in state.ram)
            {
                if (ram.placement == S1Placement.NotInstalled) continue;
                if (ram.placement != S1Placement.Seated || !ram.retained || ram.memoryType != S1RamType.Ddr4)
                    return Result(S1InspectionCategory.AssemblyIncomplete, "ram_not_ready");
                ramCount++;
                ramTotal += ram.capacityGb;
                a2 |= ram.slot == S1RamSlot.A2;
                b2 |= ram.slot == S1RamSlot.B2;
                specificationMatches &= ram.capacityGb == 8 && (ramSpecification == null || ramSpecification == ram.specification);
                ramSpecification = ram.specification;
            }
            foreach (var ssd in state.ssds)
            {
                if (ssd.placement == S1Placement.NotInstalled) continue;
                if (ssd.placement != S1Placement.Seated || ssd.trayFastener != S1ThreadState.Tight || ssd.storageInterface != S1SsdInterface.Sata ||
                    !Connected(state, basis.motherboardId, S1Connector.MotherboardSata, ssd.id, S1Connector.SsdData) ||
                    !Connected(state, basis.psuId, S1Connector.PsuSata, ssd.id, S1Connector.SsdPower))
                    return Result(S1InspectionCategory.AssemblyIncomplete, "ssd_not_ready");
                ssdCount++;
                specificationMatches &= ssd.capacityGb >= 500 && ssd.ratedEnduranceTbw >= 600;
            }
            foreach (var gpu in state.gpus)
            {
                if (gpu.placement == S1Placement.NotInstalled) continue;
                if (gpu.placement != S1Placement.Seated || !gpu.retained || gpu.bracketFastener != S1ThreadState.Tight || gpu.auxiliaryPower != S1Connector.GpuPcie8 ||
                    !Connected(state, basis.psuId, S1Connector.PsuPcie8, gpu.id, S1Connector.GpuPcie8))
                    return Result(S1InspectionCategory.AssemblyIncomplete, "gpu_not_ready");
                gpuCount++;
                specificationMatches &= gpu.slot == S1GpuSlot.PrimaryCpu && gpu.vramGb >= 8 && gpu.declaredBoardPowerW <= 150;
            }
            if (ramCount == 0 || ssdCount == 0 || gpuCount == 0)
                return Result(S1InspectionCategory.AssemblyIncomplete, "required_component_missing");
            if (!specificationMatches || ramCount != 2 || ramTotal != 16 || !a2 || !b2 || ssdCount != 1 || gpuCount != 1)
                return Result(S1InspectionCategory.SpecificationMismatch, "w2_requirements_not_met", true);
            return Result(S1InspectionCategory.Pass, "w2_ready", true);
        }

        public static S1InspectionResult Evaluate(S1TaskBState state)
        {
            if (!Valid(state)) return Result(S1InspectionCategory.SystemError, "invalid_snapshot");
            if (!state.powerIsolated || state.motorRunning || !state.baseStable || state.assemblyObstructed ||
                state.frontGuardPlacement != S1Placement.Seated || !state.frontGuardAligned || !state.frontGuardClipped ||
                state.bladePlacement != S1Placement.Seated || !state.bladeCompatible || !state.bladeKeyAligned || state.spinner != S1ThreadState.Tight ||
                state.rearGuardPlacement != S1Placement.Seated || !state.rearGuardAligned || state.rearLock == S1ThreadState.Absent)
                return Result(S1InspectionCategory.PreconditionNotReady, "protective_assembly_not_ready");
            if (state.rearLock == S1ThreadState.EngagedLoose)
                return Result(S1InspectionCategory.SymptomPresent, "original_fault_unresolved", true);
            return Result(S1InspectionCategory.Pass, "fan_ready_no_original_symptom", true);
        }

        private static S1InspectionResult Result(S1InspectionCategory category, string reason, bool canRunDiagnostic = false)
        {
            return new S1InspectionResult(category, reason, canRunDiagnostic);
        }

        private static bool Connected(S1TaskAState state, string source, S1Connector sourceConnector, string target, S1Connector targetConnector)
        {
            foreach (var cable in state.cables)
                if (cable.sourceConnected && cable.targetConnected && cable.sourcePartId == source && cable.sourceConnector == sourceConnector &&
                    cable.targetPartId == target && cable.targetConnector == targetConnector) return true;
            return false;
        }

        private static bool Valid(S1TaskAState state)
        {
            if (state == null || !state.systemIntegrityValid || !Finite(state.capturedAtSeconds) || state.capturedAtSeconds < 0 || !Finite(state.workpieceYawDegrees) ||
                state.baseState == null || state.ram == null || state.ssds == null || state.gpus == null || state.cables == null) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var basis = state.baseState;
            if (!Identity(ids, basis.caseId) || !Identity(ids, basis.motherboardId) || !Identity(ids, basis.cpuId) || !Identity(ids, basis.coolerId) || !Identity(ids, basis.psuId)) return false;
            var ramSlots = new HashSet<S1RamSlot>();
            foreach (var ram in state.ram)
            {
                if (ram == null || !Identity(ids, ram.id) || string.IsNullOrWhiteSpace(ram.specification) || !Defined(ram.memoryType) || ram.memoryType == S1RamType.Unknown ||
                    !Positive(ram.capacityGb) || !Defined(ram.placement) || !Defined(ram.slot)) return false;
                if (ram.placement == S1Placement.NotInstalled)
                {
                    if (ram.slot != S1RamSlot.None || ram.retained) return false;
                }
                else if (ram.slot == S1RamSlot.None || !ramSlots.Add(ram.slot)) return false;
            }
            var ssdMounts = new HashSet<S1SsdMount>();
            foreach (var ssd in state.ssds)
            {
                if (ssd == null || !Identity(ids, ssd.id) || !Defined(ssd.storageInterface) || ssd.storageInterface == S1SsdInterface.Unknown ||
                    !Positive(ssd.capacityGb) || !Positive(ssd.ratedEnduranceTbw) || !Defined(ssd.placement) || !Defined(ssd.mount) || !Defined(ssd.trayFastener)) return false;
                if (ssd.placement == S1Placement.NotInstalled)
                {
                    if (ssd.mount != S1SsdMount.None || ssd.trayFastener != S1ThreadState.Absent) return false;
                }
                else if (ssd.mount == S1SsdMount.None || !ssdMounts.Add(ssd.mount)) return false;
            }
            var gpuSlots = new HashSet<S1GpuSlot>();
            foreach (var gpu in state.gpus)
            {
                if (gpu == null || !Identity(ids, gpu.id) || !Positive(gpu.vramGb) || !Positive(gpu.declaredBoardPowerW) ||
                    !Defined(gpu.placement) || !Defined(gpu.slot) || !Defined(gpu.bracketFastener) || !Defined(gpu.auxiliaryPower) || gpu.auxiliaryPower == S1Connector.None) return false;
                if (gpu.placement == S1Placement.NotInstalled)
                {
                    if (gpu.slot != S1GpuSlot.None || gpu.retained || gpu.bracketFastener != S1ThreadState.Absent) return false;
                }
                else if (gpu.slot == S1GpuSlot.None || !gpuSlots.Add(gpu.slot)) return false;
            }
            var targetPorts = new HashSet<string>(StringComparer.Ordinal);
            var partIds = new HashSet<string>(ids, StringComparer.Ordinal);
            foreach (var cable in state.cables)
            {
                if (cable == null || !Identity(ids, cable.id) || !Defined(cable.sourceConnector) || !Defined(cable.targetConnector)) return false;
                if (cable.sourceConnected && (cable.sourceConnector == S1Connector.None || !partIds.Contains(cable.sourcePartId))) return false;
                if (cable.targetConnected && (cable.targetConnector == S1Connector.None || !partIds.Contains(cable.targetPartId) ||
                    !targetPorts.Add(cable.targetPartId + "/" + (int)cable.targetConnector))) return false;
            }
            return true;
        }

        private static bool Valid(S1TaskBState state)
        {
            if (state == null || !state.systemIntegrityValid || !Finite(state.capturedAtSeconds) || state.capturedAtSeconds < 0 || !Finite(state.workpieceYawDegrees) ||
                !Defined(state.frontGuardPlacement) || !Defined(state.bladePlacement) || !Defined(state.rearGuardPlacement) || !Defined(state.spinner) || !Defined(state.rearLock)) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            return Identity(ids, state.baseId) && Identity(ids, state.frontGuardId) && Identity(ids, state.bladeId) && Identity(ids, state.spinnerId) && Identity(ids, state.rearGuardId) && Identity(ids, state.rearLockId);
        }

        private static bool Identity(HashSet<string> ids, string id)
        {
            return !string.IsNullOrWhiteSpace(id) && ids.Add(id);
        }

        private static bool Defined<T>(T value) where T : struct
        {
            return Enum.IsDefined(typeof(T), value);
        }

        private static bool Positive(double value)
        {
            return Finite(value) && value > 0;
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
