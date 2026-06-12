# GameObjectPool — Unity 游戏对象池插件

## 概述

高性能、可扩展的 Unity 游戏对象池系统。支持同步/异步加载、自动回收、过期清理、运行时统计、Editor 可视化调试，适配 YooAsset / Addressables / Resources 等任意资源管线。

### 安装

**通过 Unity Package Manager (UPM) 从 GitHub 导入：**

```
Window → Package Manager → + → Add package from git URL...
```

输入仓库地址（带上子路径）：

```
https://github.com/immortal5205/UnityGameObjectPool.git?path=/Assets/Plugins/NuoYan/GameObjectPool
```

例如项目在 TEngine 仓库中：

```
https://github.com/Alex-Rachel/TEngine.git?path=/Assets/Plugins/NuoYan/GameObjectPool
```

> 依赖：[UniTask](https://github.com/Cysharp/UniTask)（`com.cysharp.unitask`）会在导入时自动安装。

### 核心特性

- 🚀 **高性能** — Get / Return 均为 **O(1)** 操作
- 🔌 **资源管线无关** — 通过 `IResourceLoader` 注入，适配任意资源系统
- ⚡ **异步优先** — 基于 UniTask，原生支持 CancellationToken
- 📊 **运行时统计** — 实时查询池容量、活跃数、使用率
- 🛠 **Editor 工具链** — 配置自动生成、Inspector 可视化调试
- 🔄 **生命周期回调** — `IPoolCallback` 接口，支持 OnGet / OnReturn
- 🧹 **自动过期清理** — 闲置对象自动释放，逐帧分摊避免尖峰
- 🔗 **编译安全引用** — 自动生成常量类，告别散落字符串

---

## 目录结构

```
GameObjectPool/
├── package.json                       # UPM 包清单（支持 GitHub URL 导入）
├── Runtime/                          # 运行时代码
│   ├── NuoYan.GameObjectPool.Runtime.asmdef
│   ├── GameObjectPool.cs             # 池管理器入口（MonoBehaviour 单例）
│   ├── GameObjectPoolHelper.cs       # 静态快捷方法
│   ├── GameplaySystem.cs             # ConfigPool 内部实现 + 数据结构
│   ├── IResourceLoader.cs            # 资源加载器接口 + 默认实现（Resources）
│   ├── IPoolCallback.cs              # 对象生命周期回调接口
│   ├── PoolConfigScriptableObject.cs # 池配置 ScriptableObject 定义
│   ├── PoolObjectMonitor.cs          # 对象销毁自动回收入口
│   └── PoolItemConstants.cs          # [自动生成] 编译安全常量引用
├── Editor/                           # Editor 工具（UNITY_EDITOR 包裹）
│   ├── NuoYan.GameObjectPool.Editor.asmdef
│   ├── GameObjectPoolEditor.cs       # GameObjectPool Inspector 可视化
│   └── PoolConfigScriptableObjectInspector.cs  # 配置面板 + 常量类生成
├── Resources/
│   └── PoolConfig.asset              # 池配置文件
└── README.md
```

---

## 快速开始

### 1. 创建配置文件

右键 `Project → Create → NuoYan → PoolConfig`，创建 `PoolConfig.asset`，放入 `Resources/` 目录。

### 2. 设置资源文件夹

在 PoolConfig Inspector 中：

1. 添加 `资源文件夹列表`，指定存放预制体的文件夹路径
2. 设置默认的池大小和过期时间
3. **useFullPath**：勾选后生成的常量值为完整路径，否则为文件名
4. 点击 **"生成对象池配置及常量类"**

### 3. 初始化池管理器

```csharp
using NuoYan.Pool;

// 使用默认资源加载器（Resources）
GameObjectPool.Instance.Initialize(new DefaultResourceLoader());

// 使用自定义加载器（如 YooAsset / Addressables）
GameObjectPool.Instance.Initialize(new MyResourceLoader());
```

> **注意**：`Initialize` 必须在调用任何 `GetGameObject` 之前调用。数据结构在 `Awake` 中已惰性初始化，但资源加载器和配置需要显式注入。

### 4. 获取和归还对象

```csharp
// 异步获取
var go = await GameObjectPool.Instance.GetGameObjectAsync("Player");
// 或使用常量（编译安全）
var go = await GameObjectPool.Instance.GetGameObjectAsync(PoolItemConstants.Player);
// 支持取消令牌
var go = await GameObjectPool.Instance.GetGameObjectAsync("Player", cancellationToken);

// 同步获取
var go = GameObjectPool.Instance.GetGameObject(PoolItemConstants.Player);

// 使用快捷方法
var go = await GameObjectPoolHelper.LoadGameObjectAsync(PoolItemConstants.Player);

// 归还
GameObjectPool.Instance.Release(go);
// 或
GameObjectPoolHelper.Release(go);
```

### 5. 预加载

```csharp
// 同步预加载 5 个对象到池中
GameObjectPool.Instance.Preload(PoolItemConstants.Player, 5);

// 异步预加载
await GameObjectPool.Instance.PreloadAsync(PoolItemConstants.Player, 5, cancellationToken);
```

---

## 架构概览

```
┌─────────────────────────────────────────────────┐
│               GameObjectPool                     │
│   (SingletonMono, 对外统一入口)                    │
│                                                   │
│  ┌─────────────────────────────────────────┐     │
│  │  ConfigPool (asset="Player")            │     │
│  │  ┌─────────────────────────────────┐   │     │
│  │  │ AvailableObjects                │   │     │
│  │  │  "Player" → Queue<PooledObject> │   │     │
│  │  │  "Enemy"  → Queue<PooledObject> │   │     │
│  │  └─────────────────────────────────┘   │     │
│  │  ┌─────────────────────────────────┐   │     │
│  │  │ LoadedPrefabs                   │   │     │
│  │  │  "Player" → PrefabRefInfo(ref)  │   │     │
│  │  └─────────────────────────────────┘   │     │
│  │  ┌─────────────────────────────────┐   │     │
│  │  │ _objectLookup                   │   │     │
│  │  │  GameObject → PooledObject      │   │     │
│  │  └─────────────────────────────────┘   │     │
│  └─────────────────────────────────────────┘     │
│                                                   │
│  ┌─────────────────────────────────────────┐     │
│  │  ConfigPool (asset="Enemy")             │     │
│  │  ...                                    │     │
│  └─────────────────────────────────────────┘     │
│                                                   │
│  _gameObjectToPool: GameObject → ConfigPool       │
└─────────────────────────────────────────────────┘
```

### 核心数据结构

| 结构 | 作用 | 复杂度 |
|------|------|--------|
| `AvailableObjects` | `Dictionary<string, Queue<PooledObject>>` — 按资源路径分组的空闲对象 | Get: O(1) |
| `_objectLookup` | `Dictionary<GameObject, PooledObject>` — 对象查找映射 | Return: O(1) |
| `AllObjects` | `HashSet<PooledObject>` — 池中所有对象集合 | 遍历统计: O(n) |
| `_gameObjectToPool` | `Dictionary<GameObject, ConfigPool>` — 全局归属映射 | Release: O(1) |
| `LoadedPrefabs` | `Dictionary<string, PrefabRefInfo>` — 预制体缓存+引用计数 | 复用加载: O(1) |

---

## API 参考

### 初始化

| 方法 | 说明 |
|------|------|
| `Initialize(IResourceLoader loader)` | 设置自定义资源加载器，加载池配置 |
| `ReloadConfig()` | 运行时热重载配置（仅新增，不影响已有池） |

### 获取对象

| 方法 | 说明 |
|------|------|
| `GetGameObject(assetPath)` | 同步获取，池满则驱逐最旧对象 |
| `GetGameObjectAsync(assetPath, cancellationToken)` | 异步获取，支持取消 |
| `Preload(assetPath, count)` | 同步预加载指定数量 |
| `PreloadAsync(assetPath, count, cancellationToken)` | 异步预加载，支持取消 |

所有获取方法在找不到配置或池满且全活跃时抛出 `InvalidOperationException`。

### 归还对象

| 方法 | 说明 |
|------|------|
| `Release(go)` | 归还到池中。对象不属于任何池时仅警告，不销毁 |

### 批量操作

| 方法 | 说明 |
|------|------|
| `ReleaseAll(configAsset)` | 释放指定配置的所有活跃对象 |
| `ReleaseAll()` | 释放所有池的所有活跃对象 |
| `ClearAllPools()` | 清空所有池（销毁对象并卸载预制体） |
| `ForceCleanup()` | 立即执行一次全量过期清理 |

### 运行时统计

| 方法 | 说明 |
|------|------|
| `GetTotalCount(configAsset)` | 指定池的总对象数 |
| `GetActiveCount(configAsset)` | 指定池的活跃对象数 |
| `GetAvailableCount(configAsset)` | 指定池的可用对象数 |
| `GetUsageRate(configAsset)` | 指定池的使用率 (0-1) |
| `GetPoolCount()` | 池配置总数 |

### 快捷静态方法（GameObjectPoolHelper）

```csharp
GameObjectPoolHelper.LoadGameObject(assetPath);
GameObjectPoolHelper.LoadGameObjectAsync(assetPath, cancellationToken);
GameObjectPoolHelper.Release(go);
```

---

## 自定义资源加载器

默认使用 `Resources.Load`，接入 YooAsset：

```csharp
public class YooAssetResourceLoader : IResourceLoader
{
    public GameObject LoadPrefab(string location)
    {
        return YooAssets.LoadAssetSync<GameObject>(location).AssetObject as GameObject;
    }

    public async UniTask<GameObject> LoadPrefabAsync(string location, CancellationToken cancellationToken = default)
    {
        var handle = YooAssets.LoadAssetAsync<GameObject>(location);
        await handle.ToUniTask(cancellationToken: cancellationToken);
        return handle.AssetObject as GameObject;
    }

    public GameObject LoadGameObject(string location, Transform parent = null)
    {
        var prefab = LoadPrefab(location);
        return prefab ? Object.Instantiate(prefab, parent) : null;
    }

    public async UniTask<GameObject> LoadGameObjectAsync(string location, Transform parent = null, CancellationToken cancellationToken = default)
    {
        var prefab = await LoadPrefabAsync(location, cancellationToken);
        return prefab ? Object.Instantiate(prefab, parent) : null;
    }

    public void UnloadAsset(GameObject gameObject)
    {
        Resources.UnloadAsset(gameObject); // YooAsset 通过引用计数管理
    }
}

// 使用
GameObjectPool.Instance.Initialize(new YooAssetResourceLoader());
```

---

## 池对象生命周期回调

将 `IPoolCallback` 挂载到预制体的根节点或子组件上：

```csharp
public class EnemyController : MonoBehaviour, IPoolCallback
{
    private Rigidbody _rb;
    private Animator _anim;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _anim = GetComponent<Animator>();
    }

    public void OnGet()
    {
        // 从池中取出时：重置状态
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _anim.Play("Idle");
        gameObject.SetActive(true);
    }

    public void OnReturn()
    {
        // 归还到池时：清理状态（已自动 SetActive(false)）
        _rb.velocity = Vector3.zero;
        _anim.StopPlayback();
    }
}
```

回调在以下时机自动调用：

| 时机 | 调用方法 | 说明 |
|------|---------|------|
| 从池取出（新创建或复用） | `OnGet()` | `SetActive(true)` 之后 |
| 归还到池 | `OnReturn()` | `SetActive(false)` 之前 |

> 回调中抛出的异常会被捕获并打印错误日志，不会中断池操作。

---

## Editor 工具

### PoolConfig 面板

- **资源文件夹列表**：添加预制体文件夹路径，设置池大小、过期时间、useFullPath
- **常量类路径**：指定生成的 `PoolItemConstants.cs` 输出位置
- **useFullPath**：勾选后常量存完整路径（如 `"Assets/Prefabs/Player.prefab"`），否则仅文件名（如 `"Player"`）
- **"生成对象池配置及常量类"**：扫描文件夹，填充配置列表，自动生成常量类

### GameObjectPool Inspector

运行时选中池管理器对象，Inspector 实时显示：

- 每个池的**使用率进度条**（已用/最大）
- **活跃/空闲对象数量**
- 每个对象的**倒计时和过期进度**
- 预制体**引用计数**和上次访问时间

> 选中时自动 0.1 秒刷新，取消选中停止刷新，不影响性能。

---

## 性能特征

### 时间复杂度

| 操作 | 复杂度 | 说明 |
|------|--------|------|
| Get（有可用） | **O(1)** | Dictionary 定位队列 |
| Get（需创建） | **O(1)** | Instantiate + 注册 |
| Get（池满驱逐） | **O(n)** | 遍历 AllObjects 找最旧（低频兜底） |
| Return | **O(1)** | _objectLookup 直接定位 |
| 定期清理（单帧） | **O(N/k)** | 逐帧分摊，k=池数量 |
| 统计查询 | **O(n)** | 纯调试用途 |

### 内存开销

- 每个 PooledObject 额外 ~8 bytes（`_objectLookup` 字典条目）
- 无 static 共享集合，实例级别安全
- 过期对象在下一帧清理，不会滞留

### 最佳实践

| 场景 | 建议 |
|------|------|
| 高频获取/归还 | 确保池容量充足，避免驱逐 |
| 大量预制体 | 按功能分组到不同池配置 |
| 粒子/特效 | 使用完后立即 `Release` |
| 场景切换 | 调用 `ReleaseAll()` 或 `ClearAllPools()` |
| 预加载 | 在 loading 界面调用 `PreloadAsync` |
| ONGUI/Update 中 | 用异步 `GetGameObjectAsync`，避免同步加载 |

---

## 常见问题

**Q: 为什么 Release 时对象不属于任何池？**
A: 如果对象不是通过池的 `GetGameObject`/`GetGameObjectAsync` 获取的，池不会有其记录。这时 `Release` 仅输出警告，不会销毁。通过池获取的对象会自动注册。

**Q: 池满且所有对象都在使用时会发生什么？**
A: 抛出 `InvalidOperationException`。建议根据峰值调整 `poolcnt`，或使用 `Preload` 预热。

**Q: 如何让每个预制体在进出池时执行自定义逻辑？**
A: 在预制体上挂载 `IPoolCallback` 组件，实现 `OnGet` / `OnReturn`。

**Q: 常量类和配置不同步怎么办？**
A: 在 PoolConfig Inspector 中点击 **"生成对象池配置及常量类"** 重新生成。

---

## 依赖

- [UniTask](https://github.com/Cysharp/UniTask) — 异步操作基础

---

## 许可

MIT License
