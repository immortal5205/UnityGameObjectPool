#region Class Documentation

/************************************************************************************************************
Class Name:     GameObjectPool.cs
Type:           Pool, GameObject, GameObjectPool

Example:
                // 异步加载游戏物体。
                var gameObject = await GameObjectPool.Instance.GetGameObjectAsync(path, token);

                // 同步加载游戏物体。
                var gameObject = GameObjectPool.Instance.GetGameObject(path);

Example1:
                // 异步加载游戏物体。
                var gameObject = await GameObjectPoolHelper.LoadGameObjectAsync(path, token);

                // 同步加载游戏物体。
                var gameObject = GameObjectPoolHelper.LoadGameObject(path);
************************************************************************************************************/

#endregion

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    /// <summary>
    /// 预制体引用计数信息。
    /// </summary>
    internal class PrefabRefInfo
    {
        public GameObject Prefab;
        public int RefCount;
        public float LastAccessTime;
        public string AssetPath;

        public PrefabRefInfo(GameObject prefab, string assetPath)
        {
            this.Prefab = prefab;
            this.AssetPath = assetPath;
            this.RefCount = 0;
            this.LastAccessTime = Time.time;
        }

        public void AddRef()
        {
            RefCount++;
            LastAccessTime = Time.time;
        }

        public void RemoveRef()
        {
            if (RefCount > 0)
            {
                RefCount--;
                if (RefCount > 0)
                {
                    LastAccessTime = Time.time;
                }
#if UNITY_EDITOR
                Debug.Log($"RemoveRef: {AssetPath}, refCount: {RefCount}");
#endif
            }
            else
            {
                Debug.LogWarning($"尝试减少已经为0的引用计数: {AssetPath}");
            }
        }

        public bool CanUnload(float expireTime)
        {
            return RefCount <= 0 && expireTime > 0 && (Time.time - LastAccessTime) > expireTime;
        }
    }

    [Serializable]
    internal class PooledObject
    {
        public GameObject gameObject;
        public string assetPath;
        public float lastUsedTime;
        public bool isActive;
        public string instanceName;
        public bool isRefCountReduced;

        public PooledObject(GameObject go, string path)
        {
            gameObject = go;
            assetPath = path;
            lastUsedTime = Time.time;
            isActive = false;
            instanceName = go.name;
            isRefCountReduced = false;
        }

        /// <summary>
        /// 获取过期进度 (0-1)，1表示即将过期。
        /// </summary>
        public float GetExpireProgress(float expireTime)
        {
            if (expireTime <= 0 || isActive) return 0f;
            float timeElapsed = Time.time - lastUsedTime;
            return Mathf.Clamp01(timeElapsed / expireTime);
        }

        /// <summary>
        /// 获取剩余时间。
        /// </summary>
        public float GetRemainingTime(float expireTime)
        {
            if (expireTime <= 0 || isActive) return -1f;
            float timeElapsed = Time.time - lastUsedTime;
            return Mathf.Max(0f, expireTime - timeElapsed);
        }
    }

    /// <summary>
    /// Inspector显示用的对象信息。
    /// </summary>
    [Serializable]
    internal class PoolObjectInfo
    {
        [SerializeField] public string objectName;
        [SerializeField] public string assetPath;
        [SerializeField] public bool isActive;
        [SerializeField] public float lastUsedTime;
        [SerializeField] public float remainingTime;
        [SerializeField] public float expireProgress;
        [SerializeField] public GameObject gameObject;

        public void UpdateFromPooledObject(PooledObject pooledObj, float expireTime)
        {
            objectName = pooledObj.instanceName;
            assetPath = pooledObj.assetPath;
            isActive = pooledObj.isActive;
            lastUsedTime = pooledObj.lastUsedTime;
            remainingTime = pooledObj.GetRemainingTime(expireTime);
            expireProgress = pooledObj.GetExpireProgress(expireTime);
            gameObject = pooledObj.gameObject;
        }
    }

    /// <summary>
    /// Inspector显示用的预制体信息.
    /// </summary>
    [Serializable]
    internal class PrefabRefInfoDisplay
    {
        [SerializeField] public string assetPath;
        [SerializeField] public int refCount;
        [SerializeField] public float lastAccessTime;
        [SerializeField] public GameObject prefab;

        public void UpdateFromPrefabRefInfo(PrefabRefInfo info)
        {
            assetPath = info.AssetPath;
            refCount = info.RefCount;
            lastAccessTime = info.LastAccessTime;
            prefab = info.Prefab;
        }
    }

    /// <summary>
    /// Inspector显示用的池信息。
    /// </summary>
    [Serializable]
    internal class ConfigPoolInfo
    {
        [SerializeField] public string configAsset;
        [SerializeField] public int maxCount;
        [SerializeField] public float expireTime;
        [SerializeField] public int totalObjects;
        [SerializeField] public int activeObjects;
        [SerializeField] public int availableObjects;
        [SerializeField] public int loadedPrefabs;
        [SerializeField] public List<string> assetPaths = new List<string>();
        [SerializeField] public List<PoolObjectInfo> objects = new List<PoolObjectInfo>();
        [SerializeField] public List<PrefabRefInfoDisplay> prefabRefs = new List<PrefabRefInfoDisplay>();

        public void UpdateFromPool(ConfigPool pool)
        {
            configAsset = pool.Config.asset;
            maxCount = pool.Config.poolcnt;
            expireTime = pool.Config.time;
            totalObjects = pool.AllObjects.Count;

            activeObjects = 0;
            foreach (var obj in pool.AllObjects)
            {
                if (obj.isActive) activeObjects++;
            }

            // 统计所有路径队列中的可用对象总数
            availableObjects = 0;
            foreach (var kvp in pool.AvailableObjects)
            {
                availableObjects += kvp.Value.Count;
            }

            loadedPrefabs = pool.LoadedPrefabs.Count;

            assetPaths.Clear();
            assetPaths.AddRange(pool.LoadedPrefabs.Keys);

            objects.Clear();
            foreach (var pooledObj in pool.AllObjects)
            {
                if (pooledObj.gameObject != null)
                {
                    PoolObjectInfo info = new PoolObjectInfo();
                    info.UpdateFromPooledObject(pooledObj, pool.Config.time);
                    objects.Add(info);
                }
            }

            prefabRefs.Clear();
            foreach (var kvp in pool.LoadedPrefabs)
            {
                PrefabRefInfoDisplay info = new PrefabRefInfoDisplay();
                info.UpdateFromPrefabRefInfo(kvp.Value);
                prefabRefs.Add(info);
            }
        }
    }

    /// <summary>
    /// 配置组对象池 - 管理一个PoolConfig下的所有资源。
    /// </summary>
    internal class ConfigPool
    {
        public readonly PoolConfig Config;
        /// <summary>按资源路径分组的可用对象队列，O(1)获取</summary>
        public readonly Dictionary<string, Queue<PooledObject>> AvailableObjects;
        public readonly HashSet<PooledObject> AllObjects;
        /// <summary>GameObject → PooledObject 快速映射，O(1)查找</summary>
        private readonly Dictionary<GameObject, PooledObject> _objectLookup;
        public readonly Dictionary<string, PrefabRefInfo> LoadedPrefabs;
        public readonly Dictionary<string, List<UniTaskCompletionSource<GameObject>>> PendingRequests;
        public readonly HashSet<string> LoadingAssets;
        public readonly Transform PoolRoot;

        private readonly IResourceLoader _resourceLoader;

        // 实例级临时集合，避免跨池污染
        private readonly List<PooledObject> _expiredObjects = new List<PooledObject>();
        private readonly List<string> _expiredPrefabs = new List<string>();

        public ConfigPool(PoolConfig config, IResourceLoader resourceLoader)
        {
            _resourceLoader = resourceLoader;
            Config = config;
            AvailableObjects = new Dictionary<string, Queue<PooledObject>>();
            AllObjects = new HashSet<PooledObject>();
            _objectLookup = new Dictionary<GameObject, PooledObject>();
            LoadedPrefabs = new Dictionary<string, PrefabRefInfo>();
            PendingRequests = new Dictionary<string, List<UniTaskCompletionSource<GameObject>>>();
            LoadingAssets = new HashSet<string>();

            // 创建池根节点
            GameObject poolRootGo = new GameObject($"ConfigPool_{config.asset.Replace('/', '_')}");
            PoolRoot = poolRootGo.transform;
            PoolRoot.SetParent(GameObjectPool.Instance.poolContainer);
            poolRootGo.SetActive(false);
        }

        /// <summary>
        /// 判断资源路径是否匹配此池的配置。
        /// 按优先级依次尝试：精确匹配 → 前缀匹配 → 文件名匹配 → 后缀匹配。
        /// </summary>
        /// <param name="assetPath">资源路径</param>
        public bool MatchesAsset(string assetPath)
        {
            // 精确匹配
            if (assetPath == Config.asset) return true;

            // 前缀匹配（如 "Assets/Characters/" 匹配该目录下所有资源）
            if (assetPath.StartsWith(Config.asset)) return true;

            // 文件名匹配（如 "EntityComponent" 匹配任意路径下的同名.prefab）
            string assetName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            if (assetName == Config.asset) return true;

            // 带扩展名后缀匹配
            if (assetPath.EndsWith(Config.asset)) return true;

            return false;
        }

        /// <summary>
        /// 同步获取对象，如果资源未加载则同步加载。
        /// </summary>
        public GameObject Get(string assetPath)
        {
            if (!LoadedPrefabs.ContainsKey(assetPath))
            {
                if (LoadingAssets.Contains(assetPath))
                {
                    Debug.LogWarning($"资源 {assetPath} 正在异步加载中，同步获取可能导致重复加载，建议使用异步方法");
                }

                try
                {
                    GameObject prefab = _resourceLoader.LoadPrefab(assetPath);
                    if (prefab != null)
                    {
                        LoadedPrefabs[assetPath] = new PrefabRefInfo(prefab, assetPath);
#if UNITY_EDITOR
                        Debug.Log($"同步加载资源成功: {assetPath}");
#endif
                    }
                    else
                    {
                        throw new Exception($"同步加载资源失败: {assetPath}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"同步加载资源异常: {assetPath}, 错误: {e.Message}");
                    throw;
                }
            }

            return GetInternal(assetPath);
        }

        /// <summary>
        /// 异步获取对象。同一资源并发请求合并为一次加载。
        /// </summary>
        public async UniTask<GameObject> GetAsync(string assetPath, CancellationToken cancellationToken = default)
        {
            if (LoadedPrefabs.ContainsKey(assetPath))
            {
                return GetInternal(assetPath);
            }

            // 资源正在加载中，排队等待
            if (LoadingAssets.Contains(assetPath))
            {
                var completionSource = new UniTaskCompletionSource<GameObject>();
                if (!PendingRequests.ContainsKey(assetPath))
                {
                    PendingRequests[assetPath] = new List<UniTaskCompletionSource<GameObject>>();
                }

                PendingRequests[assetPath].Add(completionSource);

                try
                {
                    return await completionSource.Task.AttachExternalCancellation(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    PendingRequests[assetPath].Remove(completionSource);
                    throw;
                }
            }

            LoadingAssets.Add(assetPath);
            try
            {
                GameObject prefab = await _resourceLoader.LoadPrefabAsync(assetPath, cancellationToken);
                if (prefab != null)
                {
                    LoadedPrefabs[assetPath] = new PrefabRefInfo(prefab, assetPath);
#if UNITY_EDITOR
                    Debug.Log($"异步加载资源成功: {assetPath}");
#endif

                    // 通知所有等待者
                    if (PendingRequests.ContainsKey(assetPath))
                    {
                        var requests = PendingRequests[assetPath];
                        PendingRequests.Remove(assetPath);

                        foreach (var request in requests)
                        {
                            try
                            {
                                var go = GetInternal(assetPath);
                                request.TrySetResult(go);
                            }
                            catch (Exception e)
                            {
                                request.TrySetException(e);
                            }
                        }
                    }

                    return GetInternal(assetPath);
                }
                else
                {
                    throw new Exception($"无法异步加载资源: {assetPath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"异步加载资源失败: {assetPath}, 错误: {e.Message}");

                if (PendingRequests.ContainsKey(assetPath))
                {
                    var requests = PendingRequests[assetPath];
                    PendingRequests.Remove(assetPath);

                    foreach (var request in requests)
                    {
                        request.TrySetException(e);
                    }
                }

                throw;
            }
            finally
            {
                LoadingAssets.Remove(assetPath);
            }
        }

        /// <summary>
        /// 从池中获取对象（内部实现）。
        /// O(1)：通过按路径分组的字典直接定位到该资源的可用队列。
        /// </summary>
        private GameObject GetInternal(string assetPath)
        {
            // O(1): 按资源路径定位到专属队列
            if (AvailableObjects.TryGetValue(assetPath, out var queue) && queue.Count > 0)
            {
                // 惰性清理：跳过已被外部销毁或已标记减少引用的对象
                while (queue.Count > 0)
                {
                    var pooledObj = queue.Dequeue();

                    if (pooledObj.gameObject == null || pooledObj.isRefCountReduced)
                    {
                        if (pooledObj.gameObject == null && !pooledObj.isRefCountReduced)
                        {
                            OnObjectReallyDestroyed(pooledObj);
                        }
                        else
                        {
                            AllObjects.Remove(pooledObj);
                            if (pooledObj.gameObject != null)
                                _objectLookup.Remove(pooledObj.gameObject);
                        }
                        continue;
                    }

                    ActivateObject(pooledObj);
                    return pooledObj.gameObject;
                }
            }

            // 池中无可用对象，尝试创建新的
            if (AllObjects.Count < Config.poolcnt)
            {
                return CreateNewObject(assetPath);
            }

            // 池已满，驱逐最旧的非活跃对象
            return EvictAndCreate(assetPath);
        }

        /// <summary>
        /// 创建新对象并加入池管理。
        /// </summary>
        private GameObject CreateNewObject(string assetPath)
        {
            var prefabRefInfo = LoadedPrefabs[assetPath];
            GameObject instantiate = GameObject.Instantiate(prefabRefInfo.Prefab);
            var pooledObj = new PooledObject(instantiate, assetPath);
            AllObjects.Add(pooledObj);
            _objectLookup[instantiate] = pooledObj;
            prefabRefInfo.AddRef();

            // 挂载生命周期监听组件（如果预制体已预挂则直接复用）
            var monitor = instantiate.GetComponent<PoolObjectMonitor>();
            if (monitor == null)
                monitor = instantiate.AddComponent<PoolObjectMonitor>();
            monitor.Initialize(this, pooledObj);

            ActivateObject(pooledObj);
            return instantiate;
        }

        /// <summary>
        /// 池已满时驱逐最旧的非活跃对象，为新对象腾出空间。
        /// </summary>
        private GameObject EvictAndCreate(string assetPath)
        {
            PooledObject oldestObj = null;
            float oldestTime = float.MaxValue;

            foreach (var obj in AllObjects)
            {
                if (!obj.isActive && !obj.isRefCountReduced && obj.lastUsedTime < oldestTime)
                {
                    oldestTime = obj.lastUsedTime;
                    oldestObj = obj;
                }
            }

            if (oldestObj != null)
            {
                DestroyPooledObject(oldestObj);
                return CreateNewObject(assetPath);
            }

            throw new InvalidOperationException(
                $"对象池已满 (max={Config.poolcnt}) 且所有对象都在使用中，无法创建 '{assetPath}'");
        }

        /// <summary>
        /// 激活对象（设置状态、父节点、回调）。
        /// </summary>
        private void ActivateObject(PooledObject pooledObj)
        {
            pooledObj.isActive = true;
            pooledObj.lastUsedTime = Time.time;
            pooledObj.gameObject.SetActive(true);
            pooledObj.gameObject.transform.SetParent(null);

            // 触发对象获取回调（新创建和从队列取出都会走到这里）
            var callbacks = pooledObj.gameObject.GetComponents<IPoolCallback>();
            foreach (var cb in callbacks)
            {
                try { cb.OnGet(); }
                catch (Exception e) { Debug.LogError($"IPoolCallback.OnGet 异常: {e.Message}"); }
            }
        }

        /// <summary>
        /// 归还对象到池中（外部调用）。
        /// O(1)：通过 _objectLookup 字典直接定位。
        /// </summary>
        public void Return(GameObject go)
        {
            if (!_objectLookup.TryGetValue(go, out var pooledObj))
                return;

            if (!pooledObj.isActive)
                return;

            pooledObj.isActive = false;
            pooledObj.lastUsedTime = Time.time;

            // 触发对象归还回调
            var callbacks = go.GetComponents<IPoolCallback>();
            foreach (var cb in callbacks)
            {
                try { cb.OnReturn(); }
                catch (Exception e) { Debug.LogError($"IPoolCallback.OnReturn 异常: {e.Message}"); }
            }

            go.SetActive(false);
            go.transform.SetParent(PoolRoot);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // 按资源路径放入对应队列
            if (!AvailableObjects.TryGetValue(pooledObj.assetPath, out var queue))
            {
                queue = new Queue<PooledObject>();
                AvailableObjects[pooledObj.assetPath] = queue;
            }
            queue.Enqueue(pooledObj);
        }

        /// <summary>
        /// 对象被外部销毁时的回调（由 PoolObjectMonitor.OnDestroy 触发）。
        /// </summary>
        public void OnObjectDestroyed(PooledObject pooledObj)
        {
            if (!pooledObj.isRefCountReduced)
            {
                OnObjectReallyDestroyed(pooledObj);
            }
            else
            {
                AllObjects.Remove(pooledObj);
                if (pooledObj.gameObject != null)
                    _objectLookup.Remove(pooledObj.gameObject);
            }
        }

        /// <summary>
        /// 实际处理对象销毁：减少引用计数、清理集合。
        /// </summary>
        private void OnObjectReallyDestroyed(PooledObject pooledObj)
        {
            if (pooledObj.isRefCountReduced)
                return;

            pooledObj.isRefCountReduced = true;
            AllObjects.Remove(pooledObj);

            if (pooledObj.gameObject != null)
                _objectLookup.Remove(pooledObj.gameObject);

            // 减少预制体引用计数
            if (LoadedPrefabs.TryGetValue(pooledObj.assetPath, out PrefabRefInfo refInfo))
            {
                refInfo.RemoveRef();
            }
        }

        /// <summary>
        /// 主动销毁池对象（池满驱逐或过期清理时调用）。
        /// </summary>
        private void DestroyPooledObject(PooledObject pooledObj)
        {
            if (pooledObj.isRefCountReduced)
                return;

            // 先减少引用计数和清理映射
            OnObjectReallyDestroyed(pooledObj);

            if (pooledObj.gameObject != null)
            {
                GameObject.Destroy(pooledObj.gameObject);
            }
        }

        /// <summary>
        /// 检查并清理过期对象。
        /// 不再需要重建可用队列（队列按路径分组，惰性清理已处理损坏条目）。
        /// </summary>
        public void CheckExpiredObjects()
        {
            if (Config.time <= 0) return;

            float currentTime = Time.time;

            // 收集过期对象
            _expiredObjects.Clear();
            foreach (var obj in AllObjects)
            {
                if (!obj.isActive && !obj.isRefCountReduced && (currentTime - obj.lastUsedTime) > Config.time)
                {
                    _expiredObjects.Add(obj);
                }
            }

            // 销毁过期对象
            foreach (var expiredObj in _expiredObjects)
            {
                DestroyPooledObject(expiredObj);
            }

            CheckExpiredPrefabs();
        }

        /// <summary>
        /// 检查并卸载过期的预制体引用。
        /// </summary>
        private void CheckExpiredPrefabs()
        {
            if (Config.time <= 0) return;

            _expiredPrefabs.Clear();
            foreach (var kvp in LoadedPrefabs)
            {
                var refInfo = kvp.Value;
                if (refInfo.CanUnload(Config.time))
                {
                    _expiredPrefabs.Add(kvp.Key);
                }
            }

            foreach (var assetPath in _expiredPrefabs)
            {
                var refInfo = LoadedPrefabs[assetPath];
#if UNITY_EDITOR
                Debug.Log($"卸载过期预制体: {assetPath}, 引用计数: {refInfo.RefCount}");
#endif
                _resourceLoader.UnloadAsset(refInfo.Prefab);
                LoadedPrefabs.Remove(assetPath);
            }
        }

        /// <summary>
        /// 清空此池的所有对象和资源。
        /// </summary>
        public void Clear()
        {
            // 销毁所有托管对象
            foreach (var obj in AllObjects)
            {
                if (obj.gameObject != null)
                {
                    GameObject.Destroy(obj.gameObject);
                }
            }

            AllObjects.Clear();
            AvailableObjects.Clear();
            _objectLookup.Clear();

            // 卸载所有预制体
            foreach (var kvp in LoadedPrefabs)
            {
                var refInfo = kvp.Value;
                if (refInfo.Prefab != null)
                {
#if UNITY_EDITOR
                    Debug.Log($"清理时卸载预制体: {kvp.Key}, 引用计数: {refInfo.RefCount}");
#endif
                    _resourceLoader.UnloadAsset(refInfo.Prefab);
                }
            }

            LoadedPrefabs.Clear();
            LoadingAssets.Clear();

            // 取消所有等待请求
            foreach (var requests in PendingRequests.Values)
            {
                foreach (var request in requests)
                {
                    request.TrySetCanceled();
                }
            }
            PendingRequests.Clear();

            if (PoolRoot != null)
            {
                GameObject.Destroy(PoolRoot.gameObject);
            }
        }
    }
}
