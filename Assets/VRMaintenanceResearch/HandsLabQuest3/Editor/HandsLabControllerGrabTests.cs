#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

namespace VRMaintenanceResearch.HandsLabQuest3.Editor
{
    public static partial class HandsLabEditor
    {
        // Real controllers update pose while squeezing; the original fixture squeezed at a frozen pose.
        static IEnumerator MovingControllerGrip()
        {
            yield return 1d;
            Lab.ResetObjects();
            var mug = Lab.objects.Single(o => o.stableId == "handslab.grip_mug");
            var pose = PickPose(mug, 1);
            for (int i = 0; i < 24; i++) { AimWrist(1, pose); yield return .06d; }
            for (int i = 0; i < 12; i++) { ApproachPalm(1, mug); yield return .08d; }
            yield return .3d;
            Check(Lab.guards[1].InteractionsAllowed, "right input guard allows moving controller");
            rightGrip = 1;
            for (int i = 0; i < 12; i++) { rightPosition += Vector3.right * .0001f; yield return .03d; }
            yield return .5d;
            Check(mug.IsHeld, "grip selects mug while controller pose changes");
            rightGrip = 0;
            for (int i = 0; i < 12; i++) { rightPosition -= Vector3.right * .0001f; yield return .03d; }
            yield return .5d;
            Check(!mug.IsHeld, "grip release works while controller pose changes");
            inputSequenceComplete = true;
        }
    }
}
#endif
