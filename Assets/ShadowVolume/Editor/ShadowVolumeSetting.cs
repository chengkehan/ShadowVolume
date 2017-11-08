using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System;
using UnityEngine.SceneManagement;

public class ShadowVolumeSetting : EditorWindow
{
    private static ShadowVolumeSetting s_instance = null;

    [MenuItem("Window/Lighting/Shadow Volume Setting")]
    private static void Init()
    {
        if (s_instance == null)
        {
            s_instance = EditorWindow.CreateInstance<ShadowVolumeSetting>();
            s_instance.name = "Shadow Volume Setting";
            s_instance.minSize = new Vector2(480, 400);
            s_instance.maxSize = new Vector2(480, 400);
        }
        s_instance.ShowUtility();
    }

    public const string EXPORT_OBJ_COMMENT = "# Shadow Volume Mesh";

    private int groundLayer = 1;

    private string shadowVolumeTag = "Untagged";

    private Light dirLight = null;

    private Camera mainCam = null;

    private float capsOffset = 0.001f;

    private bool twoSubMeshes = false;

    private BakingTaskManager bakingTaskManager = new BakingTaskManager();

    private void OnEnable()
    {
        SetupDirLight();
        SetupMainCam();
        EditorApplication.update += ShadowVolumeSettingUpdate;
        EditorSceneManager.sceneOpened += ShadowVolumeSceneOpened;
    }

    private void OnDisable()
    {
        EditorApplication.update -= ShadowVolumeSettingUpdate;
        EditorSceneManager.sceneOpened -= ShadowVolumeSceneOpened;
        ReleaseMemory();
    }

    private void ShadowVolumeSceneOpened(Scene scene, OpenSceneMode mode)
    {
        SetupDirLight();
        SetupMainCam();
    }

    private void RefreshSceneViews()
    {
        SceneView.RepaintAll();
    }

    public static void MarkSceneAsDirty()
    {
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }

    private void ReleaseMemory()
    {
        System.GC.Collect();
        Resources.UnloadUnusedAssets();
        EditorUtility.UnloadUnusedAssetsImmediate();
    }

    private void ShadowVolumeSettingUpdate()
    {
        BakingTaskManagerUpdate();
    }

    private void BakingTaskManagerUpdate()
    {
        if (bakingTaskManager.IsBusy)
        {
            bool isCancelled = EditorUtility.DisplayCancelableProgressBar("Baking Shadow Volume", string.Empty, bakingTaskManager.ProgressValue);
            if(isCancelled)
            {
                bakingTaskManager.StopAllTasks();
                ReleaseMemory();
            }
        }
        else
        {
            EditorUtility.ClearProgressBar();
        }

        bakingTaskManager.Update();

        ABakingTask completeTask = bakingTaskManager.PopCompleteTask();
        if(completeTask != null)
        {
            CreateShadowVolume(completeTask);
            ShadowVolumeCameraDraw();
            MarkSceneAsDirty();
        }
    }

    private void CombineAllShadowVolumesIntoOne()
    {
        ShadowVolumeObject[] svObjs = FindObjectsOfType<ShadowVolumeObject>();
        if(svObjs == null || svObjs.Length == 0)
        {
            ShowNotification("Nothing to be combined");
            return;
        }

        DeleteCombinedShadowVolume();

        List<Mesh> combinedMeshes = new List<Mesh>();
        List<Vector3> combinedVertices = new List<Vector3>();
        List<int> combinedTriangles = new List<int>();
        int index = 0;
        foreach(var svObj in svObjs)
        {
            ++index;

            Matrix4x4 l2w = svObj.transform.localToWorldMatrix;
            MeshFilter mf = svObj.GetComponent<MeshFilter>();
            if(mf == null || mf.sharedMesh == null)
            {
                Debug.LogError("Skip a Shadow Volume as Combining. " + svObj.gameObject.name, svObj.gameObject);
                continue;
            }

            EditorUtility.DisplayProgressBar(string.Empty, "Combining", (float)index / (float)svObjs.Length);

            Mesh mesh = mf.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            if (combinedVertices.Count + vertices.Length >= 65000)
            {
                combinedMeshes.Add(CreateCombinedMesh(combinedVertices, combinedTriangles));
                combinedVertices.Clear();
                combinedTriangles.Clear();
            }

            int numVertices = vertices.Length;
            int trianglesLength = triangles.Length;
            int trianglesOffset = combinedVertices.Count;
            for(int i = 0; i < numVertices; ++i)
            {
                vertices[i] = l2w.MultiplyPoint(vertices[i]);
            }
            for(int i = 0; i < trianglesLength; ++i)
            {
                triangles[i] += trianglesOffset;
            }

            combinedVertices.AddRange(vertices);
            combinedTriangles.AddRange(triangles);
        }
        combinedMeshes.Add(CreateCombinedMesh(combinedVertices, combinedTriangles));

        foreach(var combinedMesh in combinedMeshes)
        {
            GameObject combinedGo = new GameObject("_$SV Combined$_");
            combinedGo.transform.parent = GetRoot();
            combinedGo.transform.localScale = Vector3.one;
            combinedGo.transform.localEulerAngles = Vector3.zero;
            combinedGo.transform.localPosition = Vector3.zero;

            MeshFilter combinedMF = combinedGo.AddComponent<MeshFilter>();
            combinedMF.sharedMesh = combinedMesh;

            MeshRenderer combinedMR = combinedGo.AddComponent<MeshRenderer>();
            combinedMR.sharedMaterial = GetDebugMaterial();
            combinedMR.enabled = false;

            combinedGo.AddComponent<ShadowVolumeCombined>();
        }

        foreach (var svObj in svObjs)
        {
            DestroyImmediate(svObj.gameObject);
        }

        EditorUtility.ClearProgressBar();
    }

