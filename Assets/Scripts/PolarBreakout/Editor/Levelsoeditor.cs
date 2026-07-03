using UnityEditor;
using UnityEngine;

namespace PolarBreakout.Editor
{
    [CustomEditor(typeof(LevelSO))]
    public class LevelSOEditor : UnityEditor.Editor
    {
        private BrickTypeSO _fillType;
        private int _fillRing;

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
        }
    }
}