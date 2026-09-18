using UnityEngine;
namespace VRMaintenanceResearch.TrainingV2
{
    [RequireComponent(typeof(UnityEngine.UI.Button))]
    public sealed class TrainingPanelCommand : MonoBehaviour
    {
        [SerializeField] TrainingPanel owner;
        [SerializeField] string id;
        public string Id => id;
        public void Configure(TrainingPanel panel, string command) { owner = panel; id = command; }
        void OnEnable() { GetComponent<UnityEngine.UI.Button>().onClick.AddListener(Execute); }
        void OnDisable() { GetComponent<UnityEngine.UI.Button>().onClick.RemoveListener(Execute); }
        void Execute() { if (owner != null) owner.Dispatch(id); }
    }
}