    private void GotoSVO()
    {
        if(Selection.activeGameObject == null)
        {
            ShowNotification("Please Select a GameObject");
        }
        else
        {
            ShadowVolumeObject svo = FindShadowVolumeObject(Selection.activeGameObject);
            if(svo != null)
            {
                Selection.activeGameObject = svo.gameObject;
            }
            else
            {
                svo = Selection.activeGameObject.GetComponent<ShadowVolumeObject>();
                if(svo != null)
                {
                    Selection.activeGameObject = svo.source;
                }
            }
        }
    }

    private void ExportShadowVolumeMesh()
    {
        if (Selection.activeGameObject == null)
        {
            ShowNotification("Please Select a GameObject");
        }
        else
        {
            ShadowVolumeObject svo = FindShadowVolumeObject(Selection.activeGameObject);
            if(svo == null)
            {
                svo = Selection.activeGameObject.GetComponent<ShadowVolumeObject>();
            }
            if(svo == null)
            {
                ShowNotification("Cannot find Shadow Volume Object");
                return;
            }

            MeshFilter mf = svo.GetComponent<MeshFilter>();
            if(mf == null || mf.sharedMesh == null)
            {
                ShowNotification("Cannot find Shadow Volume Mesh");
                return;
            }
            if(!mf.sharedMesh.isReadable)
            {
                ShowNotification("Shadow Volume Mesh is not readable");
                return;
            }

            string savePath = EditorUtility.SaveFilePanelInProject("Export Shadow Volume Mesh", mf.sharedMesh.name, "obj", string.Empty);
            if(string.IsNullOrEmpty(savePath))
            {
                return;
            }

            File.WriteAllText(savePath, ShadowVolume_ObjExporter.DoExport(svo.gameObject, EXPORT_OBJ_COMMENT));


            AssetDatabase.Refresh();
        }
    }

    private Mesh CreateCombinedMesh(List<Vector3> combinedVertices, List<int> combinedTriangles)
    {
        Mesh mesh = new Mesh();
        mesh.name = "Shadow Volume Combined";
        mesh.vertices = combinedVertices.ToArray();
        mesh.triangles = combinedTriangles.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void DeleteCombinedShadowVolume()
    {
        ShadowVolumeCombined[] svCombinedList = FindObjectsOfType<ShadowVolumeCombined>();
        foreach(var item in svCombinedList)
        {
            DestroyImmediate(item.gameObject);
        }
    }

    private void CreateShadowVolume(ABakingTask completeBakingTask)
    {
        Transform root = GetRoot();

        Material debugMtrl = GetDebugMaterial();

        ShadowVolumeObject svObj = FindShadowVolumeObject(completeBakingTask.Transform.gameObject);
        if (svObj == null)
        {
            GameObject newMeshGo = new GameObject("_$SV " + completeBakingTask.Transform.gameObject.name + "$_");
            newMeshGo.transform.parent = root;
            newMeshGo.transform.localScale = completeBakingTask.Transform.lossyScale;
            newMeshGo.transform.localEulerAngles = completeBakingTask.Transform.eulerAngles;
            newMeshGo.transform.localPosition = completeBakingTask.Transform.position;
            MeshFilter mf = newMeshGo.AddComponent<MeshFilter>();
            mf.sharedMesh = completeBakingTask.GetNewMesh();
            MeshRenderer mr = newMeshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = debugMtrl;
            mr.enabled = false;

            svObj = newMeshGo.AddComponent<ShadowVolumeObject>();
            svObj.source = completeBakingTask.Transform.gameObject;
            svObj.sourceMeshRenderer = completeBakingTask.Transform.GetComponent<MeshRenderer>();
            svObj.sourceMeshFilter = completeBakingTask.Transform.GetComponent<MeshFilter>();
            svObj.meshFilter = mf;
			svObj.l2w = mf.transform.localToWorldMatrix;
            svObj.wPos = mf.transform.position;
        }
        else
        {
            svObj.transform.parent = root;
            svObj.transform.localScale = completeBakingTask.Transform.lossyScale;
            svObj.transform.localEulerAngles = completeBakingTask.Transform.eulerAngles;
            svObj.transform.localPosition = completeBakingTask.Transform.position;

			svObj.l2w = svObj.meshFilter.transform.localToWorldMatrix;
            svObj.wPos = svObj.meshFilter.transform.position;

            MeshFilter mf = svObj.gameObject.GetComponent<MeshFilter>();
            if (mf == null)
            {
                mf = svObj.gameObject.AddComponent<MeshFilter>();
            }
            mf.sharedMesh = completeBakingTask.GetNewMesh();

            MeshRenderer mr = svObj.gameObject.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                mr = svObj.gameObject.AddComponent<MeshRenderer>();
            }
            mr.sharedMaterial = debugMtrl;
            mr.enabled = false;
        }
    }

    private void SetupDirLight()
    {
        Light[] lights = Light.GetLights(LightType.Directional, 0);
        if (lights != null)
        {
            foreach (var light in lights)
            {
                dirLight = light;
                break;
            }
        }
    }

    private void SetupMainCam()
    {
        if(Camera.main != null)
        {
            mainCam = Camera.main;
        }
    }

    private void CreateShadowVolumeCamera()
    {
        if(mainCam != null)
        {
            ShadowVolumeCamera svc = mainCam.GetComponent<ShadowVolumeCamera>();
            if(svc == null)
            {
                mainCam.gameObject.AddComponent<ShadowVolumeCamera>();
            }
        }
    }

