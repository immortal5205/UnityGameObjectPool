using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class PathCopyUtility
{
    /// <summary>
    /// 获得Asset路径，如果选择的是文件夹 Asset/MyFolder，如果选择的是文件 Asset/MyFolder/MyFile.txt
    /// </summary>
    [MenuItem("Assets/Get Asset Path", priority = 3)]
    public static void CopyAssetPath()
    {
        UnityEngine.Object selObj = Selection.activeObject;
        if (selObj != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(selObj);
            EditorGUIUtility.systemCopyBuffer = assetPath;
            Debug.Log($"Asset Path Is:{assetPath}");
        }
    }

    [MenuItem("Assets/Get Directory Path", priority = 3)]
    public static void CopyDirectoryPath()
    {
        UnityEngine.Object selObj = Selection.activeObject;

        if (selObj != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(selObj);
            string dirPath = Application.dataPath.Replace("Assets", "");
            string fullPath = Path.Combine(dirPath, assetPath);
            EditorGUIUtility.systemCopyBuffer = fullPath;
            Debug.Log($"Directory Path Is: {fullPath}");
        }
    }

    [MenuItem("Assets/Get Folder Name", priority = 3)]
    public static void GetAddressablePath()
    {
        UnityEngine.Object selObj = Selection.activeObject;

        if (selObj != null)
        {
            string assetPath = AssetDatabase.GetAssetPath(selObj);
            var split = assetPath.Split('/');
            var name = split.Last();
            assetPath = name.Split('.').First();
            EditorGUIUtility.systemCopyBuffer = assetPath;
            Debug.Log($"Folder Name Is: {assetPath}");
        }
    }
}
