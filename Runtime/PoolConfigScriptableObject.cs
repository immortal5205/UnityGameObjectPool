using System;
using System.Collections.Generic;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    [CreateAssetMenu(fileName = "PoolConfig", menuName = "NuoYan/PoolConfig", order = 10)]
    public class PoolConfigScriptableObject : ScriptableObject
    {
#if UNITY_EDITOR
        [SerializeField] internal List<PoolFolderEntry> folderEntries = new List<PoolFolderEntry>();
        [SerializeField] internal string constantClassPath = "Assets/Plugins/Extension/GameObjectPool/Runtime/PoolItemConstants";
#endif
        public List<PoolConfig> configs;
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