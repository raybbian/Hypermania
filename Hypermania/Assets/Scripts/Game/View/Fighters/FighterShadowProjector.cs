using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.View.Fighters
{
    // Sits on a big quad under the camera. Each frame every fighter is drawn
    // into its own offscreen silhouette, and the quad's material lays those
    // silhouettes down onto the floor. Purely image based, so whatever is on
    // screen (jumps, hit jitter, root motion) is what casts the shadow.
    [RequireComponent(typeof(MeshRenderer))]
    public class FighterShadowProjector : MonoBehaviour
    {
        private const int MaxCasters = 2;

        [SerializeField]
        private Camera _camera;

        // One entry per player, matching the outline layers.
        [SerializeField]
        private LayerMask[] _casterLayers;

        // Has to sort behind the fighters but above the stage art.
        [SerializeField]
        private string _sortingLayer = "Character";

        [SerializeField]
        private int _sortingOrder = -100;

        private SilhouettePass _pass;

        private void OnEnable()
        {
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.sortingLayerName = _sortingLayer;
            meshRenderer.sortingOrder = _sortingOrder;

            _pass = new SilhouettePass(SortingLayer.GetLayerValueFromName(_sortingLayer));
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _pass.Dispose();
            _pass = null;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (cam != _camera)
                return;
            _pass.CasterLayers = _casterLayers;
            cam.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(_pass);
        }

        private class SilhouettePass : ScriptableRenderPass
        {
            private static readonly int[] SilhouetteIds =
            {
                Shader.PropertyToID("_FighterSilhouette0"),
                Shader.PropertyToID("_FighterSilhouette1"),
            };

            private static readonly List<ShaderTagId> ShaderTags = new List<ShaderTagId>
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("Universal2D"),
            };

            public LayerMask[] CasterLayers;

            // Persistent so they are still valid when the shadow quad samples
            // them later in the frame.
            private readonly RTHandle[] _silhouettes = new RTHandle[MaxCasters];

            // Health bars share the fighters' layers, so also filter by sorting layer.
            private readonly SortingLayerRange _sortingLayerRange;

            private class PassData
            {
                public RendererListHandle RendererList;
            }

            public SilhouettePass(int sortingLayerValue)
            {
                // The 2D renderer runs anything this early before it draws sprites.
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
                _sortingLayerRange = new SortingLayerRange((short)sortingLayerValue, (short)sortingLayerValue);
            }

            public void Dispose()
            {
                for (int i = 0; i < _silhouettes.Length; i++)
                {
                    _silhouettes[i]?.Release();
                    _silhouettes[i] = null;
                }
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (CasterLayers == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1;
                desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;

                for (int i = 0; i < CasterLayers.Length && i < MaxCasters; i++)
                {
                    RenderingUtils.ReAllocateHandleIfNeeded(
                        ref _silhouettes[i],
                        desc,
                        FilterMode.Bilinear,
                        TextureWrapMode.Clamp,
                        name: $"_FighterSilhouette{i}"
                    );
                    TextureHandle target = renderGraph.ImportTexture(_silhouettes[i]);

                    using (
                        var builder = renderGraph.AddRasterRenderPass<PassData>(
                            $"FighterSilhouette{i}",
                            out PassData passData
                        )
                    )
                    {
                        var filtering = new FilteringSettings(RenderQueueRange.all, CasterLayers[i])
                        {
                            sortingLayerRange = _sortingLayerRange,
                        };
                        DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(
                            ShaderTags,
                            renderingData,
                            cameraData,
                            lightData,
                            SortingCriteria.CommonTransparent
                        );
                        passData.RendererList = renderGraph.CreateRendererList(
                            new RendererListParams(renderingData.cullResults, drawing, filtering)
                        );

                        builder.UseRendererList(passData.RendererList);
                        builder.SetRenderAttachment(target, 0, AccessFlags.Write);
                        builder.AllowPassCulling(false);
                        builder.SetGlobalTextureAfterPass(target, SilhouetteIds[i]);

                        builder.SetRenderFunc(
                            static (PassData data, RasterGraphContext ctx) =>
                            {
                                ctx.cmd.ClearRenderTarget(false, true, Color.clear);
                                ctx.cmd.DrawRendererList(data.RendererList);
                            }
                        );
                    }
                }
            }
        }
    }
}
