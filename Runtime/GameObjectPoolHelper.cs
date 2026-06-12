using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool
{
    public static class GameObjectPoolHelper
    {
        public static GameObject LoadGameObject(string assetPath)
        {
            return GameObjectPool.Instance.GetGameObject(assetPath);
        }

        public static async UniTask<GameObject> LoadGameObjectAsync(string assetPath, CancellationToken cancellationToken = default)
        {
            return await GameObjectPool.Instance.GetGameObjectAsync(assetPath, cancellationToken);
        }

        public static void Release(GameObject go)
        {
            GameObjectPool.Instance.Release(go);
        }
    }
}