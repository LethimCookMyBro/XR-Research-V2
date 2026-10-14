using System;
using System.Collections.Generic;

namespace VRMaintenanceResearch.ResearchFlow
{
    public static class ResearchS1TaskACatalog
    {
        public static IEnumerable<string> Ids => new[] { "M01", "M02", "M03", "M04", "M05", "S01", "S02", "S03", "G01", "G02" };

        public static string Asset(string id)
        {
            switch (id)
            {
                case "M01": case "M02": case "M03": return "RAM_DDR4_UDIMM_BARE";
                case "M04": case "M05": return "RAM_DDR5_UDIMM_BARE";
                case "S01": case "S02": case "S03": return "SSD_SATA_25_7MM";
                case "G01": case "G02": return "GPU_GXB_8PIN";
                default: throw new ArgumentException("Unknown S1 option identity.", nameof(id));
            }
        }

        public static S1RamState Ram(string id)
        {
            if (Asset(id).IndexOf("RAM_", StringComparison.Ordinal) != 0) throw new ArgumentException("Not a RAM identity.", nameof(id));
            bool ddr5 = id == "M04" || id == "M05";
            return new S1RamState { id = id, specification = ddr5 ? "S1-DDR5-8" : id == "M03" ? "S1-DDR4-16" : "S1-DDR4-8",
                memoryType = ddr5 ? S1RamType.Ddr5 : S1RamType.Ddr4, capacityGb = id == "M03" ? 16 : 8 };
        }

        public static S1SsdState Ssd(string id)
        {
            if (Asset(id) != "SSD_SATA_25_7MM") throw new ArgumentException("Not an SSD identity.", nameof(id));
            return new S1SsdState { id = id, storageInterface = S1SsdInterface.Sata,
                capacityGb = id == "S01" ? 1000 : id == "S02" ? 500 : 250, ratedEnduranceTbw = id == "S01" ? 300 : id == "S02" ? 600 : 800 };
        }

        public static S1GpuState Gpu(string id)
        {
            if (Asset(id) != "GPU_GXB_8PIN") throw new ArgumentException("Not a GPU identity.", nameof(id));
            return new S1GpuState { id = id, vramGb = id == "G01" ? 8 : 12, declaredBoardPowerW = id == "G01" ? 120 : 200,
                auxiliaryPower = S1Connector.GpuPcie8 };
        }

        public static string Label(string id)
        {
            string asset = Asset(id);
            if (asset.StartsWith("RAM_", StringComparison.Ordinal))
            {
                var ram = Ram(id);
                return id + " | " + (ram.memoryType == S1RamType.Ddr4 ? "DDR4" : "DDR5") + " | " + ram.capacityGb + " GB | " + ram.specification;
            }
            if (asset == "SSD_SATA_25_7MM")
            {
                var ssd = Ssd(id);
                return id + " | SATA 2.5 in | " + ssd.capacityGb + " GB | " + ssd.ratedEnduranceTbw + " TBW";
            }
            var gpu = Gpu(id);
            return id + " | " + gpu.vramGb + " GB | " + gpu.declaredBoardPowerW + " W | PCIe 8-pin";
        }

        public static int LayoutIndex(int index, int count, string layout)
        {
            if (count <= 0 || index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            if (layout != "P" && layout != "Q") throw new ArgumentException("S1 layout must be P or Q.", nameof(layout));
            return layout == "P" ? index : count - 1 - index;
        }
    }
}
