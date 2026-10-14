using System;

namespace VRMaintenanceResearch.ResearchFlow
{
    public enum ParticipantGroup { Thai, Japanese, Dev, Test, Pilot }
    public enum ParticipantLanguage { Thai, Japanese, English }
    public enum ExperimentInputMode { Controller, HandTracking, TechnicalBoth }
    public enum TaskOrder { AB, BA }
    public enum RegistryState { Available, InProgress, Completed, Aborted, Invalid }
    public enum ResearchFlowState
    {
        ResearcherSetup, TutorialWelcome, TutorialInteractions, TutorialReferenceInspect,
        TutorialComplete, Task1Ready, Task1Active, PostTask1Checkpoint, Task2Ready,
        Task2Active, PostTask2Checkpoint, ParticipantComplete, ResearcherSummary
    }
    public enum TutorialStep { Movement, PointSelect, GrabRotateRelease, Reference, Inspect }

    /// <summary>Pure session gate. The runtime persists each accepted transition before enabling interaction.</summary>
    public sealed class ResearchSessionFlow
    {
        public TaskOrder Order { get; }
        public ResearchFlowState State { get; private set; }
        public int TutorialCompleted { get; private set; }
        public bool Paused { get; private set; }
        public bool? InspectPassed { get; private set; }
        public bool IsTutorial => State >= ResearchFlowState.TutorialWelcome && State <= ResearchFlowState.TutorialComplete;
        public bool IsTaskActive => State == ResearchFlowState.Task1Active || State == ResearchFlowState.Task2Active;
        public bool CanInteract => !Paused && !InspectPassed.HasValue && (IsTaskActive || State == ResearchFlowState.TutorialInteractions || State == ResearchFlowState.TutorialReferenceInspect);
        public string CurrentTask
        {
            get
            {
                if (State < ResearchFlowState.Task1Ready || State > ResearchFlowState.PostTask2Checkpoint) return "";
                bool first = State <= ResearchFlowState.PostTask1Checkpoint;
                return (first == (Order == TaskOrder.AB)) ? "A" : "B";
            }
        }

        public ResearchSessionFlow(TaskOrder order, ResearchFlowState state = ResearchFlowState.ResearcherSetup,
            int tutorialCompleted = 0, bool paused = false, bool? inspectPassed = null)
        {
            if (!Enum.IsDefined(typeof(TaskOrder), order) || !Enum.IsDefined(typeof(ResearchFlowState), state)
                || tutorialCompleted < 0 || tutorialCompleted > 5) throw new ArgumentException("Invalid flow snapshot");
            if ((state <= ResearchFlowState.TutorialWelcome && tutorialCompleted != 0)
                || (state == ResearchFlowState.TutorialInteractions && tutorialCompleted > 2)
                || (state == ResearchFlowState.TutorialReferenceInspect && (tutorialCompleted < 3 || tutorialCompleted > 4))
                || (state >= ResearchFlowState.TutorialComplete && tutorialCompleted != 5)) throw new ArgumentException("Tutorial progress is inconsistent with flow state");
            Order = order; State = state; TutorialCompleted = tutorialCompleted; Paused = paused; InspectPassed = inspectPassed;
            if (inspectPassed.HasValue && !IsTaskActive) throw new ArgumentException("Inspect modal requires an active task");
        }

        public bool StartSession()
        {
            if (State != ResearchFlowState.ResearcherSetup) return false;
            State = ResearchFlowState.TutorialWelcome; return true;
        }
        public bool StartTutorial()
        {
            if (Paused || State != ResearchFlowState.TutorialWelcome) return false;
            State = ResearchFlowState.TutorialInteractions; return true;
        }
        public bool CompleteTutorialStep(TutorialStep step)
        {
            if (!CanInteract || !IsTutorial || (int)step != TutorialCompleted) return false;
            TutorialCompleted++;
            if (TutorialCompleted == 3) State = ResearchFlowState.TutorialReferenceInspect;
            if (TutorialCompleted == 5) State = ResearchFlowState.TutorialComplete;
            return true;
        }
        public bool Continue()
        {
            if (Paused || State != ResearchFlowState.TutorialComplete) return false;
            State = ResearchFlowState.Task1Ready; return true;
        }
        public bool StartTask()
        {
            if (Paused) return false;
            if (State == ResearchFlowState.Task1Ready) State = ResearchFlowState.Task1Active;
            else if (State == ResearchFlowState.Task2Ready) State = ResearchFlowState.Task2Active;
            else return false;
            return true;
        }
        public bool Inspect(bool passed)
        {
            if (!IsTaskActive || !CanInteract) return false;
            InspectPassed = passed; return true;
        }
        public bool ContinueInspect()
        {
            if (Paused || !InspectPassed.HasValue) return false;
            if (InspectPassed.Value) State = State == ResearchFlowState.Task1Active ? ResearchFlowState.PostTask1Checkpoint : ResearchFlowState.PostTask2Checkpoint;
            InspectPassed = null; return true;
        }
        public bool EndTask()
        {
            if (!IsTaskActive) return false;
            State = State == ResearchFlowState.Task1Active ? ResearchFlowState.PostTask1Checkpoint : ResearchFlowState.PostTask2Checkpoint;
            InspectPassed = null; return true;
        }
        // Checkpoint advance is researcher-only. ResearcherResume clears a pause; the controller decides
        // who may call it (participant only in technical sessions or the tutorial).
        public bool ResearcherResumeCheckpoint()
        {
            if (Paused) return false;
            if (State == ResearchFlowState.PostTask1Checkpoint) State = ResearchFlowState.Task2Ready;
            else if (State == ResearchFlowState.PostTask2Checkpoint) State = ResearchFlowState.ParticipantComplete;
            else return false;
            return true;
        }
        public bool OpenResearcherSummary()
        {
            if (State != ResearchFlowState.ParticipantComplete) return false;
            State = ResearchFlowState.ResearcherSummary; return true;
        }
        public bool Pause()
        {
            if (Paused || State == ResearchFlowState.ResearcherSetup || State >= ResearchFlowState.ParticipantComplete) return false;
            Paused = true; return true;
        }
        public bool ResearcherResume()
        {
            if (!Paused) return false;
            Paused = false; return true;
        }
    }
}

