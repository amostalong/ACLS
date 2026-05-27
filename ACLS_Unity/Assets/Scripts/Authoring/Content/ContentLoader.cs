using System.Collections.Generic;
using UnityEngine;
using YooAsset;
using Cysharp.Threading.Tasks;

namespace ACLS.Authoring
{
    public static class ContentLoader
    {
        private static readonly Dictionary<string, object> _handles = new();

        public static async UniTask<T> LoadAsync<T>(string location, string resourcesFallback = null, bool allowFallbackToResources = true) where T : Object
        {
            if (TryLoadFromCache(location, out T cached))
                return cached;

            if (YooAssetBootstrapper.IsInitialized)
            {
                var pkg = YooAssetBootstrapper.DefaultPackage;
                if (pkg == null)
                    return null;
                var handle = pkg.LoadAssetAsync<T>(location);
                await handle;
                if (handle.IsValid)
                {
                    CacheHandle(location, handle);
                    return handle.GetAssetObject<T>();
                }
                Debug.LogError($"[ContentLoader] YooAsset load failed: location={location}");
                handle.Dispose();
            }

#if UNITY_EDITOR
            if (TryLoadFromAssetDatabase(location, out T editorAsset))
                return editorAsset;
#endif

            if (allowFallbackToResources && !string.IsNullOrWhiteSpace(resourcesFallback))
            {
                var res = Resources.Load<T>(resourcesFallback);
                if (res != null)
                    return res;
            }

            return null;
        }

        public static T LoadSync<T>(string location, string resourcesFallback = null, bool allowFallbackToResources = true) where T : Object
        {
            if (TryLoadFromCache(location, out T cached))
                return cached;

            if (YooAssetBootstrapper.IsInitialized)
            {
                var pkg = YooAssetBootstrapper.DefaultPackage;
                if (pkg == null)
                    return null;
                var handle = pkg.LoadAssetAsync<T>(location);
                handle.WaitForAsyncComplete();
                if (handle.IsValid)
                {
                    CacheHandle(location, handle);
                    return handle.GetAssetObject<T>();
                }
                Debug.LogError($"[ContentLoader] YooAsset load failed: location={location}");
                handle.Dispose();
            }

#if UNITY_EDITOR
            if (TryLoadFromAssetDatabase(location, out T editorAsset))
                return editorAsset;
#endif

            if (allowFallbackToResources && !string.IsNullOrWhiteSpace(resourcesFallback))
            {
                var res = Resources.Load<T>(resourcesFallback);
                if (res != null)
                    return res;
            }

            return null;
        }

        private static bool TryLoadFromCache<T>(string location, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrWhiteSpace(location))
                return false;

            if (_handles.TryGetValue(location, out var obj) && obj is AssetHandle handle && handle.IsValid)
            {
                asset = handle.GetAssetObject<T>();
                return asset != null;
            }

            return false;
        }

        private static void CacheHandle(string location, object handle)
        {
            if (string.IsNullOrWhiteSpace(location))
                return;

            if (_handles.TryGetValue(location, out var old) && old is System.IDisposable d)
                d.Dispose();

            _handles[location] = handle;
        }

#if UNITY_EDITOR
        private static bool TryLoadFromAssetDatabase<T>(string location, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrWhiteSpace(location))
                return false;
            if (!location.StartsWith("Assets/"))
                return false;
            asset = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(location);
            return asset != null;
        }
#endif
    }
}
