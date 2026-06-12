using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    /// <summary>
    /// 对象销毁监听器。
    /// 推荐将本组件预挂在预制体上以避免运行时的 GetComponent + AddComponent 开销。
    /// 如未预挂，首次从池中取出时会自动添加。
    /// </summary>
    internal class PoolObjectMonitor : MonoBehaviour
    {
        private ConfigPool _pool;
        private PooledObject _pooledObject;

        public void Initialize(ConfigPool pool, PooledObject pooledObject)
        {
            _pool = pool;
            _pooledObject = pooledObject;
        }

        private void OnDestroy()
        {
            if (_pool != null && _pooledObject != null)
            {
                _pool.OnObjectDestroyed(_pooledObject);
            }
        }
    }
}