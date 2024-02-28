using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections;
using Unity.EditorCoroutines.Editor;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

[CustomEditor(typeof(LightingScenarioData))]
public class LightingScenarioEditor : Editor
{
    public SerializedProperty geometrySceneName;
    public SerializedProperty lightingSceneName;
    public SerializedProperty storeRendererInfos;
    public SerializedProperty lightmapsMode;
    public SerializedProperty lightmaps;
    public SerializedProperty lightProbes;
    public SerializedProperty rendererInfos;
    public SerializedProperty hasRealtimeLights;

    public void OnEnable()
    {
        geometrySceneName = serializedObject.FindProperty("geometrySceneName");
        lightingSceneName = serializedObject.FindProperty("lightingSceneName");
        storeRendererInfos = serializedObject.FindProperty("storeRendererInfos");
        lightmapsMode = serializedObject.FindProperty("lightmapsMode");
        lightmaps = serializedObject.FindProperty("lightmaps");
        lightProbes = serializedObject.FindProperty("lightProbesAsset");
        rendererInfos = serializedObject.FindProperty("rendererInfos");
        hasRealtimeLights = serializedObject.FindProperty("hasRealtimeLights");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(geometrySceneName);
        EditorGUILayout.PropertyField(lightingSceneName);
        EditorGUILayout.PropertyField(storeRendererInfos);
        EditorGUILayout.PropertyField(rendererInfos);
        EditorGUILayout.BeginVertical("box");

        EditorGUILayout.LabelField("Stored Data", EditorStyles.boldLabel);
        //Begin disabled group as this is a data summary display
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.PropertyField(lightmapsMode);
        EditorGUILayout.TextField("Lightmaps count", lightmaps.arraySize.ToString());
        EditorGUILayout.TextField("Renderer Infos count", rendererInfos.arraySize.ToString());
        EditorGUILayout.ObjectField(lightProbes);
        EditorGUILayout.PropertyField(hasRealtimeLights);

        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        LightingScenarioData scenarioData = (LightingScenarioData)target;

        if (GUILayout.Button("Generate lighting scenario data"))
        {
            EditorCoroutineUtility.StartCoroutine(LoadLightingScenarioScenes(scenarioData, false), this);
        }

        if (GUILayout.Button("Load Lighting scenario"))
        {
            EditorCoroutineUtility.StartCoroutine(LoadLightingScenarioScenes(scenarioData, true), this); 
        }

        serializedObject.ApplyModifiedProperties();
    }

    public IEnumerator LoadLightingScenarioScenes(LightingScenarioData scenarioData, bool onlyLoad)
    {

        if (UnityEditor.Lightmapping.giWorkflowMode != UnityEditor.Lightmapping.GIWorkflowMode.OnDemand)
        {
            Debug.LogError("Lightmap switcher requires Auto Generate lighting mode disabled.");
            yield break;
        }

        Debug.Log("Loading scenario " + scenarioData.name);

        if(scenarioData.geometrySceneName == "" )
        {
            Debug.LogError("Geometry scene name cannot be null. Stopping generation.");
            yield break;
        }
        if (scenarioData.lightingSceneName == "")
        {
            Debug.LogError("Lighting scene name cannot be null. Stopping generation.");
            yield break;
        }

        Lightmapping.lightingDataAsset = null; //Set the lightingDataAsset to null for some reason

        if (Application.isPlaying)
        {
            var unloadScenes = SceneManager.GetAllScenes().Where(s => s.name.ToLower().Contains("lighting")).ToArray();// && s.name != lightingSceneName.stringValue).ToArray();
            if (unloadScenes.Any())
            {
                foreach (var scene in unloadScenes)
                {
                    SceneManager.UnloadScene(scene);
                }
            }

            if (!SceneManager.GetAllScenes().Any(s => s.name == geometrySceneName.stringValue))
            {
                SceneManager.LoadScene(geometrySceneName.stringValue, LoadSceneMode.Additive); //Synchronously loads the Geometry scene
            }
            var operation = SceneManager.LoadSceneAsync(lightingSceneName.stringValue, LoadSceneMode.Additive); //Synchronously loads the Lighting scene
            operation.allowSceneActivation = true;
            while(!operation.isDone)
            {
                yield return null;
            }
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(lightingSceneName.stringValue)); //Set the active scene to the lighting scene, which we know exists because we just loaded it
            //See if there is already a lighting scene loaded, if so, store it as previous lighting scene
                //Unload previous lighting scene
        }
        else
        {
            if (!EditorSceneManager.GetAllScenes().Any(s => s.name == geometrySceneName.stringValue))
            {
                var asyncGeoLoad = EditorSceneManager.OpenScene($"Assets/Scenes/{geometrySceneName.stringValue}.unity", OpenSceneMode.Additive); //Synchronously loads the Geometry scene
            }
            var syncLightLoad = EditorSceneManager.OpenScene($"Assets/Scenes/{lightingSceneName.stringValue}.unity", OpenSceneMode.Additive); //Synchronously loads the Lighting scene
            EditorSceneManager.SetActiveScene(syncLightLoad); //Set the active scene to the lighting scene, which we know exists because we just loaded it
        }

