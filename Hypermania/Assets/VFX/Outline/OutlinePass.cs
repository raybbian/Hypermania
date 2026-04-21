using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class OutlinePass : ScriptableRenderPass
{
    readonly OutlineFeature.Settings settings;
    readonly OutlineFeature feature;

    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    static readonly int AlphaThresholdId = Shader.PropertyToID("_AlphaThreshold");

    static readonly List<ShaderTagId> shaderTags = new List<ShaderTagId>
    {
        new ShaderTagId("SRPDefaultUnlit"),
        new ShaderTagId("UniversalForward"),
        new ShaderTagId("UniversalForwardOnly"),
        new ShaderTagId("Universal2D"),
    };

    // Per-player material instances so concurrent blit passes don't stomp
    // each other's _OutlineColor/_OutlineWidth/_AlphaThreshold.
    Material[] _perPlayerMaterials;
    Material _materialSource;

    public OutlinePass(OutlineFeature.Settings s, OutlineFeature f)
    {
        settings = s;
        feature = f;
        renderPassEvent = s.renderPassEvent;
    }

    public void Cleanup()
    {
        if (_perPlayerMaterials == null)
            return;
        foreach (var m in _perPlayerMaterials)
        {
            if (m == null)
                continue;
            if (Application.isPlaying)
                Object.Destroy(m);
            else
                Object.DestroyImmediate(m);
        }
        _perPlayerMaterials = null;
        _materialSource = null;
    }

    void EnsureMaterials(int count)
    {
        if (
            _perPlayerMaterials != null
            && _perPlayerMaterials.Length == count
            && _materialSource == settings.outlineMaterial
        )
            return;

        Cleanup();
        _materialSource = settings.outlineMaterial;
        _perPlayerMaterials = new Material[count];
        for (int i = 0; i < count; i++)
            _perPlayerMaterials[i] = new Material(settings.outlineMaterial);
    }

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
        if (settings.outlineMaterial == null || settings.players == null || settings.players.Count == 0)
            return;

        EnsureMaterials(settings.players.Count);

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData = frameData.Get<UniversalCameraData>();
        var renderingData = frameData.Get<UniversalRenderingData>();
        var lightData = frameData.Get<UniversalLightData>();

        var baseDesc = resourceData.activeColorTexture.GetDescriptor(renderGraph);

        // Outline width is authored at a 720p reference so visual thickness
        // stays constant across display resolutions.
        const float kReferenceHeight = 720f;
        float thicknessScale = baseDesc.height / kReferenceHeight;

        for (int i = 0; i < settings.players.Count; i++)
        {
            var player = settings.players[i];
            if (player == null || player.layerMask == 0)
                continue;

            // Hype-driven glow multiplier. 0 means the other player has more
            // hype — skip the entire pass so this player doesn't glow at all.
            float glow = feature != null ? feature.GetPlayerGlow(i) : 1f;
            if (glow <= 0f)
                continue;

            // Per-player silhouette RT; unique name prevents RenderGraph aliasing.
            var desc = baseDesc;
            desc.name = $"_CharacterSilhouette_{i}";
            desc.depthBufferBits = 0;
            desc.msaaSamples = settings.msaaSamples;
            desc.clearBuffer = true;
            desc.clearColor = Color.clear;
            desc.format = GraphicsFormat.R8G8B8A8_UNorm;
            TextureHandle silhouette = renderGraph.CreateTexture(desc);

            using (
                var builder = renderGraph.AddRasterRenderPass<SilhouettePassData>(
                    $"CharacterSilhouette_{i}",
                    out var passData
                )
            )
            {
                var filtering = new FilteringSettings(RenderQueueRange.all, player.layerMask);
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
                    continue;

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(silhouette, 0, AccessFlags.ReadWrite);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(
                    static (SilhouettePassData d, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.DrawRendererList(d.rendererList);
                    }
                );
            }

            // Runtime color override (from GameView: hype/mania/burst) takes
            // precedence over the authored color. Scale HDR (drives bloom) and
            // width by glow; at glow=1 the outline matches the authored look.
            Color baseColor = feature != null ? feature.GetPlayerColor(i, player.outlineColor) : player.outlineColor;
            Color scaledColor = baseColor * glow;
            scaledColor.a = baseColor.a;

            var mat = _perPlayerMaterials[i];
            mat.SetColor(OutlineColorId, scaledColor);
            mat.SetFloat(OutlineWidthId, player.outlineWidth * glow * thicknessScale);
            mat.SetFloat(AlphaThresholdId, player.alphaThreshold);

            using (
                var builder = renderGraph.AddRasterRenderPass<OutlineBlitPassData>($"OutlineBlit_{i}", out var passData)
            )
            {
                passData.source = silhouette;
                passData.material = mat;

                builder.UseTexture(silhouette);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(
                    static (OutlineBlitPassData d, RasterGraphContext ctx) =>
                    {
                        Blitter.BlitTexture(ctx.cmd, d.source, new Vector4(1, 1, 0, 0), d.material, 0);
                    }
                );
            }
        }
    }
}
