using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PolarBreakout.Editor
{
    [CustomEditor(typeof(LevelSO))]
    public class LevelSOEditor : UnityEditor.Editor
    {
        private enum PatternType { Checkerboard, EveryNthDistance, BorderHexes }

        private BrickTypeSO _fillType;
        private int _fillDistance;

        private BrickTypeSO _brush;
        private bool _paintModeEnabled;

        private PatternType _patternType;
        private int _patternInterval = 2;
        private int _patternOffset;

        private enum RandomFillMode { Scatter, Clustered }

        private readonly List<BrickTypeSO> _randomBrickPool = new List<BrickTypeSO>();
        private float _randomFillChance = 0.7f;
        private int _randomSeed;
        private RandomFillMode _randomFillMode;
        private int _clusterSeedCount = 3;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var level = (LevelSO)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Fill (testing)", EditorStyles.boldLabel);

            _fillType = (BrickTypeSO)EditorGUILayout.ObjectField("Brick Type", _fillType, typeof(BrickTypeSO), false);
            _fillDistance = Mathf.Max(0, EditorGUILayout.IntField("Distance", _fillDistance));

            using (new EditorGUI.DisabledScope(_fillType == null || level.gridSettings == null))
            {
                if (GUILayout.Button($"Fill At Distance {_fillDistance}"))
                {
                    Undo.RecordObject(level, "Fill At Distance");
                    level.FillByDistance(_fillDistance, _fillType);
                    EditorUtility.SetDirty(level);
                }

                if (GUILayout.Button("Fill All"))
                {
                    Undo.RecordObject(level, "Fill All");
                    level.FillWithinDistance(_fillType);
                    EditorUtility.SetDirty(level);
                }
            }

            if (GUILayout.Button($"Clear At Distance {_fillDistance}"))
            {
                Undo.RecordObject(level, "Clear At Distance");
                level.ClearByDistance(_fillDistance);
                EditorUtility.SetDirty(level);
            }

            if (GUILayout.Button("Clear All Bricks"))
            {
                Undo.RecordObject(level, "Clear Level");
                level.placements.Clear();
                EditorUtility.SetDirty(level);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pattern Designer", EditorStyles.boldLabel);

            _patternType = (PatternType)EditorGUILayout.EnumPopup("Pattern", _patternType);
            if (_patternType == PatternType.EveryNthDistance)
            {
                _patternInterval = Mathf.Max(1, EditorGUILayout.IntField("Interval", _patternInterval));
                _patternOffset = Mathf.Max(0, EditorGUILayout.IntField("Offset", _patternOffset));
            }

            using (new EditorGUI.DisabledScope(_fillType == null || level.gridSettings == null))
            {
                if (GUILayout.Button("Apply Pattern"))
                {
                    Undo.RecordObject(level, "Apply Pattern");
                    switch (_patternType)
                    {
                        case PatternType.Checkerboard:
                            level.FillCheckerboard(_fillType);
                            break;
                        case PatternType.EveryNthDistance:
                            level.FillEveryNthDistance(_fillType, _patternInterval, _patternOffset);
                            break;
                        case PatternType.BorderHexes:
                            level.FillBorderHexes(_fillType);
                            break;
                    }
                    EditorUtility.SetDirty(level);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Random Level Designer", EditorStyles.boldLabel);

            EditorGUILayout.LabelField("Brick Pool");
            for (int i = 0; i < _randomBrickPool.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _randomBrickPool[i] = (BrickTypeSO)EditorGUILayout.ObjectField(
                        _randomBrickPool[i], typeof(BrickTypeSO), false);
                    if (GUILayout.Button("X", GUILayout.Width(20)))
                    {
                        _randomBrickPool.RemoveAt(i);
                        break;
                    }
                }
            }
            if (GUILayout.Button("Add Brick Type"))
                _randomBrickPool.Add(null);

            _randomFillMode = (RandomFillMode)EditorGUILayout.EnumPopup("Fill Mode", _randomFillMode);
            _randomFillChance = EditorGUILayout.Slider(
                _randomFillMode == RandomFillMode.Scatter ? "Fill Chance" : "Target Fill %", _randomFillChance, 0f, 1f);
            if (_randomFillMode == RandomFillMode.Clustered)
                _clusterSeedCount = Mathf.Max(1, EditorGUILayout.IntField("Seed Count", _clusterSeedCount));
            _randomSeed = EditorGUILayout.IntField("Seed", _randomSeed);

            bool hasValidBrick = _randomBrickPool.Exists(b => b != null);
            using (new EditorGUI.DisabledScope(!hasValidBrick || level.gridSettings == null))
            {
                string buttonLabel = _randomFillMode == RandomFillMode.Scatter
                    ? "Generate Random Level" : "Generate Clustered Level";
                if (GUILayout.Button(buttonLabel))
                {
                    Undo.RecordObject(level, buttonLabel);
                    var pool = _randomBrickPool.FindAll(b => b != null);
                    if (_randomFillMode == RandomFillMode.Scatter)
                        level.GenerateRandomLevel(pool, _randomFillChance, _randomSeed);
                    else
                        level.GenerateClusteredLevel(pool, _randomFillChance, _clusterSeedCount, _randomSeed);
                    EditorUtility.SetDirty(level);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Paint Level", EditorStyles.boldLabel);

            _brush = (BrickTypeSO)EditorGUILayout.ObjectField("Brush (left-click)", _brush, typeof(BrickTypeSO), false);
            EditorGUILayout.HelpBox(
                "Left-click/drag in the Scene view to paint the brush brick. Right-click/drag to erase.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(level.gridSettings == null))
            {
                string label = _paintModeEnabled ? "Stop Painting" : "Start Painting in Scene View";
                if (GUILayout.Button(label))
                {
                    _paintModeEnabled = !_paintModeEnabled;
                    SetSubscribed(_paintModeEnabled);
                    SceneView.RepaintAll();
                }
            }
        }

        private void OnDisable()
        {
            SetSubscribed(false);
        }

        private void SetSubscribed(bool subscribed)
        {
            // Hooked into the static SceneView.duringSceneGui event (rather than overriding the
            // classic per-selection OnSceneGUI) so painting keeps working across whatever the
            // Scene view click itself does to the current selection - the paint clicks land on
            // empty space (no real GameObject/collider there, just Handles-drawn cells), and
            // Unity's default behavior for a click that misses everything is to clear the
            // selection, which would otherwise tear down this Editor mid-stroke.
            SceneView.duringSceneGui -= OnDuringSceneGui;
            if (subscribed) SceneView.duringSceneGui += OnDuringSceneGui;
        }

        private void OnDuringSceneGui(SceneView sceneView)
        {
            if (this == null) { SceneView.duringSceneGui -= OnDuringSceneGui; return; }

            var level = (LevelSO)target;
            if (!_paintModeEnabled || level == null || level.gridSettings == null) return;

            var settings = level.gridSettings;
            var placementLookup = BuildPlacementLookup(level);

            DrawCells(settings, placementLookup);
            HandlePaintInput(level, settings);

            // Keep repainting while the tool is active so drag strokes and hover feedback
            // stay responsive instead of only updating on the next unrelated repaint.
            SceneView.RepaintAll();
        }

        private static Dictionary<(int q, int r), BrickTypeSO> BuildPlacementLookup(LevelSO level)
        {
            var lookup = new Dictionary<(int, int), BrickTypeSO>();
            foreach (var placement in level.placements)
            {
                if (placement.brickType != null)
                    lookup[(placement.q, placement.r)] = placement.brickType;
            }
            return lookup;
        }

        private static void DrawCells(PolarGridSettings settings, Dictionary<(int q, int r), BrickTypeSO> placementLookup)
        {
            float hexRadius = Mathf.Max(0.01f, settings.hexSize - settings.hexGap);

            foreach (var coord in settings.EnumerateValidCoordinates())
            {
                Vector2 center = settings.HexToWorld(coord);
                Vector2[] outline = PolarMeshUtility.BuildHexOutlinePoints(hexRadius);

                var points3D = new Vector3[outline.Length];
                for (int i = 0; i < outline.Length; i++)
                    points3D[i] = new Vector3(outline[i].x + center.x, outline[i].y + center.y, 0f);

                bool hasBrick = placementLookup.TryGetValue((coord.q, coord.r), out var brickType);
                Color fill = hasBrick ? brickType.color : new Color(1f, 1f, 1f, 0.05f);
                fill.a = hasBrick ? 0.65f : fill.a;

                Handles.color = fill;
                Handles.DrawAAConvexPolygon(points3D);

                Handles.color = new Color(1f, 1f, 1f, 0.25f);
                Handles.DrawAAPolyLine(2f, AppendFirst(points3D));
            }
        }

        private static Vector3[] AppendFirst(Vector3[] points)
        {
            var closed = new Vector3[points.Length + 1];
            System.Array.Copy(points, closed, points.Length);
            closed[points.Length] = points[0];
            return closed;
        }

        private void HandlePaintInput(LevelSO level, PolarGridSettings settings)
        {
            // Claim the scene view's default control AND its hot control so a click/drag here
            // paints instead of falling through to Unity's own click-to-select/rect-select
            // handling - without grabbing hotControl, the Scene view's built-in rect-selection
            // tool processes the same mouse-down/drag and draws a marquee instead.
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            HandleUtility.AddDefaultControl(controlId);

            Event current = Event.current;
            bool isPaintButton = current.button == 0 || current.button == 1;

            switch (current.type)
            {
                case EventType.MouseDown when isPaintButton:
                    GUIUtility.hotControl = controlId;
                    PaintAtMouse(level, settings, current);
                    current.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == controlId:
                    PaintAtMouse(level, settings, current);
                    current.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == controlId:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
            }
        }

        private void PaintAtMouse(LevelSO level, PolarGridSettings settings, Event current)
        {
            if (!TryGetCoordUnderMouse(current.mousePosition, settings, out var coord)) return;

            if (current.button == 0 && _brush != null)
            {
                Undo.RecordObject(level, "Paint Brick");
                level.SetBrick(coord, _brush);
                EditorUtility.SetDirty(level);
            }
            else if (current.button == 1)
            {
                Undo.RecordObject(level, "Erase Brick");
                level.ClearBrick(coord);
                EditorUtility.SetDirty(level);
            }
        }

        private static bool TryGetCoordUnderMouse(Vector2 mousePosition, PolarGridSettings settings, out HexCoordinate coord)
        {
            coord = default;

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return false;

            float t = -ray.origin.z / ray.direction.z;
            if (t < 0f) return false;

            Vector3 worldPoint = ray.origin + ray.direction * t;
            coord = settings.WorldToHex(new Vector2(worldPoint.x, worldPoint.y));
            return settings.IsValidCoordinate(coord);
        }
    }
}
