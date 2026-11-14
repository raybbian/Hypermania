using Game.Sim;
using Game.View.Fighters;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using Utils;

namespace Game.View.Overlay
{
    // Black and white impact frames on a killing blow. While it runs, the
    // whole screen is replaced by an ink drawing of the two fighters over
    // speed lines, flipping between a white and a black background. It also
    // says how the music and camera should react, for GameView to pass on.
    // Lives on the battle camera, next to its FighterSilhouetteCapture.
    [RequireComponent(typeof(Camera), typeof(FighterSilhouetteCapture))]
    public class ImpactFrameView : MonoBehaviour
    {
        // Matches _ImpactFrame in VFX/ImpactFrame/ImpactFrame.shader.
        private static readonly int ImpactFrameId = Shader.PropertyToID("_ImpactFrame");

        [SerializeField]
        private Material _material;

        // How many flashes play, alternating white and black backgrounds.
        [SerializeField]
        private int _flashes = 3;

        [SerializeField]
        private int _ticksPerFlash = 6;

        // How far the fighters punch in toward the hit on the first tick.
        // It eases back out to nothing by the end.
        [SerializeField]
        private float _punchZoom = 1.25f;

        // The music drops to this much of its volume while the impact frames
        // play, then swells back over the return ticks.
        [SerializeField]
        private float _musicVolume = 0.25f;

        [SerializeField]
        private int _musicReturnTicks = 30;

        public float MusicVolume { get; private set; } = 1f;

        // The camera stays pushed in on the hit for this long after the
        // blow, well past the impact frames themselves.
        [SerializeField]
        private int _cameraHoldTicks = 75;

        // Where the camera should hold, or null to frame the fight as usual.
        public Vector2? CameraFocus { get; private set; }

        private Camera _camera;
        private ImpactFramePass _pass;
        private GameOptions _options;
        private Frame _impactFrame = Frame.NullFrame;
        private Vector2 _hitLocation;
        private bool _active;
        private int _flash;
        private float _zoom;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _pass = new ImpactFramePass();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _pass = null;
        }

        public void Init(GameOptions options)
        {
            _options = options;
            _impactFrame = Frame.NullFrame;
        }

        // Runs for every simulated frame, so the one-frame hit notifier on the
        // killing blow is never skipped.
        public void RollbackRender(in GameState state)
        {
            for (int i = 0; i < state.Fighters.Length; i++)
            {
                if (IsKillingBlow(state, i))
                {
                    _impactFrame = state.RealFrame;
                    _hitLocation = (Vector2)state.Fighters[i].HitLocation.Value;
                    return;
                }
            }

            // This frame was simulated again after a rollback and the blow
            // no longer lands on it.
            if (state.RealFrame == _impactFrame)
                _impactFrame = Frame.NullFrame;
        }

        // Deaths without a hit (time up) get no impact frames.
        private bool IsKillingBlow(in GameState state, int victim)
        {
            if (!state.Fighters[victim].HitLastRealFrame)
                return false;

            if (state.GameMode == GameMode.RoundEnd && state.Fighters[victim].State == CharacterState.Death)
                return true;

            // Someone who runs out of health mid rhythm combo is kept alive
            // until it is over, and then dies without being hit. The combo's
            // finisher is the blow that counts for them.
            return state.Fighters[victim ^ 1].RhythmComboFinisherActive
                && state.Fighters[victim].Health <= 0
                && !_options.Players[victim].Immortal;
        }

        public void Render(in GameState state)
        {
            int elapsed = state.RealFrame - _impactFrame;
            int duration = _flashes * _ticksPerFlash;
            bool landed = _impactFrame != Frame.NullFrame && elapsed >= 0;

            // The lerp clamps, so this holds the dip until the frames end.
            float musicReturn = (float)(elapsed - duration) / Mathf.Max(1, _musicReturnTicks);
            MusicVolume = landed ? Mathf.Lerp(_musicVolume, 1f, musicReturn) : 1f;
            CameraFocus = landed && elapsed < _cameraHoldTicks ? _hitLocation : (Vector2?)null;

            _active = landed && elapsed < duration;
            if (!_active)
                return;

            _flash = elapsed / _ticksPerFlash;
            float remaining = 1f - (float)elapsed / duration;
            _zoom = 1f + (_punchZoom - 1f) * remaining * remaining;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (!_active || cam != _camera)
                return;

            // Worked out here so it uses where the camera ended up this frame.
            Vector3 center = cam.WorldToViewportPoint(_hitLocation);
            Shader.SetGlobalVector(ImpactFrameId, new Vector4(center.x, center.y, _flash, _zoom));
            _pass.Material = _material;
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
        }

        private class ImpactFramePass : ScriptableRenderPass
        {
            public Material Material;

            private class PassData
            {
                public Material Material;
            }

            public ImpactFramePass()
            {
                // After post processing so bloom and friends leave it crisp.
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("ImpactFrame", out PassData passData))
                {
                    passData.Material = Material;

                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                    // Reads the fighter silhouettes.
                    builder.UseAllGlobalTextures(true);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc(
                        static (PassData data, RasterGraphContext ctx) =>
                        {
                            ctx.cmd.DrawProcedural(Matrix4x4.identity, data.Material, 0, MeshTopology.Triangles, 3);
                        }
                    );
                }
            }
        }
    }
}
