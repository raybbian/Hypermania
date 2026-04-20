using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public LayerMask layerMask;
        public Material outlineMaterial;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        public Color outlineColor = Color.white;

        [Range(0, 16)]
        public float outlineWidth = 2f;

        [Range(0, 1)]
        public float alphaThreshold = 0.1f;
    }

    public Settings settings = new Settings();
    OutlinePass pass;

    public override void Create()
    {
        pass = new OutlinePass(settings);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (settings.outlineMaterial == null || settings.layerMask == 0)
            return;
        renderer.EnqueuePass(pass);
    }
}
