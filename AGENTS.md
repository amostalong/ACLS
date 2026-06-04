# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Game design

A CK3-like, text-only character/relations simulation set in **184 CE 三国** (黄巾之乱 onset). Player controls one character at a time (architypal 架空小士族子弟); on death the family line continues through the eldest son. World NPCs are real historical figures. Time advances in **daily ticks**, real-time auto-advance, pause/speed via space + 1/2/3.

The full design + phase plan lives at `C:/Users/dd/.Codex/plans/snuggly-jingling-fiddle.md`. Read it before making non-trivial gameplay decisions.

## Engine & toolchain

- **Unity Editor:** `2022.3.62f2` (LTS), pinned in `ACLS_Unity/ProjectSettings/ProjectVersion.txt`. Open this project with that exact editor — newer editors will silently upgrade project files.
- **Project root for the Editor:** `ACLS_Unity/` (the folder with `Assets/`, `Packages/`, `ProjectSettings/`). Open *that*, not the repo root.
- **Target platform:** Standalone Desktop. No XR/VR plugin currently configured (the `com.unity.modules.xr/vr` modules are baseline Unity, not active runtime).
- **C# version:** Unity 2022.3 ships C# 9 — switch expressions, target-typed `new`, init-only setters all available.

## Resource management (YooAsset)