        if (!onlyLoad)
        {
            scenarioData.hasRealtimeLights = SearchLightsNeededRealtime();
            Debug.Log("Lightmap switcher - Start baking"); //Check if the lighting scene needs requires dynamic lighting ( if not, never try to load the lighting scene ).
            Lightmapping.lightingDataAsset = null; //Remove reference to LightingDataAsset so that Unity doesn't delete the previous bake
            if (!Application.isPlaying)
            {
                yield return EditorCoroutineUtility.StartCoroutine(BuildLightingAsync(scenarioData), this);

            }
            else
            {
                yield return EditorCoroutineUtility.StartCoroutine(BuildLightingAsync(scenarioData), this);
            }
        }
        else
        {
            GameObject.FindObjectOfType<LevelLightmapData>().LoadLightingScenarioData(scenarioData);
            yield return null;
        }
    }


    private IEnumerator BuildLightingAsync(LightingScenarioData scenarioData)
    {
        Scene lightingScene = SceneManager.GetSceneByName(scenarioData.lightingSceneName);
        Scene geometryScene = SceneManager.GetSceneByName(scenarioData.geometrySceneName);

        Lightmapping.BakeAsync();
        while (Lightmapping.isRunning) { yield return null; }
        EditorSceneManager.SaveScene(geometryScene);
        EditorSceneManager.SaveScene(lightingScene);
        StoreLightingData();
        EditorSceneManager.CloseScene(lightingScene, true);
        AssetDatabase.SaveAssets();
    }

    public bool SearchLightsNeededRealtime()
    {
        bool latestBuildHasRealtimeLights = false;

        var lights = FindObjectsOfType<Light>();
        var reflectionProbes = FindObjectsOfType<ReflectionProbe>();

        foreach (Light light in lights)
        {
            if (light.lightmapBakeType == LightmapBakeType.Mixed || light.lightmapBakeType == LightmapBakeType.Realtime)
                latestBuildHasRealtimeLights = true;
        }
        if (reflectionProbes.Length > 0)
            latestBuildHasRealtimeLights = true;

        return latestBuildHasRealtimeLights;
    }

    public void StoreLightingData()
    {
        LightingScenarioData scenarioData = (LightingScenarioData)target;

        GenerateLightingData(scenarioData);
        if (scenarioData.lightProbesAsset == null)
        {
            var probes = ScriptableObject.CreateInstance<LightProbesAsset>();
            string path = AssetDatabase.GetAssetPath(scenarioData);
            if (path == "")
            {
                path = "Assets";
            }
            else if (Path.GetExtension(path) != "")
            {
                path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(scenarioData)), "");
            }

            string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath(path + scenarioData.name + "_LightProbes" + ".asset");
            AssetDatabase.CreateAsset(probes, assetPathAndName);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            scenarioData.lightProbesAsset = probes;
        }
        scenarioData.lightProbesAsset.lightProbes = LightmapSettings.lightProbes.bakedProbes;

        EditorUtility.SetDirty(scenarioData.lightProbesAsset);
        EditorUtility.SetDirty(scenarioData);
        AssetDatabase.SaveAssets();
    }

    static void GenerateLightingData(LightingScenarioData data)
    {
        var newRendererInfos = new List<LevelLightmapData.RendererInfo>();
        var newLightmapsLight = new List<Texture2D>();
        var newLightmapsDir = new List<Texture2D>();
        var newLightmapsShadow = new List<Texture2D>();

        data.lightmapsMode = LightmapSettings.lightmapsMode;

        //TODO : Fin better solution for terrain. This is not compatible with several terrains.
        Terrain terrain = FindObjectOfType<Terrain>();
        if (terrain != null && terrain.lightmapIndex != -1 && terrain.lightmapIndex != 65534)
        {
            LevelLightmapData.RendererInfo terrainRendererInfo = new LevelLightmapData.RendererInfo();
            terrainRendererInfo.lightmapScaleOffset = terrain.lightmapScaleOffset;

            Texture2D lightmaplight = LightmapSettings.lightmaps[terrain.lightmapIndex].lightmapColor;
            terrainRendererInfo.lightmapIndex = newLightmapsLight.IndexOf(lightmaplight);
            if (terrainRendererInfo.lightmapIndex == -1)
            {
                terrainRendererInfo.lightmapIndex = newLightmapsLight.Count;
                newLightmapsLight.Add(lightmaplight);
            }

            if (data.lightmapsMode != LightmapsMode.NonDirectional)
            {
                Texture2D lightmapdir = LightmapSettings.lightmaps[terrain.lightmapIndex].lightmapDir;
                terrainRendererInfo.lightmapIndex = newLightmapsDir.IndexOf(lightmapdir);
                if (terrainRendererInfo.lightmapIndex == -1)
                {
                    terrainRendererInfo.lightmapIndex = newLightmapsDir.Count;
                    newLightmapsDir.Add(lightmapdir);
                }
            }
            if (LightmapSettings.lightmaps[terrain.lightmapIndex].shadowMask != null)
            {
                Texture2D lightmapShadow = LightmapSettings.lightmaps[terrain.lightmapIndex].shadowMask;
                terrainRendererInfo.lightmapIndex = newLightmapsShadow.IndexOf(lightmapShadow);
                if (terrainRendererInfo.lightmapIndex == -1)
                {
                    terrainRendererInfo.lightmapIndex = newLightmapsShadow.Count;
                    newLightmapsShadow.Add(lightmapShadow);
                }
            }
            if (data.storeRendererInfos)
            {
                newRendererInfos.Add(terrainRendererInfo);
                if (Application.isEditor)
                    Debug.Log("Terrain lightmap stored in" + terrainRendererInfo.lightmapIndex.ToString());
            }

        }

        var renderers = FindObjectsOfType(typeof(Renderer));

        foreach (Renderer renderer in renderers)
        {
            if (renderer.lightmapIndex != -1 && renderer.lightmapIndex != 65534)
            {
                LevelLightmapData.RendererInfo info = new LevelLightmapData.RendererInfo();
                info.transformHash = LevelLightmapData.GetStableHash(renderer.gameObject.transform); 
                info.meshHash = renderer.gameObject.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                info.name = renderer.gameObject.name;
                info.lightmapScaleOffset = renderer.lightmapScaleOffset; 

                Texture2D lightmaplight = LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapColor;
                info.lightmapIndex = newLightmapsLight.IndexOf(lightmaplight); 
                if (info.lightmapIndex == -1)
                {
                    info.lightmapIndex = newLightmapsLight.Count;
                    newLightmapsLight.Add(lightmaplight);
                }

                if (data.lightmapsMode != LightmapsMode.NonDirectional)
                {
                    Texture2D lightmapdir = LightmapSettings.lightmaps[renderer.lightmapIndex].lightmapDir;
                    info.lightmapIndex = newLightmapsDir.IndexOf(lightmapdir);
                    if (info.lightmapIndex == -1)
                    {
                        info.lightmapIndex = newLightmapsDir.Count;
                        newLightmapsDir.Add(lightmapdir);
                    }
                }
                if (LightmapSettings.lightmaps[renderer.lightmapIndex].shadowMask != null)
                {
                    Texture2D lightmapShadow = LightmapSettings.lightmaps[renderer.lightmapIndex].shadowMask;
                    info.lightmapIndex = newLightmapsShadow.IndexOf(lightmapShadow);
                    if (info.lightmapIndex == -1)
                    {
                        info.lightmapIndex = newLightmapsShadow.Count;
                        newLightmapsShadow.Add(lightmapShadow);
                    }
                }
                if (data.storeRendererInfos)
                {
                    newRendererInfos.Add(info);
                    if (Application.isEditor)
                        Debug.Log("stored info for " + renderers.Length + " meshrenderers");
                }
            }
        }
        data.lightmaps = newLightmapsLight.ToArray(); 
        data.lightmapsDir = newLightmapsDir.ToArray();
        data.shadowMasks = newLightmapsShadow.ToArray();
        data.rendererInfos = newRendererInfos.ToArray();
    }
}