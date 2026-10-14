#if RESEARCH_S1_TASK_CHECKS
using System;
using VRMaintenanceResearch.ResearchFlow;

internal static class ResearchS1TaskChecks
{
    private static int checks, failures;

    private static void Expect(string name, S1InspectionResult actual, S1InspectionCategory category, bool canRunDiagnostic)
    {
        checks++;
        if (actual.Category == category && actual.CanRunDiagnostic == canRunDiagnostic && actual.Passed == (category == S1InspectionCategory.Pass)) return;
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": expected " + category + "/diagnostic=" + canRunDiagnostic + ", actual " + actual.Category + "/diagnostic=" + actual.CanRunDiagnostic + "/reason=" + actual.ReasonCode);
    }

    private static S1TaskAState CompleteA()
    {
        return new S1TaskAState
        {
            systemIntegrityValid = true,
            baseState = new S1TaskABaseState
            {
                caseId = "case", motherboardId = "board", cpuId = "cpu", coolerId = "cooler", psuId = "psu",
                caseIntact = true, motherboardSecured = true, cpuSeated = true, coolerSecured = true, psuSecured = true,
                atx24Connected = true, epsConnected = true, cpuFanConnected = true, frontPanelConnected = true, powerIsolated = true
            },
            ram = new[] { Ram("M01", S1RamType.Ddr4, 8, "K8", S1RamSlot.A2), Ram("M02", S1RamType.Ddr4, 8, "K8", S1RamSlot.B2) },
            ssds = new[] { Ssd("S02", 500, 600) },
            gpus = new[] { Gpu("G01", 8, 120) },
            cables = new[]
            {
                Cable("data", "board", S1Connector.MotherboardSata, "S02", S1Connector.SsdData),
                Cable("sata", "psu", S1Connector.PsuSata, "S02", S1Connector.SsdPower),
                Cable("pcie", "psu", S1Connector.PsuPcie8, "G01", S1Connector.GpuPcie8)
            }
        };
    }

    private static S1RamState Ram(string id, S1RamType type, double capacity, string spec, S1RamSlot slot)
    {
        return new S1RamState { id = id, memoryType = type, capacityGb = capacity, specification = spec, placement = S1Placement.Seated, slot = slot, retained = true };
    }

    private static S1SsdState Ssd(string id, double capacity, double endurance)
    {
        return new S1SsdState { id = id, storageInterface = S1SsdInterface.Sata, capacityGb = capacity, ratedEnduranceTbw = endurance, placement = S1Placement.Seated, mount = S1SsdMount.RearTray1, trayFastener = S1ThreadState.Tight };
    }

    private static S1GpuState Gpu(string id, double memory, double power)
    {
        return new S1GpuState { id = id, vramGb = memory, declaredBoardPowerW = power, placement = S1Placement.Seated, slot = S1GpuSlot.PrimaryCpu, retained = true, bracketFastener = S1ThreadState.Tight, auxiliaryPower = S1Connector.GpuPcie8 };
    }

    private static S1CableState Cable(string id, string source, S1Connector sourceConnector, string target, S1Connector targetConnector)
    {
        return new S1CableState { id = id, sourcePartId = source, sourceConnector = sourceConnector, targetPartId = target, targetConnector = targetConnector, sourceConnected = true, targetConnected = true };
    }

    private static S1TaskBState CompleteB()
    {
        return new S1TaskBState
        {
            systemIntegrityValid = true, powerIsolated = true, baseStable = true,
            baseId = "fan", frontGuardId = "front", bladeId = "blade", spinnerId = "spinner", rearGuardId = "rear", rearLockId = "lock",
            frontGuardPlacement = S1Placement.Seated, bladePlacement = S1Placement.Seated, rearGuardPlacement = S1Placement.Seated,
            frontGuardAligned = true, frontGuardClipped = true, bladeCompatible = true, bladeKeyAligned = true, rearGuardAligned = true,
            spinner = S1ThreadState.Tight, rearLock = S1ThreadState.Tight
        };
    }

    private static int Main()
    {
        Console.WriteLine("SYNTHETIC_STATE_UNIT_TESTS_ONLY; no Unity mechanics, input, E2E, device, or participant evidence.");
        Expect("A property-valid assembly", ResearchS1TaskEvaluator.Evaluate(CompleteA()), S1InspectionCategory.Pass, true);
        var b = CompleteB();
        b.rearLock = S1ThreadState.EngagedLoose;
        Expect("B initial engaged loose", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.SymptomPresent, true);
        b.rearLock = S1ThreadState.Tight;
        Expect("B tightened", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.Pass, true);
        b.rearLock = S1ThreadState.EngagedLoose;
        Expect("B loosened after pass", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.SymptomPresent, true);
        EnumeratedOptions();
        TaskANegatives();
        TaskBNegatives();
        MalformedSnapshots();
        CurrentStateTransitions();
        Console.WriteLine("checks=" + checks + "; failures=" + failures);
        return failures == 0 ? 0 : 1;
    }

    private static void EnumeratedOptions()
    {
        string[] ids = { "M01", "M02", "M03", "M04", "M05" };
        S1RamType[] types = { S1RamType.Ddr4, S1RamType.Ddr4, S1RamType.Ddr4, S1RamType.Ddr5, S1RamType.Ddr5 };
        double[] capacities = { 8, 8, 16, 8, 8 };
        string[] specifications = { "K8", "K8", "K16", "K5", "K5" };
        double[] ssdCapacity = { 1000, 500, 250 }, endurance = { 300, 600, 800 };
        double[] gpuVram = { 8, 12 }, gpuPower = { 120, 200 };
        int passes = 0;
        for (int first = 0; first < 5; first++)
        for (int second = first + 1; second < 5; second++)
        for (int permutation = 0; permutation < 2; permutation++)
        for (int ssd = 0; ssd < 3; ssd++)
        for (int gpu = 0; gpu < 2; gpu++)
        {
            var a = CompleteA();
            a.ram = new[]
            {
                Ram(ids[first], types[first], capacities[first], specifications[first], permutation == 0 ? S1RamSlot.A2 : S1RamSlot.B2),
                Ram(ids[second], types[second], capacities[second], specifications[second], permutation == 0 ? S1RamSlot.B2 : S1RamSlot.A2)
            };
            a.ssds[0] = Ssd("S0" + (ssd + 1), ssdCapacity[ssd], endurance[ssd]);
            a.gpus[0] = Gpu("G0" + (gpu + 1), gpuVram[gpu], gpuPower[gpu]);
            a.cables[0].targetPartId = a.ssds[0].id;
            a.cables[1].targetPartId = a.ssds[0].id;
            a.cables[2].targetPartId = a.gpus[0].id;
            var expected = second >= 3 ? S1InspectionCategory.AssemblyIncomplete :
                first == 0 && second == 1 && ssd == 1 && gpu == 0 ? S1InspectionCategory.Pass : S1InspectionCategory.SpecificationMismatch;
            var result = ResearchS1TaskEvaluator.Evaluate(a);
            Expect("enumerated " + ids[first] + "+" + ids[second] + "/" + permutation + "/S" + (ssd + 1) + "/G" + (gpu + 1), result, expected, second < 3);
            if (result.Passed) passes++;
        }
        checks++;
        if (passes != 2) { failures++; Console.Error.WriteLine("FAIL enumerated accepted permutations: expected 2, actual " + passes); }
        Console.WriteLine("Enumerated 120 option and RAM-slot permutations; passing permutations=" + passes + "; expected=2.");

        var renamed = CompleteA();
        renamed.ram[0].id = "equivalent-ram-left";
        renamed.ram[1].id = "equivalent-ram-right";
        renamed.ram[0].specification = renamed.ram[1].specification = "equivalent-kit";
        renamed.ssds[0].id = "equivalent-storage";
        renamed.gpus[0].id = "equivalent-graphics";
        renamed.cables[0].targetPartId = renamed.cables[1].targetPartId = "equivalent-storage";
        renamed.cables[2].targetPartId = "equivalent-graphics";
        Expect("property-equivalent renamed IDs", ResearchS1TaskEvaluator.Evaluate(renamed), S1InspectionCategory.Pass, true);
        renamed.ssds[0].capacityGb = 2000;
        renamed.ssds[0].ratedEnduranceTbw = 1200;
        renamed.gpus[0].vramGb = 16;
        renamed.gpus[0].declaredBoardPowerW = 150;
        Expect("property-equivalent thresholds not ID whitelist", ResearchS1TaskEvaluator.Evaluate(renamed), S1InspectionCategory.Pass, true);
    }

    private static void CheckA(string name, Action<S1TaskAState> mutate, S1InspectionCategory category, bool diagnostic = false)
    {
        var a = CompleteA();
        mutate(a);
        Expect(name, ResearchS1TaskEvaluator.Evaluate(a), category, diagnostic);
    }

    private static void TaskANegatives()
    {
        CheckA("single DDR4 16 GB is mismatch not incomplete", a => a.ram = new[] { Ram("M03", S1RamType.Ddr4, 16, "K16", S1RamSlot.A2) }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("compatible A1/B1", a => { a.ram[0].slot = S1RamSlot.A1; a.ram[1].slot = S1RamSlot.B1; }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("compatible A1/A2", a => { a.ram[0].slot = S1RamSlot.A1; a.ram[1].slot = S1RamSlot.A2; }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("different RAM specifications", a => a.ram[1].specification = "another-kit", S1InspectionCategory.SpecificationMismatch, true);
        CheckA("compatible secondary GPU slot", a => a.gpus[0].slot = S1GpuSlot.SecondaryFullLength, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("SSD 499 GB boundary", a => a.ssds[0].capacityGb = 499, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("SSD 599 TBW boundary", a => a.ssds[0].ratedEnduranceTbw = 599, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("GPU 7 GB boundary", a => a.gpus[0].vramGb = 7, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("GPU 151 W boundary", a => a.gpus[0].declaredBoardPowerW = 151, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("no RAM", a => a.ram = Array.Empty<S1RamState>(), S1InspectionCategory.AssemblyIncomplete);
        CheckA("RAM removed after pass", a => { a.ram[0].placement = S1Placement.NotInstalled; a.ram[0].slot = S1RamSlot.None; a.ram[0].retained = false; }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("SSD removed after pass", a => { a.ssds[0].placement = S1Placement.NotInstalled; a.ssds[0].mount = S1SsdMount.None; a.ssds[0].trayFastener = S1ThreadState.Absent; }, S1InspectionCategory.AssemblyIncomplete);
        CheckA("GPU removed after pass", a => { a.gpus[0].placement = S1Placement.NotInstalled; a.gpus[0].slot = S1GpuSlot.None; a.gpus[0].retained = false; a.gpus[0].bracketFastener = S1ThreadState.Absent; }, S1InspectionCategory.AssemblyIncomplete);
        CheckA("RAM partial", a => a.ram[0].placement = S1Placement.Partial, S1InspectionCategory.AssemblyIncomplete);
        CheckA("SSD partial", a => a.ssds[0].placement = S1Placement.Partial, S1InspectionCategory.AssemblyIncomplete);
        CheckA("GPU partial", a => a.gpus[0].placement = S1Placement.Partial, S1InspectionCategory.AssemblyIncomplete);
        CheckA("RAM not retained", a => a.ram[0].retained = false, S1InspectionCategory.AssemblyIncomplete);
        CheckA("SSD unfastened", a => a.ssds[0].trayFastener = S1ThreadState.EngagedLoose, S1InspectionCategory.AssemblyIncomplete);
        CheckA("GPU unfastened", a => a.gpus[0].bracketFastener = S1ThreadState.Absent, S1InspectionCategory.AssemblyIncomplete);
        CheckA("GPU slot latch open", a => a.gpus[0].retained = false, S1InspectionCategory.AssemblyIncomplete);
        CheckA("no cables", a => a.cables = Array.Empty<S1CableState>(), S1InspectionCategory.AssemblyIncomplete);
        for (int i = 0; i < 3; i++)
        {
            int cable = i;
            CheckA("disconnected target " + i, a => a.cables[cable].targetConnected = false, S1InspectionCategory.AssemblyIncomplete);
            CheckA("disconnected source " + i, a => a.cables[cable].sourceConnected = false, S1InspectionCategory.AssemblyIncomplete);
            CheckA("wrong source identity " + i, a => a.cables[cable].sourcePartId = "cooler", S1InspectionCategory.AssemblyIncomplete);
            CheckA("wrong source connector " + i, a => a.cables[cable].sourceConnector = S1Connector.Eps8, S1InspectionCategory.AssemblyIncomplete);
            CheckA("wrong target identity " + i, a => a.cables[cable].targetPartId = "case", S1InspectionCategory.AssemblyIncomplete);
            CheckA("wrong target connector " + i, a => a.cables[cable].targetConnector = S1Connector.Eps8, S1InspectionCategory.AssemblyIncomplete);
        }
        CheckA("SSD data and power swapped", a => { a.cables[0].targetConnector = S1Connector.SsdPower; a.cables[1].targetConnector = S1Connector.SsdData; }, S1InspectionCategory.AssemblyIncomplete);
        CheckA("obstructed assembly", a => a.assemblyObstructed = true, S1InspectionCategory.AssemblyIncomplete);
        Action<S1TaskAState>[] baseFailures =
        {
            a => a.baseState.caseIntact = false, a => a.baseState.motherboardSecured = false,
            a => a.baseState.cpuSeated = false, a => a.baseState.coolerSecured = false, a => a.baseState.psuSecured = false,
            a => a.baseState.atx24Connected = false, a => a.baseState.epsConnected = false,
            a => a.baseState.cpuFanConnected = false, a => a.baseState.frontPanelConnected = false, a => a.baseState.powerIsolated = false
        };
        for (int i = 0; i < baseFailures.Length; i++) CheckA("base predicate " + i, baseFailures[i], S1InspectionCategory.AssemblyIncomplete);
        CheckA("extra fully seated RAM cannot compensate", a => a.ram = new[] { a.ram[0], a.ram[1], Ram("extra", S1RamType.Ddr4, 8, "K8", S1RamSlot.A1) }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("extra partial RAM prevents pass", a => { a.ram = new[] { a.ram[0], a.ram[1], Ram("extra", S1RamType.Ddr4, 8, "K8", S1RamSlot.A1) }; a.ram[2].placement = S1Placement.Partial; }, S1InspectionCategory.AssemblyIncomplete);
        CheckA("extra SSD fully connected", a =>
        {
            a.ssds = new[] { a.ssds[0], Ssd("extra", 500, 600) };
            a.ssds[1].mount = S1SsdMount.RearTray2;
            a.cables = new[] { a.cables[0], a.cables[1], a.cables[2], Cable("extra-data", "board", S1Connector.MotherboardSata, "extra", S1Connector.SsdData), Cable("extra-power", "psu", S1Connector.PsuSata, "extra", S1Connector.SsdPower) };
        }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("extra GPU fully connected", a =>
        {
            a.gpus = new[] { a.gpus[0], Gpu("extra", 8, 120) };
            a.gpus[1].slot = S1GpuSlot.SecondaryFullLength;
            a.cables = new[] { a.cables[0], a.cables[1], a.cables[2], Cable("extra-power", "psu", S1Connector.PsuPcie8, "extra", S1Connector.GpuPcie8) };
        }, S1InspectionCategory.SpecificationMismatch, true);
        CheckA("incomplete wins over specification mismatch", a => { a.gpus[0].declaredBoardPowerW = 200; a.cables[0].targetConnected = false; }, S1InspectionCategory.AssemblyIncomplete);
    }

    private static void TaskBNegatives()
    {
        var b = CompleteB();
        b.rearLock = S1ThreadState.EngagedLoose;
        b.frontGuardPlacement = S1Placement.NotInstalled;
        b.frontGuardClipped = false;
        Expect("B access opens safety gate", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.PreconditionNotReady, false);
        b.frontGuardPlacement = S1Placement.Seated;
        b.frontGuardClipped = true;
        Expect("B closing access does not clear fault", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.SymptomPresent, true);
        b.rearLock = S1ThreadState.Tight;
        Expect("B direct repair without reference or Examine", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.Pass, true);
        b.rearLock = S1ThreadState.Absent;
        Expect("B lock removed after pass", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.PreconditionNotReady, false);
        Action<S1TaskBState>[] unsafeStates =
        {
            x => x.powerIsolated = false, x => x.motorRunning = true, x => x.baseStable = false, x => x.assemblyObstructed = true,
            x => x.frontGuardPlacement = S1Placement.NotInstalled, x => x.frontGuardPlacement = S1Placement.Partial,
            x => x.frontGuardAligned = false, x => x.frontGuardClipped = false,
            x => x.bladePlacement = S1Placement.NotInstalled, x => x.bladePlacement = S1Placement.Partial,
            x => x.bladeCompatible = false, x => x.bladeKeyAligned = false,
            x => x.spinner = S1ThreadState.Absent, x => x.spinner = S1ThreadState.EngagedLoose,
            x => x.rearGuardPlacement = S1Placement.NotInstalled, x => x.rearGuardPlacement = S1Placement.Partial,
            x => x.rearGuardAligned = false, x => x.rearLock = S1ThreadState.Absent
        };
        for (int i = 0; i < unsafeStates.Length; i++)
        {
            b = CompleteB();
            unsafeStates[i](b);
            Expect("B protective precondition " + i, ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.PreconditionNotReady, false);
        }
        b = CompleteB(); b.motorRunning = true; b.frontGuardClipped = false;
        Expect("B open guard while motor running", ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.PreconditionNotReady, false);
        S1InspectionCategory[,] categories =
        {
            { S1InspectionCategory.PreconditionNotReady, S1InspectionCategory.PreconditionNotReady, S1InspectionCategory.PreconditionNotReady },
            { S1InspectionCategory.PreconditionNotReady, S1InspectionCategory.PreconditionNotReady, S1InspectionCategory.PreconditionNotReady },
            { S1InspectionCategory.PreconditionNotReady, S1InspectionCategory.SymptomPresent, S1InspectionCategory.Pass }
        };
        bool[,] diagnosticReady = { { false, false, false }, { false, false, false }, { false, true, true } };
        foreach (S1ThreadState spinner in Enum.GetValues(typeof(S1ThreadState)))
        foreach (S1ThreadState rearLock in Enum.GetValues(typeof(S1ThreadState)))
        {
            b = CompleteB(); b.spinner = spinner; b.rearLock = rearLock;
            Expect("B thread combinations " + spinner + "/" + rearLock, ResearchS1TaskEvaluator.Evaluate(b), categories[(int)spinner, (int)rearLock], diagnosticReady[(int)spinner, (int)rearLock]);
        }
    }

    private static void CurrentStateTransitions()
    {
        var a = CompleteA();
        Expect("A before removal", ResearchS1TaskEvaluator.Evaluate(a), S1InspectionCategory.Pass, true);
        a.ssds[0].placement = S1Placement.NotInstalled;
        a.ssds[0].mount = S1SsdMount.None;
        a.ssds[0].trayFastener = S1ThreadState.Absent;
        Expect("A same snapshot after SSD removal", ResearchS1TaskEvaluator.Evaluate(a), S1InspectionCategory.AssemblyIncomplete, false);
        a.ssds[0].placement = S1Placement.Seated;
        a.ssds[0].mount = S1SsdMount.RearTray1;
        a.ssds[0].trayFastener = S1ThreadState.Tight;
        Expect("A same snapshot after SSD reassembly", ResearchS1TaskEvaluator.Evaluate(a), S1InspectionCategory.Pass, true);
        a.gpus[0].retained = false;
        Expect("A same snapshot after GPU unlatch", ResearchS1TaskEvaluator.Evaluate(a), S1InspectionCategory.AssemblyIncomplete, false);
        a.gpus[0].retained = true;
        Expect("A same snapshot after GPU relatch", ResearchS1TaskEvaluator.Evaluate(a), S1InspectionCategory.Pass, true);
        CheckA("unused bench inventory is not installed", state =>
        {
            state.ram = new[] { state.ram[0], state.ram[1], Ram("M03", S1RamType.Ddr4, 16, "K16", S1RamSlot.None), Ram("M04", S1RamType.Ddr5, 8, "K5", S1RamSlot.None), Ram("M05", S1RamType.Ddr5, 8, "K5", S1RamSlot.None) };
            for (int i = 2; i < 5; i++) { state.ram[i].placement = S1Placement.NotInstalled; state.ram[i].retained = false; }
            state.ssds = new[] { state.ssds[0], Ssd("S01", 1000, 300), Ssd("S03", 250, 800) };
            for (int i = 1; i < 3; i++) { state.ssds[i].placement = S1Placement.NotInstalled; state.ssds[i].mount = S1SsdMount.None; state.ssds[i].trayFastener = S1ThreadState.Absent; }
            state.gpus = new[] { state.gpus[0], Gpu("G02", 12, 200) };
            state.gpus[1].placement = S1Placement.NotInstalled; state.gpus[1].slot = S1GpuSlot.None; state.gpus[1].retained = false; state.gpus[1].bracketFastener = S1ThreadState.Absent;
        }, S1InspectionCategory.Pass, true);
    }

    private static void MalformedSnapshots()
    {
        Expect("null A", ResearchS1TaskEvaluator.Evaluate((S1TaskAState)null), S1InspectionCategory.SystemError, false);
        Expect("null B", ResearchS1TaskEvaluator.Evaluate((S1TaskBState)null), S1InspectionCategory.SystemError, false);
        Action<S1TaskAState>[] malformedA =
        {
            a => a.systemIntegrityValid = false, a => a.baseState = null, a => a.ram = null, a => a.ssds = null, a => a.gpus = null, a => a.cables = null,
            a => a.ram[0] = null, a => a.ssds[0] = null, a => a.gpus[0] = null, a => a.cables[0] = null,
            a => a.ram[0].id = null, a => a.ssds[0].id = " ", a => a.gpus[0].id = "", a => a.cables[0].id = null,
            a => a.baseState.psuId = "board", a => a.ram[0].id = a.ram[1].id, a => a.ssds[0].id = a.ram[0].id, a => a.cables[0].id = a.cables[1].id,
            a => a.ram[0].specification = " ", a => a.ram[0].memoryType = S1RamType.Unknown, a => a.ram[0].memoryType = (S1RamType)90,
            a => a.ram[0].capacityGb = double.NaN, a => a.ssds[0].capacityGb = double.PositiveInfinity, a => a.ssds[0].ratedEnduranceTbw = double.NegativeInfinity,
            a => a.gpus[0].vramGb = 0, a => a.gpus[0].declaredBoardPowerW = -1, a => a.workpieceYawDegrees = double.NaN,
            a => a.capturedAtSeconds = double.PositiveInfinity, a => a.capturedAtSeconds = -1,
            a => a.ram[0].placement = (S1Placement)77, a => a.ram[0].slot = (S1RamSlot)77, a => a.ram[0].slot = S1RamSlot.None,
            a => a.ram[0].slot = a.ram[1].slot, a => a.ssds[0].mount = S1SsdMount.None, a => a.gpus[0].slot = S1GpuSlot.None,
            a => a.ssds[0].trayFastener = (S1ThreadState)77, a => a.gpus[0].bracketFastener = (S1ThreadState)77,
            a => a.cables[0].sourceConnector = (S1Connector)77, a => a.cables[0].targetConnector = S1Connector.None,
            a => a.cables[0].sourcePartId = "missing", a => a.cables[0].targetPartId = "missing",
            a => { a.cables[1].targetPartId = a.cables[0].targetPartId; a.cables[1].targetConnector = a.cables[0].targetConnector; },
            a => a.ram[0].placement = S1Placement.NotInstalled,
            a => a.ssds[0].placement = S1Placement.NotInstalled,
            a => a.gpus[0].placement = S1Placement.NotInstalled
        };
        for (int i = 0; i < malformedA.Length; i++) CheckA("malformed A " + i, malformedA[i], S1InspectionCategory.SystemError);
        Action<S1TaskBState>[] malformedB =
        {
            b => b.systemIntegrityValid = false, b => b.frontGuardId = null, b => b.bladeId = " ", b => b.spinnerId = b.rearLockId,
            b => b.capturedAtSeconds = double.NaN, b => b.capturedAtSeconds = -1, b => b.workpieceYawDegrees = double.PositiveInfinity,
            b => b.frontGuardPlacement = (S1Placement)9, b => b.bladePlacement = (S1Placement)9, b => b.rearGuardPlacement = (S1Placement)9,
            b => b.spinner = (S1ThreadState)9, b => b.rearLock = (S1ThreadState)9
        };
        for (int i = 0; i < malformedB.Length; i++)
        {
            var b = CompleteB(); malformedB[i](b);
            Expect("malformed B " + i, ResearchS1TaskEvaluator.Evaluate(b), S1InspectionCategory.SystemError, false);
        }
    }
}
#endif