**Decision**: We use [YooAsset](https://github.com/tuyoogame/YooAsset) (v2.x, UPM package `com.tuyoogame.yooasset`) as the resource management framework. Addressables was considered and rejected — it's designed for mobile remote-catalog workflows, which our standalone desktop game does not need. YooAsset gives us the key features we *do* need (reference-counted loading/unloading, dependency resolution, async/sync API, multi-platform builds, multi-channel via Package+Tag) without the CDN/Catalog/Variant overhead.

Key integration points:

- **`YooAssetBootstrapper`** (`Authoring/`) — initializes YooAsset at game startup. Currently in `OfflinePlayMode` (all assets from local StreamingAssets). Call `SwitchToHostMode()` when Steam DLC / CDN hot-update is needed.
- **`AssetHandle<T>`** (`Authoring/`) — lightweight wrapper with `LoadAsync` / `LoadSync` / `Dispose`. Use this for loading Texture2D, AudioClip, Sprite, etc. from YooAsset.
- **Small config files** (LlmConfig, LlmPromptConfig, Prompts/*.md, fonts) still use `Resources.Load` — they are tiny, loaded once at boot, and not worth the overhead of AB packaging. This is a pragmatic compromise, not an endorsement of Resources.Load for general use.
- **Editor configuration**: open `YooAsset → AssetBundle Collector` to set up Package/Group/Tag. The `ACLS/YooAsset/初始化资源配置` menu item creates the recommended folder structure.
- **Package structure** (planned):
  - `DefaultPackage` — core game assets (bg, ui, audio, fonts). Split into Groups by tag.
  - `SteamPackage` — (future) Steam-exclusive content.
  - `DemoPackage` — (future) demo subset.
  - `QAPackage` — (future) debug tooling assets.

Current asset loading flow: `Resources.Load` → (phase 2) gradually migrate to `AssetHandle<T>` with YooAsset.

## Architecture overview

Single runtime asmdef (`ACLS.Runtime`) and editor asmdef (`ACLS.Editor`) under `Assets/Scripts/`. **Do not split further** until Phase 3 — see plan file for rationale.

Folders inside `Assets/Scripts/` map 1:1 to namespaces:

```
Sim/         → namespace ACLS.Sim     # Pure POCO simulation core. NO UnityEngine where avoidable.
Data/        → namespace ACLS.Data    # ScriptableObject definitions (TraitDef, GameEventDef, ...)
Loc/         → namespace ACLS.Loc     # L10n key→中文 lookup
Authoring/   → namespace ACLS.Authoring  # MonoBehaviour glue: Bootstrap, ClockDriver, EventDispatcher, Registry, WorldFactory, UiBuilder
UI/          → namespace ACLS.UI      # uGUI views (HudView, CharacterPanelView, EventModalView, UiKit)
```

Key invariants:

- **`World` is a pure POCO graph.** No MonoBehaviour, no UnityEngine references inside. Future save root — `JsonUtility.ToJson(world)` is intended to round-trip the whole thing.
- **All cross-entity references are int IDs**, never object references. `0` is the sentinel for "none/unset" (we avoid `int?` because `JsonUtility` doesn't serialize nullables).
- **`World.PlayerCharacterId` is just an int.** Inheritance switches the int and fires `OnPlayerSwitched`; UI rebinds. There is no special "player object."
- **Sim does not depend on UnityEngine.** It uses `ACLS.Sim.Rng` (System.Random wrapper) instead of `UnityEngine.Random`.
- **ScriptableObject content lives at `Assets/Resources/Content/...`** (Phase 2). Phase 1 content is currently constructed *programmatically* in `WorldFactory` — see "Phase 1 boot path" below.

### Boot path (Phase 1)

`GameBootstrap` self-installs via `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` — pressing Play on **any scene** boots the game. The default `SampleScene.unity` is fine, no scene editing required. Boot order:

1. `WorldFactory.RegisterPlaceholderLocalization()` — populates `L10n` map with 中文 strings.
2. `Registry.Clear()` then registers `WorldFactory.BuildPlaceholderTraits()` and `BuildPlaceholderEvents()` — content created via `ScriptableObject.CreateInstance` in code, no `.asset` files needed yet.
3. `WorldFactory.BuildPlaceholderWorld()` — constructs the placeholder 张氏 family in 颍川.
4. `GameClockDriver` and `EventDispatcher` are added as components, bound to `World`.
5. `UiBuilder.Build()` creates a Canvas + EventSystem + HUD/CharacterPanel/EventModal procedurally. Font is resolved via `Font.CreateDynamicFontFromOSFont` against a CJK candidate list — no font asset needs to be imported.

When Phase 2 introduces real ScriptableObject assets, `Registry.Register` should be called from a `Resources.LoadAll<T>("Content/...")` pass instead of from `WorldFactory`.

### Tick loop

`World.Tick()` advances date by 1 day, fires `OnDayTick`, runs `ProcessDeaths`, then fires `OnMonthTick`/`OnYearTick` if the boundary was crossed. `EventDispatcher` listens on month tick (Periodic + Conditional events) and day tick (Dated events). UI panels listen on month tick (state changes are slow enough that daily refresh is wasteful).

`GameClockDriver.Update` translates `Time.deltaTime` into ticks at the current speed; freezes if `World.Paused` or `World.EventQueue.Count > 0`. Event modals auto-pause via `SetVisible(true) → world.Paused = true`, and auto-resume on dismiss.

## Build, run, test

There are no CLI entrypoints. To verify Phase 1:

1. Open `ACLS_Unity` in Unity Hub with editor `2022.3.62f2`.
2. Wait for compile (any error appears in the bottom Console). On first open, Unity will auto-generate `.meta` files for all the .cs files and new folders — these become untracked changes; they are normal Unity workflow and should be committed.
3. Open the default scene (`Assets/Scenes/SampleScene.unity`) — or any scene; the bootstrapper auto-attaches.
4. Press Play. HUD shows `184年1月1日`, paused. Press `2` to run; events fire over time; click choices; player ages; on death the heir takes over (or Game Over if no eligible heir).

There are no automated tests (Unity Test Framework not installed).

## Conventions for future work

- **Every player-input pause point must offer 1–4 directly clickable options.** Applies to event modals, LLM-narrated chat turns, and any future decision menu. Aim for **2–4**; **1** is allowed only when the situation truly forks one way; **0 is forbidden**. Free-text input (chat box) may coexist as a supplement, but suggested actions are mandatory — when stalled for player input with zero buttons, that is a bug.
- Place new sim types in `Sim/` and keep them MonoBehaviour-free. Anything that touches `UnityEngine` belongs in `Authoring/` or `UI/`.
- New event types: define a `GameEventDef` ScriptableObject (or build one programmatically in `WorldFactory` for prototype). Express conditions/effects through the existing `ConditionExpr` / `EffectOp` enums; extend the enums when no existing op fits.
- Trait IDs are `int`. Keep them stable across versions — they're stored on `Character.Traits` and migrating IDs would invalidate saves later. Document new IDs as constants on `WorldFactory` or a future `TraitIds` static class.
- `Assets/Resources/Content/` is the conventional drop point for SO instances; `WorldFactory` Phase 2 will switch from programmatic creation to `Resources.LoadAll<T>`.
- Treat `ACLS_Unity/Library/`, `Temp/`, `Logs/`, `UserSettings/` as machine-local — gitignore covers these.
- `.meta` files next to assets are required by Unity; create/move/delete them alongside their asset.

## Repository

`git init` already done at the repo root. `.gitignore` filters Unity-generated dirs and Codex metadata (`/.Codex/`, `/memory/`). The first commit captured the scaffolding (`ccb456b`); Phase 1 sources have not been committed yet at the time of this writing — let the user trigger commits.
