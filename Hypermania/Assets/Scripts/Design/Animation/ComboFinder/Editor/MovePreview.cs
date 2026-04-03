using System;
using System.Collections.Generic;
using System.Reflection;
using Design.Configs;
using Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;

namespace Design.Animation.ComboFinder.Editor
{
    public class MovePreviewCache
    {
        private readonly Dictionary<HitboxData, MovePreview> _previewCache = new();

        public MovePreview GetOrCreatePreview(GameObject go, HitboxData data)
        {
            if (data == null)
            {
                return null;
            }

            if (_previewCache.TryGetValue(data, out MovePreview existing) && existing != null)
            {
                return existing;
            }

            MovePreview created = new MovePreview(go, data.Clip);
            _previewCache[data] = created;
            return created;
        }

        public void DisposeAllPreviews()
        {
            foreach (KeyValuePair<HitboxData, MovePreview> pair in _previewCache)
            {
                pair.Value?.Dispose();
            }

            _previewCache.Clear();
        }
    }

    public sealed class MoveEntry
    {
        public CharacterState State;
        public HitboxData Data;
        public int FirstHitboxTick;
    }

    public sealed class MovePreview : IDisposable
    {
        private readonly PreviewRenderUtility _previewUtility;
        private readonly GameObject _instance;
        private readonly AnimationClip _clip;
        private readonly SpriteSkin[] _spriteSkins;
        private readonly IKManager2D _ikManager;

        private readonly Dictionary<int, Texture2D> _frameTextureCache = new();

        private static readonly Bounds PreviewBounds = new Bounds(
            new Vector3(0f, 1f, 0f),
            new Vector3(2.5f, 2.5f, 2.5f)
        );

        public MovePreview(GameObject prefab, AnimationClip clip)
        {
            _clip = clip;
            _previewUtility = new PreviewRenderUtility();
            _previewUtility.cameraFieldOfView = 30f;

            _instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (_instance == null)
            {
                _instance = UnityEngine.Object.Instantiate(prefab);
            }

            _spriteSkins = _instance.GetComponentsInChildren<SpriteSkin>();
            _ikManager = _instance.GetComponent<IKManager2D>();

            _instance.hideFlags = HideFlags.HideAndDontSave;
            _previewUtility.AddSingleGO(_instance);

            SetupCamera(_previewUtility.camera);
        }

        public Texture Render(float width, float height, MoveEntry move)
        {
            if (_instance == null)
            {
                return null;
            }

            if (
                _frameTextureCache.TryGetValue(move.FirstHitboxTick, out Texture2D cachedTexture)
                && cachedTexture != null
            )
            {
                return cachedTexture;
            }

            Texture2D renderedTexture = RenderToTexture(width, height, move);
            _frameTextureCache[move.FirstHitboxTick] = renderedTexture;
            return renderedTexture;
        }

        private Texture2D RenderToTexture(float width, float height, MoveEntry move)
        {
            Rect previewRect = new Rect(0, 0, width, height);
            _previewUtility.BeginPreview(previewRect, GUIStyle.none);

            AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(_instance, _clip, (float)move.FirstHitboxTick / GameManager.TPS);

            _ikManager.UpdateManager();
            foreach (var skin in _spriteSkins)
            {
                // holy hack
                var method = typeof(SpriteSkin).GetMethod("Deform", BindingFlags.Instance | BindingFlags.NonPublic);

                method?.Invoke(skin, null);
            }

            SetupCamera(_previewUtility.camera);
            _previewUtility.camera.Render();

            Texture previewTexture = _previewUtility.EndPreview();

            AnimationMode.StopAnimationMode();

            Texture2D cachedCopy = CopyToTexture2D(previewTexture, Mathf.CeilToInt(width), Mathf.CeilToInt(height));
            return cachedCopy;
        }

        private static Texture2D CopyToTexture2D(Texture source, int width, int height)
        {
            RenderTexture tempRt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;

            Graphics.Blit(source, tempRt);
            RenderTexture.active = tempRt;

            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply(false, true);

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tempRt);

            return texture;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in _frameTextureCache.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            _frameTextureCache.Clear();

            if (_instance != null)
            {
                UnityEngine.Object.DestroyImmediate(_instance);
            }

            _previewUtility?.Cleanup();
        }

        private static void SetupCamera(Camera camera)
        {
            Vector3 center = PreviewBounds.center;

            camera.orthographic = true;
            camera.orthographicSize = PreviewBounds.extents.y;

            camera.transform.position = center + new Vector3(0f, 0f, -10f);
            camera.transform.rotation = Quaternion.identity;

            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;

            camera.clearFlags = CameraClearFlags.Color;
            camera.backgroundColor = new Color(0.22f, 0.22f, 0.22f, 1f);
        }
    }
}
