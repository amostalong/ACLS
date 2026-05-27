using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace ACLS.Editor
{
    /// <summary>
    /// YooAsset 资源配置辅助工具。
    /// 提供菜单项一键创建推荐的项目配置。
    ///
    /// 使用方式：Unity 菜单 → ACLS → YooAsset → 初始化资源配置
    ///
    /// 配置说明：
    /// - 包（Package）: Dev — 游戏默认资源包
    /// - 分组（Group）:
    ///   - art_bg       → 背景图（Tag: bg, 按文件夹打包）
    ///   - art_ui       → UI Sprite（Tag: ui, 按文件夹打包）
    ///   - audio_bgm    → BGM（Tag: bgm, 单独打包）
    ///   - audio_se     → 音效（Tag: se, 按文件夹打包）
    ///   - config       → ScriptableObject 配置（Tag: config, 全分组一个包）
    ///   - fonts        → 字体文件（Tag: font, 全分组一个包）
    ///
    /// 多渠道方案（后续扩展）:
    ///   - SteamPackage → Steam 独占内容（成就UI、DLC）
    ///   - DemoPackage  → Demo 版资源子集
    ///   - QAPackage    → 调试工具资源
    /// </summary>
    public static class YooAssetSetup
    {
        private const string ContentRoot = "Assets/Content";
        private static string DefaultPackageName => ACLS.Authoring.YooAssetBootstrapper.DefaultPackageName;

        [MenuItem("ACLS/YooAsset/初始化资源配置")]
        public static void InitializeConfiguration()
        {
            EnsureDirectories();

            AssetDatabase.Refresh();
            Debug.Log("[YooAssetSetup] 资源目录结构已创建。请在 YooAsset 窗口中配置 Package / Group。");
            Debug.Log("[YooAssetSetup] 菜单路径：YooAsset → AssetBundle Collector");
        }

        [MenuItem("ACLS/YooAsset/打开 Collector")]
        public static void OpenCollector()
        {
            if (!EditorApplication.ExecuteMenuItem("YooAsset/AssetBundle Collector"))
                Debug.LogError("[YooAssetSetup] 未找到 YooAsset/AssetBundle Collector 菜单项。请确认 YooAsset 已正确安装并完成包解析。");
        }

        [MenuItem("ACLS/YooAsset/校验必需资源")]
        public static void ValidateRequiredAssets()
        {
            string[] required =
            {
                $"{ContentRoot}/Config/LlmConfig.asset",
                $"{ContentRoot}/Config/LlmPromptConfig.asset",
                $"{ContentRoot}/Prompts/SysPrompt.md",
                $"{ContentRoot}/Prompts/CharacterExpansion.md",
                $"{ContentRoot}/Prompts/Fragment_StageCreate.md",
                $"{ContentRoot}/Prompts/Fragment_WorldBuild.md",
                $"{ContentRoot}/Fonts/LXGWWenKai-Regular.ttf",
            };

            bool ok = true;
            foreach (var path in required)
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                {
                    ok = false;
                    Debug.LogError($"[YooAssetSetup] 缺少资源：{path}");
                }
            }

            if (ok)
                Debug.Log("[YooAssetSetup] 必需资源齐全。");
        }

        [MenuItem("ACLS/YooAsset/构建 Dev（当前平台）")]
        public static void BuildDefaultPackageForActiveTarget()
        {
            BuildForChannel(DefaultPackageName, EditorUserBuildSettings.activeBuildTarget);
        }

        private static void EnsureDirectories()
        {
            CreateFolder("Assets/Art/Bg");
            CreateFolder("Assets/Art/UI");
            CreateFolder("Assets/Audio/BGM");
            CreateFolder("Assets/Audio/SE");
            CreateFolder($"{ContentRoot}/Config");
            CreateFolder($"{ContentRoot}/Fonts");
            CreateFolder($"{ContentRoot}/Prompts");
        }

        private static void CreateFolder(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                var parent = System.IO.Path.GetDirectoryName(path);
                var name = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static void EnsureDefaultCollectorSetting()
        {
            var setting = BundleCollectorSettingData.Setting;
            if (setting.Packages.Any(p => p.PackageName == DefaultPackageName))
                return;

            setting.ShowPackageView = true;

            var pkg = new BundleCollectorPackage
            {
                PackageName = DefaultPackageName,
                PackageDesc = "ACLS Default Package",
                EnableAddressable = false,
                SupportExtensionless = true,
                LocationToLower = false,
                IncludeAssetGUID = false,
                AutoCollectShaders = true,
                IgnoreRuleName = nameof(NormalIgnoreRule),
            };

            pkg.Groups.Add(CreateGroup("art_bg", "bg", CreateCollector("Assets/Art/Bg", nameof(PackDirectory))));
            pkg.Groups.Add(CreateGroup("art_ui", "ui", CreateCollector("Assets/Art/UI", nameof(PackDirectory))));
            pkg.Groups.Add(CreateGroup("audio_bgm", "bgm", CreateCollector("Assets/Audio/BGM", nameof(PackSeparately))));
            pkg.Groups.Add(CreateGroup("audio_se", "se", CreateCollector("Assets/Audio/SE", nameof(PackDirectory))));
            pkg.Groups.Add(CreateGroup("config", "config", CreateCollector($"{ContentRoot}/Config", nameof(PackGroup))));
            pkg.Groups.Add(CreateGroup("fonts", "font", CreateCollector($"{ContentRoot}/Fonts", nameof(PackGroup))));
            pkg.Groups.Add(CreateGroup("prompts", "prompt", CreateCollector($"{ContentRoot}/Prompts", nameof(PackGroup))));

            setting.Packages.Add(pkg);

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[YooAssetSetup] 已创建 {DefaultPackageName} 的 Collector 配置：Assets/BundleCollectorSetting.asset");
        }

        private static BundleCollectorGroup CreateGroup(string groupName, string tags, BundleCollector collector)
        {
            var group = new BundleCollectorGroup
            {
                GroupName = groupName,
                GroupDesc = groupName,
                AssetTags = tags,
                ActiveRuleName = nameof(EnableGroup),
            };
            group.Collectors.Add(collector);
            return group;
        }

        private static BundleCollector CreateCollector(string collectPath, string packRuleName)
        {
            return new BundleCollector
            {
                CollectPath = collectPath,
                CollectorGUID = string.Empty,
                CollectorType = ECollectorType.MainAssetCollector,
                AddressRuleName = nameof(AddressByFileName),
                PackRuleName = packRuleName,
                FilterRuleName = nameof(CollectAll),
                AssetTags = string.Empty,
                UserData = string.Empty,
            };
        }

        public static void BuildForChannel(string packageName, BuildTarget target)
        {
            var outputRoot = $"Bundles/{target}/{packageName}";

            var buildParams = TryCreateBuildParameters(outputRoot, target, packageName);
            if (buildParams != null && TryInvokeYooAssetBuild(buildParams))
            {
                Debug.Log($"[YooAssetSetup] 构建请求已触发：{outputRoot}");
                return;
            }

            Debug.LogWarning("[YooAssetSetup] 未能通过反射触发构建，将打开 YooAsset Builder 窗口，请在窗口中手动点击 Build。");
            if (!EditorApplication.ExecuteMenuItem("YooAsset/AssetBundle Builder"))
                Debug.LogError("[YooAssetSetup] 未找到 YooAsset/AssetBundle Builder 菜单项。");
        }

        private static object TryCreateBuildParameters(string outputRoot, BuildTarget target, string packageName)
        {
            var type = FindType("YooAsset.Editor.ScriptableBuildParameters");
            if (type == null)
            {
                Debug.LogError("[YooAssetSetup] 未找到 YooAsset.Editor.ScriptableBuildParameters。请确认 YooAsset Editor 程序集已正确导入并完成包解析。");
                return null;
            }

            var obj = Activator.CreateInstance(type);
            SetPropertyIfExists(obj, "BuildOutputRoot", outputRoot);
            SetPropertyIfExists(obj, "BuildPipeline", "ScriptableBuildPipeline");
            SetPropertyIfExists(obj, "BuildTarget", target);
            SetPropertyIfExists(obj, "BuildPackage", packageName);
            SetPropertyIfExists(obj, "PackageName", packageName);
            return obj;
        }

        private static bool TryInvokeYooAssetBuild(object buildParams)
        {
            try
            {
                var builderType = FindType("YooAsset.Editor.AssetBundleBuilder");
                if (builderType == null)
                    return false;

                var method = builderType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                             ?? builderType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                    return false;

                var parameters = method.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = parameters[i];
                    if (p.ParameterType.IsInstanceOfType(buildParams))
                        args[i] = buildParams;
                    else if (p.ParameterType == typeof(bool))
                        args[i] = false;
                    else if (p.ParameterType.IsValueType)
                        args[i] = Activator.CreateInstance(p.ParameterType);
                    else
                        args[i] = null;
                }

                method.Invoke(null, args);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[YooAssetSetup] 触发构建失败：{e.Message}");
                return false;
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void SetPropertyIfExists(object target, string propertyName, object value)
        {
            if (target == null)
                return;
            var prop = target.GetType().GetProperty(propertyName);
            if (prop == null || !prop.CanWrite)
                return;

            if (value == null && prop.PropertyType.IsValueType)
                return;

            if (value != null && !prop.PropertyType.IsInstanceOfType(value))
                return;

            prop.SetValue(target, value);
        }
    }
}
