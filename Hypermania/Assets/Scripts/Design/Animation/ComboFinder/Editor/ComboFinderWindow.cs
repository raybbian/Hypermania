using System;
using System.Collections.Generic;
using System.Reflection;
using Design.Animation;
using Design.Configs;
using Game;
using Game.Sim;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D.Animation;
using UnityEngine.U2D.IK;
using Utils;
using Utils.SoftFloat;

namespace Design.Animation.ComboFinder.Editor
{
    public class ComboFinderWindow : EditorWindow
    {
        private const float LeftToolbarWidth = 280f;
        private const float PreviewWidth = 180f;
        private const float PreviewHeight = 120f;
        private const float CellWidth = 18f;
        private const float CellHeight = 18f;
        private const float RowPadding = 8f;
        private const float BorderThickness = 1f;

        private CharacterConfig _characterConfig;
        private AudioConfig _audioConfig;
        private GlobalConfig _globalConfig;
        private Vector2 _leftScroll;
        private Vector2 _mainScroll;

        private readonly List<MoveEntry> _moves = new();
        private readonly MovePreviewCache _previewCache = new();

        [MenuItem("Tools/Hypermania/ComboFinder")]
        public static void ShowWindow()
        {
            ComboFinderWindow window = GetWindow<ComboFinderWindow>();
            window.titleContent = new GUIContent("ComboFinder");
            window.minSize = new Vector2(1000f, 650f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshMoves();
        }

        private void OnDisable()
        {
            _previewCache.DisposeAllPreviews();
        }

        private void OnGUI()
        {
            DrawRootLayout();
        }

        private void DrawRootLayout()
        {
            Rect totalRect = new Rect(0, 0, position.width, position.height);

            Rect leftRect = new Rect(totalRect.x, totalRect.y, LeftToolbarWidth, totalRect.height);

            Rect mainRect = new Rect(leftRect.xMax, totalRect.y, totalRect.width - LeftToolbarWidth, totalRect.height);

            DrawLeftToolbar(leftRect);
            DrawMainPanel(mainRect);
        }

        private void DrawLeftToolbar(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            EditorGUILayout.LabelField("Combo Finder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();
            _characterConfig = (CharacterConfig)
                EditorGUILayout.ObjectField(
                    new GUIContent("Character Config"),
                    _characterConfig,
                    typeof(CharacterConfig),
                    false
                );
            _audioConfig = (AudioConfig)
                EditorGUILayout.ObjectField(new GUIContent("Audio Config"), _audioConfig, typeof(AudioConfig), false);
            _globalConfig = (GlobalConfig)
                EditorGUILayout.ObjectField(
                    new GUIContent("Global Config"),
                    _globalConfig,
                    typeof(GlobalConfig),
                    false
                );

            if (EditorGUI.EndChangeCheck())
            {
                OnCharacterConfigChanged();
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_characterConfig == null))
            {
                if (GUILayout.Button("Refresh"))
                {
                    RefreshMoves();
                    _previewCache.DisposeAllPreviews();
                }
            }

            EditorGUILayout.Space();

            if (_characterConfig == null)
            {
                EditorGUILayout.HelpBox("Assign a CharacterConfig.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("Character", _characterConfig.name);
                EditorGUILayout.LabelField("Valid attack moves", _moves.Count.ToString());
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMainPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll);

            EditorGUILayout.LabelField("Moves", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (_characterConfig == null || _audioConfig == null || _globalConfig == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a CharacterConfig and AudioConfig and GlobalConfig in the left panel.",
                    MessageType.Warning
                );
            }
            else if (_moves.Count == 0)
            {
                EditorGUILayout.HelpBox("No valid attack moves found.", MessageType.Info);
            }
            else
            {
                foreach (MoveEntry move in _moves)
                {
                    DrawMoveRow(move);
                    GUILayout.Space(8f);
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawMoveRow(MoveEntry move)
        {
            float gridWidth = move.Data.TotalTicks * CellWidth;
            float rowHeight = Mathf.Max(PreviewHeight, 3f * CellHeight + 40f);

            Rect outerRect = EditorGUILayout.GetControlRect(false, rowHeight + RowPadding * 2f);
            GUI.Box(outerRect, GUIContent.none, EditorStyles.helpBox);

            Rect contentRect = new Rect(
                outerRect.x + RowPadding,
                outerRect.y + RowPadding,
                outerRect.width - RowPadding * 2f,
                outerRect.height - RowPadding * 2f
            );

            Rect previewRect = new Rect(contentRect.x, contentRect.y, PreviewWidth, PreviewHeight);

            Rect labelRect = new Rect(
                previewRect.xMax + 10f,
                contentRect.y,
                Mathf.Max(100f, contentRect.width - PreviewWidth - 20f),
                18f
            );

            Rect metaRect = new Rect(
                previewRect.xMax + 10f,
                labelRect.yMax + 2f,
                Mathf.Max(100f, contentRect.width - PreviewWidth - 20f),
                18f
            );

            Rect gridRect = new Rect(previewRect.xMax + 10f, metaRect.yMax + 8f, gridWidth, 3f * CellHeight);

            EditorGUI.LabelField(labelRect, move.State.ToString(), EditorStyles.boldLabel);
            EditorGUI.LabelField(
                metaRect,
                $"Clip: {(move.Data.Clip != null ? move.Data.Clip.name : "(none)")}   Ticks: {move.Data.TotalTicks}   First Hitbox Tick: {move.FirstHitboxTick}"
            );

            DrawMovePreview(previewRect, move);
            DrawFrameGrid(gridRect, move);
        }

        private void DrawMovePreview(Rect rect, MoveEntry move)
        {
            if (
                _characterConfig == null
                || _characterConfig.Prefab == null
                || move.Data == null
                || move.Data.Clip == null
            )
            {
                EditorGUI.HelpBox(rect, "No preview available.", MessageType.None);
                return;
            }

            MovePreview preview = _previewCache.GetOrCreatePreview(_characterConfig.Prefab.gameObject, move.Data);
            if (preview == null)
            {
                EditorGUI.HelpBox(rect, "Preview init failed.", MessageType.Warning);
                return;
            }

            Texture texture = preview.Render(rect.width, rect.height, move);
            if (texture != null)
            {
                GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
            }

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), Color.black);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Color.black);
        }

        private void DrawFrameGrid(Rect rect, MoveEntry move)
        {
            if (move.Data == null || move.Data.TotalTicks <= 0)
            {
                return;
            }

            int col = 0;
            int hitStop = 0;
            FighterState[] simFighters = new FighterState[2];
            simFighters[0] = FighterState.Create(0, 9999, new SVector2(0, 0), FighterFacing.Right, 1);
            simFighters[1] = FighterState.Create(1, 9999, new SVector2((sfloat)0.5, 0), FighterFacing.Right, 1);

            Frame firstFrame =
                _audioConfig.NextBeat(
                    Frame.FirstFrame + move.FirstHitboxTick + 1,
                    AudioConfig.BeatSubdivision.QuarterNote
                )
                - move.FirstHitboxTick
                + 3;

            simFighters[0].SetState(move.State, firstFrame, firstFrame + move.Data.TotalTicks, true);
            for (int i = 0; i < 60; i++, col++)
            {
                for (int j = 0; j < 2; j++)
                {
                    if (firstFrame + i >= simFighters[j].StateEnd)
                    {
                        simFighters[j].SetState(CharacterState.Idle, firstFrame + i, Frame.Infinity);
                    }
                }

                bool notBothIdle =
                    simFighters[0].State != CharacterState.Idle || simFighters[1].State != CharacterState.Idle;
                if (!notBothIdle)
                {
                    break;
                }

                FrameData prev = move.Data.GetFrame(i - 1);
                FrameData cur = move.Data.GetFrame(i);
                if (prev != null && cur != null && !prev.HasHitbox(out _) && cur.HasHitbox(out var boxProps))
                {
                    HitOutcome outcome = simFighters[1]
                        .ApplyHit(firstFrame + i, firstFrame, _characterConfig, boxProps, SVector2.zero, 1);
                    hitStop = Mathsf.Max(outcome.Props.HitstopTicks, hitStop);
                }

                while (hitStop > 0)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        Rect cellRect = new Rect(
                            rect.x + col * CellWidth,
                            rect.y + j * CellHeight,
                            CellWidth,
                            CellHeight
                        );

                        DrawGridCell(cellRect, FrameType.Hitstop, j, col);
                    }

                    if (
                        _audioConfig.BeatWithinWindow(
                            firstFrame + col,
                            AudioConfig.BeatSubdivision.QuarterNote,
                            _globalConfig.Input.BeatCancelWindow
                        )
                    )
                    {
                        Rect beatRect = new Rect(
                            rect.x + col * CellWidth,
                            rect.y + 2 * CellHeight,
                            CellWidth,
                            CellHeight
                        );
                        DrawGridCell(beatRect, FrameType.Active, 2, col);
                    }

                    hitStop--;
                    col++;
                }

                for (int j = 0; j < 2; j++)
                {
                    Rect cellRect = new Rect(rect.x + col * CellWidth, rect.y + j * CellHeight, CellWidth, CellHeight);

                    DrawGridCell(
                        cellRect,
                        _characterConfig
                            .GetFrameData(simFighters[j].State, firstFrame + i - simFighters[j].StateStart)
                            .FrameType,
                        j,
                        col
                    );
                }
                if (
                    _audioConfig.BeatWithinWindow(
                        firstFrame + col,
                        AudioConfig.BeatSubdivision.QuarterNote,
                        _globalConfig.Input.BeatCancelWindow
                    )
                )
                {
                    Rect beatRect = new Rect(rect.x + col * CellWidth, rect.y + 2 * CellHeight, CellWidth, CellHeight);
                    DrawGridCell(beatRect, FrameType.Active, 2, col);
                }
            }
        }

