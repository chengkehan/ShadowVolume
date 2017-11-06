using UnityEngine;
using UnityEditor;
using System.Collections;
using System.IO;
using System.Text;

public class ShadowVolume_ObjExporterScript
{
    private static int StartIndex = 0;

    public static void Start()
    {
        StartIndex = 0;
    }
    public static void End()
    {
        StartIndex = 0;
    }


    public static string MeshToString(MeshFilter mf, Transform t)
    {
        Vector3 s = t.localScale;
        Vector3 p = t.localPosition;
        Quaternion r = t.localRotation;


        int numVertices = 0;
        Mesh m = mf.sharedMesh;
        if (!m)
        {
            return "####Error####";
        }

        StringBuilder sb = new StringBuilder();

        foreach (Vector3 vv in m.vertices)
        {
            Vector3 v = t.TransformPoint(vv);
            numVertices++;
            sb.Append(string.Format("v {0} {1} {2}\n", -vv.x, vv.y, vv.z));
        }
        for (int material = 0; material < m.subMeshCount; material++)
        {
            sb.Append("\n");
            sb.Append("usemtl ").Append("mat" + material).Append("\n");
            sb.Append("usemap ").Append("mat" + material).Append("\n");

            int[] triangles = m.GetTriangles(material);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                sb.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                    triangles[i] + 1 + StartIndex, triangles[i + 2] + 1 + StartIndex, triangles[i + 1] + 1 + StartIndex));
            }
        }

        StartIndex += numVertices;
        return sb.ToString();
    }
}

public class ShadowVolume_ObjExporter : ScriptableObject
{
    public static string DoExport(GameObject go, string comment)
    {
        bool makeSubmeshes = false;

        string meshName = go.GetComponent<MeshFilter>().sharedMesh.name;

        ShadowVolume_ObjExporterScript.Start();

        StringBuilder meshString = new StringBuilder();

        meshString.Append(comment + "\n");
        meshString.Append("#" + meshName + ".obj"
                            + "\n#" + System.DateTime.Now.ToLongDateString()
                            + "\n#" + System.DateTime.Now.ToLongTimeString()
                            + "\n#-------"
                            + "\n\n");

        Transform t = go.transform;

        Vector3 originalPosition = t.position;
        t.position = Vector3.zero;

        if (!makeSubmeshes)
        {
            meshString.Append("g ").Append(t.name).Append("\n");
        }
        meshString.Append(processTransform(t, makeSubmeshes));

        t.position = originalPosition;

        ShadowVolume_ObjExporterScript.End();

        return meshString.ToString();
    }

    static string processTransform(Transform t, bool makeSubmeshes)
    {
        StringBuilder meshString = new StringBuilder();

        meshString.Append("#" + t.name
                        + "\n#-------"
                        + "\n");

        if (makeSubmeshes)
        {
            meshString.Append("g ").Append(t.name).Append("\n");
        }

        MeshFilter mf = t.GetComponent<MeshFilter>();
        if (mf)
        {
            meshString.Append(ShadowVolume_ObjExporterScript.MeshToString(mf, t));
        }

        return meshString.ToString();
    }

    static void WriteToFile(string s, string filename)
    {
        using (StreamWriter sw = new StreamWriter(filename))
        {
            sw.Write(s);
        }
    }
}