    private void DestroyShadowVolumeCamera()
    {
        if(mainCam != null)
        {
            ShadowVolumeCamera svc = mainCam.GetComponent<ShadowVolumeCamera>();
            if (svc != null)
            {
                DestroyImmediate(svc);
            }
        }
    }

    private void ShadowVolumeCameraDraw()
    {
        ShadowVolumeCamera.DrawAllCameras_Editor();
    }

    private void OnGUI()
    {
        BeginBox();
        {
            OnGUI_Config();
            OnGUI_Buttons();
        }
        EndBox();

        OnGUI_Settings();
    }

    private void OnGUI_Config()
    {
        mainCam = EditorGUILayout.ObjectField("Camera", mainCam, typeof(Camera), true) as Camera;

        groundLayer = EditorGUILayout.LayerField("Ground Layer", groundLayer);
        shadowVolumeTag = EditorGUILayout.TagField("Shadow Volume Tag", shadowVolumeTag);

        dirLight = EditorGUILayout.ObjectField("Directional Light", dirLight, typeof(Light), true) as Light;

        capsOffset = EditorGUILayout.FloatField("Caps Offset", capsOffset);

        //twoSubMeshes = EditorGUILayout.Toggle("Two SubMeshes", twoSubMeshes);
        twoSubMeshes = false;
    }