        private void DrawGridCell(Rect rect, FrameType frameType, int row, int tick)
        {
            EditorGUI.DrawRect(rect, GetFrameTypeColor(frameType));

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, BorderThickness), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - BorderThickness, rect.width, BorderThickness), Color.black);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, BorderThickness, rect.height), Color.black);
            EditorGUI.DrawRect(
                new Rect(rect.xMax - BorderThickness, rect.y, BorderThickness, rect.height),
                Color.black
            );
        }

        private static Color GetFrameTypeColor(FrameType frameType)
        {
            return frameType switch
            {
                FrameType.Startup => new Color(0.6f, 0.7f, 1f),
                FrameType.Active => new Color(1f, 0.5f, 0.5f),
                FrameType.Recovery => new Color(0.75f, 1f, 0.75f),
                FrameType.Hitstun => new Color(0.9f, 0.6f, 0.5f),
                FrameType.Blockstun => new Color(0.85f, 0.85f, 0.6f),
                FrameType.Hitstop => new Color(0.4f, 0.4f, 0.4f),
                FrameType.Grabbed => new Color(0.7f, 0.3f, 0.8f),
                _ => Color.white,
            };
        }

        private void OnCharacterConfigChanged()
        {
            _previewCache.DisposeAllPreviews();
            RefreshMoves();
            Repaint();
        }

        private void RefreshMoves()
        {
            _moves.Clear();

            if (_characterConfig == null || _characterConfig.Hitboxes == null)
            {
                return;
            }

            foreach (CharacterState state in Enum.GetValues(typeof(CharacterState)))
            {
                HitboxData data = _characterConfig.Hitboxes[state];
                if (data == null)
                {
                    continue;
                }

                if (!data.HasHitbox())
                {
                    continue;
                }

                int[] frameCounts = new int[3];
                if (!data.IsValidAttack(frameCounts))
                {
                    continue;
                }

                int firstHitboxTick = frameCounts[0];
                if (firstHitboxTick < 0)
                {
                    continue;
                }

                _moves.Add(
                    new MoveEntry
                    {
                        State = state,
                        Data = data,
                        FirstHitboxTick = firstHitboxTick,
                    }
                );
            }
        }
    }
}
