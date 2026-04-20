using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class OutlinePass : ScriptableRenderPass
{
    readonly OutlineFeature.Settings settings;

    static readonly List<ShaderTagId> shaderTags = new List<ShaderTagId>
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("Universal2D"),
    };

    public OutlinePass(OutlineFeature.Settings s)
    {
        settings = s;
        renderPassEvent = s.renderPassEvent;
    }

    // One PassData class per graph pass. Keep them tiny and POCO — the
    // graph reuses them across frames.
    class SilhouettePassData
    {
        public RendererListHandle rendererList;
    }

    class OutlineBlitPassData
    {
        public TextureHandle source;
        public Material material;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (settings.outlineMaterial == null || settings.layerMask == 0)
            return;

        // URP exposes its per-frame state through typed containers on frameData.
        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();

        // --- Create a transient silhouette texture, cleared to transparent. ---
        // Base it on the camera color's descriptor so size/format match.
        var desc = resourceData.activeColorTexture.GetDescriptor(renderGraph);
        desc.name = "_CharacterSilhouette";
        desc.depthBufferBits = 0;
        desc.msaaSamples = MSAASamples.None;
        desc.clearBuffer = true;
        desc.clearColor = Color.clear;
        TextureHandle silhouette = renderGraph.CreateTexture(desc);

        // --- Pass 1: draw the character layer into the silhouette RT. ---
        using (
            var builder = renderGraph.AddRasterRenderPass<SilhouettePassData>("CharacterSilhouette", out var passData)
        )
        {
            var filtering = new FilteringSettings(RenderQueueRange.all, settings.layerMask);
            var drawing = RenderingUtils.CreateDrawingSettings(
                shaderTags,
                renderingData,
                cameraData,
                lightData,
                cameraData.defaultOpaqueSortFlags
            );

            var rlParams = new RendererListParams(renderingData.cullResults, drawing, filtering);
            passData.rendererList = renderGraph.CreateRendererList(rlParams);

            if (!passData.rendererList.IsValid())
                return;

            builder.UseRendererList(passData.rendererList);
            builder.SetRenderAttachment(silhouette, 0, AccessFlags.ReadWrite);

            builder.SetRenderFunc(
                static (SilhouettePassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.rendererList);
                }
            );
        }

        // --- Pass 2: blit silhouette onto camera color through the outline material. ---
        settings.outlineMaterial.SetColor("_OutlineColor", settings.outlineColor);
        settings.outlineMaterial.SetFloat("_OutlineWidth", settings.outlineWidth);
        settings.outlineMaterial.SetFloat("_AlphaThreshold", settings.alphaThreshold);

        using (var builder = renderGraph.AddRasterRenderPass<OutlineBlitPassData>("OutlineBlit", out var passData))
        {
            passData.source = silhouette;
            passData.material = settings.outlineMaterial;

            builder.UseTexture(silhouette);
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

            builder.SetRenderFunc(
                static (OutlineBlitPassData d, RasterGraphContext ctx) =>
                {
                    // Blitter binds d.source as _BlitTexture automatically.
                    Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                }
            );
        }
    }
}
