using System.Collections;
using Design.Configs;
using Game.Sim;
using Game.View.Events;
using Game.View.Fighters;
using UnityEngine;
using UnityEngine.UI;
using Utils;
using Utils.SoftFloat;

namespace Game.View.Overlay
{
    [RequireComponent(typeof(Animator))]
    public class SuperDisplayView : MonoBehaviour
    {
        [SerializeField]
        private RenderTexture _renderTexture;

        [SerializeField]
        private GameObject _stageRoot;

        [SerializeField]
        private RectTransform _sizeTarget;

        [SerializeField]
        private string _hiddenLayerName = "SuperDisplay";

        [SerializeField]
        private string _showStateName;

        [SerializeField]
        private string _hideStateName;

        [SerializeField]
        private Image[] _mainImages;

        [SerializeField]
        private Image[] _lightImages;

        [SerializeField]
        private Image[] _accentImages;
        private Animator _overlayAnimator;
        private Camera _spawnedCamera;
        private FighterView _spawnedFighter;
        private Coroutine _hideRoutine;
        private bool _prevIsSuperAttack;
        private bool _active;
        private CharacterConfig _config;
        private int _postDisplayHitstopTicks;
        private int _displayStartFrame;
        private FighterState _dummyFighter;

        public void Init(CharacterConfig config, int skinIndex, int postDisplayHitstopTicks)
        {
            _config = config;
            _postDisplayHitstopTicks = postDisplayHitstopTicks;

            foreach (var img in _mainImages)
                img.color = config.Skins[skinIndex].MainColor;
            foreach (var img in _lightImages)
                img.color = config.Skins[skinIndex].LightColor;
            foreach (var img in _accentImages)
                img.color = config.Skins[skinIndex].AccentColor;
            int hiddenLayer = LayerMask.NameToLayer(_hiddenLayerName);

            _spawnedFighter = Instantiate(config.Prefab, _stageRoot.transform);
            _spawnedFighter.Init(config, skinIndex);
            SetLayerRecursive(_spawnedFighter.gameObject, hiddenLayer);

            var camGo = new GameObject("SuperDisplayCamera");
            camGo.transform.SetParent(_stageRoot.transform, false);
            camGo.transform.localPosition = config.SuperDisplay.CameraLocalPosition;
            camGo.transform.localEulerAngles = Vector3.zero;
            camGo.layer = hiddenLayer;

            _spawnedCamera = camGo.AddComponent<Camera>();
            _spawnedCamera.cullingMask = 1 << hiddenLayer;
            _spawnedCamera.clearFlags = CameraClearFlags.SolidColor;
            _spawnedCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _spawnedCamera.orthographic = true;
            _spawnedCamera.orthographicSize = (float)config.SuperDisplay.CameraOrthoSize;
            _spawnedCamera.targetTexture = _renderTexture;

            _overlayAnimator = GetComponent<Animator>();
            _overlayAnimator.speed = 0f;

            _stageRoot.SetActive(false);

            ResizeRenderTextureToTarget();
        }

        public void Render(in GameState state, in FighterState fighter, SfxManager sfxManager, int playerIndex)
        {
            bool nowSuper = fighter.IsSuperAttack;
            if (!_prevIsSuperAttack && nowSuper)
            {
                Show(_config.SuperDisplay.AnimState, _config.SuperDisplay.StartFrame);
                _active = true;
                sfxManager.AddDesired(SfxKind.SuperStart, state.RealFrame, hash: playerIndex);
            }
            else if (_active && state.HitstopFramesRemaining <= _postDisplayHitstopTicks)
            {
                Hide();
                _active = false;
            }
            _prevIsSuperAttack = nowSuper;

            if (_stageRoot.activeSelf)
            {
                Vector3 worldPos = _spawnedFighter.transform.position;
                _dummyFighter.Position = new SVector2((sfloat)worldPos.x, (sfloat)worldPos.y);
                _spawnedFighter.Render(Frame.FirstFrame + _displayStartFrame, _dummyFighter);
            }
        }

        public void Show(CharacterState animState, int characterStartFrame)
        {
            _overlayAnimator.speed = 1f;
            _stageRoot.SetActive(true);

            _displayStartFrame = characterStartFrame;
            _dummyFighter = FighterState.CreateForDisplay(
                animState,
                Frame.FirstFrame,
                SVector2.zero,
                FighterFacing.Right
            );

            _overlayAnimator.Play(_showStateName);
        }

        public void Hide()
        {
            if (_hideRoutine != null)
                return;
            _hideRoutine = StartCoroutine(HideRoutine());
        }

        private IEnumerator HideRoutine()
        {
            if (!string.IsNullOrEmpty(_hideStateName))
            {
                _overlayAnimator.Play(_hideStateName);
                yield return null;
                var info = _overlayAnimator.GetCurrentAnimatorStateInfo(0);
                yield return new WaitForSeconds(info.length);
            }
            _hideRoutine = null;
            _stageRoot.SetActive(false);
        }

        private void ResizeRenderTextureToTarget()
        {
            if (_renderTexture == null || _sizeTarget == null)
                return;

            var size = _sizeTarget.rect.size;
            int w = Mathf.Abs(Mathf.RoundToInt(size.x));
            int h = Mathf.Abs(Mathf.RoundToInt(size.y));

            if (_renderTexture.width == w && _renderTexture.height == h)
                return;

            _renderTexture.Release();
            _renderTexture.width = w;
            _renderTexture.height = h;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
