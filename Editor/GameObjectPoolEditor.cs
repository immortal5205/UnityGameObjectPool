#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool.Editor
{
    [CustomEditor(typeof(GameObjectPool))]
    public class GameObjectPoolEditor : UnityEditor.Editor
    {
        private bool[] _poolFoldouts;
        private bool[] _prefabFoldouts;
        private float _lastRefreshTime;
        private const float AUTO_REFRESH_INTERVAL = 0.1f;

        // 缓存序列化属性，避免重复查找
        private SerializedProperty _poolInfosProperty;

        private void OnEnable()
        {
            _poolInfosProperty = serializedObject.FindProperty("poolInfos");
            _lastRefreshTime = Time.time;
        }

        public override void OnInspectorGUI()
        {
            var pool = (GameObjectPool)target;

            // 更新序列化对象
            serializedObject.Update();

            // 绘制默认Inspector
            DrawDefaultInspector();
            EditorGUILayout.Space();

            // 手动刷新按钮
            if (GUILayout.Button("刷新池状态信息"))
            {
                RefreshPoolInfo(pool);
            }

            // 检查是否需要自动刷新
            bool shouldAutoRefresh = pool.showDetailedInfo &&
                                     Selection.activeGameObject == pool.gameObject &&
                                     Time.time - _lastRefreshTime > AUTO_REFRESH_INTERVAL;

            if (shouldAutoRefresh)
            {
                RefreshPoolInfo(pool);
            }

            if (!pool.showDetailedInfo)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("对象池详细信息", EditorStyles.boldLabel);

            // 重新获取属性以确保数据是最新的
            _poolInfosProperty = serializedObject.FindProperty("poolInfos");

            if (_poolInfosProperty != null && _poolInfosProperty.arraySize > 0)
            {
                DrawPoolInfos();
            }
            else
            {
                EditorGUILayout.HelpBox("暂无池信息，请等待系统初始化或点击刷新按钮", MessageType.Info);
            }

            // 显示自动刷新状态
            if (Selection.activeGameObject == pool.gameObject)
            {
                EditorGUILayout.HelpBox("Inspector正在自动刷新 (仅在选中时)", MessageType.Info);
            }

            // 应用修改的属性
            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshPoolInfo(GameObjectPool pool)
        {
            pool.RefreshInspectorInfo();
            _lastRefreshTime = Time.time;
            serializedObject.Update(); // 立即更新序列化对象

            // 标记需要重绘
            if (Selection.activeGameObject == pool.gameObject)
            {
                EditorUtility.SetDirty(pool);
                Repaint();
            }
        }

        private void DrawPoolInfos()
        {
            int poolCount = _poolInfosProperty.arraySize;

            // 确保折叠状态数组大小正确
            if (_poolFoldouts == null || _poolFoldouts.Length != poolCount)
            {
                bool[] oldPoolFoldouts = _poolFoldouts;
                bool[] oldPrefabFoldouts = _prefabFoldouts;

                _poolFoldouts = new bool[poolCount];
                _prefabFoldouts = new bool[poolCount];

                // 保持之前的折叠状态
                if (oldPoolFoldouts != null)
                {
                    for (int i = 0; i < Mathf.Min(oldPoolFoldouts.Length, poolCount); i++)
                    {
                        _poolFoldouts[i] = oldPoolFoldouts[i];
                        if (oldPrefabFoldouts != null && i < oldPrefabFoldouts.Length)
                        {
                            _prefabFoldouts[i] = oldPrefabFoldouts[i];
                        }
                    }
                }
            }

            for (int i = 0; i < poolCount; i++)
            {
                DrawPoolInfo(i);
            }
        }

        private void DrawPoolInfo(int poolIndex)
        {
            var poolInfo = _poolInfosProperty.GetArrayElementAtIndex(poolIndex);
            if (poolInfo == null) return;

            var configAssetProp = poolInfo.FindPropertyRelative("configAsset");
            var totalObjectsProp = poolInfo.FindPropertyRelative("totalObjects");
            var maxCountProp = poolInfo.FindPropertyRelative("maxCount");
            var activeObjectsProp = poolInfo.FindPropertyRelative("activeObjects");

            if (configAssetProp == null || totalObjectsProp == null || maxCountProp == null || activeObjectsProp == null)
                return;

            string configAsset = configAssetProp.stringValue;
            int totalObjects = totalObjectsProp.intValue;
            int maxCount = maxCountProp.intValue;
            int activeObjects = activeObjectsProp.intValue;

            EditorGUILayout.BeginVertical("box");

            // 使用Rect布局来精确控制Foldout的大小
            Rect rect = EditorGUILayout.GetControlRect();
            Rect foldoutRect = new Rect(rect.x, rect.y, 15, rect.height);
            Rect progressRect = new Rect(rect.x + 20, rect.y, rect.width - 120, rect.height);
            Rect labelRect = new Rect(rect.x + rect.width - 95, rect.y, 95, rect.height);

            // 绘制折叠按钮
            _poolFoldouts[poolIndex] = EditorGUI.Foldout(foldoutRect, _poolFoldouts[poolIndex], GUIContent.none);

            // 使用率进度条
            float usage = maxCount > 0 ? (float)totalObjects / maxCount : 0f;
            EditorGUI.ProgressBar(progressRect, usage, $"{configAsset} ({totalObjects}/{maxCount})");

            // 活跃对象数
            EditorGUI.LabelField(labelRect, $"活跃:{activeObjects}", EditorStyles.miniLabel);

            if (_poolFoldouts[poolIndex])
            {
                EditorGUI.indentLevel++;
                DrawPoolDetails(poolInfo, poolIndex);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        private void DrawPoolDetails(SerializedProperty poolInfo, int poolIndex)
        {
            var configAssetProp = poolInfo.FindPropertyRelative("configAsset");
            var maxCountProp = poolInfo.FindPropertyRelative("maxCount");
            var expireTimeProp = poolInfo.FindPropertyRelative("expireTime");
            var loadedPrefabsProp = poolInfo.FindPropertyRelative("loadedPrefabs");

            if (configAssetProp != null)
                EditorGUILayout.LabelField($"配置路径: {configAssetProp.stringValue}");
            if (maxCountProp != null)
                EditorGUILayout.LabelField($"最大数量: {maxCountProp.intValue}");
            if (expireTimeProp != null)
                EditorGUILayout.LabelField($"过期时间: {expireTimeProp.floatValue}s");
            if (loadedPrefabsProp != null)
                EditorGUILayout.LabelField($"已加载预制体: {loadedPrefabsProp.intValue}");

            EditorGUILayout.Space();

            // 绘制预制体引用信息
            DrawPrefabRefs(poolInfo, poolIndex);

            // 绘制对象详细信息
            DrawObjectDetails(poolInfo);
        }

        private void DrawPrefabRefs(SerializedProperty poolInfo, int poolIndex)
        {
            var prefabRefsProp = poolInfo.FindPropertyRelative("prefabRefs");
            if (prefabRefsProp == null || prefabRefsProp.arraySize <= 0) return;

            // 使用简单的Foldout，不指定宽度
            _prefabFoldouts[poolIndex] = EditorGUILayout.Foldout(_prefabFoldouts[poolIndex], "预制体引用信息:");

            if (_prefabFoldouts[poolIndex])
            {
                EditorGUI.indentLevel++;

                for (int j = 0; j < prefabRefsProp.arraySize; j++)
                {
                    DrawPrefabRefInfo(prefabRefsProp.GetArrayElementAtIndex(j));
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
        }

        private void DrawPrefabRefInfo(SerializedProperty prefabRef)
        {
            if (prefabRef == null) return;

            var assetPathProp = prefabRef.FindPropertyRelative("assetPath");
            var refCountProp = prefabRef.FindPropertyRelative("refCount");
            var lastAccessTimeProp = prefabRef.FindPropertyRelative("lastAccessTime");
            var prefabObjProp = prefabRef.FindPropertyRelative("prefab");

            EditorGUILayout.BeginHorizontal("box");

            EditorGUILayout.BeginVertical();
            if (assetPathProp != null)
                EditorGUILayout.LabelField($"{System.IO.Path.GetFileName(assetPathProp.stringValue)}", EditorStyles.boldLabel);
            if (refCountProp != null)
                EditorGUILayout.LabelField($"引用计数: {refCountProp.intValue}", EditorStyles.miniLabel);
            if (lastAccessTimeProp != null)
                EditorGUILayout.LabelField($"最后访问: {(Time.time - lastAccessTimeProp.floatValue):F1}秒前", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (prefabObjProp != null)
                EditorGUILayout.ObjectField(prefabObjProp.objectReferenceValue, typeof(GameObject), false, GUILayout.Width(100));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawObjectDetails(SerializedProperty poolInfo)
        {
            var objectsProp = poolInfo.FindPropertyRelative("objects");
            if (objectsProp == null || objectsProp.arraySize <= 0) return;

            EditorGUILayout.LabelField("对象详情:", EditorStyles.boldLabel);

            for (int j = 0; j < objectsProp.arraySize; j++)
            {
                DrawObjectInfo(objectsProp.GetArrayElementAtIndex(j));
            }
        }

        private void DrawObjectInfo(SerializedProperty obj)
        {
            if (obj == null) return;

            var objNameProp = obj.FindPropertyRelative("objectName");
            var objAssetPathProp = obj.FindPropertyRelative("assetPath");
            var isActiveProp = obj.FindPropertyRelative("isActive");
            var remainingTimeProp = obj.FindPropertyRelative("remainingTime");
            var expireProgressProp = obj.FindPropertyRelative("expireProgress");
            var gameObjectProp = obj.FindPropertyRelative("gameObject");

            EditorGUILayout.BeginHorizontal("box");

            // 状态颜色指示器
            bool isActive = isActiveProp?.boolValue ?? false;
            var statusColor = isActive ? Color.green : Color.yellow;
            var prevColor = GUI.color;
            GUI.color = statusColor;
            EditorGUILayout.LabelField("●", GUILayout.Width(15));
            GUI.color = prevColor;

            EditorGUILayout.BeginVertical();

            // 对象名称和路径
            string objName = objNameProp?.stringValue ?? "Unknown";
            string objAssetPath = objAssetPathProp?.stringValue ?? "";
            EditorGUILayout.LabelField($"{objName} ({System.IO.Path.GetFileName(objAssetPath)})", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"状态: {(isActive ? "活跃" : "空闲")}", EditorStyles.miniLabel);

            // 过期进度条
            if (!isActive && remainingTimeProp != null && expireProgressProp != null)
            {
                float remainingTime = remainingTimeProp.floatValue;
                float expireProgress = expireProgressProp.floatValue;

                if (remainingTime >= 0)
                {
                    Rect expireRect = GUILayoutUtility.GetRect(100, 16, GUILayout.ExpandWidth(true), GUILayout.Height(16));
                    EditorGUI.ProgressBar(expireRect, expireProgress, $"释放倒计时: {remainingTime:F1}s");
                }
            }

            EditorGUILayout.EndVertical();

            // GameObject引用
            if (gameObjectProp != null)
                EditorGUILayout.ObjectField(gameObjectProp.objectReferenceValue, typeof(GameObject), true, GUILayout.Width(100));

            EditorGUILayout.EndHorizontal();
        }

        public override bool RequiresConstantRepaint()
        {
            // 只有在选中对象池时才需要持续重绘
            var pool = target as GameObjectPool;
            return pool != null && pool.showDetailedInfo && Selection.activeGameObject == pool.gameObject;
        }
    }
}
#endif