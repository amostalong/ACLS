using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace ACLS.Authoring
{
    /// <summary>
    /// YooAsset 资源系统启动器。
    /// 在 GameBootstrap.Awake 之前完成初始化，确保后续所有资源加载走 YooAsset。
    ///
    /// 默认包名: DefaultPackage
    /// 运行模式: OfflinePlayMode（本地加载，无远程更新）
    ///   未来如需 Steam DLC / 远程热更，切换到 HostPlayMode 即可。
    /// </summary>
    public static class YooAssetBootstrapper
    {
        public enum InitializePlayMode
        {
            Auto = 0,
            Offline = 1,
            EditorSimulate = 2,
        }

        public const string DefaultPackageName = "DefaultPackage";

        private static ResourcePackage _defaultPackage;
        private static string _remoteServicesUrl;

        /// <summary> 是否已完成初始化 </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary> 获取默认资源包 </summary>
        public static ResourcePackage DefaultPackage
        {
            get
            {
                if (_defaultPackage == null)
                    Debug.LogError($"[YooAsset] 默认包（{DefaultPackageName}）尚未初始化。请先调用 InitializeAsync()。");
                return _defaultPackage;
            }
        }

        /// <summary>
        /// 初始化 YooAsset 资源系统。
        /// 使用 OfflinePlayMode：所有资源来自本地 StreamingAssets，无远程下载。
        /// </summary>
        public static async UniTask InitializeAsync(InitializePlayMode playMode = InitializePlayMode.Auto, string packageVersion = null)
        {
            if (IsInitialized)
            {
                Debug.Log("[YooAsset] 已初始化，跳过。");
                return;
            }

            YooAssets.Initialize();

            _defaultPackage = YooAssets.CreatePackage(DefaultPackageName);
            
            _remoteServicesUrl = null;
            var initializeOptions = CreateInitializeOptions(DefaultPackageName, playMode);
            var initOper = _defaultPackage.InitializePackageAsync(initializeOptions);
            bool ok = await AwaitOperationSucceed(initOper);
            if (!ok)
            {
                Debug.LogError($"[YooAsset] 初始化失败：{GetError(initOper)}");
                return;
            }

            ok = await EnsureManifestLoadedAsync(_defaultPackage, packageVersion, appendTimeTicks: false);
            if (!ok)
            {
                Debug.LogError("[YooAsset] 加载清单失败。");
                return;
            }

            IsInitialized = true;
            Debug.Log($"[YooAsset] 初始化完成（{playMode}）");
        }

        /// <summary>
        /// 运行时切换到联机模式（用于热更/DLC 场景）。
        /// 调用前需将补丁包部署到 CDN，并配置 HostPlayModeParameters 的远程服务 URL。
        /// </summary>
        public static async UniTask SwitchToHostMode(string remoteServicesUrl, string packageVersion = null)
        {
            if (_defaultPackage == null)
            {
                Debug.LogError("[YooAsset] 默认包未创建，无法切换模式。");
                return;
            }

            IsInitialized = false;
            YooAssets.RemovePackage(DefaultPackageName);

            _defaultPackage = YooAssets.CreatePackage(DefaultPackageName);
            
            _remoteServicesUrl = remoteServicesUrl;
            var remoteService = new RemoteService(remoteServicesUrl);
            var initOptions = new HostPlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService),
            };
            var initOp = _defaultPackage.InitializePackageAsync(initOptions);
            bool ok = await AwaitOperationSucceed(initOp);
            if (!ok)
            {
                Debug.LogError($"[YooAsset] 切换联机模式失败：{GetError(initOp)}");
                return;
            }

            ok = await EnsureManifestLoadedAsync(_defaultPackage, packageVersion, appendTimeTicks: true);
            if (!ok)
            {
                Debug.LogError("[YooAsset] 切换联机模式失败：加载清单失败。");
                return;
            }

            IsInitialized = true;
            Debug.Log($"[YooAsset] 已切换到联机模式，远程服务：{remoteServicesUrl}");
        }

        /// <summary>
        /// 获取指定名称的资源包（可用于多渠道分离的场景）。
        /// 如果包不存在则创建并初始化。
        /// </summary>
        public static async UniTask<ResourcePackage> GetOrCreatePackageAsync(
            string packageName,
            bool isRemote = false,
            InitializePlayMode playMode = InitializePlayMode.Auto,
            string packageVersion = null)
        {
            if (YooAssets.TryGetPackage(packageName, out var pkg))
                return pkg;

            pkg = YooAssets.CreatePackage(packageName);

            if (isRemote)
            {
                if (string.IsNullOrWhiteSpace(_remoteServicesUrl))
                {
                    Debug.LogError("[YooAsset] 远程服务地址为空，无法初始化 HostPlayMode。");
                    return pkg;
                }

                var remoteService = new RemoteService(_remoteServicesUrl);
                var hostOptions = new HostPlayModeOptions
                {
                    BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                    CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteService),
                };
                var op = pkg.InitializePackageAsync(hostOptions);
                if (!await AwaitOperationSucceed(op))
                    return pkg;

                await EnsureManifestLoadedAsync(pkg, packageVersion, appendTimeTicks: true);
            }
            else
            {
                var op = pkg.InitializePackageAsync(CreateInitializeOptions(packageName, playMode));
                if (!await AwaitOperationSucceed(op))
                    return pkg;

                await EnsureManifestLoadedAsync(pkg, packageVersion, appendTimeTicks: false);
            }

            return pkg;
        }

        private static InitializePackageOptions CreateInitializeOptions(string packageName, InitializePlayMode playMode)
        {
            var effective = playMode;
#if UNITY_EDITOR
            if (effective == InitializePlayMode.Auto)
                effective = InitializePlayMode.EditorSimulate;
#else
            if (effective == InitializePlayMode.Auto)
                effective = InitializePlayMode.Offline;
            if (effective == InitializePlayMode.EditorSimulate)
                effective = InitializePlayMode.Offline;
#endif

            if (effective == InitializePlayMode.EditorSimulate)
            {
#if UNITY_EDITOR
                var result = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);
                var editorFileSystemParameters =
                    FileSystemParameters.CreateDefaultEditorFileSystemParameters(result.PackageRootDirectory);
                
                return new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters = editorFileSystemParameters,
                    
                };
