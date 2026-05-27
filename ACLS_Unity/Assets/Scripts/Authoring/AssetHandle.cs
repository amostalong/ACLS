using System;
using UnityEngine;
using YooAsset;

namespace ACLS.Authoring
{
    /// <summary>
    /// 轻量资源句柄封装（适配 YooAsset 3.x）。
    /// 自动管理引用计数，Dispose 时释放资源。
    ///
    /// 用法示例:
    ///   var bg = await ResHandle<Texture2D>.LoadAsync("bg_taoyuan");
    ///   // ... 使用 bg.Asset ...
    ///   bg.Dispose();
    /// </summary>
    public sealed class ResHandle<T> : IDisposable where T : UnityEngine.Object
    {
        private AssetHandle _handle;
        private bool _disposed;

        private ResHandle(AssetHandle handle)
        {
            _handle = handle;
        }

        /// <summary>加载的资源对象</summary>
        public T Asset => _handle != null && _handle.IsValid ? _handle.GetAssetObject<T>() : null;

        /// <summary>加载状态</summary>
        public bool IsValid => _handle != null && _handle.IsValid;

        /// <summary>异步加载资源（默认包）</summary>
        public static async Cysharp.Threading.Tasks.UniTask<ResHandle<T>> LoadAsync(string address)
        {
            var handle = YooAssetBootstrapper.DefaultPackage.LoadAssetAsync<T>(address);
            await handle;
            return new ResHandle<T>(handle);
        }

        /// <summary>异步加载资源（指定包）</summary>
        public static async Cysharp.Threading.Tasks.UniTask<ResHandle<T>> LoadAsync(string packageName, string address)
        {
            var pkg = YooAssets.GetPackage(packageName);
            if (pkg == null)
            {
                Debug.LogError($"[ResHandle] 资源包 {packageName} 不存在");
                return null;
            }
            var handle = pkg.LoadAssetAsync<T>(address);
            await handle;
            return new ResHandle<T>(handle);
        }

        /// <summary>同步加载资源（默认包）。阻塞当前线程，仅在非主线程或初始化时使用。</summary>
        public static ResHandle<T> LoadSync(string address)
        {
            var handle = YooAssetBootstrapper.DefaultPackage.LoadAssetAsync<T>(address);
            handle.WaitForAsyncComplete();
            return new ResHandle<T>(handle);
        }

        /// <summary>同步加载资源（指定包）。阻塞当前线程，仅在非主线程或初始化时使用。</summary>
        public static ResHandle<T> LoadSync(string packageName, string address)
        {
            var pkg = YooAssets.GetPackage(packageName);
            if (pkg == null)
            {
                Debug.LogError($"[ResHandle] 资源包 {packageName} 不存在");
                return null;
            }
            var handle = pkg.LoadAssetAsync<T>(address);
            handle.WaitForAsyncComplete();
            return new ResHandle<T>(handle);
        }

        /// <summary>释放资源（引用计数归零时卸载）</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _handle?.Dispose();
            _handle = null;
        }
    }
}
