using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class OutlineFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class PlayerOutline
    {
        public LayerMask layerMask;

        [ColorUsage(true, true)]
        public Color outlineColor = Color.white;

        [Range(0, 16)]
        public float outlineWidth = 2f;

        [Range(0, 1)]
        public float alphaThreshold = 0.1f;
    }

    [System.Serializable]
    public class Settings
    {
        public Material outlineMaterial;
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        public MSAASamples msaaSamples = MSAASamples.None;
        public List<PlayerOutline> players = new List<PlayerOutline>();
    }

    public Settings settings = new Settings();
    OutlinePass pass;

    // Runtime per-player glow multiplier (0 = no glow, 1 = authored intensity).
    // Driven by GameView from the hype meter each frame.
    private float[] _playerGlow;

    // Runtime per-player color override. Null entry = use PlayerOutline.outlineColor.
    private Color?[] _playerColor;

    // Last-created feature instance. The feature is configured per-URP-renderer and
    // there's only one active OutlineFeature in this project, so a single static handle
    // is enough for gameplay code to reach it without a scene service-locator.
    public static OutlineFeature Instance { get; private set; }

    public override void Create()
    {
        Instance = this;
        pass = new OutlinePass(settings, this);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
    {
        if (settings.outlineMaterial == null || settings.players == null || settings.players.Count == 0)
            return;
        renderer.EnqueuePass(pass);
    }

    public void SetPlayerGlow(int playerIndex, float glow)
    {
        int count = settings.players?.Count ?? 0;
        if (_playerGlow == null || _playerGlow.Length != count)
            _playerGlow = new float[count];
        if (playerIndex < 0 || playerIndex >= _playerGlow.Length)
            return;
        _playerGlow[playerIndex] = Mathf.Max(0f, glow);
    }

    public float GetPlayerGlow(int playerIndex)
    {
        if (_playerGlow == null || playerIndex < 0 || playerIndex >= _playerGlow.Length)
            return 1f;
        return _playerGlow[playerIndex];
    }

    public void SetPlayerColor(int playerIndex, Color color)
    {
        int count = settings.players?.Count ?? 0;
        if (_playerColor == null || _playerColor.Length != count)
            _playerColor = new Color?[count];
        if (playerIndex < 0 || playerIndex >= _playerColor.Length)
            return;
        _playerColor[playerIndex] = color;
    }

    public Color GetPlayerColor(int playerIndex, Color fallback)
    {
        if (_playerColor == null || playerIndex < 0 || playerIndex >= _playerColor.Length)
            return fallback;
        return _playerColor[playerIndex] ?? fallback;
    }

    protected override void Dispose(bool disposing)
    {
        pass?.Cleanup();
        if (Instance == this)
            Instance = null;
    }
}
