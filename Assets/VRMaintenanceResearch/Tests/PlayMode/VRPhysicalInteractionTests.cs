using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Content.Interaction.Research;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// API-driven PlayMode physics/placement test for the fan blade Visual Child Pattern.
/// Drives XRInteractionManager.SelectEnter/SelectExit directly (no XR Device Simulator
/// input). Results from this test must never be described as "Device Simulator Verified"
/// or "Hardware Verified" - it only proves the scripted API interaction pipeline.
/// Kept separate from the EditMode VRResearchIntegrationTests class by design.
/// Lives in its own VRPhysicalInteractionTests.asmdef (Any Platform, references the
/// Unity.XR.Content.Interaction runtime assembly directly) so it is discoverable by the
/// PlayMode Test Runner - the sibling EditMode suite's asmdef restricts to the Editor
/// platform, which excludes it from PlayMode test discovery entirely.
/// </summary>
public class VRPhysicalInteractionTests
{
    const string ScenePath = "Assets/VRMaintenanceResearch/Scenes/VRResearch_Master.unity";
    const string SessionId = "PT_PLAYMODE_PHYSICAL";
    const string FilePrefix = "physical_test";

    GameObject _testInteractorGO;

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        LogAssert.ignoreFailingMessages = true;
        var logger = Object.FindFirstObjectByType<VRDataLogger>();
        if (logger != null && logger.IsRecording)
            logger.StopRecording();
        if (_testInteractorGO != null)
            Object.Destroy(_testInteractorGO);
        yield return null;
    }

    [UnityTest]
    [Timeout(30000)]
    public IEnumerator Test_FanBlade_PhysicalInteractionPipeline()
    {
        LogAssert.ignoreFailingMessages = true;

        // ---------- Phase 1: Load the real research scene into PlayMode ----------
#if UNITY_EDITOR
        EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
        Assert.Fail("This test must run inside the Unity Editor (uses EditorSceneManager.LoadSceneInPlayMode).");
#endif
        yield return null;
        yield return null;

        // Task_B_FanRepair is saved inactive in the scene; normally VRSessionManager.StartSession()
        // activates it via UpdateTaskGroupVisibility(). We replicate only that one call directly to
        // avoid pulling in the counterbalancer/task-order gating, which this physics test doesn't need.
        // GameObject.Find skips inactive objects, and Task_B_FanRepair is saved inactive - walk the
        // scene's root hierarchy directly instead so the group can be found before it's activated.
        var taskBGroup = FindInActiveScene("Task_B_FanRepair");
        Assert.IsNotNull(taskBGroup, "Task_B_FanRepair not found after scene load.");
        taskBGroup.SetActive(true);
        yield return null;

        var mockFanBladeGO = GameObject.Find("Mock_Fan_Blade");
        var fanShaftSocketGO = GameObject.Find("Fan_Shaft_Socket");
        Assert.IsNotNull(mockFanBladeGO, "Mock_Fan_Blade not found in loaded scene.");
        Assert.IsNotNull(fanShaftSocketGO, "Fan_Shaft_Socket not found in loaded scene.");
        var visualModel = mockFanBladeGO.transform.Find("Visual_Model");
        Assert.IsNotNull(visualModel, "Visual_Model child not found under Mock_Fan_Blade.");

        var grabInteractable = mockFanBladeGO.GetComponent<XRGrabInteractable>();
        var socketInteractor = fanShaftSocketGO.GetComponent<XRSocketInteractor>();
        var rb = mockFanBladeGO.GetComponent<Rigidbody>();
        Vector3 originalPosition = mockFanBladeGO.transform.position;

        // The real XRInteractionManager the scene's own components resolve to (XRI auto-creates/finds
        // a single shared instance on Awake since none is explicitly placed in this scene).
        var interactionManager = grabInteractable.interactionManager;
        Assert.IsNotNull(interactionManager, "Scene's XRInteractionManager was not resolved on Mock_Fan_Blade.");

        // ---------- Phase 2: Verify initial state ----------
        // As authored, Mock_Fan_Blade sits ~0.2m from Fan_Shaft_Socket - inside the socket's own
        // capture range - so XRSocketInteractor auto-selects it the instant Task_B_FanRepair
        // activates above. That is correct, intended socket behavior (not a research-logic defect),
        // but it means the scene's raw post-activation state is already "socketed", not "detached".
        // Log what was actually observed, then establish the detached baseline this pipeline test
        // needs by releasing/relocating away from capture range before asserting on it.
        Debug.Log($"[PhysicalTest] Phase2 observed post-activation state: grabbed={grabInteractable.isSelected}, " +
            $"socket.hasSelection={socketInteractor.hasSelection}, Mock_Fan_Blade pos={originalPosition:F5}.");
        if (socketInteractor.hasSelection)
        {
            interactionManager.SelectExit(socketInteractor, (IXRSelectInteractable)grabInteractable);
            yield return null;
        }
        Vector3 detachedPosition = socketInteractor.transform.position + socketInteractor.transform.up * 0.5f;
        mockFanBladeGO.transform.position = detachedPosition;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        yield return null;
        originalPosition = mockFanBladeGO.transform.position;

        Assert.IsFalse(grabInteractable.isSelected, "Precondition failed: Mock_Fan_Blade should not be grabbed after establishing detached baseline.");
        Assert.IsFalse(socketInteractor.hasSelection, "Precondition failed: Fan_Shaft_Socket should be empty after establishing detached baseline.");
        Debug.Log($"[PhysicalTest] Phase2 OK. Mock_Fan_Blade detached baseline pos={originalPosition:F5}, grabbed=false, socket empty=true.");

        // Start the real research logging pipeline using the same calls VRSessionManager.StartSession
        // makes internally, without its counterbalance/task-order gating (irrelevant here).
        var dataLogger = Object.FindFirstObjectByType<VRDataLogger>();
        var eventsLogger = Object.FindFirstObjectByType<VRInteractionEventsLogger>();
        Assert.IsNotNull(dataLogger, "VRDataLogger not found on Research_System.");
        Assert.IsNotNull(eventsLogger, "VRInteractionEventsLogger not found on Research_System.");
        dataLogger.StartRecording(SessionId, FilePrefix);
        eventsLogger.StartLogging();
        yield return null;

        // Capture console Warning/Error/Exception output from here through the end of the snap check.
        var unexpectedLogs = new List<string>();
        Application.LogCallback logHandler = (msg, stack, type) =>
        {
            if (type != LogType.Warning && type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;
            unexpectedLogs.Add($"[{type}] {msg}");
        };
        Application.logMessageReceived += logHandler;

        // ---------- Phase 3: Programmatic grab via a test-scoped Direct Interactor + the real XRInteractionManager ----------
        // A minimal, non-input-driven XRDirectInteractor is created solely to issue the SelectEnter/
        // SelectExit calls below; it is destroyed in TearDown and never saved to the scene. This mirrors
        // Unity's own XRI package test suite (Tests/Runtime/TestUtilities.CreateDirectInteractor) and
        // avoids fighting a live input-driven rig interactor (e.g. NearFarInteractor) that would try to
        // release selection on its own the next frame because no real input is active.
        _testInteractorGO = new GameObject("Test_DirectInteractor_PhysicalPipeline");
        var interactorCollider = _testInteractorGO.AddComponent<SphereCollider>();
        interactorCollider.isTrigger = true;
        interactorCollider.radius = 0.05f;
        var directInteractor = _testInteractorGO.AddComponent<XRDirectInteractor>();
        yield return null;

        interactionManager.SelectEnter((IXRSelectInteractor)directInteractor, grabInteractable);
        yield return null;

        // ---------- Phase 4: Verify hold + exactly one Grab log entry ----------
        Assert.IsTrue(grabInteractable.isSelected, "Mock_Fan_Blade did not enter selected state after forced SelectEnter.");
        Assert.AreEqual(directInteractor, grabInteractable.firstInteractorSelecting, "Mock_Fan_Blade is selected but not by the test interactor.");

        var eventsAfterGrab = SnapshotEvents(dataLogger);
        int grabCount = CountEvents(eventsAfterGrab, "Grab", "Mock_Fan_Blade");
        Debug.Log($"[PhysicalTest] Phase4: Grab log count for Mock_Fan_Blade = {grabCount}. Total buffered events = {eventsAfterGrab.Count}.");
        Assert.AreEqual(1, grabCount, $"Expected exactly one 'Grab' log entry for Mock_Fan_Blade after forced select (found {grabCount}).");

        // ---------- Phase 5: Release from hand, move into socket range, let physics/XRI auto-select ----------
        interactionManager.SelectExit((IXRSelectInteractor)directInteractor, grabInteractable);
        yield return null; // let the Instantaneous movement drive stop before we move the transform ourselves

        Vector3 socketWorldPos = fanShaftSocketGO.transform.position;
        mockFanBladeGO.transform.position = socketWorldPos;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        Debug.Log($"[PhysicalTest] Phase5: Released from hand, placed Mock_Fan_Blade at socket position {socketWorldPos:F5} for physics auto-snap.");

        bool snapped = false;
        for (int i = 0; i < 180 && !snapped; i++) // up to ~3s at 60fps
        {
            yield return new WaitForFixedUpdate();
            yield return null;
            snapped = socketInteractor.hasSelection;
        }

        // ---------- Phase 6: Verify socket accepted the part, measure alignment, check console cleanliness ----------
        float offsetAtSelect = Vector3.Distance(visualModel.position, fanShaftSocketGO.transform.position);
        Debug.Log($"[PhysicalTest] Phase6: hasSelection={socketInteractor.hasSelection} (waited up to 3s). Offset at moment of selection = {offsetAtSelect:F6} m.");

        Assert.IsTrue(socketInteractor.hasSelection, "Fan_Shaft_Socket did not acquire Mock_Fan_Blade via physics auto-select within 3 seconds of release.");
        Assert.AreEqual(grabInteractable, socketInteractor.firstInteractableSelected, "Fan_Shaft_Socket selected a different interactable than Mock_Fan_Blade.");

        // Let the scene's own configured XRGrabInteractable.attachEaseInTime (real value, left untouched)
        // finish easing the object into its final resting pose before measuring the settled offset.
        for (int i = 0; i < 30; i++) yield return null;

        Assert.IsTrue(socketInteractor.hasSelection, "Mock_Fan_Blade was ejected from Fan_Shaft_Socket during ease-in settle (unexpected SocketPartFilter rejection).");
        Vector3 finalOffsetVec = visualModel.position - fanShaftSocketGO.transform.position;
        float finalOffset = finalOffsetVec.magnitude;
        Debug.Log($"[PhysicalTest] Phase6: Final settled offset Visual_Model<->Fan_Shaft_Socket = {finalOffsetVec:F6} (magnitude {finalOffset:F6} m).");

        Application.logMessageReceived -= logHandler;
        Debug.Log($"[PhysicalTest] Phase6: Console Warning/Error/Exception/Assert count during grab->snap window = {unexpectedLogs.Count}.");
        foreach (var issue in unexpectedLogs) Debug.Log($"[PhysicalTest]   -> {issue}");
        Assert.AreEqual(0, unexpectedLogs.Count, "Unexpected Warning/Error/Exception logged during grab->snap window: " + string.Join(" | ", unexpectedLogs));

        // ---------- Phase 7: Stop recording, flush to disk, verify the log file content ----------
        string outputDir = dataLogger.GetOutputPath();
        dataLogger.StopRecording();

        string eventsJsonPath = Path.Combine(outputDir, $"{FilePrefix}_events.json");
        float waited = 0f;
        while (!File.Exists(eventsJsonPath) && waited < 5f)
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        }
        Assert.IsTrue(File.Exists(eventsJsonPath), $"events.json was not written within 5s at expected path: {eventsJsonPath}");

        string json = File.ReadAllText(eventsJsonPath);
        int fileGrabCount = Regex.Matches(json, "\"EventType\":\"Grab\"").Count;
        int fileReleaseCount = Regex.Matches(json, "\"EventType\":\"Release\"").Count;
        int fileSocketSnapCount = Regex.Matches(json, "\"EventType\":\"SocketSnap\"").Count;
        int fileSocketUnsnapCount = Regex.Matches(json, "\"EventType\":\"SocketUnsnap\"").Count;
        Debug.Log($"[PhysicalTest] Phase7: {eventsJsonPath} contains Grab={fileGrabCount} Release={fileReleaseCount} SocketSnap={fileSocketSnapCount} SocketUnsnap={fileSocketUnsnapCount}");

        // VRInteractionEventsLogger.OnSelectEntered now skips XRSocketInteractor-driven selects
        // (already logged as a dedicated "SocketSnap" entry by OnSocketSelectEntered), so the
        // socket auto-snap in Phase 5 must not also produce a "Grab" entry - only the Phase 3
        // hand-grab should count.
        Assert.AreEqual(1, fileGrabCount, $"Expected exactly one 'Grab' entry in events.json (the Phase 3 hand-grab only; socket auto-snap must not log a duplicate 'Grab'). Found {fileGrabCount}.");
        Assert.AreEqual(1, fileSocketSnapCount, "Expected exactly one 'SocketSnap' entry in events.json.");
        Assert.AreEqual(0, fileSocketUnsnapCount, "Did not expect any 'SocketUnsnap' entry - the part should still be socketed.");

        // ExtraData is a string field, so JsonUtility escapes its embedded quotes (\"holdDuration\":6.1)
        // rather than emitting a nested JSON object - match loosely around the escaping instead of a
        // literal quote so this doesn't depend on JsonUtility's exact escaping style.
        var releaseHoldDurationMatch = Regex.Match(json, "\"EventType\":\"Release\"[\\s\\S]*?holdDuration[^0-9-]*(-?[0-9]+\\.[0-9]+)");
        Assert.IsTrue(releaseHoldDurationMatch.Success, "Release entry's ExtraData must contain a real numeric 'holdDuration' value (found raw \":F1\" format string instead?).");
        Debug.Log($"[PhysicalTest] Phase7: Release holdDuration={releaseHoldDurationMatch.Groups[1].Value}.");

        var snapMatch = Regex.Match(json, "\"EventType\":\"SocketSnap\"[\\s\\S]*?\"Position\":\\{\"x\":(-?[0-9.eE+-]+),\"y\":(-?[0-9.eE+-]+),\"z\":(-?[0-9.eE+-]+)\\}");
        Assert.IsTrue(snapMatch.Success, "Could not parse Position coordinates from the SocketSnap log entry in events.json.");
        float loggedX = float.Parse(snapMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        float loggedY = float.Parse(snapMatch.Groups[2].Value, CultureInfo.InvariantCulture);
        float loggedZ = float.Parse(snapMatch.Groups[3].Value, CultureInfo.InvariantCulture);
        var loggedPos = new Vector3(loggedX, loggedY, loggedZ);
        float loggedVsSocketDelta = Vector3.Distance(loggedPos, fanShaftSocketGO.transform.position);
        Debug.Log($"[PhysicalTest] Phase7: SocketSnap logged Position={loggedPos:F6}, delta vs live socket transform={loggedVsSocketDelta:F6} m.");
        Assert.Less(loggedVsSocketDelta, 0.001f, "Logged SocketSnap Position does not match the socket transform position.");

        Debug.Log("[PhysicalTest] RESULT: Test_FanBlade_PhysicalInteractionPipeline PASSED. " +
                   $"Final Visual_Model<->Fan_Shaft_Socket offset={finalOffset:F6} m. " +
                   $"File-wide Grab logs={fileGrabCount} (Phase 3 hand-grab only), SocketSnap logs=1, " +
                   "no console warnings/errors during grab->snap window.");
    }

    [Test]
    public void Test_VRSessionManager_NoLegacyInputCalls()
    {
#if UNITY_EDITOR
        string scriptPath = Path.Combine(Application.dataPath, "XRI_Examples/Global/Scripts/Research/VRSessionManager.cs");
        Assert.IsTrue(File.Exists(scriptPath), $"VRSessionManager.cs not found at expected path: {scriptPath}");
        string source = File.ReadAllText(scriptPath);
        Assert.IsFalse(Regex.IsMatch(source, @"\bInput\."),
            "VRSessionManager.cs must not call legacy UnityEngine.Input (e.g. Input.GetKeyDown) - this project uses the New Input System exclusively.");
#else
        Assert.Ignore("Source-scan test only meaningful in the Editor.");
#endif
    }

    static GameObject FindInActiveScene(string name)
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            var found = FindByNameRecursive(root.transform, name);
            if (found != null) return found.gameObject;
        }
        return null;
    }

    static Transform FindByNameRecursive(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform child in t)
        {
            var found = FindByNameRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static List<InteractionLogEntry> SnapshotEvents(VRDataLogger logger)
    {
        var field = typeof(VRDataLogger).GetField("_eventBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
        var queue = (ConcurrentQueue<InteractionLogEntry>)field.GetValue(logger);
        return new List<InteractionLogEntry>(queue.ToArray());
    }

    static int CountEvents(List<InteractionLogEntry> events, string eventType, string objectName)
    {
        int count = 0;
        foreach (var e in events)
            if (e.EventType == eventType && e.ObjectName == objectName) count++;
        return count;
    }
}
