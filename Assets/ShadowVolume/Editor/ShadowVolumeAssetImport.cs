using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public class ShadowVolumeAssetImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if(assetPath.ToLower().EndsWith(".obj"))
        {
            if (File.ReadAllText(assetPath).Contains(ShadowVolumeSetting.EXPORT_OBJ_COMMENT))
            {
                ModelImporter modelImporter = assetImporter as ModelImporter;
                modelImporter.importMaterials = false;
                modelImporter.importNormals = ModelImporterNormals.None;
            }
        }
    }
}
