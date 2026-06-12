using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    public interface IResourceLoader
    {
        GameObject LoadPrefab(string location);
        UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default);
        GameObject LoadGameObject(string location, Transform parent = null);
        UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default);
        void UnloadAsset(GameObject gameObject);
    }

    internal class DefaultResourceLoader : IResourceLoader
    {
        public GameObject LoadPrefab(string location)
        {
            return Resources.Load<GameObject>(location);
        }

        public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
        {
            return await Resources.LoadAsync<GameObject>(location).ToUniTask(cancellationToken: cancellationToken) as GameObject;
        }

        public GameObject LoadGameObject(string location, Transform parent = null)
        {
            var prefab = Resources.Load<GameObject>(location);
            if (prefab == null) return null;

            var instance = GameObject.Instantiate(prefab);
            if (instance != null && parent != null)
            {
                instance.transform.SetParent(parent);
            }

            return instance;
        }

        public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
        {
            var prefab = await Resources.LoadAsync<GameObject>(location).ToUniTask(cancellationToken: cancellationToken) as GameObject;
            if (prefab == null) return null;

            var instance = GameObject.Instantiate(prefab);
            if (instance != null && parent != null)
            {
                instance.transform.SetParent(parent);
            }

            return instance;
        }

        public void UnloadAsset(GameObject gameObject)
        {
            Resources.UnloadAsset(gameObject);
        }
    }
}