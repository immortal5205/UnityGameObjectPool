using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    /// <summary>
    /// 游戏对象池管理器。使用前请先调用 Initialize(IResourceLoader loader) 设置资源加载器。
    /// </summary>
    public class GameObjectPool : MonoBehaviour
    {
        private static GameObjectPool _instance;
        public static GameObjectPool Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("[GameObjectPool]");
                    _instance = go.AddComponent<GameObjectPool>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
        [Header("检查间隔")] public float checkInterval = 10f;

        [Header("Inspector显示设置")] public bool showDetailedInfo = true;

        [Header("池状态信息")][SerializeField, HideInInspector] private List<ConfigPoolInfo> poolInfos = new List<ConfigPoolInfo>();

        public Transform poolContainer;
        internal IResourceLoader _resourceLoader;

        private List<PoolConfig> _poolConfigs;
        private List<ConfigPool> _configPools;
        private Dictionary<GameObject, ConfigPool> _gameObjectToPool;

        // 预加载临时列表（实例级，避免跨池污染）
        private readonly List<GameObject> _preloadedObjects = new List<GameObject>();

        // 逐帧清理游标
        private int _cleanupCursor;
        private float _lastCleanupTime;
        private bool _initialized;

        #region 初始化
        public void Awake()
        {
            // 惰性初始化：确保数据结构在 Initialize 前就可用
            _configPools = new List<ConfigPool>();
            _gameObjectToPool = new Dictionary<GameObject, ConfigPool>();
            _poolConfigs = new List<PoolConfig>();
        }

        /// <summary>
        /// 初始化池设置，注入自定义资源加载器。
        /// </summary>
        /// <param name="loader">资源加载器实现</param>
        public void Initialize(IResourceLoader loader)
        {
            _resourceLoader = loader ?? throw new ArgumentNullException(nameof(loader));

            if (poolContainer == null)
            {
                GameObject containerGo = new GameObject("PoolContainer");
                poolContainer = containerGo.transform;
                poolContainer.SetParent(transform);
            }

            LoadConfig();
            _lastCleanupTime = Time.time;
            _initialized = true;

            Debug.Log($"对象池初始化完成，共加载了 {_configPools.Count} 个对象池");
        }

        /// <summary>
        /// 加载对象池配置。
        /// </summary>
        private void LoadConfig()
        {
            _configPools.Clear();

            try
            {
                var configAsset = Resources.Load<PoolConfigScriptableObject>("PoolConfig");
                if (configAsset == null || configAsset.configs == null || configAsset.configs.Count == 0)
                {
                    Debug.LogWarning("未找到对象池配置文件或配置为空，请检查 Resources/PoolConfig.asset");
                    _poolConfigs = new List<PoolConfig>();
                    return;
                }

                _poolConfigs = configAsset.configs;
                // 按路径长度降序排列，确保更长的前缀先匹配
                _poolConfigs.Sort((a, b) => b.asset.Length.CompareTo(a.asset.Length));

                foreach (var config in _poolConfigs)
                {
                    var configPool = new ConfigPool(config, _resourceLoader);
                    _configPools.Add(configPool);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"加载对象池配置失败: {e.Message}");
                _poolConfigs = new List<PoolConfig>();
            }
        }

        /// <summary>
        /// 重新加载配置（支持热更场景）。
        /// 只增加新的池配置，不影响已有的池实例。
        /// </summary>
        public void ReloadConfig()
        {
            try
            {
                var configAsset = Resources.Load<PoolConfigScriptableObject>("PoolConfig");
                if (configAsset == null || configAsset.configs == null)
                    return;

                var newConfigs = configAsset.configs;
                newConfigs.Sort((a, b) => b.asset.Length.CompareTo(a.asset.Length));

                foreach (var config in newConfigs)
                {
                    // 跳过已存在的配置
                    bool exists = false;
                    foreach (var existingPool in _configPools)
                    {
                        if (existingPool.Config.asset == config.asset)
                        {
                            exists = true;
                            break;
                        }
                    }
                    if (!exists)
                    {
                        var configPool = new ConfigPool(config, _resourceLoader);
                        _configPools.Add(configPool);
#if UNITY_EDITOR
                        Debug.Log($"热重载：添加新池配置 {config.asset}");
#endif
                    }
                }

                _poolConfigs = newConfigs;
            }
            catch (Exception e)
            {
                Debug.LogError($"重新加载配置失败: {e.Message}");
            }
        }

        #endregion

        #region 生命周期与清理

        private void Update()
        {
            if (!_initialized) return;

            if (_configPools == null || _configPools.Count == 0) return;

            // 逐帧分摊清理：每帧只清理一个池，避免尖峰卡顿
            if (Time.time - _lastCleanupTime >= checkInterval / _configPools.Count)
            {
                PerformIncrementalCleanup();
                _lastCleanupTime = Time.time;
            }

#if UNITY_EDITOR
            // Editor 下自动同步 Inspector 信息
            if (showDetailedInfo)
            {
                UpdateInspectorInfo();
            }
#endif
        }

        /// <summary>
        /// 逐帧增量清理：每帧只处理一个池，分摊开销。
        /// </summary>
        private void PerformIncrementalCleanup()
        {
            int startIndex = _cleanupCursor % _configPools.Count;
            _configPools[startIndex].CheckExpiredObjects();
            _cleanupCursor = (_cleanupCursor + 1) % _configPools.Count;
        }

        /// <summary>
        /// 立即对所有池执行一次完整清理（一次性扫描全部）。
        /// </summary>
        public void ForceCleanup()
        {
            if (_configPools == null) return;

            foreach (var pool in _configPools)
            {
                pool.CheckExpiredObjects();
            }

            _lastCleanupTime = Time.time;
        }

        private void OnDestroy()
        {
            ClearAllPools();
        }

        #endregion

        #region 公共 API — 获取对象

        /// <summary>
        /// 同步获取游戏对象。优先从池中取，池中无可用对象且未满则创建，池满则驱逐最旧对象。
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        /// <returns>游戏对象实例</returns>
        /// <exception cref="InvalidOperationException">池已满且所有对象都在使用中</exception>
        /// <exception cref="Exception">资源加载失败</exception>
        public GameObject GetGameObject(string assetPath)
        {
            ConfigPool pool = FindConfigPool(assetPath);

            if (pool == null)
            {
                throw new InvalidOperationException(
                    $"未找到匹配 '{assetPath}' 的对象池配置，请检查 PoolConfig.asset");
            }

            GameObject go = pool.Get(assetPath);

            if (go != null)
            {
                _gameObjectToPool[go] = pool;
            }

            return go;
        }

        /// <summary>
        /// 异步获取游戏对象。优先从池中取，池中无可用对象且未满则创建，池满则驱逐最旧对象。
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>游戏对象实例</returns>
        /// <exception cref="InvalidOperationException">池已满且所有对象都在使用中</exception>
        /// <exception cref="Exception">资源加载失败</exception>
        public async UniTask<GameObject> GetGameObjectAsync(string assetPath, CancellationToken cancellationToken = default)
        {
            ConfigPool pool = FindConfigPool(assetPath);

            if (pool == null)
            {
                throw new InvalidOperationException(
                    $"未找到匹配 '{assetPath}' 的对象池配置，请检查 PoolConfig.asset");
            }

            GameObject go = await pool.GetAsync(assetPath, cancellationToken);

            if (go != null)
            {
                _gameObjectToPool[go] = pool;
            }

            return go;
        }

        /// <summary>
        /// 归还游戏对象到池中。如果对象不属于任何池，仅输出警告不做销毁。
        /// </summary>
        /// <param name="go">要归还的游戏对象</param>
        public void Release(GameObject go)
        {
            if (go == null) return;

            if (_gameObjectToPool.TryGetValue(go, out ConfigPool pool))
            {
                pool.Return(go);
                _gameObjectToPool.Remove(go);
            }
            else
            {
                Debug.LogWarning(
                    $"GameObjectPool.Release: 对象 '{go.name}' 不属于任何池，已忽略。" +
                    "请确认该对象是通过 GetGameObject/GetGameObjectAsync 获取的池管理对象。");
            }
        }

        #endregion

        #region 公共 API — 预加载

        /// <summary>
        /// 同步预加载指定数量的对象到池中。
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        /// <param name="count">预加载数量</param>
        public void Preload(string assetPath, int count = 1)
        {
            ConfigPool pool = FindConfigPool(assetPath);
            if (pool == null)
            {
                Debug.LogWarning($"资源 {assetPath} 没有对应的池配置，无法预加载");
                return;
            }

            _preloadedObjects.Clear();
            for (int i = 0; i < count; i++)
            {
                GameObject go = pool.Get(assetPath);
                if (go != null)
                {
                    _preloadedObjects.Add(go);
                }
            }

            foreach (var go in _preloadedObjects)
            {
                pool.Return(go);
                _gameObjectToPool.Remove(go);
            }
        }

        /// <summary>
        /// 异步预加载指定数量的对象到池中。
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        /// <param name="count">预加载数量</param>
        /// <param name="cancellationToken">取消令牌</param>
        public async UniTask PreloadAsync(string assetPath, int count = 1, CancellationToken cancellationToken = default)
        {
            ConfigPool pool = FindConfigPool(assetPath);
            if (pool == null)
            {
                Debug.LogWarning($"资源 {assetPath} 没有对应的池配置，无法预加载");
                return;
            }

            _preloadedObjects.Clear();
            for (int i = 0; i < count; i++)
            {
                GameObject go = await pool.GetAsync(assetPath, cancellationToken);
                if (go != null)
                {
                    _preloadedObjects.Add(go);
                }
            }

            foreach (var go in _preloadedObjects)
            {
                pool.Return(go);
                _gameObjectToPool.Remove(go);
            }
        }

        #endregion

        #region 公共 API — 统计与查询

        /// <summary>
        /// 获取指定配置的总对象数。
        /// </summary>
        public int GetTotalCount(string configAsset)
        {
            var pool = FindConfigPool(configAsset);
            return pool?.AllObjects.Count ?? 0;
        }

        /// <summary>
        /// 获取指定配置的活跃对象数。
        /// </summary>
        public int GetActiveCount(string configAsset)
        {
            var pool = FindConfigPool(configAsset);
            if (pool == null) return 0;

            int count = 0;
            foreach (var obj in pool.AllObjects)
            {
                if (obj.isActive) count++;
            }
            return count;
        }

        /// <summary>
        /// 获取指定配置的可用对象数。
        /// </summary>
        public int GetAvailableCount(string configAsset)
        {
            var pool = FindConfigPool(configAsset);
            if (pool == null) return 0;

            int count = 0;
            foreach (var kvp in pool.AvailableObjects)
            {
                count += kvp.Value.Count;
            }
            return count;
        }

        /// <summary>
        /// 获取指定配置的池使用率 (0-1)。
        /// </summary>
        public float GetUsageRate(string configAsset)
        {
            var pool = FindConfigPool(configAsset);
            if (pool == null || pool.Config.poolcnt <= 0) return 0f;
            return (float)pool.AllObjects.Count / pool.Config.poolcnt;
        }

        /// <summary>
        /// 获取所有池配置的数量。
        /// </summary>
        public int GetPoolCount()
        {
            return _configPools?.Count ?? 0;
        }

        #endregion

        #region 公共 API — 批量操作

        /// <summary>
        /// 释放指定配置的所有活跃对象使其回到可用队列（不销毁）。
        /// </summary>
        public void ReleaseAll(string configAsset)
        {
            var pool = FindConfigPool(configAsset);
            if (pool == null) return;

            // 收集活跃对象（避免遍历时修改集合）
            var activeObjects = new List<GameObject>();
            foreach (var obj in pool.AllObjects)
            {
                if (obj.isActive && obj.gameObject != null)
                {
                    activeObjects.Add(obj.gameObject);
                }
            }

            foreach (var go in activeObjects)
            {
                Release(go);
            }
        }

        /// <summary>
        /// 释放所有池的所有活跃对象。
        /// </summary>
        public void ReleaseAll()
        {
            if (_configPools == null) return;

            foreach (var pool in _configPools)
            {
                ReleaseAll(pool.Config.asset);
            }
        }

        /// <summary>
        /// 清空所有池（销毁所有对象和卸载资源）。
        /// </summary>
        public void ClearAllPools()
        {
            if (_configPools == null) return;

            foreach (var pool in _configPools)
            {
                pool.Clear();
            }

            _gameObjectToPool.Clear();
            poolInfos.Clear();
        }

        #endregion

        #region Editor / Inspector

        /// <summary>
        /// 手动刷新Inspector信息。
        /// </summary>
        public void RefreshInspectorInfo()
        {
            UpdateInspectorInfo();
        }

        private void UpdateInspectorInfo()
        {
            if (_configPools == null) return;

            poolInfos.Clear();
            foreach (var pool in _configPools)
            {
                var info = new ConfigPoolInfo();
                info.UpdateFromPool(pool);
                poolInfos.Add(info);
            }
        }

        #endregion

        #region 内部方法

        /// <summary>
        /// 查找匹配指定资源路径的池配置。O(n)，n=池配置数量（通常很小）。
        /// </summary>
        private ConfigPool FindConfigPool(string assetPath)
        {
            foreach (var pool in _configPools)
            {
                if (pool.MatchesAsset(assetPath))
                {
                    return pool;
                }
            }

            return null;
        }

        #endregion
    }
}
