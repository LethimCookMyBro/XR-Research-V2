using UnityEngine;
using UnityEngine.UI;

namespace UnityEngine.XR.Content.Interaction.Research
{
    /// <summary>
    /// World-space coaching panel that floats in front of the player,
    /// displaying current task instructions and progress.
    /// </summary>
    public class VRResearchCoachingUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private TMPro.TextMeshProUGUI _titleText;
        [SerializeField] private TMPro.TextMeshProUGUI _instructionText;
        [SerializeField] private TMPro.TextMeshProUGUI _progressText;
        [SerializeField] private Image _borderImage;
        [SerializeField] private Image _backgroundImage;

        [Header("Positioning")]
        [SerializeField] private Transform _headTransform;
        [SerializeField] private float _distanceFromHead = 1.5f;
        [SerializeField] private float _heightOffset = 0.3f;
        [SerializeField] private float _followSpeed = 3f;
        [SerializeField] private float _lookAtSpeed = 5f;
        [SerializeField] private bool _followHead = true;

        [Header("Visual Settings")]
        [SerializeField] private Color _defaultBorderColor = new Color(0.2f, 0.6f, 1f, 0.9f);
        [SerializeField] private Color _completedBorderColor = new Color(0.2f, 0.9f, 0.3f, 0.9f);
        [SerializeField] private Color _failedBorderColor = new Color(0.9f, 0.2f, 0.2f, 0.9f);
        [SerializeField] private Color _backgroundColor = new Color(0.1f, 0.1f, 0.15f, 0.85f);
        [SerializeField] private float _fadeSpeed = 2f;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _taskStartClip;
        [SerializeField] private AudioClip _taskCompleteClip;
        [SerializeField] private AudioClip _taskFailedClip;
        [SerializeField] private AudioClip _scenarioCompleteClip;

        private CanvasGroup _canvasGroup;
        private float _targetAlpha = 1f;
        private bool _isVisible;

        private void Awake()
        {
            if (_canvas == null) _canvas = GetComponentInChildren<Canvas>();
            if (_canvas != null)
            {
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvasGroup = _canvas.gameObject.AddComponent<CanvasGroup>();
            }

            if (_headTransform == null)
            {
                var cam = Camera.main;
                if (cam != null) _headTransform = cam.transform;
            }

            // Set initial position
            if (_canvas != null)
            {
                _canvas.transform.localPosition = new Vector3(0, 1.5f, 1.5f);
                _canvas.transform.localScale = Vector3.one * 0.001f;
            }
        }

        private void Start()
        {
            // Set initial colors
            if (_borderImage != null) _borderImage.color = _defaultBorderColor;
            if (_backgroundImage != null) _backgroundImage.color = _backgroundColor;

            Show();
        }

        private void LateUpdate()
        {
            if (!_followHead || _headTransform == null || _canvas == null) return;

            // Calculate target position (in front of head, at eye level)
            Vector3 targetPos = _headTransform.position
                + _headTransform.forward * _distanceFromHead
                + Vector3.up * _heightOffset;

            // Smooth follow
            _canvas.transform.position = Vector3.Lerp(
                _canvas.transform.position,
                targetPos,
                _followSpeed * Time.deltaTime
            );

            // Look at head
            Vector3 lookDir = _headTransform.position - _canvas.transform.position;
            lookDir.y = 0; // Keep upright
            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(-lookDir);
                _canvas.transform.rotation = Quaternion.Slerp(
                    _canvas.transform.rotation,
                    targetRot,
                    _lookAtSpeed * Time.deltaTime
                );
            }

            // Fade
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = Mathf.Lerp(_canvasGroup.alpha, _targetAlpha, _fadeSpeed * Time.deltaTime);
            }
        }

        /// <summary>
        /// Update the coaching UI with a new task.
        /// </summary>
        public void UpdateTask(int taskIndex, int totalTasks, ResearchTask task)
        {
            if (_titleText != null)
                _titleText.text = $"MISSION {taskIndex + 1}/{totalTasks}";

            if (_instructionText != null)
                _instructionText.text = task.instructionText;

            if (_progressText != null)
                _progressText.text = $"Progress: {taskIndex + 1} / {totalTasks}";

            // Reset border color
            if (_borderImage != null)
                _borderImage.color = _defaultBorderColor;

            Show();
            PlaySound(_taskStartClip);
        }

        /// <summary>
        /// Show completion state.
        /// </summary>
        public void ShowCompletion()
        {
            if (_titleText != null)
                _titleText.text = "SCENARIO COMPLETE";

            if (_instructionText != null)
                _instructionText.text = "Thank you for participating!\nThe recording will stop automatically.";

            if (_progressText != null)
                _progressText.text = "";

            if (_borderImage != null)
                _borderImage.color = _completedBorderColor;

            PlaySound(_scenarioCompleteClip);
            Show();
        }

        /// <summary>
        /// Flash completed state for current task.
        /// </summary>
        public void FlashCompleted()
        {
            if (_borderImage != null)
                _borderImage.color = _completedBorderColor;

            PlaySound(_taskCompleteClip);

            // Reset after delay
            Invoke(nameof(ResetBorderColor), 2f);
        }

        /// <summary>
        /// Flash failed state for current task.
        /// </summary>
        public void FlashFailed()
        {
            if (_borderImage != null)
                _borderImage.color = _failedBorderColor;

            PlaySound(_taskFailedClip);

            Invoke(nameof(ResetBorderColor), 2f);
        }

        private void ResetBorderColor()
        {
            if (_borderImage != null)
                _borderImage.color = _defaultBorderColor;
        }

        public void Show()
        {
            _isVisible = true;
            _targetAlpha = 1f;
            if (_canvasGroup != null) _canvasGroup.alpha = 1f;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            _isVisible = false;
            _targetAlpha = 0f;
        }

        private void PlaySound(AudioClip clip)
        {
            if (_audioSource != null && clip != null)
            {
                _audioSource.PlayOneShot(clip);
            }
        }
    }
}