#else
                throw new PlatformNotSupportedException("EditorSimulate 模式仅支持在 Unity Editor 下运行。");
#endif
            }

            return new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
            };
        }

        private static async UniTask<bool> AwaitOperationSucceed(AsyncOperationBase operation)
        {
            if (operation == null)
                return false;

            while (!operation.IsDone)
                await UniTask.Yield();

            return operation.Status == EOperationStatus.Succeeded;
        }

        private static async UniTask<bool> EnsureManifestLoadedAsync(
            ResourcePackage package,
            string packageVersion,
            bool appendTimeTicks,
            int timeout = 60)
        {
            if (package == null)
                return false;

            string version = packageVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                var requestOp = package.RequestPackageVersionAsync(new RequestPackageVersionOptions(appendTimeTicks, timeout));
                if (!await AwaitOperationSucceed(requestOp))
                {
                    Debug.LogError($"[YooAsset] 请求版本失败：{GetError(requestOp)}");
                    return false;
                }
                version = requestOp.PackageVersion;
            }

            var loadOp = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(version, timeout));
            if (!await AwaitOperationSucceed(loadOp))
            {
                Debug.LogError($"[YooAsset] 加载清单失败：{GetError(loadOp)}");
                return false;
            }

            return true;
        }

        private static string GetError(AsyncOperationBase operation)
        {
            if (operation == null)
                return "";
            return operation.Error ?? "";
        }

        private sealed class RemoteService : IRemoteService
        {
            private readonly string _baseUrl;

            public RemoteService(string baseUrl)
            {
                _baseUrl = NormalizeBaseUrl(baseUrl);
            }

            public System.Collections.Generic.IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                if (string.IsNullOrEmpty(_baseUrl))
                    return new[] { fileName };
                return new[] { $"{_baseUrl}{fileName}" };
            }

            private static string NormalizeBaseUrl(string url)
            {
                if (string.IsNullOrWhiteSpace(url))
                    return "";
                url = url.Trim();
                return url.EndsWith("/") ? url : $"{url}/";
            }
        }
    }
}