    private void OnGUI_Buttons()
    {
        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Bake All"))
            {
                BakeAll();
            }

            BeginGUIColor(Color.red);
            bool clearBakedData_b = GUILayout.Button("Clear Baked Data");
            EndGUIColor();
            if(clearBakedData_b)
            {
                bool b = EditorUtility.DisplayDialog(string.Empty, "Clear Baked Data?", "YES", "NO");
                if (b)
                {
                    ClearBakedData();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Bake Selected"))
            {
                BakeSelected();
            }

            BeginGUIColor(new Color(0.75f, 0, 0, 1));
            bool clearSelected_b = GUILayout.Button("Clear Selected");
            EndGUIColor();
            if(clearSelected_b)
            {
                ClearSelected();
                ShadowVolumeCameraDraw();
                MarkSceneAsDirty();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        {
            if(GUILayout.Button("Combine Into One"))
            {
                bool b = EditorUtility.DisplayDialog(string.Empty, "Are you sure combine all shadow volume meshes into one?", "YES", "NO");
                if (b)
                {
                    CombineAllShadowVolumesIntoOne();
                    ShadowVolumeCameraDraw();
                    MarkSceneAsDirty();
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        {
            BeginGUIColor(Color.green);
            bool exportShadowVolumeMesh_b = GUILayout.Button("Export Shadow Volume Mesh");
            EndGUIColor();
            if (exportShadowVolumeMesh_b)
            {
                ExportShadowVolumeMesh();
            }

            BeginGUIColor(new Color(0, 0.75f, 0, 1));
            bool gotoSVO_b = GUILayout.Button("Goto SVO");
            EndGUIColor();
            if(gotoSVO_b)
            {
                GotoSVO();
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private void OnGUI_Settings()
    {
        if (mainCam != null)
        {
            ShadowVolumeCamera svc = mainCam.GetComponent<ShadowVolumeCamera>();
            if (svc != null)
            {
                BeginBox();
                {
                    // Shadow Color
                    EditorGUI.BeginChangeCheck();
                    svc.shadowColor = EditorGUILayout.ColorField("Shadow Color", svc.shadowColor);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ShadowVolumeCamera.DrawAllCameras_Editor();
                        RefreshSceneViews();
                        MarkSceneAsDirty();
                    }

                    // Two-Side Stencil
                    EditorGUI.BeginChangeCheck();
                    svc.isTwoSideStencil = EditorGUILayout.Toggle("Two-Side Stencil", svc.isTwoSideStencil);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ShadowVolumeCamera.DrawAllCameras_Editor();
                        RefreshSceneViews();
                        MarkSceneAsDirty();
                    }

                    // RenderTexture Composite
                    EditorGUI.BeginChangeCheck();
                    svc.isRenderTextureComposite = EditorGUILayout.Toggle("RT Composite", svc.isRenderTextureComposite);
                    if(EditorGUI.EndChangeCheck())
                    {
                        ShadowVolumeCamera.DrawAllCameras_Editor();
                        RefreshSceneViews();
                        MarkSceneAsDirty();
                    }

                    // anti-aliasing
                    EditorGUI.BeginChangeCheck();
                    svc.anti_aliasing = EditorGUILayout.Toggle("Anti-Aliasing", svc.anti_aliasing);
                    if(EditorGUI.EndChangeCheck())
                    {
                        ShadowVolumeCamera.DrawAllCameras_Editor();
                        RefreshSceneViews();
                        MarkSceneAsDirty();
                    }

					// Lock Root
					EditorGUI.BeginChangeCheck();
					Transform root = GetRoot();
					bool svvb = root.gameObject.hideFlags == HideFlags.None;
                    svvb = !EditorGUILayout.Toggle("Lock Root", !svvb);
                    if(EditorGUI.EndChangeCheck())
                    {
                        Selection.activeGameObject = null;
                        root.gameObject.hideFlags = svvb ? HideFlags.None : HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                        MarkSceneAsDirty();
                    }

                    BeginBox();
                    {
                        // Shadow Distance
                        EditorGUI.BeginChangeCheck();
                        svc.shadowDistance = Mathf.Max(EditorGUILayout.FloatField("Shadow Distance", svc.shadowDistance), 0.0f);
                        if (EditorGUI.EndChangeCheck())
                        {
                            ShadowVolumeCamera.DrawAllCameras_Editor();
                            RefreshSceneViews();
                            MarkSceneAsDirty();
                        }

                        // Shadow Distance Fade
                        if (svc.IsShadowDistanceEnabled() && svc.isRenderTextureComposite)
                        {
                            EditorGUI.BeginChangeCheck();
                            svc.shadowDistanceFade = EditorGUILayout.Toggle("Fade", svc.shadowDistanceFade);
                            if (EditorGUI.EndChangeCheck())
                            {
                                ShadowVolumeCamera.DrawAllCameras_Editor();
                                RefreshSceneViews();
                                MarkSceneAsDirty();
                            }

                            EditorGUI.BeginChangeCheck();
                            svc.shadowDistanceFadeLength = Mathf.Min(Mathf.Max(EditorGUILayout.FloatField("Fade Length", svc.shadowDistanceFadeLength), 0.1f), svc.shadowDistance);
                            if (EditorGUI.EndChangeCheck())
                            {
                                ShadowVolumeCamera.DrawAllCameras_Editor();
                                RefreshSceneViews();
                                MarkSceneAsDirty();
                            }
                        }
                    }
                    EndBox();
                }
                EndBox();
            }
        }
    }

    private bool CheckingBeforeBaking()
    {
        if(mainCam == null)
        {
            ShowNotification("Set Camera");
            return false;
        }
        if(groundLayer == 0)
        {
            ShowNotification("Set Ground Layer");
            return false;
        }
        if(dirLight == null)
        {
            ShowNotification("Set Directional Light");
            return false;
        }
        return true;
    }

    private void ShowNotification(string txt)
    {
        ShowNotification(new GUIContent(txt));
    }

    private void BakeAll()
    {
        if (!CheckingBeforeBaking())
        {
            return;
        }

        GameObject[] gos = GameObject.FindGameObjectsWithTag(shadowVolumeTag);
        if(gos == null || gos.Length == 0)
        {
            ShowNotification("There is not any GameObject with tag " + shadowVolumeTag);
        }
        else
        {
            CreateShadowVolumeCamera();
            DeleteCombinedShadowVolume();
            DeleteOldShadowVolumes();
            bakingTaskManager.Init();

            foreach(var go in gos)
            {
                if (go == null)
                {
                    continue;
                }

                BakeAGameObject(go);
            }
        }
    }

    private void ClearSelected()
    {
        GameObject[] selectedGos = Selection.gameObjects;
        if (selectedGos == null || selectedGos.Length == 0)
        {
            ShowNotification("Select one or more GameObject in Scene");
        }
        else
        {
            foreach(var selectedGo in selectedGos)
            {
                ShadowVolumeObject svObj = FindShadowVolumeObject(selectedGo);
                if(svObj == null)
                {
                    svObj = selectedGo.GetComponent<ShadowVolumeObject>();
                }
                if(svObj != null)
                {
                    DestroyImmediate(svObj.gameObject);
                }
            }
        }
    }

    private void BakeSelected()
    {
        if(!CheckingBeforeBaking())
        {
            return;
        }

        GameObject[] selectedGos = Selection.gameObjects;
        if (selectedGos == null || selectedGos.Length == 0)
        {
            ShowNotification("Select one or more GameObject in Scene");
        }
        else
        {
            CreateShadowVolumeCamera();
            DeleteCombinedShadowVolume();
            DeleteOldShadowVolumes();
            bakingTaskManager.Init();

            foreach (var selectedGo in selectedGos)
            {
                if (selectedGo == null)
                {
                    continue;
                }
                BakeAGameObject(selectedGo);
            }
        }
    }

    private void ClearBakedData()
    {
        DestroyShadowVolumeCamera();
        DeleteRoot();
        ReleaseMemory();
        MarkSceneAsDirty();
    }

    private void BakeAGameObject(GameObject go)
    {
        ShadowVolumeObject svo = go.GetComponent<ShadowVolumeObject>();
        if(svo != null)
        {
            go = svo.source;
        }

        PrefabType type = PrefabUtility.GetPrefabType(go);
        if (type == PrefabType.Prefab)
        {
            Debug.LogError("Skip a Shadow Volume Baking, because of it is a Prefab. " + go.name, go);
            return;
        }

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if(mf == null)
        {
            Debug.LogError("Skip a Shadow Volume Baking, becase of there is no MeshFilter. " + go.name, go);
            return;
        }
        if(mf.sharedMesh == null)
        {
            Debug.LogError("Skip a Shadow Volume Baking, becase of there is no Mesh. " + go.name, go);
            return;
        }
        if(!mf.sharedMesh.isReadable)
        {
            Debug.LogError("Skip a Shadow Volume Baking, becase of Mesh is not readable. " + go.name, go);
            return;
        }

        Transform transform = go.transform;

        ABakingTask task = new ABakingTask();
        task.Init(transform, transform.localToWorldMatrix, transform.worldToLocalMatrix, dirLight.transform.forward, mf.sharedMesh, capsOffset, groundLayer, twoSubMeshes);
        bakingTaskManager.AddTask(task);
    }

    private ShadowVolumeObject FindShadowVolumeObject(GameObject source)
    {
        ShadowVolumeObject[] objs = FindObjectsOfType<ShadowVolumeObject>();
        if(objs == null)
        {
            return null;
        }
        foreach(var obj in objs)
        {
            if(obj.source == source)
            {
                return obj;
            }
        }
        return null;
    }

    private Transform GetRoot()
    {
        ShadowVolumeRoot root = FindObjectOfType<ShadowVolumeRoot>();
        if(root == null)
        {
            GameObject rootGo = new GameObject("_$ShadowVolumeRoot$_");
            rootGo.transform.parent = null;
            rootGo.transform.localScale = Vector3.one;
            rootGo.transform.localEulerAngles = Vector3.zero;
            rootGo.transform.localPosition = Vector3.zero;
            rootGo.AddComponent<ShadowVolumeRoot>();
            return rootGo.transform;
        }
        else
        {
            return root.transform;
        }
    }

    private Material GetDebugMaterial()
    {
        return GetRoot().GetComponent<ShadowVolumeRoot>().DebugMaterial;
    }

    private void DeleteRoot()
    {
        ShadowVolumeRoot root = FindObjectOfType<ShadowVolumeRoot>();
        if (root != null)
        {
            DestroyImmediate(root.gameObject);
        }
    }

    private void DeleteOldShadowVolumes()
    {
        ShadowVolumeObject[] objs = FindObjectsOfType<ShadowVolumeObject>();
        if(objs != null)
        {
            foreach(var obj in objs)
            {
                if (obj.source == null)
                {
                    DestroyImmediate(obj.gameObject);
                }
            }
        }
    }

    private void BeginBox()
    {
        EditorGUILayout.BeginVertical(GUI.skin.GetStyle("Box"));
        EditorGUILayout.Space();
    }

    private void EndBox()
    {
        EditorGUILayout.Space();
        EditorGUILayout.EndVertical();
    }

    private Color tempGUIColor = Color.white;
    private void BeginGUIColor(Color c)
    {
        tempGUIColor = GUI.color;
        GUI.color = c;
    }
    private void EndGUIColor()
    {
        GUI.color = tempGUIColor;
    }

    private class BakingTaskManager
    {
        public bool IsBusy
        {
            get
            {
                return idleTasks.Count > 0 || workingTasks.Count > 0;
            }
        }

        public float ProgressValue
        {
            get
            {
                int totalAmount = totalTasks.Count;
                if(totalAmount == 0)
                {
                    return 0;
                }
                else
                {
                    return (float)(completeTasks.Count + popTasks.Count) / (float)totalAmount;
                }
            }
        }

        private List<ABakingTask> idleTasks = new List<ABakingTask>();

        private List<ABakingTask> workingTasks = new List<ABakingTask>();

        private List<ABakingTask> completeTasks = new List<ABakingTask>();

        private List<ABakingTask> totalTasks = new List<ABakingTask>();

        private List<ABakingTask> popTasks = new List<ABakingTask>();

        private int concurrentTasks = 3;

        public ABakingTask PopCompleteTask()
        {
            if (completeTasks.Count > 0)
            {
                ABakingTask task = completeTasks[completeTasks.Count - 1];
                completeTasks.RemoveAt(completeTasks.Count - 1);
                popTasks.Add(task);

                return task;
            }
            return null;
        }

        public void Init()
        {
            if(IsBusy)
            {
                Debug.LogError("BakingTaskManager Init Failed.");
                return;
            }

            ClearAllTaskLists();
        }

        public void AddTask(ABakingTask task)
        {
            totalTasks.Add(task);
            idleTasks.Add(task);
        }

        public void Update()
        {
            int numWorkingTasks = workingTasks.Count;
            for(int i = 0; i < numWorkingTasks; ++i)
            {
                ABakingTask task = workingTasks[i];
                if(task.IsComplete)
                {
                    completeTasks.Add(task);
                    workingTasks.RemoveAt(i);
                    --i;
                    --numWorkingTasks;
                }
            }

            if(workingTasks.Count < concurrentTasks && idleTasks.Count > 0)
            {
                ABakingTask idleTask = idleTasks[idleTasks.Count - 1];
                idleTasks.RemoveAt(idleTasks.Count - 1);
                workingTasks.Add(idleTask);
                idleTask.Start();
            }
        }

        public void StopAllTasks()
        {
            DestroyTasks(totalTasks);
            ClearAllTaskLists();
        }

        private void ClearAllTaskLists()
        {
            idleTasks.Clear();
            workingTasks.Clear();
            completeTasks.Clear();
            totalTasks.Clear();
            popTasks.Clear();
        }

        private void DestroyTasks(List<ABakingTask> tasks)
        {
            foreach(var aTask in tasks)
            {
                aTask.Destroy();
            }
        }
    }

    private class ABakingTask
    {
        private volatile bool _isComplete = false;
        public bool IsComplete
        {
            private set
            {
                _isComplete = value;
            }
            get
            {
                return _isComplete;
            }
        }

        private volatile bool _isWorking = false;
        public bool IsWorking
        {
            private set
            {
                _isWorking = value;
            }
            get
            {
                return _isWorking;
            }
        }

        public Transform Transform
        {
            get
            {
                return transform;
            }
        }

        private Transform transform = null;
        private Matrix4x4 l2w = Matrix4x4.identity;
        private Matrix4x4 w2l = Matrix4x4.identity;
        private Vector3 wLightDir = Vector3.zero;
        private Vector3 oLightDir = Vector3.zero;
        private float svoffset = 0.001f;
        private int groundLayer = 0;
        private bool twoSubMeshes = false;

        private Mesh mesh = null;
        private Vector3[] meshVertices = null;
        private int[] meshTriangles = null;

        private Vector3[] newVertices = null;
        private int[] newTriangles = null;
        private int[] newTrianglesCaps = null;

        private Thread thread = null;

        private Triangle[] trianglesData = null;
        private Triangle[] trianglesGroundData = null;
        private List<TriangleAndLine> trisLines = new List<TriangleAndLine>();
        private Dictionary<long, int> trisLinesMap = new Dictionary<long, int>();
        private long TLMS = 1000000L;
        private long TLMS2 = 1000000L * 1000000L;

        public Mesh GetNewMesh()
        {
            if(!IsComplete)
            {
                return null;
            }

            Mesh newMesh = new Mesh();
            newMesh.name = "Shadow Volume " + mesh.name;
            newMesh.vertices = newVertices;
            if(twoSubMeshes)
            {
                // 2 SubMeshes
                newMesh.subMeshCount = 2;
                newMesh.SetTriangles(newTriangles, 0);
                newMesh.SetTriangles(newTrianglesCaps, 1);
            }
            else
            {
                // 1 SubMesh
                int[] combinedTriangles = new int[newTriangles.Length + newTrianglesCaps.Length];
                System.Array.Copy(newTriangles, 0, combinedTriangles, 0, newTriangles.Length);
                System.Array.Copy(newTrianglesCaps, 0, combinedTriangles, newTriangles.Length, newTrianglesCaps.Length);
                newMesh.subMeshCount = 1;
                newMesh.vertices = newVertices;
                newMesh.triangles = combinedTriangles;
            }
            newMesh.RecalculateBounds();
            return newMesh;
        }

        public void Init(Transform transform, Matrix4x4 l2w, Matrix4x4 w2l, Vector3 wLightDir, Mesh mesh, float svoffset, int groundLayer, bool twoSubMeshes)
        {
            this.transform = transform;
            this.l2w = l2w;
            this.w2l = w2l;
            this.wLightDir = wLightDir.normalized;
            this.oLightDir = w2l.MultiplyVector(wLightDir).normalized;
            this.mesh = mesh;
            this.meshVertices = mesh.vertices;
            this.meshTriangles = mesh.triangles;
            this.svoffset = svoffset;
            this.groundLayer = 1 << groundLayer;
            this.twoSubMeshes = twoSubMeshes;
        }

        public void Start()
        {
            if(thread != null)
            {
                return;
            }

            IsComplete = false;
            IsWorking = true;

            PrepareTriangleGroundData();

            thread = new Thread(Working);
            thread.Start();
        }

        public void Destroy()
        {
            IsWorking = false;

            if(thread != null)
            {
                try
                {
                    thread.Abort();
                }
                catch(System.Exception e)
                {
                    // Do nothing
                }
                thread = null;
            }
        }

        private void Working()
        {
            if (!IsWorking) return;

            PrepareTrianglesData();

            if (!IsWorking) return;

            CreateNewVerticesAndTriangles();

            if (!IsWorking) return;

            SimplifyNewTriangles();

            if (!IsWorking) return;

            SimplifyNewVertices();

            if (!IsWorking) return;

            IsComplete = true;
        }

        private void PrepareTriangleGroundData()
        {
            trianglesGroundData = new Triangle[meshTriangles.Length / 3];
            Vector3 oLightDir = -this.oLightDir;

            for (int i = 0; i < meshTriangles.Length; i += 3)
            {
                Vector3 p0 = meshVertices[meshTriangles[i + 0]];
                Vector3 p1 = meshVertices[meshTriangles[i + 1]];
                Vector3 p2 = meshVertices[meshTriangles[i + 2]];

                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                n = Vector3Normalize(n);
                bool valid = Vector3.Dot(n, oLightDir) > 0;

                trianglesGroundData[i / 3] = new Triangle() { p0 = PointHitOnGround(p0), p1 = PointHitOnGround(p1), p2 = PointHitOnGround(p2), n = n, valid = valid };
                if (Mathf.Approximately(n.magnitude, 0.0f))
                {
                    Debug.LogError("Error: Normal is Zero.");
                }
            }
        }

        private void PrepareTrianglesData()
        {
            trianglesData = new Triangle[meshTriangles.Length / 3];
            Vector3 oLightDir = -this.oLightDir;

            for (int i = 0; i < meshTriangles.Length; i += 3)
            {
                if (!IsWorking) return;

                Vector3 p0 = meshVertices[meshTriangles[i + 0]];
                Vector3 p1 = meshVertices[meshTriangles[i + 1]];
                Vector3 p2 = meshVertices[meshTriangles[i + 2]];

                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0);
                n = Vector3Normalize(n);
                bool valid = Vector3.Dot(n, oLightDir) > 0;

                trianglesData[i / 3] = new Triangle() { p0 = p0, p1 = p1, p2 = p2, n = n, valid = valid };
                if (Mathf.Approximately(n.magnitude, 0.0f))
                {
                    Debug.LogError("Error: Normal is Zero.");
                }
            }
        }

        private Vector3 PointHitOnGround(Vector3 p)
        {
            RaycastHit hit;
            Ray ray = new Ray(l2w.MultiplyPoint(p), l2w.MultiplyVector(oLightDir));
            if (Physics.Raycast(ray, out hit, 9999, groundLayer))
            {
                return w2l.MultiplyPoint(hit.point) + oLightDir * svoffset;
            }
            else
            {
                return p + oLightDir * svoffset;
            }
        }

        private void CreateNewVerticesAndTriangles()
        {
            List<Triangle> validTriangles = new List<Triangle>();
            List<Triangle> validGroundTriangles = new List<Triangle>();
            for (int i = 0; i < trianglesData.Length; ++i)
            {
                if (!IsWorking) return;

                Triangle triangle = trianglesData[i];
                Triangle groundTriangle = trianglesGroundData[i];
                // Using back face against light
                if (!triangle.valid)
                {
                    // Set triangle face to light
                    Vector3 p = triangle.p2;
                    triangle.p2 = triangle.p1;
                    triangle.p1 = p;
                    // The Save As Above
                    p = groundTriangle.p2;
                    groundTriangle.p2 = groundTriangle.p1;
                    groundTriangle.p1 = p;

                    validTriangles.Add(triangle);
                    validGroundTriangles.Add(groundTriangle);
                }
            }

            newVertices = new Vector3[validTriangles.Count * 3 * 2];
            newTriangles = new int[validTriangles.Count * 3 * 6];
            newTrianglesCaps = new int[validTriangles.Count * 3 * 2];
            int triLineId = 0;
            for (int i = 0; i < validTriangles.Count; ++i)
            {
                if (!IsWorking) return;

                int vi0 = i * 6 + 0;
                int vi1 = i * 6 + 1;
                int vi2 = i * 6 + 2;
                int vi3 = i * 6 + 3;
                int vi4 = i * 6 + 4;
                int vi5 = i * 6 + 5;

                int tiIndex = 0;
                int[] tiList = null;
                {
                    int tstep = 18;
                    tiList = new int[tstep];
                    tiIndex = 0;
                    for (tiIndex = 0; tiIndex < tstep; ++tiIndex)
                    {
                        tiList[tiIndex] = i * tstep + tiIndex;
                    }
                }
                int[] triList_caps = null;
                {
                    int tstep = 6;
                    triList_caps = new int[tstep];
                    tiIndex = 0;
                    for (tiIndex = 0; tiIndex < tstep; ++tiIndex)
                    {
                        triList_caps[tiIndex] = i * tstep + tiIndex;
                    }
                }

                Triangle groundTriangle = validGroundTriangles[i];
                Triangle triangle = validTriangles[i];
                newVertices[vi0] = triangle.p0 - oLightDir * svoffset;
                newVertices[vi1] = triangle.p1 - oLightDir * svoffset;
                newVertices[vi2] = triangle.p2 - oLightDir * svoffset;

                newVertices[vi3] = groundTriangle.p0;
                newVertices[vi4] = groundTriangle.p1;
                newVertices[vi5] = groundTriangle.p2;

                tiIndex = 0;

                newTrianglesCaps[triList_caps[tiIndex++]] = vi0;
                newTrianglesCaps[triList_caps[tiIndex++]] = vi1;
                newTrianglesCaps[triList_caps[tiIndex++]] = vi2;

                newTrianglesCaps[triList_caps[tiIndex++]] = vi3;
                newTrianglesCaps[triList_caps[tiIndex++]] = vi4;
                newTrianglesCaps[triList_caps[tiIndex++]] = vi5;
                Swap(newTrianglesCaps, triList_caps[tiIndex - 1], triList_caps[tiIndex - 2]);

                tiIndex = 0;

                newTriangles[tiList[tiIndex++]] = vi0;
                newTriangles[tiList[tiIndex++]] = vi3;
                newTriangles[tiList[tiIndex++]] = vi4;
                newTriangles[tiList[tiIndex++]] = vi0;
                newTriangles[tiList[tiIndex++]] = vi4;
                newTriangles[tiList[tiIndex++]] = vi1;

                trisLines.Add(new TriangleAndLine() { i0 = vi0, i1 = vi3, i2 = vi4, v0 = newVertices[vi0], v1 = newVertices[vi1], id = ++triLineId });
                trisLinesMap.Add(vi0 * TLMS2 + vi3 * TLMS + vi4, trisLines.Count - 1);
                trisLines.Add(new TriangleAndLine() { i0 = vi0, i1 = vi4, i2 = vi1, v0 = newVertices[vi0], v1 = newVertices[vi1], id = triLineId });
                trisLinesMap.Add(vi0 * TLMS2 + vi4 * TLMS + vi1, trisLines.Count - 1);

                newTriangles[tiList[tiIndex++]] = vi1;
                newTriangles[tiList[tiIndex++]] = vi4;
                newTriangles[tiList[tiIndex++]] = vi5;
                newTriangles[tiList[tiIndex++]] = vi1;
                newTriangles[tiList[tiIndex++]] = vi5;
                newTriangles[tiList[tiIndex++]] = vi2;

                trisLines.Add(new TriangleAndLine() { i0 = vi1, i1 = vi4, i2 = vi5, v0 = newVertices[vi1], v1 = newVertices[vi2], id = ++triLineId });
                trisLinesMap.Add(vi1 * TLMS2 + vi4 * TLMS + vi5, trisLines.Count - 1);
                trisLines.Add(new TriangleAndLine() { i0 = vi1, i1 = vi5, i2 = vi2, v0 = newVertices[vi1], v1 = newVertices[vi2], id = triLineId });
                trisLinesMap.Add(vi1 * TLMS2 + vi5 * TLMS + vi2, trisLines.Count - 1);

                newTriangles[tiList[tiIndex++]] = vi2;
                newTriangles[tiList[tiIndex++]] = vi5;
                newTriangles[tiList[tiIndex++]] = vi3;
                newTriangles[tiList[tiIndex++]] = vi2;
                newTriangles[tiList[tiIndex++]] = vi3;
                newTriangles[tiList[tiIndex++]] = vi0;

                trisLines.Add(new TriangleAndLine() { i0 = vi2, i1 = vi5, i2 = vi3, v0 = newVertices[vi2], v1 = newVertices[vi0], id = ++triLineId });
                trisLinesMap.Add(vi2 * TLMS2 + vi5 * TLMS + vi3, trisLines.Count - 1);
                trisLines.Add(new TriangleAndLine() { i0 = vi2, i1 = vi3, i2 = vi0, v0 = newVertices[vi2], v1 = newVertices[vi0], id = triLineId });
                trisLinesMap.Add(vi2 * TLMS2 + vi3 * TLMS + vi0, trisLines.Count - 1);
            }
        }

        private void SimplifyNewTriangles()
        {
            List<int> trianglesList = new List<int>();
            int numTriangles = newTriangles.Length / 3;
            for (int i = 0; i < numTriangles; ++i)
            {
                if (!IsWorking) return;

                int vi0 = newTriangles[i * 3 + 0];
                int vi1 = newTriangles[i * 3 + 1];
                int vi2 = newTriangles[i * 3 + 2];

                bool isDuplicated = false;

                for (int j = 0; j < numTriangles; ++j)
                {
                    if (!IsWorking) return;

                    if (i != j)
                    {
                        int vi3 = newTriangles[j * 3 + 0];
                        int vi4 = newTriangles[j * 3 + 1];
                        int vi5 = newTriangles[j * 3 + 2];
                        if (TrianglesCanBeSimplified(vi0, vi1, vi2, vi3, vi4, vi5))
                        {
                            isDuplicated = true;
                            break;
                        }
                    }
                }

                if (!isDuplicated)
                {
                    trianglesList.Add(newTriangles[i * 3 + 0]);
                    trianglesList.Add(newTriangles[i * 3 + 1]);
                    trianglesList.Add(newTriangles[i * 3 + 2]);
                }
            }

            newTriangles = trianglesList.ToArray();
        }

        private void SimplifyNewVertices()
        {
            int trianglesLength0 = 0;
            int[] triangles0 = null;
            {
                triangles0 = newTriangles;
                trianglesLength0 = triangles0.Length;
                int numVertices = newVertices.Length;
                for (int i = 0; i < trianglesLength0; ++i)
                {
                    if (!IsWorking) return;

                    Vector3 vi = newVertices[triangles0[i]];
                    for (int j = 0; j < numVertices; ++j)
                    {
                        Vector3 vj = newVertices[j];
                        if (vi == vj)
                        {
                            triangles0[i] = j;
                            break;
                        }
                    }
                }
            }

            int trianglesLength1 = 0;
            int[] triangles1 = null;
            {
                triangles1 = newTrianglesCaps;
                trianglesLength1 = triangles1.Length;
                int numVertices = newVertices.Length;
                for (int i = 0; i < trianglesLength1; ++i)
                {
                    if (!IsWorking) return;

                    Vector3 vi = newVertices[triangles1[i]];
                    for (int j = 0; j < numVertices; ++j)
                    {
                        Vector3 vj = newVertices[j];
                        if (vi == vj)
                        {
                            triangles1[i] = j;
                            break;
                        }
                    }
                }
            }

            List<Vector3> verticesList = new List<Vector3>(this.newVertices);
            for (int i = 0; i < verticesList.Count; ++i)
            {
                if (!IsWorking) return;

                Vector3 vi = verticesList[i];
                for (int j = i; j < verticesList.Count; ++j)
                {
                    if (i != j)
                    {
                        Vector3 vj = verticesList[j];
                        if (vi == vj)
                        {
                            verticesList.RemoveAt(j);
                            for (int k = 0; k < trianglesLength0; ++k)
                            {
                                if (triangles0[k] >= j)
                                {
                                    --triangles0[k];
                                }
                            }
                            for (int k = 0; k < trianglesLength1; ++k)
                            {
                                if (triangles1[k] >= j)
                                {
                                    --triangles1[k];
                                }
                            }
                            --j;
                        }
                    }
                }
            }

            newVertices = verticesList.ToArray();
            newTriangles = triangles0;
            newTrianglesCaps = triangles1;
        }

        private bool TrianglesCanBeSimplified(int vi0, int vi1, int vi2, int vi3, int vi4, int vi5)
        {
            TriangleAndLine me = trisLines[trisLinesMap[vi0 * TLMS2 + vi1 * TLMS + vi2]];
            int meIndex = 1;
            TriangleAndLine other = trisLines[trisLinesMap[vi3 * TLMS2 + vi4 * TLMS + vi5]];
            int otherIndex = 1;
            int length = trisLines.Count;

            if (meIndex != -1 && otherIndex != -1)
            {
                if (me.id != other.id)
                {
                    Line lineMe = new Line() { p0 = me.v0, p1 = me.v1 };
                    Line lineOther = new Line() { p0 = other.v0, p1 = other.v1 };
                    if (LineEqual(lineMe, lineOther))
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                Debug.LogError("Error");
            }

            return false;
        }

        private void Swap(int[] list, int i, int j)
        {
            int v = list[i];
            list[i] = list[j];
            list[j] = v;
        }

        private Vector3 Vector3Normalize(Vector3 n)
        {
            float m = n.magnitude;
            n /= m;
            return n;
        }

        private bool LineEqual(Line a, Line b)
        {
            return
                (a.p0 == b.p0 && a.p1 == b.p1) ||
                (a.p0 == b.p1 && a.p1 == b.p0);
        }

        private struct Triangle
        {
            public Vector3 p0;
            public Vector3 p1;
            public Vector3 p2;
            public Vector3 n;
            public bool valid;
        }

        private struct TriangleAndLine
        {
            public int id;

            public int i0;
            public int i1;
            public int i2;

            public Vector3 v0;
            public Vector3 v1;
        }

        private struct Line
        {
            public int id;
            public bool valid;
            public Vector3 p0;
            public Vector3 p1;
        }
    }
}
