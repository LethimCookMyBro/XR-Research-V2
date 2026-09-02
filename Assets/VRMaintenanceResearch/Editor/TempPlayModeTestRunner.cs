using System.Collections.Generic;
using System.IO;
using NUnit.Framework.Interfaces;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(UnityEngine.XR.Content.Interaction.Research.Editor.TempPlayModeResultCallback))]

namespace UnityEngine.XR.Content.Interaction.Research.Editor
{
    /// <summary>
    /// Temporary helper (not part of the research test suite) that kicks off the
    /// VRPhysicalInteractionTests PlayMode run via TestRunnerApi and writes a JSON
    /// summary to Temp/PhysicalTest_Result.json once finished.
    /// </summary>
    public static class TempPlayModeTestRunner
    {
        internal const string ResultPath = "Temp/PhysicalTest_Result.json";

        [MenuItem("Tools/VR Research/Run Physical Interaction Test (PlayMode)")]
        public static void RunPhysicalInteractionTest()
        {
            if (File.Exists(ResultPath)) File.Delete(ResultPath);

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            var filter = new Filter
            {
                testMode = TestMode.PlayMode,
                testNames = new[] { "VRPhysicalInteractionTests.Test_FanBlade_PhysicalInteractionPipeline" }
            };
            api.Execute(new ExecutionSettings(filter));
            Debug.Log("[TempPlayModeTestRunner] PlayMode test run started for VRPhysicalInteractionTests.");
        }
    }

    // Entering Play Mode triggers a domain reload, which wipes any ICallbacks instance
    // registered via TestRunnerApi.RegisterCallbacks. ITestRunCallback + the assembly-level
    // [TestRunCallback] attribute is the NUnit-level callback mechanism that is instead
    // rediscovered via reflection after reload, so this is the reliable way to catch
    // RunFinished for a PlayMode test run driven from an Editor script.
    public class TempPlayModeResultCallback : ITestRunCallback
    {
        public void RunStarted(ITest testsToRun) { }

        public void RunFinished(ITestResult result)
        {
            var list = new List<string>();
            Collect(result, list);
            string json = "{\"results\":[" + string.Join(",", list) + "]}";
            File.WriteAllText(TempPlayModeTestRunner.ResultPath, json);
            Debug.Log($"[TempPlayModeTestRunner] RunFinished. Wrote {TempPlayModeTestRunner.ResultPath} with {list.Count} leaf result(s).");
        }

        public void TestStarted(ITest test) { }
        public void TestFinished(ITestResult result) { }

        static void Collect(ITestResult result, List<string> list)
        {
            if (result.Test.IsSuite)
            {
                if (result.Children != null)
                    foreach (var child in result.Children) Collect(child, list);
            }
            else
            {
                string name = EscapeJson(result.Test.FullName);
                string msg = EscapeJson(result.Message ?? "");
                string stack = EscapeJson(result.StackTrace ?? "");
                list.Add("{\"name\":\"" + name + "\",\"resultState\":\"" + result.ResultState +
                    "\",\"duration\":" + result.Duration.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ",\"message\":\"" + msg + "\",\"stackTrace\":\"" + stack + "\"}");
            }
        }

        static string EscapeJson(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }
}
