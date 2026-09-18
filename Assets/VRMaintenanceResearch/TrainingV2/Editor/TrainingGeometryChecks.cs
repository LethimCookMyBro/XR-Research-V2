#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRTrainingV2.Editor
{
    public static class TrainingGeometryChecks
    {
        // Isolated physics scene: no authored scene physics is advanced by this fixture.
        // Direct transforms and simulation are logic evidence, never input-driven evidence.
        public static string Run()
        {
            if(Application.isPlaying)throw new InvalidOperationException("Run geometry fixture in Edit Mode");
            var scene=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var physics=scene.GetPhysicsScene();
            var root=new GameObject("TrainingGeometry_TEST_ONLY");
            SceneManager.MoveGameObjectToScene(root,scene);
            try
            {
                if(!physics.IsValid() || physics.Equals(Physics.defaultPhysicsScene))throw new InvalidOperationException("Preview fixture requires an isolated physics scene; global physics will not be advanced");
                var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.transform.SetParent(root.transform);pad.transform.position=Vector3.zero;pad.transform.localScale=new Vector3(.3f,.018f,.34f);
                var piece=GameObject.CreatePrimitive(PrimitiveType.Cube);piece.transform.SetParent(root.transform);piece.transform.localScale=new Vector3(.14f,.10f,.18f);
                var body=piece.AddComponent<Rigidbody>();body.useGravity=true;
                var p=root.AddComponent<TrainingPrototype>();p.block=piece.AddComponent<XRGrabInteractable>();p.blockCollider=piece.GetComponent<BoxCollider>();p.targetPad=pad.GetComponent<BoxCollider>();
                var position=pad.transform.position+Vector3.up*.059f;
                void Check(bool test,string message){if(!test)throw new Exception("TRAINING_GEOMETRY_FAIL "+message);}
                void Move(Vector3 value,Quaternion rotation)
                {
                    body.position=value;body.rotation=rotation;piece.transform.SetPositionAndRotation(value,rotation);
                    body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;body.WakeUp();Physics.SyncTransforms();
                }
                void Settle(Vector3 offset,Quaternion rotation)
                {
                    Move(position+offset+Vector3.up*.03f,rotation);
                    for(int step=0;step<120;step++)physics.Simulate(.02f);
                    Physics.SyncTransforms();
                }
                Settle(Vector3.zero,Quaternion.identity);
                Check(new TrainingRules().Validate(p.Snapshot())=="pass","physically settled centred block passes first time");
                Settle(Vector3.right*.03f,Quaternion.identity);
                Check(Mathf.Abs(p.Snapshot().positionErrorM-.03f)<.001f,"world metres independent of target scale");
                Check(new TrainingRules().Validate(p.Snapshot())=="pass","prototype tolerance allows 3 cm after contact");
                Settle(Vector3.right*.06f,Quaternion.identity);
                Check(new TrainingRules().Validate(p.Snapshot())=="position_mismatch","6 cm does not pass merely nearby");
                Settle(Vector3.zero,Quaternion.Euler(0,90,0));
                Check(new TrainingRules().Validate(p.Snapshot())=="orientation_mismatch","wrong orientation fails after contact");
                Move(position+Vector3.up*.01f,Quaternion.identity);
                var hover=p.Snapshot();
                Check(hover.speedMps==0f&&!hover.supported&&new TrainingRules().Validate(hover)!="pass","1 cm zero-speed hover is not contact before physics step");
                for(int step=0;step<120;step++)physics.Simulate(.02f);
                Check(new TrainingRules().Validate(p.Snapshot())=="pass","hover passes only after falling onto support");
                Move(position+Vector3.up*.04f,Quaternion.identity);
                Check(new TrainingRules().Validate(p.Snapshot())!="pass","4 cm hovering block fails");
                Move(position+Vector3.right*.17f,Quaternion.identity);
                Check(!p.Snapshot().insideTarget,"full object containment enforced");
                return "TRAINING_GEOMETRY_PASS checks=9; isolated simulated physics, not input-driven";
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                Physics.SyncTransforms();
            }
        }
    }
}
#endif