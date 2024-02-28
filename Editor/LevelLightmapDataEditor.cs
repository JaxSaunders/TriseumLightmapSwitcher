using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections;
using Unity.EditorCoroutines.Editor;

[CustomEditor(typeof(LevelLightmapData))]
public class LevelLightmapDataEditor : Editor
{
    public SerializedProperty lightingScenariosScenes;
    public SerializedProperty lightingScenariosData;
    public SerializedProperty lightingScenesNames;
    public SerializedProperty allowLoadingLightingScenes;
    public SerializedProperty applyLightmapScaleAndOffset;
    //public bool usev2 = true;
    LevelLightmapData lightmapData;
    GUIContent allowLoading = new GUIContent("Allow loading Lighting Scenes", "Allow the Level Lightmap Data script to load a lighting scene additively at runtime if the lighting scenario contains realtime lights.");

    private Editor[] _editors;

    public void OnEnable()
    {
        lightmapData = target as LevelLightmapData;
        lightingScenariosScenes = serializedObject.FindProperty("lightingScenariosScenes");
        lightingScenesNames = serializedObject.FindProperty("lightingScenesNames");
        lightingScenariosData = serializedObject.FindProperty("lightingScenariosData");
        allowLoadingLightingScenes = serializedObject.FindProperty("allowLoadingLightingScenes");
        applyLightmapScaleAndOffset = serializedObject.FindProperty("applyLightmapScaleAndOffset");
        _editors = new Editor[lightingScenariosData.arraySize];
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(allowLoadingLightingScenes, allowLoading);
        EditorGUILayout.PropertyField(lightingScenariosData, new GUIContent("Lighting Scenarios"), includeChildren: true);

        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();
            lightingScenesNames.arraySize = lightingScenariosScenes.arraySize;

            for (int i = 0; i < lightingScenariosScenes.arraySize; i++) // Conside use onvalidate function to fill lightingSceneNames.
            {
                lightingScenesNames.GetArrayElementAtIndex(i).stringValue = lightingScenariosScenes.GetArrayElementAtIndex(i).objectReferenceValue == null ? "" : lightingScenariosScenes.GetArrayElementAtIndex(i).objectReferenceValue.name;
            }
            serializedObject.ApplyModifiedProperties();
        }

        EditorGUILayout.Space();
        for (int i = 0; i < _editors.Length; i++)
        {
            var data = lightingScenariosData.GetArrayElementAtIndex(i).objectReferenceValue;
            if (data != null)
            {
                CreateCachedEditor(data, null, ref _editors[i]);
                EditorGUILayout.LabelField(data.name, EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                _editors[i].OnInspectorGUI();
                EditorGUI.indentLevel--;
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Empty Lightmap Data"))
        {
            //LevelLightmapData newData = new LevelLightmapData();

            var newData = ScriptableObject.CreateInstance<LightingScenarioData>();
            string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath("Assets/LightingData/RenameMe.asset");
            AssetDatabase.CreateAsset(newData, assetPathAndName);
        }

        serializedObject.ApplyModifiedProperties();
    }
}