using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PolarBreakout.Editor
{
    [CustomEditor(typeof(LevelSO))]
    public class LevelSOEditor : UnityEditor.Editor
    {
        private BrickTypeSO _fillType;
        private int _fillRing;

        private BrickTypeSO _brush;
        private bool _paintModeEnabled;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var level = (LevelSO)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Fill (testing)", EditorStyles.boldLabel);

            _fillType = (BrickTypeSO)EditorGUILayout.ObjectField("Brick Type", _fillType, typeof(BrickTypeSO), false);
            _fillRing = EditorGUILayout.IntField("Ring", _fillRing);

            using (new EditorGUI.DisabledScope(_fillType == null || level.gridSettings == null))
            {
                if (GUILayout.Button($"Fill Ring {_fillRing}"))
                {
                    Undo.RecordObject(level, "Fill Ring");
                    level.FillRing(_fillRing, _fillType);
                    EditorUtility.SetDirty(level);
                }

                if (GUILayout.Button("Fill All Rings"))
                {
                    Undo.RecordObject(level, "Fill All Rings");
                    for (int r = 0; r < level.gridSettings.ringCount; r++)
                        level.FillRing(r, _fillType);
                    EditorUtility.SetDirty(level);
                }
            }

            if (GUILayout.Button("Clear All Bricks"))
            {
                Undo.RecordObject(level, "Clear Level");
                level.placements.Clear();
                EditorUtility.SetDirty(level);
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

        private static Dictionary<(int ring, int segment), BrickTypeSO> BuildPlacementLookup(LevelSO level)
        {
            var lookup = new Dictionary<(int, int), BrickTypeSO>();
            foreach (var placement in level.placements)
            {
                if (placement.brickType != null)
                    lookup[(placement.ring, placement.segment)] = placement.brickType;
            }
            return lookup;
        }

        private static void DrawCells(PolarGridSettings settings, Dictionary<(int ring, int segment), BrickTypeSO> placementLookup)
        {
            for (int ring = 0; ring < settings.ringCount; ring++)
            {
                int segmentCount = settings.SegmentsInRing(ring);
                for (int segment = 0; segment < segmentCount; segment++)
                {
                    var coord = new PolarCoordinate(ring, segment);
                    settings.GetBrickRadialRange(coord, out float innerRadius, out float outerRadius);
                    settings.GetBrickAngleRange(coord, out float startAngleDeg, out float endAngleDeg);

                    Vector2[] outline = PolarMeshUtility.BuildArcOutlinePoints(
                        innerRadius, outerRadius, startAngleDeg, endAngleDeg, settings.curveResolutionDegrees);

                    var points3D = new Vector3[outline.Length];
                    for (int i = 0; i < outline.Length; i++)
                        points3D[i] = new Vector3(outline[i].x, outline[i].y, 0f);

                    bool hasBrick = placementLookup.TryGetValue((ring, segment), out var brickType);
                    Color fill = hasBrick ? brickType.color : new Color(1f, 1f, 1f, 0.05f);
                    fill.a = hasBrick ? 0.65f : fill.a;

                    Handles.color = fill;
                    Handles.DrawAAConvexPolygon(points3D);

                    Handles.color = new Color(1f, 1f, 1f, 0.25f);
                    Handles.DrawAAPolyLine(2f, AppendFirst(points3D));
                }
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
                level.SetBrick(coord.ring, coord.segment, _brush);
                EditorUtility.SetDirty(level);
            }
            else if (current.button == 1)
            {
                Undo.RecordObject(level, "Erase Brick");
                level.ClearBrick(coord.ring, coord.segment);
                EditorUtility.SetDirty(level);
            }
        }

        private static bool TryGetCoordUnderMouse(Vector2 mousePosition, PolarGridSettings settings, out PolarCoordinate coord)
        {
            coord = default;

            Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
            if (Mathf.Abs(ray.direction.z) < 0.0001f) return false;

            float t = -ray.origin.z / ray.direction.z;
            if (t < 0f) return false;

            Vector3 worldPoint = ray.origin + ray.direction * t;
            float radius = new Vector2(worldPoint.x, worldPoint.y).magnitude;
            float angleDeg = Mathf.Repeat(Mathf.Atan2(worldPoint.y, worldPoint.x) * Mathf.Rad2Deg, 360f);

            int ring = Mathf.RoundToInt((radius - settings.firstRingRadius) / settings.ringSpacing);
            if (ring < 0 || ring >= settings.ringCount) return false;

            int segmentCount = settings.SegmentsInRing(ring);
            float segAngle = 360f / segmentCount;
            int segment = Mathf.Clamp(Mathf.FloorToInt(angleDeg / segAngle), 0, segmentCount - 1);

            coord = new PolarCoordinate(ring, segment);
            return true;
        }
    }
}
