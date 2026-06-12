using System;
using System.Collections.Generic;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "NuoYan/PoolConfig", order = 10)]
    public class PoolConfigScriptableObject : ScriptableObject
    {
#if UNITY_EDITOR
        [SerializeField] internal List<PoolFolderEntry> folderEntries = new List<PoolFolderEntry>();
        [SerializeField] internal string constantClassPath = "Assets/Scripts/GameObjectPool/Runtime/PoolItemConstants";
#endif
        public List<PoolConfig> configs;
        private static readonly string _configPath = "Assets/Resources/GameObjectPool";


        public static PoolConfigScriptableObject GetInstance()
        {
            var cfg = Resources.Load<PoolConfigScriptableObject>("GameObjectPool/PoolConfig");
            if (cfg == null)
            {
                return null;
            }
            return cfg;
        }
#if UNITY_EDITOR
        [MenuItem("Tools/NuoYan/Pool/Create GameObjectPool Config")]
        public static void Create()
        {
            if (GetInstance() == null)
            {
                var cfg = ScriptableObject.CreateInstance<PoolConfigScriptableObject>();
                if (!Directory.Exists(_configPath))
                {
                    Directory.CreateDirectory(_configPath);
                }
                AssetDatabase.CreateAsset(cfg, Path.Combine(_configPath, "PoolConfig.asset"));
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
            else
                Debug.LogError("已存在PoolConfig，请勿重复创建");
        }
#endif
    }

    /// <summary>
    /// 对象池配置项。
    /// </summary>
    [Serializable]
    public class PoolConfig
    {
        public string asset;
        public float time;
        public int poolcnt;
    }

#if UNITY_EDITOR
    [Serializable]
    internal class PoolFolderEntry
    {
        public string folderPath;
        public float time = 10f;
        public int poolcnt = 10;
        public bool useFullPath;
    }
#endif
}