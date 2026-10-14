#if RESEARCH_S1_TASK_A_CHECKS
using System;
using System.Collections.Generic;
using VRMaintenanceResearch.ResearchFlow;
internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    public static int Main()
    {
        var ids = new HashSet<string>();
        foreach (var id in ResearchS1TaskACatalog.Ids) Check(ids.Add(id), "unique instance " + id);
        Check(ids.Count == 10, "five RAM plus three SSD plus two GPU");
        foreach (var id in new[] { "M01", "M02", "M03", "M04", "M05" })
        {
            var ram = ResearchS1TaskACatalog.Ram(id);
            Check(ram.id == id && ram.placement == S1Placement.NotInstalled && ram.slot == S1RamSlot.None && !ram.retained, "RAM initially outside " + id);
            Check(ram.memoryType == (id == "M04" || id == "M05" ? S1RamType.Ddr5 : S1RamType.Ddr4), "RAM generation " + id);
            Check(ram.capacityGb == (id == "M03" ? 16 : 8), "RAM capacity " + id);
        }
        Check(ResearchS1TaskACatalog.Ram("M01").specification == ResearchS1TaskACatalog.Ram("M02").specification, "same DDR4 kit");
        Check(ResearchS1TaskACatalog.Ram("M04").specification == ResearchS1TaskACatalog.Ram("M05").specification, "same DDR5 kit");
        double[] gb = { 1000, 500, 250 }, tbw = { 300, 600, 800 };
        for (int i = 0; i < 3; i++)
        {
            var s = ResearchS1TaskACatalog.Ssd("S0" + (i + 1));
            Check(s.capacityGb == gb[i] && s.ratedEnduranceTbw == tbw[i], "SSD properties " + i);
            Check(s.mount == S1SsdMount.None && s.trayFastener == S1ThreadState.Absent, "SSD uninstalled " + i);
        }
        var g1 = ResearchS1TaskACatalog.Gpu("G01"); var g2 = ResearchS1TaskACatalog.Gpu("G02");
        Check(g1.vramGb == 8 && g1.declaredBoardPowerW == 120, "G01 properties");
        Check(g2.vramGb == 12 && g2.declaredBoardPowerW == 200, "G02 properties");
        Check(ResearchS1TaskACatalog.Asset("G01") == ResearchS1TaskACatalog.Asset("G02"), "same GPU geometry");
        Check(g1.auxiliaryPower == S1Connector.GpuPcie8 && g2.auxiliaryPower == S1Connector.GpuPcie8, "same GPU plug");
        Check(g1.bracketFastener == S1ThreadState.Absent && g2.bracketFastener == S1ThreadState.Absent, "no implicit fastening");
        var changed = ResearchS1TaskACatalog.Ram("M01"); changed.capacityGb = 999;
        Check(ResearchS1TaskACatalog.Ram("M01").capacityGb == 8, "no shared mutable catalog snapshot");
        foreach (var bad in new[] { "", "M00", "S04", "m01", null })
        {
            bool threw = false; try { ResearchS1TaskACatalog.Asset(bad); } catch (ArgumentException) { threw = true; }
            Check(threw, "reject unknown identity " + bad);
        }
        bool typeRejected = false; try { ResearchS1TaskACatalog.Ram("G01"); } catch (ArgumentException) { typeRejected = true; }
        Check(typeRejected, "reject wrong category");
        for (int n = 1; n <= 5; n++) for (int i = 0; i < n; i++)
        {
            Check(ResearchS1TaskACatalog.LayoutIndex(i, n, "P") == i, "P identity");
            Check(ResearchS1TaskACatalog.LayoutIndex(i, n, "Q") == n - 1 - i, "Q reversed");
        }
        foreach (var bad in new[] { "p", "R", "", null })
        {
            bool threw = false; try { ResearchS1TaskACatalog.LayoutIndex(0, 5, bad); } catch (ArgumentException) { threw = true; }
            Check(threw, "reject unassigned layout");
        }
        Console.WriteLine("S1_TASK_A_CATALOG_PASS checks=" + checks);
        return 0;
    }
}

#endif
