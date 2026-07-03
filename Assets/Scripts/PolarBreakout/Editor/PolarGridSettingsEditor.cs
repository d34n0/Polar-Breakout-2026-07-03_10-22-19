using UnityEditor;
using UnityEngine;

namespace PolarBreakout.Editor
{
    [CustomEditor(typeof(PolarGridSettings))]
    public class PolarGridSettingsEditor : UnityEditor.Editor
    {
        private float _targetBrickArcWidth = 0.5f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var settings = (PolarGridSettings)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Auto-Generate Segment Counts", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Fills segmentsPerRing (one value per ring, ringCount entries) so each " +
                "brick's arc width is approximately the target below, regardless of ring radius. " +
                "Bigger rings automatically get more, smaller bricks.",
                MessageType.None);

            _targetBrickArcWidth = EditorGUILayout.FloatField("Target Brick Width (world units)", _targetBrickArcWidth);

            if (GUILayout.Button("Auto-Fill Segment Counts"))
            {
                Undo.RecordObject(settings, "Auto-Fill Segment Counts");

                var newCounts = new int[settings.ringCount];
                for (int ring = 0; ring < settings.ringCount; ring++)
                {
                    float circumference = 2f * Mathf.PI * settings.RingRadius(ring);
                    int segments = Mathf.Max(3, Mathf.RoundToInt(circumference / Mathf.Max(0.05f, _targetBrickArcWidth)));
                    newCounts[ring] = segments;
                }

                settings.segmentsPerRing = newCounts;
                EditorUtility.SetDirty(settings);
            }
        }
    }
}
