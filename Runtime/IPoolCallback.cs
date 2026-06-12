namespace NuoYan.Pool.GameObjectPool
{
    /// <summary>
    /// 池对象生命周期回调接口。
    /// 将此接口挂载到预制体或子组件上，对象从池中取出/归还时会自动调用对应方法。
    ///
    /// 典型用途：
    /// - OnGet: 初始化状态（Rigidbody.velocity = 0, 粒子 Play 等）
    /// - OnReturn: 清理状态（停止协程、重置动画、粒子 Stop 等）
    ///
    /// 注意：回调中抛出的异常会被捕获并打印错误日志，不会中断池操作。
    /// </summary>
    public interface IPoolCallback
    {
        /// <summary>
        /// 对象从池中取出时调用（SetActive(true) 之后）。
        /// </summary>
        void OnGet();

        /// <summary>
        /// 对象归还到池中时调用（SetActive(false) 之前）。
        /// </summary>
        void OnReturn();
    }
}
