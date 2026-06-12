#if UNITY_EDITOR

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NuoYan.Pool.GameObjectPool.Editor
{
    [CustomEditor(typeof(PoolConfigScriptableObject))]
    internal class PoolConfigScriptableObjectInspector : UnityEditor.Editor
    {
        private SerializedProperty _folderEntriesProp;
        private SerializedProperty _configsProp;
        private SerializedProperty _constantClassPathProp;
        private readonly List<string> _folderValidationCache = new List<string>();

        private void OnEnable()
        {
            _folderEntriesProp = serializedObject.FindProperty("folderEntries");
            _configsProp = serializedObject.FindProperty("configs");
            _constantClassPathProp = serializedObject.FindProperty("constantClassPath");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawFolderEntries();
            EditorGUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(_folderEntriesProp == null || _folderEntriesProp.arraySize == 0))
            {
                if (GUILayout.Button("生成对象池配置及常量类", GUILayout.Height(28f)))
                {
                    GeneratePoolConfigs();
                }
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.PropertyField(_configsProp, includeChildren: true);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawFolderEntries()
        {
            if (_folderEntriesProp == null)
            {
                EditorGUILayout.HelpBox("未找到 folderEntries 字段。", MessageType.Error);
                return;
            }

            EditorGUILayout.PropertyField(_folderEntriesProp, new GUIContent("资源文件夹列表"), includeChildren: true);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_constantClassPathProp, new GUIContent("常量类路径"));
            if (GUILayout.Button("选择路径", GUILayout.Width(80f)))
            {
                string path = EditorUtility.OpenFolderPanel("选择常量类输出路径", Application.dataPath, string.Empty);
                if (!string.IsNullOrEmpty(path))
                {
                    path = path.Replace(Application.dataPath, "Assets");
                    _constantClassPathProp.stringValue = path;
                    serializedObject.ApplyModifiedProperties();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (_folderEntriesProp.arraySize > 0)
            {
                EditorGUILayout.HelpBox(
                    "每个文件夹可设置 useFullPath：\n" +
                    "✓ 勾选 → 常量值为完整资源路径（如 \"Assets/Prefabs/Player.prefab\"）\n" +
                    "✗ 不勾选 → 常量值为文件名（如 \"Player\"）",
                    MessageType.Info);
            }
        }

        private void GeneratePoolConfigs()
        {
            serializedObject.ApplyModifiedProperties();

            var targetObject = (PoolConfigScriptableObject)target;
            Undo.RecordObject(targetObject, "Generate Pool Configs");

            if (_configsProp == null)
            {
                EditorUtility.DisplayDialog("错误", "未找到 configs 字段。", "确定");
                return;
            }

            targetObject.configs ??= new List<PoolConfig>();
            targetObject.configs.Clear();

            // 临时存储：常量名 → 常量值
            var constants = new List<(string name, string value)>();
            bool hasDuplicates = false;
            var seenConstantNames = new HashSet<string>();

            _folderValidationCache.Clear();

            for (int i = 0; i < _folderEntriesProp.arraySize; i++)
            {
                var entryProp = _folderEntriesProp.GetArrayElementAtIndex(i);
                var folderPathProp = entryProp.FindPropertyRelative("folderPath");
                var timeProp = entryProp.FindPropertyRelative("time");
                var poolProp = entryProp.FindPropertyRelative("poolcnt");
                var useFullPathProp = entryProp.FindPropertyRelative("useFullPath");

                string folderPath = folderPathProp?.stringValue?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(folderPath))
                {
                    continue;
                }

                bool useFullPath = useFullPathProp?.boolValue ?? false;

                if (!folderPath.StartsWith("Assets"))
                {
                    folderPath = Path.Combine("Assets", folderPath);
                    folderPath = folderPath.Replace("\\", "/");
                }

                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    _folderValidationCache.Add(folderPath);
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(assetPath))
                    {
                        continue;
                    }

                    string assetName = Path.GetFileNameWithoutExtension(assetPath);
                    if (string.IsNullOrEmpty(assetName))
                    {
                        continue;
                    }

                    string assetValue = useFullPath ? assetPath : assetName;

                    targetObject.configs.Add(new PoolConfig
                    {
                        asset = assetValue,
                        time = timeProp?.floatValue ?? 0f,
                        poolcnt = poolProp?.intValue ?? 0
                    });

                    // 收集常量（同名去重，保留第一个）
                    if (!seenConstantNames.Contains(assetName))
                    {
                        seenConstantNames.Add(assetName);
                        constants.Add((assetName, assetValue));
                    }
                    else
                    {
                        hasDuplicates = true;
                    }
                }
            }

            // 生成常量类
            string constantPath = _constantClassPathProp?.stringValue;
            if (!string.IsNullOrEmpty(constantPath))
            {
                GenerateConstantsClass(constantPath, constants);
            }

            if (hasDuplicates)
            {
                Debug.LogWarning("存在同名的资源文件，常量名保留了第一个出现的配置，后续同名资源被跳过。");
            }

            if (_folderValidationCache.Count > 0)
            {
                EditorUtility.DisplayDialog("警告",
                    $"以下路径不是有效的资源文件夹，将被忽略：\n{string.Join("\n", _folderValidationCache)}", "确定");
            }

            EditorUtility.SetDirty(targetObject);
            serializedObject.Update();
        }

        /// <summary>
        /// 生成静态常量类，提供编译期安全的资源路径引用。
        /// </summary>
        private static void GenerateConstantsClass(string classFilePath, List<(string name, string value)> constants)
        {
            string className = Path.GetFileNameWithoutExtension(classFilePath);
            if (string.IsNullOrEmpty(className))
                className = "PoolItemConstants";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// Auto-generated by PoolConfigScriptableObjectInspector — do not edit manually");
            sb.AppendLine("// Re-generate from PoolConfig.asset Inspector");
            sb.AppendLine();
            sb.AppendLine("namespace NuoYan.Pool.GameObjectPool");
            sb.AppendLine("{");
            sb.AppendLine("    public static class " + className);
            sb.AppendLine("    {");

            foreach (var (name, value) in constants)
            {
                // 确保常量名是合法 C# 标识符
                string safeName = SanitizeIdentifier(name);
                sb.AppendLine($"        public const string {safeName} = \"{EscapeStringLiteral(value)}\";");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText($"{classFilePath}.cs", sb.ToString());
            AssetDatabase.Refresh();

            Debug.Log($"已生成常量类: {classFilePath}.cs，共 {constants.Count} 个常量");
        }

        /// <summary>
        /// 将字符串转为 C# 合法标识符（替换非法字符为下划线）。
        /// </summary>
        private static string SanitizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unknown";

            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_')
                {
                    chars[i] = '_';
                }
            }

            string result = new string(chars);
            // 不能以数字开头
            if (result.Length > 0 && char.IsDigit(result[0]))
                result = "_" + result;

            return result;
        }

        /// <summary>
        /// 转义字符串字面量中的特殊字符。
        /// </summary>
        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}

#endif
