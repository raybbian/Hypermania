using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Game.View.Fighters
{
    // Draws each fighter into their own screen-sized texture before the rest
    // of the frame renders, for effects that need to know what the fighters
    // look like on screen (floor shadows, impact frames). Shaders read them
    // through VFX/FighterSilhouette/FighterSilhouettes.hlsl.
    [RequireComponent(typeof(Camera))]
    public class FighterSilhouetteCapture : MonoBehaviour
    {
        private const int MaxFighters = 2;

        // One entry per player, matching the outline layers.
        [SerializeField]
        private LayerMask[] _fighterLayers;

        // Health bars share the fighters' layers, so only renderers on this
        // sorting layer are captured.
        [SerializeField]
        private string _sortingLayer = "Character";

        private Camera _camera;
        private SilhouettePass _pass;

        private void OnEnable()
        {
            _camera = GetComponent<Camera>();
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
            _pass.FighterLayers = _fighterLayers;
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

            public LayerMask[] FighterLayers;

            // Persistent so they are still valid when other shaders sample
            // them later in the frame.
            private readonly RTHandle[] _silhouettes = new RTHandle[MaxFighters];
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
                if (FighterLayers == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.msaaSamples = 1;
                desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;

                for (int i = 0; i < FighterLayers.Length && i < MaxFighters; i++)
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
                        var filtering = new FilteringSettings(RenderQueueRange.all, FighterLayers[i])
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
