using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Events;

namespace QoLChinesePatch
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("org.bepinex.plugins.qol_unknown", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "casualtiesunknown.qolchinesepatch";
        public const string PluginName = "QoL Unknown Chinese Patch";
        public const string PluginVersion = "0.10.14";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> EnableTargetedQoLPatches;
        internal static ConfigEntry<bool> EnableManualScanHotkey;
        internal static ConfigEntry<KeyCode> ManualScanKey;
        internal static ConfigEntry<bool> DumpCandidateTexts;
        internal static ConfigEntry<bool> PatchQoLInternalLocale;
        internal static ConfigEntry<bool> PatchUnitySettersDiagnostic;
        internal static ConfigEntry<bool> EnableSafeTextSetterPatch;
        internal static ConfigEntry<bool> EnableAutoScanWatcher;
        internal static ConfigEntry<bool> EnableClickScanTrigger;
        internal static ConfigEntry<float> ClickScanCooldownSeconds;
        internal static ConfigEntry<float> ClickScanDelaySeconds;
        internal static ConfigEntry<int> ClickScanBurstFrames;
        internal static ConfigEntry<float> ClickScanBurstInterval;
        internal static ConfigEntry<float> AutoScanInterval;
        internal static ConfigEntry<float> ScanDebounceSeconds;
        internal static ConfigEntry<int> BurstScanFrames;
        internal static ConfigEntry<float> BurstScanInterval;
        internal static ConfigEntry<bool> EnableExistingSceneScan;
        internal static ConfigEntry<bool> EnableSceneLoadedScan;
        internal static ConfigEntry<bool> EnableEscSettingsMenuWatcher;
        internal static ConfigEntry<float> EscSettingsMenuScanInterval;
        internal static ConfigEntry<bool> EnableQoLMenuLifecycleTrace;
        internal static ConfigEntry<bool> EnableTraceStackForKeyMethods;
        internal static ConfigEntry<bool> EnablePreciseEnhancedSettingsButtonHook;
        internal static ConfigEntry<int> PreciseEnhancedSettingsButtonScanBursts;
        internal static ConfigEntry<float> PreciseEnhancedSettingsButtonScanInterval;
        internal static ConfigEntry<bool> EnableSourceBasedEnhancedMenuFix;
        internal static ConfigEntry<int> SourceBasedEnhancedMenuScanBursts;
        internal static ConfigEntry<float> SourceBasedEnhancedMenuScanInterval;
        internal static ConfigEntry<bool> EnableSourceEnhancedButtonObjectWatcher;
        internal static ConfigEntry<float> SourceEnhancedButtonObjectWatcherInterval;
        internal static ConfigEntry<float> SourceEnhancedButtonObjectWatcherDuration;
        internal static ConfigEntry<bool> EnableKrokMpStyleLifecycleShim;
        internal static ConfigEntry<float> KrokMpStyleLifecycleShimInterval;
        internal static ConfigEntry<float> KrokMpStyleLifecycleShimDuration;
        internal static ConfigEntry<bool> EnableEnhancedSettingsPanelLocalizer;
        internal static ConfigEntry<float> EnhancedSettingsPanelLocalizerInterval;
        internal static ConfigEntry<bool> EnableEnhancedSettingsPanelMissingDump;

        private Harmony _harmony;
        private float _nextScanAllowed;
        private float _nextAutoScan;
        private float _nextClickScanAllowed;
        private readonly HashSet<string> _activeBursts = new HashSet<string>(StringComparer.Ordinal);
        private bool _lastUIMainMenuOpen;
        private bool _settingsMenuBurstActive;
        private float _nextEscSettingsMenuScan;
        private int _sourceButtonWatcherSerial;
        private int _krokMpLifecycleShimSerial;
        private bool _krokMpUpdateWatcherDone;
        private bool _krokMpUpdateWatcherExpiredLogged;
        private float _krokMpUpdateWatcherStart;
        private float _nextKrokMpUpdateWatcherAttempt;
        private int _krokMpUpdateWatcherAttempts;
        private float _nextEnhancedPanelLocalizeCheck;
        private string _lastEnhancedPanelFingerprint = string.Empty;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _krokMpUpdateWatcherStart = Time.realtimeSinceStartup;

            EnableTargetedQoLPatches = Config.Bind("Patch", "EnableTargetedQoLPatches", true,
                "Patch known QoL Unknown UI construction methods and scan text once after they run. Recommended mode.");
            PatchQoLInternalLocale = Config.Bind("Patch", "PatchQoLInternalLocale", true,
                "Patch QoL's tiny built-in locale lookup when available.");
            EnableSafeTextSetterPatch = Config.Bind("Patch", "EnableSafeTextSetterPatch", true,
                "Patch global UI/TMP text setters in safe mode. It only translates known dictionary/phrase strings and avoids scene-wide scanning on every setter. Recommended for automatic translation of newly opened panels.");
            PatchUnitySettersDiagnostic = Config.Bind("Patch", "PatchUnitySettersDiagnostic", false,
                "Compatibility alias/diagnostic switch. Leave false unless asked; EnableSafeTextSetterPatch already handles normal auto-translation.");
            EnableAutoScanWatcher = Config.Bind("Patch", "EnableAutoScanWatcher", false,
                "Fallback periodic lightweight scan. Disabled by default since click-triggered scans are more efficient. Enable only if some screens still need manual refresh.");
            EnableClickScanTrigger = Config.Bind("Patch", "EnableClickScanTrigger", true,
                "After mouse/keyboard UI actions, run a short delayed known-text scan. Re-enabled in v0.10.2 because v0.6.4's successful KrokMP path relied on late UI-action rescans.");
            ClickScanCooldownSeconds = Config.Bind("Performance", "ClickScanCooldownSeconds", 0.35f,
                "Minimum seconds between click-triggered scan bursts. 1.0 is a good balance between responsiveness and performance.");
            ClickScanDelaySeconds = Config.Bind("Performance", "ClickScanDelaySeconds", 0.10f,
                "Initial delay after a click/key action before scanning. Helps catch popups/dropdowns created shortly after the input.");
            ClickScanBurstFrames = Config.Bind("Performance", "ClickScanBurstFrames", 4,
                "Number of delayed scans after one click/key action. Higher catches slower UI creation, lower saves more performance.");
            ClickScanBurstInterval = Config.Bind("Performance", "ClickScanBurstInterval", 0.18f,
                "Delay between click-triggered scans.");
            AutoScanInterval = Config.Bind("Performance", "AutoScanInterval", 1.5f,
                "Seconds between fallback automatic scans if EnableAutoScanWatcher=true.");
            EnableManualScanHotkey = Config.Bind("Patch", "EnableManualScanHotkey", false,
                "Allow pressing the hotkey to translate currently visible QoL UI texts.");
            ManualScanKey = Config.Bind("Patch", "ManualScanKey", KeyCode.F8,
                "Manual scan hotkey. Open the QoL menu and press this if a text is not translated automatically.");
            ScanDebounceSeconds = Config.Bind("Performance", "ScanDebounceSeconds", 0.75f,
                "Minimum seconds between automatic scan bursts.");
            BurstScanFrames = Config.Bind("Performance", "BurstScanFrames", 6,
                "How many delayed scans to run after a QoL UI method finishes.");
            BurstScanInterval = Config.Bind("Performance", "BurstScanInterval", 0.15f,
                "Delay between scans in a burst.");
            DumpCandidateTexts = Config.Bind("Diagnostics", "DumpCandidateTexts", false,
                "Write untranslated QoL-looking UI texts into BepInEx/plugins/QoLChinesePatch/missing-texts.txt during targeted scans.");
            EnableExistingSceneScan = Config.Bind("Patch", "EnableExistingSceneScan", false,
                "Trace build default: disabled. Existing scene scan is a repair attempt and would pollute lifecycle diagnosis.");
            EnableSceneLoadedScan = Config.Bind("Patch", "EnableSceneLoadedScan", false,
                "Trace build default: disabled. Scene loaded scan is a repair attempt and would pollute lifecycle diagnosis.");
            EnableEscSettingsMenuWatcher = Config.Bind("Patch", "EnableEscSettingsMenuWatcher", false,
                "Watch the vanilla ESC/UIMainMenu settings menu and run the same QoL-style scan used by v0.6.4 when it opens or changes tabs. This restores stable translation of Gameplay/Video/Audio/Keybinds pages.");
            EscSettingsMenuScanInterval = Config.Bind("Performance", "EscSettingsMenuScanInterval", 0.45f,
                "Legacy setting retained for compatibility; ESC watcher is off by default in this trace build.");
            EnableQoLMenuLifecycleTrace = Config.Bind("Diagnostics", "EnableQoLMenuLifecycleTrace", false,
                "Trace QoL Unknown menu lifecycle methods and selected button presses. Disabled by default in v0.9.7 repair build.");
            EnableTraceStackForKeyMethods = Config.Bind("Diagnostics", "EnableTraceStackForKeyMethods", false,
                "Print compact stack traces for the first calls to key QoL menu methods. Disabled by default in v0.9.7 repair build.");
            EnablePreciseEnhancedSettingsButtonHook = Config.Bind("Patch", "EnablePreciseEnhancedSettingsButtonHook", false,
                "Legacy v0.9.7 Button.Press hook. Disabled by default in v0.9.8 because the source-based QoL menu hook is more precise.");
            PreciseEnhancedSettingsButtonScanBursts = Config.Bind("Performance", "PreciseEnhancedSettingsButtonScanBursts", 4,
                "Number of short scans after EnhancedSettingsButton is pressed. Keep small; this is not a polling loop.");
            PreciseEnhancedSettingsButtonScanInterval = Config.Bind("Performance", "PreciseEnhancedSettingsButtonScanInterval", 0.08f,
                "Interval between the few scans after EnhancedSettingsButton is pressed.");
            EnableSourceBasedEnhancedMenuFix = Config.Bind("Patch", "EnableSourceBasedEnhancedMenuFix", true,
                "Source-based repair: patch QoLSettingsMenu.BuildSettingsUI / EnhancedMenuController.OpenMenu and attach directly to EnhancedSettingsButton onClick. This replaces path guessing and ESC watcher attempts.");
            SourceBasedEnhancedMenuScanBursts = Config.Bind("Performance", "SourceBasedEnhancedMenuScanBursts", 6,
                "Number of short forced scans after the real QoL enhanced menu opens. Keep small; this is not a loop.");
            SourceBasedEnhancedMenuScanInterval = Config.Bind("Performance", "SourceBasedEnhancedMenuScanInterval", 0.10f,
                "Interval between source-based enhanced menu scans.");
            EnableSourceEnhancedButtonObjectWatcher = Config.Bind("Patch", "EnableSourceEnhancedButtonObjectWatcher", false,
                "Source-based finite watcher: after SampleScene loads, look only for Main Camera/Canvas/GammaPanel/EnhancedSettingsButton and attach a listener. This avoids guessing paths from Button.Press and does not scan text in a loop.");
            SourceEnhancedButtonObjectWatcherInterval = Config.Bind("Performance", "SourceEnhancedButtonObjectWatcherInterval", 0.25f,
                "Interval for the finite EnhancedSettingsButton object watcher.");
            SourceEnhancedButtonObjectWatcherDuration = Config.Bind("Performance", "SourceEnhancedButtonObjectWatcherDuration", 45f,
                "Maximum seconds to look for the EnhancedSettingsButton after startup or scene load.");
            EnableKrokMpStyleLifecycleShim = Config.Bind("Patch", "EnableKrokMpStyleLifecycleShim", false,
                "Simulate the practical KrokMP effect that lets QoL create its enhanced settings button: if QoL missed PlayerCamera.Start, rebuild the EnhancedSettingsButton from GammaPanel/Button and wire it to EnhancedMenuController.OpenMenu.");
            KrokMpStyleLifecycleShimInterval = Config.Bind("Performance", "KrokMpStyleLifecycleShimInterval", 0.25f,
                "Interval for the finite KrokMP-style lifecycle shim watcher.");
            KrokMpStyleLifecycleShimDuration = Config.Bind("Performance", "KrokMpStyleLifecycleShimDuration", 45f,
                "Maximum seconds to wait for Main Camera/Canvas/GammaPanel/Button so the legacy shim can create EnhancedSettingsButton. Disabled by default in release-clean builds.");
            EnableEnhancedSettingsPanelLocalizer = Config.Bind("Patch", "EnableEnhancedSettingsPanelLocalizer", true,
                "Source-confirmed targeted localizer: when QoL's EnhancedSettingsPanel is active, translate only texts under that panel. This mimics the successful SetupInGameToggle targeted translation without global/ESC polling.");
            EnhancedSettingsPanelLocalizerInterval = Config.Bind("Performance", "EnhancedSettingsPanelLocalizerInterval", 0.12f,
                "Minimum seconds between active EnhancedSettingsPanel checks. Only the panel subtree is inspected.");
            EnableEnhancedSettingsPanelMissingDump = Config.Bind("Diagnostics", "EnableEnhancedSettingsPanelMissingDump", false,
                "Diagnostic only. When the active EnhancedSettingsPanel still contains English text that cannot be translated, append it to QoLChinesePatch/missing-enhanced-panel.txt with its object path.");

            Translator.LoadTranslations(Paths.PluginPath, Logger);
            _harmony = new Harmony(PluginGuid);
            RuntimeTextPatcher.Apply(_harmony, Logger);
            if (EnableSceneLoadedScan.Value)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                Logger.LogInfo("Scene loaded scan enabled. Existing loaded scenes will also be scanned once.");
            }
            if (EnableSourceEnhancedButtonObjectWatcher.Value)
            {
                SceneManager.sceneLoaded += OnSceneLoadedForSourceEnhancedButtonWatcher;
                StartCoroutine(SourceEnhancedButtonObjectWatcherCoroutine("startup"));
                Logger.LogInfo("Source-based EnhancedSettingsButton object watcher enabled.");
            }
            if (EnableKrokMpStyleLifecycleShim.Value)
            {
                SceneManager.sceneLoaded += OnSceneLoadedForKrokMpStyleLifecycleShim;
                StartCoroutine(KrokMpStyleLifecycleShimCoroutine("startup"));
                _krokMpUpdateWatcherDone = false;
                _krokMpUpdateWatcherExpiredLogged = false;
                _krokMpUpdateWatcherStart = Time.realtimeSinceStartup;
                Logger.LogInfo("KrokMP-style lifecycle shim enabled. It will create the missing QoL EnhancedSettingsButton if QoL missed PlayerCamera.Start. Update watcher is also active for independent mode.");
            }
            if (EnableExistingSceneScan.Value)
            {
                ScanExistingLoadedScenes("awake-existing-scenes");
            }
            // v0.9.6 is trace-first. Do not start delayed PreGen/ESC/click scan repair loops here.
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Translations: {Translator.Count}; sourceBasedEnhancedMenuFix={EnableSourceBasedEnhancedMenuFix.Value}; sourceButtonObjectWatcher={EnableSourceEnhancedButtonObjectWatcher.Value}; krokMpStyleLifecycleShim={EnableKrokMpStyleLifecycleShim.Value}; clickScanTrigger={EnableClickScanTrigger.Value}; enhancedPanelMissingDump={EnableEnhancedSettingsPanelMissingDump.Value}; preciseEnhancedButtonHook={EnablePreciseEnhancedSettingsButtonHook.Value}; traceOnlyMode={EnableQoLMenuLifecycleTrace.Value}");
        }


        private IEnumerator DelayedPreGenTemplateScanCoroutine()
        {
            // v0.9.5: KrokMP success logs show a useful scene-loaded PreGen scan occurring after UI objects exist.
            // Without KrokMP, the existing-scene scan runs too early (total=0), so reproduce only that timing effect: 
            // a small number of one-shot known-text scans during early PreGen. This is not an ESC/click polling loop.
            float[] delays = new float[] { 0.6f, 1.4f, 2.6f };
            for (int i = 0; i < delays.Length; i++)
            {
                yield return new WaitForSecondsRealtime(delays[i]);
                Scene scene = SceneManager.GetActiveScene();
                string sceneName = scene.IsValid() ? scene.name : string.Empty;
                if (sceneName == "PreGen" || string.IsNullOrEmpty(sceneName))
                {
                    RuntimeTextPatcher.ScanAllKnownTextObjects("delayed-pregen-template-scan-" + i, Logger);
                }
                else
                {
                    Logger.LogInfo("Delayed PreGen template scan stopped because active scene is " + sceneName);
                    yield break;
                }
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo($"Scene loaded scan requested: {scene.name} ({mode})");
            RuntimeTextPatcher.ScanAllKnownTextObjects("scene-loaded:" + scene.name, Logger);
        }

        private void ScanExistingLoadedScenes(string reason)
        {
            try
            {
                int count = SceneManager.sceneCount;
                Logger.LogInfo($"Existing scene scan requested: sceneCount={count}, reason={reason}");
                for (int i = 0; i < count; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.IsValid() || !scene.isLoaded) continue;
                    RuntimeTextPatcher.ScanAllKnownTextObjects("existing-scene:" + scene.name, Logger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Existing scene scan failed: " + ex.Message);
            }
        }

        private void Update()
        {
            if (EnableManualScanHotkey.Value && Input.GetKeyDown(ManualScanKey.Value))
            {
                Logger.LogInfo("Manual QoL text scan requested.");
                RuntimeTextPatcher.ScanQoLTextObjects("manual-hotkey", Logger, force: true);
            }

            TickKrokMpStyleLifecycleShimUpdateWatcher();

            if (EnableEnhancedSettingsPanelLocalizer != null && EnableEnhancedSettingsPanelLocalizer.Value)
            {
                MaybeLocalizeEnhancedSettingsPanel();
            }

            if (EnableClickScanTrigger.Value && IsLikelyUiActionInput() && Time.unscaledTime >= _nextClickScanAllowed)
            {
                _nextClickScanAllowed = Time.unscaledTime + Math.Max(0.08f, Math.Min(0.20f, ClickScanCooldownSeconds.Value));
                StartCoroutine(ClickScanCoroutine("click-trigger"));
            }

            HandleEscSettingsMenuWatcher();

            if (EnableAutoScanWatcher.Value && Time.unscaledTime >= _nextAutoScan)
            {
                _nextAutoScan = Time.unscaledTime + Math.Max(0.5f, AutoScanInterval.Value);
                RuntimeTextPatcher.ScanQoLTextObjects("auto-watch", Logger, force: false);
            }
        }

        private void TickKrokMpStyleLifecycleShimUpdateWatcher()
        {
            if (EnableKrokMpStyleLifecycleShim == null || !EnableKrokMpStyleLifecycleShim.Value) return;
            if (_krokMpUpdateWatcherDone) return;

            float now = Time.realtimeSinceStartup;
            float duration = Math.Max(5f, KrokMpStyleLifecycleShimDuration != null ? KrokMpStyleLifecycleShimDuration.Value : 45f);
            if (now - _krokMpUpdateWatcherStart > duration)
            {
                if (!_krokMpUpdateWatcherExpiredLogged)
                {
                    _krokMpUpdateWatcherExpiredLogged = true;
                    Logger.LogWarning($"KrokMP-style lifecycle update watcher expired after {_krokMpUpdateWatcherAttempts} attempts without creating/finding EnhancedSettingsButton.");
                }
                _krokMpUpdateWatcherDone = true;
                return;
            }

            if (now < _nextKrokMpUpdateWatcherAttempt) return;
            float interval = Math.Max(0.05f, KrokMpStyleLifecycleShimInterval != null ? KrokMpStyleLifecycleShimInterval.Value : 0.25f);
            _nextKrokMpUpdateWatcherAttempt = now + interval;
            _krokMpUpdateWatcherAttempts++;

            if (TryEnsureKrokMpStyleEnhancedSettingsButton("update-watcher:attempt-" + _krokMpUpdateWatcherAttempts))
            {
                _krokMpUpdateWatcherDone = true;
                Logger.LogInfo($"KrokMP-style lifecycle update watcher completed after {_krokMpUpdateWatcherAttempts} attempts.");
                return;
            }

            if (_krokMpUpdateWatcherAttempts == 1 || _krokMpUpdateWatcherAttempts % 20 == 0)
            {
                Scene scene = SceneManager.GetActiveScene();
                string sceneName = scene.IsValid() ? scene.name : string.Empty;
                Logger.LogInfo($"KrokMP-style lifecycle update watcher: waiting for GammaPanel/Button; attempt={_krokMpUpdateWatcherAttempts}, scene={sceneName}");
            }
        }

        private void HandleEscSettingsMenuWatcher()
        {
            // Disabled in v0.9.4. The ESC settings menu is not opened immediately by ESC; it appears after an extra button click.
            // Polling UIMainMenu every frame was also the source of lag when KrokMP was loaded, because loose type lookup touched KrokMP assemblies.
            return;
        }

        private static bool IsUIMainMenuOpen()
        {
            try
            {
                Type t = RuntimeTextPatcher.SafeFindType("UIMainMenu");
                if (t == null) return false;
                MethodInfo isOpen = AccessTools.Method(t, "IsOpen");
                if (isOpen != null && isOpen.ReturnType == typeof(bool) && isOpen.GetParameters().Length == 0)
                {
                    object result = isOpen.Invoke(null, null);
                    if (result is bool b) return b;
                }
                FieldInfo f = AccessTools.Field(t, "mainmenu_open") ?? AccessTools.Field(t, "_mainmenu_open");
                if (f != null && f.FieldType == typeof(bool))
                {
                    object result = f.GetValue(null);
                    if (result is bool b) return b;
                }
            }
            catch { }
            return false;
        }

        private static bool IsLikelyUiActionInput()
        {
            return Input.GetMouseButtonDown(0)
                || Input.GetMouseButtonDown(1)
                || Input.GetMouseButtonDown(2)
                || Input.GetMouseButtonUp(0)
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space)
                || Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.Tab);
        }

        private void MaybeLocalizeEnhancedSettingsPanel()
        {
            float now = Time.unscaledTime;
            if (now < _nextEnhancedPanelLocalizeCheck) return;
            _nextEnhancedPanelLocalizeCheck = now + Math.Max(0.05f, EnhancedSettingsPanelLocalizerInterval.Value);
            try
            {
                GameObject root = RuntimeTextPatcher.FindActiveEnhancedSettingsPanelRoot();
                if (root == null) return;
                string fp = RuntimeTextPatcher.MakeTextFingerprint(root);
                if (fp == _lastEnhancedPanelFingerprint) return;
                _lastEnhancedPanelFingerprint = fp;
                RuntimeTextPatcher.ScanKnownTextUnder(root, "enhanced-settings-panel-active", Logger);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("EnhancedSettingsPanel localizer failed: " + ex.Message);
            }
        }

        private IEnumerator ClickScanCoroutine(string reason)
        {
            // v0.9.4: the vanilla ESC settings page is opened by pressing ESC and then clicking a separate button.
            // The old watcher tried to poll UIMainMenu and became expensive with KrokMP loaded.
            // Use input-driven delayed exact scans instead: cheap when idle, late enough to catch submenu creation.
            float delay = Math.Max(0.0f, ClickScanDelaySeconds.Value);
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            int count = Math.Max(1, ClickScanBurstFrames.Value);
            float interval = Math.Max(0.05f, ClickScanBurstInterval.Value);
            for (int i = 0; i < count; i++)
            {
                RuntimeTextPatcher.ScanAllKnownTextObjects(reason + "-known-" + i, Logger);
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        internal void RequestSettingsMenuScanBurst(string reason)
        {
            if (_settingsMenuBurstActive) return;
            StartCoroutine(SettingsMenuScanBurstCoroutine(reason ?? "esc-settings"));
        }

        internal void RequestPreciseEnhancedSettingsButtonScan(string reason)
        {
            StartCoroutine(PreciseEnhancedSettingsButtonScanCoroutine(reason ?? "precise-enhanced-settings-button"));
        }

        private IEnumerator PreciseEnhancedSettingsButtonScanCoroutine(string reason)
        {
            int count = Math.Max(1, Math.Min(8, PreciseEnhancedSettingsButtonScanBursts.Value));
            float interval = Math.Max(0.03f, Math.Min(0.25f, PreciseEnhancedSettingsButtonScanInterval.Value));
            // Wait until the button callback has created the enhanced settings page.
            yield return null;
            for (int i = 0; i < count; i++)
            {
                RuntimeTextPatcher.ScanQoLTextObjects(reason + "-" + i, Logger, force: true);
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        internal void RequestSourceBasedEnhancedMenuScan(string reason)
        {
            StartCoroutine(SourceBasedEnhancedMenuScanCoroutine(reason ?? "source-enhanced-menu"));
        }

        private IEnumerator SourceBasedEnhancedMenuScanCoroutine(string reason)
        {
            int count = Math.Max(1, Math.Min(10, SourceBasedEnhancedMenuScanBursts.Value));
            float interval = Math.Max(0.03f, Math.Min(0.25f, SourceBasedEnhancedMenuScanInterval.Value));
            yield return null;
            for (int i = 0; i < count; i++)
            {
                RuntimeTextPatcher.ScanQoLTextObjects(reason + "-qol-" + i, Logger, force: true);
                RuntimeTextPatcher.ScanAllKnownTextObjects(reason + "-known-" + i, Logger);
                yield return new WaitForSecondsRealtime(interval);
            }
        }

        private IEnumerator SettingsMenuScanBurstCoroutine(string reason)
        {
            _settingsMenuBurstActive = true;
            // Use the v0.6.4 scan route instead of the v0.9.2 known-only scan.
            // This catches vanilla ESC menu labels by object path (settings/button/keybind) as well as exact dictionary matches.
            for (int i = 0; i < 8; i++)
            {
                RuntimeTextPatcher.ScanQoLTextObjects(reason, Logger, force: true);
                yield return new WaitForSecondsRealtime(0.12f);
            }
            _settingsMenuBurstActive = false;
        }

        internal void RequestScanBurst(string reason)
        {
            if (!EnableTargetedQoLPatches.Value) return;
            float now = Time.unscaledTime;
            if (now < _nextScanAllowed) return;
            _nextScanAllowed = now + Math.Max(0.15f, ScanDebounceSeconds.Value);

            string key = reason ?? "unknown";
            if (_activeBursts.Contains(key)) return;
            StartCoroutine(ScanBurstCoroutine(key));
        }

        private IEnumerator ScanBurstCoroutine(string reason)
        {
            _activeBursts.Add(reason);
            int count = Math.Max(1, BurstScanFrames.Value);
            float interval = Math.Max(0.05f, BurstScanInterval.Value);
            for (int i = 0; i < count; i++)
            {
                RuntimeTextPatcher.ScanQoLTextObjects(reason, Logger, force: false);
                yield return new WaitForSecondsRealtime(interval);
            }
            _activeBursts.Remove(reason);
        }


        private void OnSceneLoadedForSourceEnhancedButtonWatcher(Scene scene, LoadSceneMode mode)
        {
            try
            {
                string sceneName = scene.IsValid() ? scene.name : string.Empty;
                if (EnableSourceEnhancedButtonObjectWatcher != null && EnableSourceEnhancedButtonObjectWatcher.Value &&
                    (sceneName == "SampleScene" || sceneName == "PreGen" || string.IsNullOrEmpty(sceneName)))
                {
                    StartCoroutine(SourceEnhancedButtonObjectWatcherCoroutine("scene-loaded:" + sceneName));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Source EnhancedSettingsButton watcher scene hook failed: " + ex.Message);
            }
        }

        private IEnumerator SourceEnhancedButtonObjectWatcherCoroutine(string reason)
        {
            int serial = ++_sourceButtonWatcherSerial;
            float duration = Math.Max(5f, SourceEnhancedButtonObjectWatcherDuration != null ? SourceEnhancedButtonObjectWatcherDuration.Value : 45f);
            float interval = Math.Max(0.05f, SourceEnhancedButtonObjectWatcherInterval != null ? SourceEnhancedButtonObjectWatcherInterval.Value : 0.25f);
            float end = Time.unscaledTime + duration;
            int attempts = 0;
            Logger.LogInfo($"Source-based EnhancedSettingsButton object watcher #{serial} started: reason={reason}, duration={duration:0.0}s");
            while (Time.unscaledTime <= end)
            {
                attempts++;
                if (TryAttachSourceEnhancedButtonListenerDirect(reason + ":attempt-" + attempts, allowResourcesFallback: (attempts % 8 == 0)))
                {
                    Logger.LogInfo($"Source-based EnhancedSettingsButton object watcher #{serial} attached after {attempts} attempts.");
                    yield break;
                }
                if (attempts == 1 || attempts % 20 == 0)
                {
                    Scene scene = SceneManager.GetActiveScene();
                    string sceneName = scene.IsValid() ? scene.name : string.Empty;
                    Logger.LogInfo($"Source-based EnhancedSettingsButton object watcher #{serial}: waiting; attempt={attempts}, scene={sceneName}");
                }
                yield return new WaitForSecondsRealtime(interval);
            }
            Logger.LogWarning($"Source-based EnhancedSettingsButton object watcher #{serial} expired without finding EnhancedSettingsButton. reason={reason}");
        }

        private bool TryAttachSourceEnhancedButtonListenerDirect(string reason, bool allowResourcesFallback)
        {
            GameObject go = GameObject.Find("Main Camera/Canvas/GammaPanel/EnhancedSettingsButton");
            if (go == null) go = GameObject.Find("Canvas/GammaPanel/EnhancedSettingsButton");
            if (go == null && allowResourcesFallback)
            {
                try
                {
                    foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
                    {
                        if (b == null || b.gameObject == null) continue;
                        if (b.gameObject.name == "EnhancedSettingsButton")
                        {
                            go = b.gameObject;
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("Source EnhancedSettingsButton watcher Resources fallback failed: " + ex.Message);
                }
            }
            if (go == null) return false;
            Button button = go.GetComponent<Button>();
            if (button == null)
            {
                Logger.LogWarning("Source-based EnhancedSettingsButton object watcher found object but no Button: " + RuntimeTextPatcher.DebugPath(go));
                return true;
            }
            SourceEnhancedSettingsButtonListener listener = go.GetComponent<SourceEnhancedSettingsButtonListener>();
            if (listener == null)
            {
                listener = go.AddComponent<SourceEnhancedSettingsButtonListener>();
                button.onClick.AddListener(listener.OnClicked);
                Logger.LogInfo("Source-based EnhancedSettingsButton object watcher attached listener to " + RuntimeTextPatcher.DebugPath(go) + " reason=" + reason);
            }
            else
            {
                Logger.LogInfo("Source-based EnhancedSettingsButton object watcher found existing listener on " + RuntimeTextPatcher.DebugPath(go) + " reason=" + reason);
            }
            return true;
        }


        private void OnSceneLoadedForKrokMpStyleLifecycleShim(Scene scene, LoadSceneMode mode)
        {
            try
            {
                string sceneName = scene.IsValid() ? scene.name : string.Empty;
                if (EnableKrokMpStyleLifecycleShim != null && EnableKrokMpStyleLifecycleShim.Value &&
                    (sceneName == "SampleScene" || sceneName == "PreGen" || string.IsNullOrEmpty(sceneName)))
                {
                    StartCoroutine(KrokMpStyleLifecycleShimCoroutine("scene-loaded:" + sceneName));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("KrokMP-style lifecycle shim scene hook failed: " + ex.Message);
            }
        }

        private IEnumerator KrokMpStyleLifecycleShimCoroutine(string reason)
        {
            int serial = ++_krokMpLifecycleShimSerial;
            float duration = Math.Max(5f, KrokMpStyleLifecycleShimDuration != null ? KrokMpStyleLifecycleShimDuration.Value : 45f);
            float interval = Math.Max(0.05f, KrokMpStyleLifecycleShimInterval != null ? KrokMpStyleLifecycleShimInterval.Value : 0.25f);
            float end = Time.unscaledTime + duration;
            int attempts = 0;
            Logger.LogInfo($"KrokMP-style lifecycle shim #{serial} started: reason={reason}, duration={duration:0.0}s");
            while (Time.unscaledTime <= end)
            {
                attempts++;
                if (TryEnsureKrokMpStyleEnhancedSettingsButton(reason + ":attempt-" + attempts))
                {
                    Logger.LogInfo($"KrokMP-style lifecycle shim #{serial} completed after {attempts} attempts.");
                    yield break;
                }
                if (attempts == 1 || attempts % 20 == 0)
                {
                    Scene scene = SceneManager.GetActiveScene();
                    string sceneName = scene.IsValid() ? scene.name : string.Empty;
                    Logger.LogInfo($"KrokMP-style lifecycle shim #{serial}: waiting for GammaPanel/Button; attempt={attempts}, scene={sceneName}");
                }
                yield return new WaitForSecondsRealtime(interval);
            }
            Logger.LogWarning($"KrokMP-style lifecycle shim #{serial} expired without creating/finding EnhancedSettingsButton. reason={reason}");
        }

        private bool TryEnsureKrokMpStyleEnhancedSettingsButton(string reason)
        {
            try
            {
                GameObject existing = FindEnhancedSettingsButtonObject();
                if (existing != null)
                {
                    AttachKrokMpStyleEnhancedSettingsListener(existing, reason + ":existing");
                    return true;
                }

                GameObject source = FindGammaPanelSourceButton();
                if (source == null) return false;

                GameObject clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                clone.name = "EnhancedSettingsButton";
                try
                {
                    RectTransform rt = clone.GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition += new Vector2(0f, rt.rect.height + 10f);
                }
                catch (Exception ex) { Logger.LogWarning("KrokMP-style lifecycle shim rect setup failed: " + ex.Message); }

                Button button = clone.GetComponent<Button>();
                if (button != null)
                {
                    button.onClick = new Button.ButtonClickedEvent();
                }

                TrySetButtonTextLikeQoL(clone);
                AttachKrokMpStyleEnhancedSettingsListener(clone, reason + ":created");
                RuntimeTextPatcher.ScanAllKnownTextObjects("krokmp-style-created-enhancedsettingsbutton-known", Logger);
                Logger.LogInfo("KrokMP-style lifecycle shim created EnhancedSettingsButton from " + RuntimeTextPatcher.DebugPath(source) + " -> " + RuntimeTextPatcher.DebugPath(clone));
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("KrokMP-style lifecycle shim failed: " + ex.Message);
                return false;
            }
        }

        private static GameObject FindEnhancedSettingsButtonObject()
        {
            GameObject go = GameObject.Find("Main Camera/Canvas/GammaPanel/EnhancedSettingsButton");
            if (go == null) go = GameObject.Find("Canvas/GammaPanel/EnhancedSettingsButton");
            if (go != null) return go;
            try
            {
                foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
                {
                    if (b == null || b.gameObject == null) continue;
                    if (b.gameObject.name == "EnhancedSettingsButton" && b.gameObject.scene.IsValid()) return b.gameObject;
                }
            }
            catch { }
            return null;
        }

        private static GameObject FindGammaPanelSourceButton()
        {
            GameObject source = GameObject.Find("Main Camera/Canvas/GammaPanel/Button");
            if (source == null) source = GameObject.Find("Canvas/GammaPanel/Button");
            if (source != null) return source;
            try
            {
                foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
                {
                    if (b == null || b.gameObject == null) continue;
                    if (b.gameObject.name != "Button") continue;
                    string path = RuntimeTextPatcher.DebugPath(b.gameObject);
                    if (b.gameObject.scene.IsValid() && (path.EndsWith("GammaPanel/Button", StringComparison.Ordinal) || path.Contains("/GammaPanel/Button"))) return b.gameObject;
                }
            }
            catch { }
            return null;
        }

        private static void TrySetButtonTextLikeQoL(GameObject go)
        {
            try
            {
                Component[] comps = go.GetComponentsInChildren<Component>(true);
                foreach (Component c in comps)
                {
                    if (c == null) continue;
                    Type t = c.GetType();
                    if (t.Name.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) < 0 && t.Name.IndexOf("TMP_Text", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    try
                    {
                        Component loc = c.GetComponent("UILocalizer");
                        if (loc != null) UnityEngine.Object.Destroy(loc);
                    }
                    catch { }
                    PropertyInfo prop = t.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(c, "OPTION\nMENU", null);
                        return;
                    }
                }
            }
            catch { }
        }

        private static void AttachKrokMpStyleEnhancedSettingsListener(GameObject go, string reason)
        {
            Button button = go.GetComponent<Button>();
            if (button == null)
            {
                Plugin.Log?.LogWarning("KrokMP-style lifecycle shim found EnhancedSettingsButton but no Button: " + RuntimeTextPatcher.DebugPath(go));
                return;
            }
            var listener = go.GetComponent<KrokMpStyleEnhancedSettingsButtonListener>();
            if (listener == null)
            {
                listener = go.AddComponent<KrokMpStyleEnhancedSettingsButtonListener>();
                button.onClick.AddListener(listener.OnClicked);
                Plugin.Log?.LogInfo("KrokMP-style lifecycle shim listener attached to " + RuntimeTextPatcher.DebugPath(go) + " reason=" + reason);
            }
        }

        private void OnDestroy()
        {
            try { if (EnableSceneLoadedScan != null && EnableSceneLoadedScan.Value) SceneManager.sceneLoaded -= OnSceneLoaded; } catch { }
            try { if (EnableSourceEnhancedButtonObjectWatcher != null && EnableSourceEnhancedButtonObjectWatcher.Value) SceneManager.sceneLoaded -= OnSceneLoadedForSourceEnhancedButtonWatcher; } catch { }
            try { if (EnableKrokMpStyleLifecycleShim != null && EnableKrokMpStyleLifecycleShim.Value) SceneManager.sceneLoaded -= OnSceneLoadedForKrokMpStyleLifecycleShim; } catch { }
            try { _harmony?.UnpatchSelf(); } catch { }
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }

    internal static class Translator
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> PhraseMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private static List<KeyValuePair<string, string>> PhraseRules = new List<KeyValuePair<string, string>>();
        private static readonly List<PlaceholderRule> PlaceholderRules = new List<PlaceholderRule>();
        private static readonly HashSet<string> DumpedMissing = new HashSet<string>(StringComparer.Ordinal);
        public static int Count => Map.Count + PhraseMap.Count;

        public static void LoadTranslations(string pluginPath, ManualLogSource log)
        {
            Map.Clear(); PhraseMap.Clear(); PhraseRules.Clear(); Cache.Clear(); PlaceholderRules.Clear(); DumpedMissing.Clear();
            string dir = Path.Combine(pluginPath, "QoLChinesePatch");
            Directory.CreateDirectory(dir);
            foreach (string file in Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var entries = TinyJsonStringDictionary.Parse(File.ReadAllText(file, Encoding.UTF8));
                    bool isPhraseFile = Path.GetFileName(file).IndexOf("phrase", StringComparison.OrdinalIgnoreCase) >= 0
                                     || Path.GetFileName(file).IndexOf("replacement", StringComparison.OrdinalIgnoreCase) >= 0;
                    foreach (var kv in entries)
                    {
                        if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                        if (isPhraseFile) PhraseMap[kv.Key] = kv.Value;
                        else Map[kv.Key] = kv.Value;
                    }
                    log.LogInfo($"Loaded {entries.Count} {(isPhraseFile ? "phrase replacements" : "exact translations")} from {file}");
                }
                catch (Exception ex) { log.LogWarning($"Failed to load translation file {file}: {ex.Message}"); }
            }
            foreach (var kv in Map)
            {
                var rule = PlaceholderRule.TryCreate(kv.Key, kv.Value);
                if (rule != null) PlaceholderRules.Add(rule);
            }
            PhraseRules = new List<KeyValuePair<string, string>>(PhraseMap);
            PhraseRules.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            log.LogInfo($"Placeholder rules: {PlaceholderRules.Count}; phrase rules: {PhraseRules.Count}");
        }

        public static string Translate(string input)
        {
            if (string.IsNullOrEmpty(input) || (Map.Count == 0 && PhraseRules.Count == 0 && PlaceholderRules.Count == 0)) return input;
            if (Cache.TryGetValue(input, out var cached)) return cached;
            string output = TranslateNoCache(input);
            if (Cache.Count < 16384) Cache[input] = output;
            return output;
        }

        private static string TranslateNoCache(string input)
        {
            if (Map.TryGetValue(input, out var direct)) return direct;
            string trimmed = input.Trim();
            if (trimmed.Length != input.Length && Map.TryGetValue(trimmed, out var trimmedOut))
            {
                int prefixLen = input.Length - input.TrimStart().Length;
                int suffixLen = input.Length - input.TrimEnd().Length;
                return input.Substring(0, prefixLen) + trimmedOut + input.Substring(input.Length - suffixLen);
            }
            foreach (var rule in PlaceholderRules)
            {
                if (rule.TryTranslate(input, out var replaced)) return replaced;
            }

            string phraseOutput = input;
            bool phraseChanged = false;
            foreach (var kv in PhraseRules)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (phraseOutput.IndexOf(kv.Key, StringComparison.Ordinal) < 0) continue;
                phraseOutput = phraseOutput.Replace(kv.Key, kv.Value);
                phraseChanged = true;
            }
            if (phraseChanged)
            {
                // Second-pass exact lookup: useful when long phrase rules or previous phrase rules
                // convert a partially localized sentence into a canonical key. This prevents
                // mixed alerts such as "Please 重启游戏 for changes to take effect!".
                if (Map.TryGetValue(phraseOutput, out var postPhraseDirect)) return postPhraseDirect;
                string phraseTrimmed = phraseOutput.Trim();
                if (phraseTrimmed.Length != phraseOutput.Length && Map.TryGetValue(phraseTrimmed, out var postPhraseTrimmed))
                {
                    int prefixLen = phraseOutput.Length - phraseOutput.TrimStart().Length;
                    int suffixLen = phraseOutput.Length - phraseOutput.TrimEnd().Length;
                    return phraseOutput.Substring(0, prefixLen) + postPhraseTrimmed + phraseOutput.Substring(phraseOutput.Length - suffixLen);
                }
                return phraseOutput;
            }

            return input;
        }

        public static bool TryTranslate(ref string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string newValue = Translate(value);
            if (newValue == value) return false;
            value = newValue;
            return true;
        }

        public static bool IsKnownEnglishText(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (Map.ContainsKey(value)) return true;
            string trimmed = value.Trim();
            if (trimmed.Length != value.Length && Map.ContainsKey(trimmed)) return true;
            foreach (var rule in PlaceholderRules)
                if (rule.Matches(value)) return true;
            foreach (var kv in PhraseRules)
                if (!string.IsNullOrEmpty(kv.Key) && value.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static void DumpMissingCandidate(string text, string source)
        {
            if (!Plugin.DumpCandidateTexts.Value) return;
            if (string.IsNullOrWhiteSpace(text)) return;
            if (!LooksLikeEnglishUiText(text)) return;
            if (!DumpedMissing.Add(text)) return;
            try
            {
                string dir = Path.Combine(Paths.PluginPath, "QoLChinesePatch");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "missing-texts.txt"), EscapeForJson(text) + "\t# " + source + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void DumpEnhancedPanelMissingCandidate(string text, string source)
        {
            try
            {
                if (Plugin.EnableEnhancedSettingsPanelMissingDump == null || !Plugin.EnableEnhancedSettingsPanelMissingDump.Value) return;
                if (string.IsNullOrWhiteSpace(text)) return;
                if (!LooksLikeEnglishUiText(text)) return;
                string key = "enhanced-panel:" + text;
                if (!DumpedMissing.Add(key)) return;
                string dir = Path.Combine(Paths.PluginPath, "QoLChinesePatch");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "missing-enhanced-panel.txt"), EscapeForJson(text) + "\t# " + source + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static bool LooksLikeEnglishUiText(string s)
        {
            if (s.Length < 2 || s.Length > 300) return false;
            int letters = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) letters++;
                if (c >= 0x4e00 && c <= 0x9fff) return false;
            }
            return letters >= 2;
        }

        private static string EscapeForJson(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"") + "\": \"\",";

        private sealed class PlaceholderRule
        {
            private readonly string _prefix, _suffix, _output;
            private PlaceholderRule(string prefix, string suffix, string output) { _prefix = prefix; _suffix = suffix; _output = output; }
            public static PlaceholderRule TryCreate(string key, string value)
            {
                int p = key.IndexOf("{0", StringComparison.Ordinal);
                if (p < 0) return null;
                int end = key.IndexOf('}', p);
                if (end < 0) return null;
                return new PlaceholderRule(key.Substring(0, p), key.Substring(end + 1), value);
            }
            public bool Matches(string input)
            {
                if (!_prefix.Equals(string.Empty) && !input.StartsWith(_prefix, StringComparison.Ordinal)) return false;
                if (!_suffix.Equals(string.Empty) && !input.EndsWith(_suffix, StringComparison.Ordinal)) return false;
                return input.Length >= _prefix.Length + _suffix.Length;
            }
            public bool TryTranslate(string input, out string output)
            {
                output = null;
                if (!Matches(input)) return false;
                string captured = input.Substring(_prefix.Length, input.Length - _prefix.Length - _suffix.Length);
                output = ReplacePlaceholderToken(_output, captured);
                return true;
            }

            private static string ReplacePlaceholderToken(string template, string captured)
            {
                if (string.IsNullOrEmpty(template)) return template;
                var sb = new StringBuilder(template.Length + captured.Length);
                for (int i = 0; i < template.Length; i++)
                {
                    if (template[i] == '{' && i + 1 < template.Length && template[i + 1] == '0')
                    {
                        int end = template.IndexOf('}', i + 2);
                        if (end >= 0)
                        {
                            sb.Append(captured);
                            i = end;
                            continue;
                        }
                    }
                    sb.Append(template[i]);
                }
                return sb.ToString();
            }
        }
    }

    internal static class RuntimeTextPatcher
    {
        private static readonly Dictionary<int, string> RecentlyProcessed = new Dictionary<int, string>();
        private static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private static float ClearTouchedAt;

        public static Type SafeFindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;
            if (TypeCache.TryGetValue(typeName, out var cached)) return cached;

            Type type = null;
            try { type = Type.GetType(typeName, false); } catch { }
            if (type == null && typeName.IndexOf(',') >= 0)
            {
                string plain = typeName.Substring(0, typeName.IndexOf(',')).Trim();
                try { type = Type.GetType(plain, false); } catch { }
            }
            if (type == null)
            {
                string plain = typeName;
                int comma = plain.IndexOf(',');
                if (comma >= 0) plain = plain.Substring(0, comma).Trim();
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        // Assembly.GetType(name,false) does not enumerate/load all types, so it avoids ReflectionTypeLoadException spam from optional KrokMP/Steamworks dependencies.
                        type = asm.GetType(plain, false);
                        if (type != null) break;
                    }
                    catch { }
                }
            }
            TypeCache[typeName] = type;
            return type;
        }

        public static void Apply(Harmony harmony, ManualLogSource log)
        {
            if (Plugin.PatchQoLInternalLocale.Value) PatchLikelyLocaleMethods(harmony, log);
            PatchQoLModuleManagerState(harmony, log);
            if (Plugin.EnableTargetedQoLPatches.Value) PatchKnownQoLUiMethods(harmony, log);
            if (Plugin.EnableQoLMenuLifecycleTrace != null && Plugin.EnableQoLMenuLifecycleTrace.Value) PatchQoLMenuLifecycleTraceMethods(harmony, log);
            if (Plugin.EnableSourceBasedEnhancedMenuFix != null && Plugin.EnableSourceBasedEnhancedMenuFix.Value) PatchSourceBasedEnhancedMenuFix(harmony, log);
            if (Plugin.EnablePreciseEnhancedSettingsButtonHook != null && Plugin.EnablePreciseEnhancedSettingsButtonHook.Value) PatchPreciseEnhancedSettingsButtonHook(harmony, log);
            if (Plugin.EnableEscSettingsMenuWatcher.Value) PatchGameSettingsMenuMethods(harmony, log);
            PatchPlayerCameraAlerts(harmony, log);
            if (Plugin.EnableSafeTextSetterPatch.Value || Plugin.PatchUnitySettersDiagnostic.Value) PatchGlobalTextSetters(harmony, log);
        }

        private static void PatchQoLModuleManagerState(Harmony harmony, ManualLogSource log)
        {
            try
            {
                Type type = SafeFindType("QoL_Unknown.QoLModuleManager");
                if (type == null)
                {
                    log.LogInfo("QoL module registry localizer: QoLModuleManager type not found yet.");
                    return;
                }

                int patched = 0;
                MethodInfo setup = type.GetMethod("SetupRegistry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (setup != null)
                {
                    try
                    {
                        harmony.Patch(setup, postfix: new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixQoLModuleManagerSetupRegistry)));
                        patched++;
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning("QoL module registry localizer could not patch SetupRegistry: " + ex.Message);
                    }
                }

                int changed = LocalizeQoLModuleManager("startup", log);
                log.LogInfo($"QoL module registry localizer ready. patchedMethods={patched}; startupChanged={changed}");
            }
            catch (Exception ex)
            {
                log.LogWarning("QoL module registry localizer failed: " + ex.Message);
            }
        }

        public static void PostfixQoLModuleManagerSetupRegistry()
        {
            try { LocalizeQoLModuleManager("postfix:QoLModuleManager.SetupRegistry", Plugin.Log); } catch { }
        }

        public static int LocalizeQoLModuleManager(string reason, ManualLogSource log)
        {
            int total = 0, changed = 0;
            try
            {
                Type type = SafeFindType("QoL_Unknown.QoLModuleManager");
                if (type == null) return 0;
                FieldInfo modulesField = type.GetField("Modules", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object modulesObj = modulesField != null ? modulesField.GetValue(null) : null;
                IEnumerable modules = modulesObj as IEnumerable;
                if (modules == null) return 0;

                foreach (object module in modules)
                {
                    if (module == null) continue;
                    total++;
                    changed += TranslateStringMember(module, "Name");
                    changed += TranslateStringMember(module, "Description");
                }

                if (changed > 0 || (log != null && reason == "startup"))
                    log?.LogInfo($"QoL module registry localized [{reason}]: modules={total}; changed={changed}");
            }
            catch (Exception ex)
            {
                log?.LogWarning($"QoL module registry localized [{reason}] failed: {ex.Message}");
            }
            return changed;
        }

        private static int TranslateStringMember(object obj, string name)
        {
            try
            {
                if (obj == null) return 0;
                Type t = obj.GetType();
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null && fi.FieldType == typeof(string))
                {
                    string value = fi.GetValue(obj) as string;
                    if (string.IsNullOrEmpty(value)) return 0;
                    string translated = Translator.Translate(value);
                    if (translated == value) return 0;
                    fi.SetValue(obj, translated);
                    return 1;
                }

                PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanRead && pi.CanWrite && pi.PropertyType == typeof(string))
                {
                    string value = pi.GetValue(obj, null) as string;
                    if (string.IsNullOrEmpty(value)) return 0;
                    string translated = Translator.Translate(value);
                    if (translated == value) return 0;
                    pi.SetValue(obj, translated, null);
                    return 1;
                }
            }
            catch { }
            return 0;
        }

        private static void PatchPlayerCameraAlerts(Harmony harmony, ManualLogSource log)
        {
            try
            {
                Type type = SafeFindType("PlayerCamera");
                if (type == null)
                {
                    log.LogInfo("Alert text patch: PlayerCamera type not found.");
                    return;
                }

                int count = 0;
                var prefix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PrefixTranslateStringArguments));
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method == null || method.Name != "DoAlert" || method.ContainsGenericParameters) continue;
                    bool hasStringArg = false;
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        if (parameter.ParameterType == typeof(string))
                        {
                            hasStringArg = true;
                            break;
                        }
                    }
                    if (!hasStringArg) continue;
                    try
                    {
                        harmony.Patch(method, prefix: prefix);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning($"Alert text patch could not patch PlayerCamera.DoAlert overload: {ex.Message}");
                    }
                }
                if (count > 0) log.LogInfo($"Alert text methods patched: {count}");
            }
            catch (Exception ex)
            {
                log.LogWarning("Alert text patching failed: " + ex.Message);
            }
        }

        public static void PrefixTranslateStringArguments(object[] __args)
        {
            try
            {
                if (__args == null) return;
                for (int i = 0; i < __args.Length; i++)
                {
                    string value = __args[i] as string;
                    if (string.IsNullOrEmpty(value)) continue;
                    string translated = value;
                    Translator.TryTranslate(ref translated);
                    if (!ReferenceEquals(translated, value) && translated != value)
                    {
                        __args[i] = translated;
                    }
                }
            }
            catch { }
        }

        private static void PatchGameSettingsMenuMethods(Harmony harmony, ManualLogSource log)
        {
            try
            {
                Type type = SafeFindType("UIMainMenu");
                if (type == null)
                {
                    log.LogInfo("ESC settings menu watcher: UIMainMenu type not found for direct patch; Update watcher remains active.");
                    return;
                }

                string[] methodNames = {
                    "SetOpen", "Open", "Close", "Toggle", "SetTab", "OpenTab", "SelectTab", "SwitchTab",
                    "SetCurrentTab", "ChangeTab", "ShowTab", "Refresh", "RefreshSettings", "RefreshKeybinds",
                    "LoadSettings", "ApplySettings", "UpdateSettingsUI"
                };
                int count = 0;
                var postfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixGameSettingsMenuMethod));
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    if (method.ContainsGenericParameters) continue;
                    bool match = false;
                    foreach (string mn in methodNames)
                    {
                        if (method.Name == mn) { match = true; break; }
                    }
                    if (!match) continue;
                    try { harmony.Patch(method, postfix: postfix); count++; }
                    catch (Exception ex) { log.LogWarning($"Could not patch UIMainMenu.{method.Name}: {ex.Message}"); }
                }
                log.LogInfo($"ESC settings menu direct methods patched: {count}");
            }
            catch (Exception ex)
            {
                log.LogWarning("ESC settings menu method patching failed: " + ex.Message);
            }
        }


        private static void PatchSourceBasedEnhancedMenuFix(Harmony harmony, ManualLogSource log)
        {
            int count = 0;
            try
            {
                var qsm = SafeFindType("QoL_Unknown.QoLSettingsMenu");
                var emc = SafeFindType("QoL_Unknown.EnhancedMenuController");
                var menuPostfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixSourceBasedEnhancedMenuLifecycle));
                var buildPostfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixQoLBuildSettingsUI));

                MethodInfo build = qsm != null ? AccessTools.Method(qsm, "BuildSettingsUI") : null;
                if (build != null)
                {
                    harmony.Patch(build, postfix: buildPostfix);
                    count++;
                }
                else log.LogWarning("Source-based enhanced menu fix: QoLSettingsMenu.BuildSettingsUI not found.");

                if (emc != null)
                {
                    foreach (string methodName in new[] { "OpenMenu", "SetupBindings", "SetupInGameToggle" })
                    {
                        foreach (MethodInfo m in emc.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                        {
                            if (m.Name != methodName || m.ContainsGenericParameters) continue;
                            try { harmony.Patch(m, postfix: menuPostfix); count++; }
                            catch (Exception ex) { log.LogWarning($"Source-based enhanced menu fix could not patch EnhancedMenuController.{m.Name}: {ex.Message}"); }
                        }
                    }
                }
                else log.LogWarning("Source-based enhanced menu fix: EnhancedMenuController type not found.");

                log.LogInfo($"Source-based enhanced menu methods patched: {count}");
            }
            catch (Exception ex)
            {
                log.LogWarning("Source-based enhanced menu fix patching failed: " + ex.Message);
            }
        }

        public static void PostfixQoLBuildSettingsUI(MethodBase __originalMethod)
        {
            try
            {
                InstallSourceEnhancedSettingsButtonListener();
                Plugin.Instance?.RequestSourceBasedEnhancedMenuScan("source-buildsettingsui");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Source-based enhanced menu BuildSettingsUI postfix failed: " + ex.Message);
            }
        }

        public static void PostfixSourceBasedEnhancedMenuLifecycle(MethodBase __originalMethod)
        {
            try
            {
                string reason = "source-" + (__originalMethod != null ? (__originalMethod.DeclaringType?.Name + "." + __originalMethod.Name) : "enhanced-menu");
                Plugin.Instance?.RequestSourceBasedEnhancedMenuScan(reason);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Source-based enhanced menu lifecycle postfix failed: " + ex.Message);
            }
        }

        internal static void InstallSourceEnhancedSettingsButtonListener()
        {
            GameObject go = GameObject.Find("Main Camera/Canvas/GammaPanel/EnhancedSettingsButton");
            if (go == null)
            {
                foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
                {
                    if (b == null || b.gameObject == null) continue;
                    if (b.gameObject.name == "EnhancedSettingsButton") { go = b.gameObject; break; }
                }
            }
            if (go == null)
            {
                Plugin.Log?.LogInfo("Source-based enhanced menu fix: EnhancedSettingsButton not found yet.");
                return;
            }
            Button button = go.GetComponent<Button>();
            if (button == null)
            {
                Plugin.Log?.LogInfo("Source-based enhanced menu fix: EnhancedSettingsButton has no Button component: " + GetPath(go));
                return;
            }
            var listener = go.GetComponent<SourceEnhancedSettingsButtonListener>();
            if (listener == null)
            {
                listener = go.AddComponent<SourceEnhancedSettingsButtonListener>();
                button.onClick.AddListener(listener.OnClicked);
                Plugin.Log?.LogInfo("Source-based enhanced menu fix: listener attached to " + GetPath(go));
            }
        }

        private static void PatchPreciseEnhancedSettingsButtonHook(Harmony harmony, ManualLogSource log)
        {
            int count = 0;
            var postfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixEnhancedSettingsButtonEvent));
            try
            {
                Type buttonType = SafeFindType("UnityEngine.UI.Button, UnityEngine.UI");
                if (buttonType == null)
                {
                    log.LogWarning("Precise enhanced settings button hook: UnityEngine.UI.Button type not found.");
                    return;
                }
                foreach (string methodName in new[] { "Press", "OnPointerClick", "OnSubmit" })
                {
                    MethodInfo method = AccessTools.Method(buttonType, methodName);
                    if (method == null) continue;
                    try { harmony.Patch(method, postfix: postfix); count++; }
                    catch (Exception ex) { log.LogWarning($"Precise enhanced settings button hook failed for Button.{methodName}: {ex.Message}"); }
                }
                log.LogInfo($"Precise enhanced settings button methods patched: {count}");
            }
            catch (Exception ex)
            {
                log.LogWarning("Precise enhanced settings button hook patching failed: " + ex.Message);
            }
        }

        public static void PostfixEnhancedSettingsButtonEvent(object __instance, MethodBase __originalMethod)
        {
            try
            {
                Component comp = __instance as Component;
                if (comp == null) return;
                string path = GetPath(comp.gameObject);
                if (!IsEnhancedSettingsButtonPath(path, comp.gameObject.name)) return;

                string method = __originalMethod != null ? __originalMethod.Name : "Button";
                Plugin.Log?.LogInfo($"Precise enhanced settings button hook fired from {method}: {path}");
                Plugin.Instance?.RequestPreciseEnhancedSettingsButtonScan("precise-enhanced-settings-button");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Precise enhanced settings button hook failed at runtime: " + ex.Message);
            }
        }

        private static bool IsEnhancedSettingsButtonPath(string path, string name)
        {
            string p = (path ?? string.Empty).ToLowerInvariant();
            string n = (name ?? string.Empty).ToLowerInvariant();
            if (n.Contains("enhancedsettingsbutton")) return true;
            if (p.Contains("enhancedsettingsbutton")) return true;
            // KrokMP success trace showed Main Camera/Canvas/GammaPanel/EnhancedSettingsButton.
            if (p.Contains("gammapanel") && (p.Contains("settingsbutton") || p.Contains("enhancedsettings"))) return true;
            return false;
        }

        private static void PatchQoLMenuLifecycleTraceMethods(Harmony harmony, ManualLogSource log)
        {
            int count = 0;
            try
            {
                var prefix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(TraceMenuMethodPrefix));
                var postfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(TraceMenuMethodPostfix));

                foreach (Type type in EnumerateQoLUnknownTypes())
                {
                    if (!IsTraceTargetType(type)) continue;
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (method == null || method.ContainsGenericParameters) continue;
                        if (!IsTraceTargetMethod(method)) continue;
                        try { harmony.Patch(method, prefix: prefix, postfix: postfix); count++; }
                        catch (Exception ex) { log.LogWarning($"Trace patch failed for {type.FullName}.{method.Name}: {ex.Message}"); }
                    }
                }

                foreach (string typeName in new[] { "PreRunScript" })
                {
                    Type type = SafeFindType(typeName);
                    if (type == null) continue;
                    foreach (string methodName in new[] { "Awake", "Start", "OnEnable" })
                    {
                        MethodInfo mi = AccessTools.Method(type, methodName);
                        if (mi == null) continue;
                        try { harmony.Patch(mi, prefix: prefix, postfix: postfix); count++; }
                        catch (Exception ex) { log.LogWarning($"Trace patch failed for {typeName}.{methodName}: {ex.Message}"); }
                    }
                }

                try
                {
                    Type buttonType = SafeFindType("UnityEngine.UI.Button, UnityEngine.UI");
                    MethodInfo press = AccessTools.Method(buttonType, "Press");
                    if (press != null)
                    {
                        harmony.Patch(press, prefix: prefix);
                        count++;
                    }
                }
                catch (Exception ex) { log.LogWarning("Trace patch failed for UnityEngine.UI.Button.Press: " + ex.Message); }

                log.LogInfo($"QoL menu lifecycle trace methods patched: {count}");
            }
            catch (Exception ex)
            {
                log.LogWarning("QoL menu lifecycle trace patching failed: " + ex.Message);
            }
        }

        private static IEnumerable<Type> EnumerateQoLUnknownTypes()
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = string.Empty;
                try { asmName = asm.GetName().Name ?? string.Empty; } catch { }
                if (asmName.IndexOf("QoL", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }
                catch { continue; }
                if (types == null) continue;
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    if (t.Namespace == "QoL_Unknown" || (t.FullName != null && t.FullName.IndexOf("QoL_Unknown", StringComparison.Ordinal) >= 0))
                        yield return t;
                }
            }
        }

        private static bool IsTraceTargetType(Type type)
        {
            if (type == null || type.FullName == null) return false;
            string n = type.FullName;
            return n.Contains("QoLSettingsMenu")
                || n.Contains("EnhancedMenuController")
                || n.Contains("PreGenPatcher")
                || n.Contains("KrokoshaMpCompat")
                || n.Contains("PreRunScript");
        }

        private static bool IsTraceTargetMethod(MethodInfo method)
        {
            string n = method.Name ?? string.Empty;
            if (n == "BuildSettingsUI" || n == "SetupCustomMenu" || n == "OpenMenu" || n == "SetupBindings" ||
                n == "SetupInGameToggle" || n == "RefreshUIValues" || n == "ApplyAllSettings" ||
                n == "ReapplySettingsAcrossFrames" || n == "SyncMainMenuRunSettingsScreen" || n == "EnsureInitialized" ||
                n == "LoadSavedSettings" || n == "RefreshSettingsFromPrefs" || n == "Initialize" ||
                n == "EnsureResolved" || n == "EnsureInitializedForRuntime") return true;
            if (n.Contains("BuildSettingsUI") || n.Contains("b__9_0") || n.Contains("SetupInGameToggle") || n.Contains("OpenMenu")) return true;
            return false;
        }

        private static readonly Dictionary<string, int> TraceCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public static void TraceMenuMethodPrefix(MethodBase __originalMethod, object __instance)
        {
            TraceMenuMethod("ENTER", __originalMethod, __instance, includeStack: false);
        }

        public static void TraceMenuMethodPostfix(MethodBase __originalMethod, object __instance)
        {
            TraceMenuMethod("EXIT", __originalMethod, __instance, includeStack: true);
        }

        private static void TraceMenuMethod(string phase, MethodBase method, object instance, bool includeStack)
        {
            try
            {
                if (method == null || Plugin.Log == null) return;
                string key = (method.DeclaringType != null ? method.DeclaringType.FullName : "<null>") + "." + method.Name + ":" + phase;
                int n;
                TraceCounts.TryGetValue(key, out n);
                n++;
                TraceCounts[key] = n;
                if (n > 8) return;

                string scene = SceneManager.GetActiveScene().IsValid() ? SceneManager.GetActiveScene().name : "<none>";
                string inst = DescribeInstance(instance);
                string textSummary = TextObjectSummary();
                Plugin.Log.LogInfo($"[QOL-MENU-TRACE] {phase} #{n} {key} scene={scene} t={Time.realtimeSinceStartup:F2} instance={inst} {textSummary}");

                if (includeStack && Plugin.EnableTraceStackForKeyMethods != null && Plugin.EnableTraceStackForKeyMethods.Value && ShouldPrintStack(method, n))
                {
                    string stack = new System.Diagnostics.StackTrace(2, false).ToString();
                    Plugin.Log.LogInfo($"[QOL-MENU-TRACE-STACK] {key}\n{TrimStack(stack, 18)}");
                }
            }
            catch { }
        }

        private static bool ShouldPrintStack(MethodBase method, int count)
        {
            if (count > 2) return false;
            string name = method.Name ?? string.Empty;
            string type = method.DeclaringType != null ? method.DeclaringType.FullName : string.Empty;
            return name == "BuildSettingsUI" || name.Contains("b__9_0") || name == "OpenMenu" || name == "SetupInGameToggle" ||
                   name == "SetupCustomMenu" || name == "Press" || type.Contains("PreGenPatcher") || type.Contains("PreRunScript");
        }

        private static string DescribeInstance(object instance)
        {
            if (instance == null) return "<static/null>";
            try
            {
                Component c = instance as Component;
                if (c != null) return GetPath(c.gameObject);
                GameObject go = instance as GameObject;
                if (go != null) return GetPath(go);
                return instance.GetType().FullName;
            }
            catch { return "<describe-failed>"; }
        }

        private static string TextObjectSummary()
        {
            try
            {
                int total = 0, nonEmpty = 0, known = 0;
                CountTextType("UnityEngine.UI.Text, UnityEngine.UI", ref total, ref nonEmpty, ref known);
                CountTextType("TMPro.TMP_Text, Unity.TextMeshPro", ref total, ref nonEmpty, ref known);
                return $"texts=total:{total},nonEmpty:{nonEmpty},known:{known}";
            }
            catch { return "texts=<count-failed>"; }
        }

        private static void CountTextType(string typeName, ref int total, ref int nonEmpty, ref int known)
        {
            Type type = SafeFindType(typeName);
            if (type == null) return;
            PropertyInfo prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanRead) return;
            UnityEngine.Object[] objs = Resources.FindObjectsOfTypeAll(type);
            total += objs.Length;
            foreach (var obj in objs)
            {
                if (obj == null) continue;
                string value = prop.GetValue(obj, null) as string;
                if (string.IsNullOrEmpty(value)) continue;
                nonEmpty++;
                if (Translator.IsKnownEnglishText(value)) known++;
            }
        }

        private static string TrimStack(string stack, int maxLines)
        {
            if (string.IsNullOrEmpty(stack)) return string.Empty;
            string[] lines = stack.Replace("\r\n", "\n").Split('\n');
            if (lines.Length <= maxLines) return stack;
            return string.Join("\n", lines, 0, maxLines) + "\n...";
        }

        private static void PatchKnownQoLUiMethods(Harmony harmony, ManualLogSource log)
        {
            string[] typeNames = {
                "QoL_Unknown.QoLSettingsMenu",
                "QoL_Unknown.EnhancedMenuController",
                "QoL_Unknown.PreGenPatcher",
                "QoL_Unknown.SaveMenuPatcher",
                "QoL_Unknown.SavePanelPatcher",
                "QoL_Unknown.InventorySortButtonPatcher",
                "QoL_Unknown.BulkCraftingPatcher",
                "QoL_Unknown.QuickStashPatcher",
                "QoL_Unknown.CraftingPreviewPatcher",
                "QoL_Unknown.VolumeSliderPatcher",
                "QoL_Unknown.AudioSafetyPatcher",
                "QoL_Unknown.ControllerPatcher",
                "QoL_Unknown.KeybindsPatcher",
                "QoL_Unknown.LayerModifierPatcher",
                "QoL_Unknown.SeededRunPatcher",
                "QoL_Unknown.ExitConfirmationPatcher",
                "QoL_Unknown.TimerMenuPatcher",
                "QoL_Unknown.SoundCannonIndicator"
            };
            string[] methodNames = {
                "BuildSettingsUI", "SetupCustomMenu", "OpenMenu", "RefreshToggle", "RefreshInGameToggle",
                "SetupToggle", "SetupInGameToggle", "ApplyModdedBetaBuildText", "ShowRestartAlert",
                "CreateQuickLoadButtons", "CreateQuickLoadButtonsDelayed", "RefreshRecipeListNextFrame",
                "ApplyAllSettings", "LoadSavedSettings", "RefreshSettingsFromPrefs", "ReapplySettingsAcrossFrames",
                "SyncWithEnhancedMenu", "SyncMainMenuRunSettingsScreen", "CreateTextButton", "CreateIconButton",
                "SetButton", "UpdateButtons", "EnsureLabelCanvas", "FormatUiScaleLabel"
            };
            int count = 0;
            var postfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixQoLUiMethod));
            foreach (string typeName in typeNames)
            {
                Type type = SafeFindType(typeName);
                if (type == null) continue;
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                {
                    bool match = false;
                    foreach (string mn in methodNames)
                    {
                        if (method.Name == mn) { match = true; break; }
                    }
                    if (!match) continue;
                    if (method.ContainsGenericParameters) continue;
                    try { harmony.Patch(method, postfix: postfix); count++; }
                    catch (Exception ex) { log.LogWarning($"Could not patch {type.FullName}.{method.Name}: {ex.Message}"); }
                }
            }
            log.LogInfo($"Targeted QoL UI methods patched: {count}");
        }

        private static void PatchLikelyLocaleMethods(Harmony harmony, ManualLogSource log)
        {
            int count = 0;
            var postfix = new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PostfixStringResult));
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string asmName = asm.GetName().Name ?? string.Empty;
                if (asmName.IndexOf("QoL", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (Type type in types)
                {
                    if (type == null || type.Namespace != "QoL_Unknown") continue;
                    string n = type.Name.ToLowerInvariant();
                    if (!(n.Contains("locale") || n.Contains("localization") || n.Contains("translation"))) continue;
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    {
                        if (method.ReturnType != typeof(string)) continue;
                        if (method.ContainsGenericParameters) continue;
                        try { harmony.Patch(method, postfix: postfix); count++; }
                        catch { }
                    }
                }
            }
            if (count > 0) log.LogInfo($"QoL internal locale string methods patched: {count}");
        }

        private static void PatchGlobalTextSetters(Harmony harmony, ManualLogSource log)
        {
            PatchStringSetter(harmony, "UnityEngine.UI.Text, UnityEngine.UI", "text", log);
            PatchStringSetter(harmony, "TMPro.TMP_Text, Unity.TextMeshPro", "text", log);
        }

        private static void PatchStringSetter(Harmony harmony, string typeName, string propName, ManualLogSource log)
        {
            try
            {
                Type type = SafeFindType(typeName);
                MethodInfo setter = type?.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance)?.GetSetMethod();
                if (setter == null) return;
                harmony.Patch(setter, prefix: new HarmonyMethod(typeof(RuntimeTextPatcher), nameof(PrefixValue)));
                log.LogInfo($"Diagnostic patched setter: {typeName}.{propName}");
            }
            catch (Exception ex) { log.LogWarning($"Patch setter failed for {typeName}.{propName}: {ex.Message}"); }
        }

        public static void PostfixQoLUiMethod(MethodBase __originalMethod)
        {
            string reason = __originalMethod?.DeclaringType?.Name + "." + __originalMethod?.Name;
            Plugin.Instance?.RequestScanBurst(reason);
        }

        public static void PostfixGameSettingsMenuMethod(MethodBase __originalMethod)
        {
            string reason = __originalMethod?.DeclaringType?.Name + "." + __originalMethod?.Name;
            Plugin.Instance?.RequestSettingsMenuScanBurst(reason);
        }

        public static void PostfixStringResult(ref string __result)
        {
            Translator.TryTranslate(ref __result);
        }

        public static void PrefixValue(ref string value)
        {
            // Diagnostic mode only. Very cheap: dictionary/cache only, no scan.
            Translator.TryTranslate(ref value);
        }

        public static void ScanAllKnownTextObjects(string reason, ManualLogSource log)
        {
            int total = 0, nonEmpty = 0, known = 0, changed = 0;
            changed += ScanAllKnownTextType("UnityEngine.UI.Text, UnityEngine.UI", reason, log, ref total, ref nonEmpty, ref known);
            changed += ScanAllKnownTextType("TMPro.TMP_Text, Unity.TextMeshPro", reason, log, ref total, ref nonEmpty, ref known);
            log.LogInfo($"Force text scan [{reason}]: total={total}, nonEmpty={nonEmpty}, known={known}, changed={changed}");
        }

        private static int ScanAllKnownTextType(string typeName, string reason, ManualLogSource log, ref int total, ref int nonEmpty, ref int known)
        {
            int changed = 0;
            try
            {
                Type type = SafeFindType(typeName);
                if (type == null)
                {
                    log.LogWarning($"Force text scan [{reason}]: type not found: {typeName}");
                    return 0;
                }
                PropertyInfo prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead || !prop.CanWrite)
                {
                    log.LogWarning($"Force text scan [{reason}]: text property unavailable: {typeName}");
                    return 0;
                }
                UnityEngine.Object[] objs = Resources.FindObjectsOfTypeAll(type);
                total += objs.Length;
                foreach (var obj in objs)
                {
                    if (obj == null) continue;
                    string value = prop.GetValue(obj, null) as string;
                    if (string.IsNullOrEmpty(value)) continue;
                    nonEmpty++;
                    if (!Translator.IsKnownEnglishText(value)) continue;
                    known++;
                    string newValue = Translator.Translate(value);
                    if (newValue == value) continue;
                    prop.SetValue(obj, newValue, null);
                    changed++;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"Force text scan [{reason}] failed for {typeName}: {ex.Message}");
            }
            return changed;
        }

        public static void ScanQoLTextObjects(string reason, ManualLogSource log, bool force)
        {
            if (Time.unscaledTime > ClearTouchedAt)
            {
                RecentlyProcessed.Clear();
                ClearTouchedAt = Time.unscaledTime + 60f;
            }
            int changed = 0, seen = 0;
            changed += ScanTextType("UnityEngine.UI.Text, UnityEngine.UI", reason, log, force, ref seen);
            changed += ScanTextType("TMPro.TMP_Text, Unity.TextMeshPro", reason, log, force, ref seen);
            if (changed > 0) log.LogInfo($"QoL text scan [{reason}] translated {changed}/{seen} text objects.");
        }

        private static int ScanTextType(string typeName, string reason, ManualLogSource log, bool force, ref int seen)
        {
            int changed = 0;
            try
            {
                Type type = SafeFindType(typeName);
                if (type == null) return 0;
                PropertyInfo prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead || !prop.CanWrite) return 0;
                UnityEngine.Object[] objs = Resources.FindObjectsOfTypeAll(type);
                foreach (var obj in objs)
                {
                    if (obj == null) continue;
                    int id = obj.GetInstanceID();
                    string value = prop.GetValue(obj, null) as string;
                    if (string.IsNullOrEmpty(value)) continue;
                    if (!force && RecentlyProcessed.TryGetValue(id, out var oldValue) && oldValue == value) continue;
                    Component comp = obj as Component;
                    string path = comp != null ? GetPath(comp.gameObject) : obj.name;
                    bool known = Translator.IsKnownEnglishText(value);
                    bool likelyQoL = known || IsLikelyQoLObjectPath(path) || IsLikelyQoLText(value);
                    if (!likelyQoL) continue;
                    seen++;
                    string newValue = Translator.Translate(value);
                    if (newValue != value)
                    {
                        prop.SetValue(obj, newValue, null);
                        changed++;
                        RecentlyProcessed[id] = newValue;
                    }
                    else
                    {
                        RecentlyProcessed[id] = value;
                        Translator.DumpMissingCandidate(value, reason + " | " + path);
                    }
                }
            }
            catch (Exception ex) { log.LogWarning($"QoL text scan failed for {typeName}: {ex.Message}"); }
            return changed;
        }

        private static bool IsLikelyQoLObjectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string s = path.ToLowerInvariant();
            return s.Contains("qol") || s.Contains("setting") || s.Contains("settings") || s.Contains("runsettings") ||
                   s.Contains("save") || s.Contains("seed") || s.Contains("slot") || s.Contains("craft") || s.Contains("recipe") ||
                   s.Contains("sort") || s.Contains("stash") || s.Contains("audio") || s.Contains("volume") ||
                   s.Contains("controller") || s.Contains("keybind") || s.Contains("layer") || s.Contains("timer") ||
                   s.Contains("dropdown") || s.Contains("button") || s.Contains("volumewarning") || s.Contains("gamma");
        }

        private static bool IsLikelyQoLText(string text)
        {
            string s = text.ToLowerInvariant();
            return s.Contains("qol") || s.Contains("quick stash") || s.Contains("bulk craft") || s.Contains("save slot") ||
                   s.Contains("world seed") || s.Contains("audio safety") || s.Contains("sort button") ||
                   s.Contains("controller") || s.Contains("keybind") || s.Contains("layer modifier") ||
                   s.Contains("crafting preview") || s.Contains("volume slider") || s.Contains("autosave") ||
                   s.Contains("run settings") || s.Contains("volume warning") || s.Contains("run time") ||
                   s.Contains("deep") || s.Contains("select entity") || s.Contains("select...");
        }

        private static string GetPath(GameObject go)
        {
            if (go == null) return string.Empty;
            var names = new List<string>();
            Transform t = go.transform;
            int limit = 0;
            while (t != null && limit++ < 32)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }


        public static GameObject FindActiveEnhancedSettingsPanelRoot()
        {
            try
            {
                foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go == null) continue;
                    string name = go.name ?? string.Empty;
                    if (!name.StartsWith("EnhancedSettingsPanel", StringComparison.Ordinal)) continue;
                    if (!go.activeInHierarchy) continue;
                    return go;
                }
            }
            catch { }
            return null;
        }

        public static string MakeTextFingerprint(GameObject root)
        {
            if (root == null) return string.Empty;
            var sb = new StringBuilder();
            try
            {
                AppendTextFingerprint(root, "UnityEngine.UI.Text, UnityEngine.UI", sb);
                AppendTextFingerprint(root, "TMPro.TMP_Text, Unity.TextMeshPro", sb);
            }
            catch { }
            return sb.ToString();
        }

        private static void AppendTextFingerprint(GameObject root, string typeName, StringBuilder sb)
        {
            Type type = SafeFindType(typeName);
            if (type == null) return;
            PropertyInfo prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanRead) return;
            foreach (var obj in root.GetComponentsInChildren(type, true))
            {
                if (obj == null) continue;
                string value = prop.GetValue(obj, null) as string;
                if (string.IsNullOrEmpty(value)) continue;
                sb.Append(value).Append('|');
            }
        }

        public static void ScanKnownTextUnder(GameObject root, string reason, ManualLogSource log)
        {
            int total = 0, nonEmpty = 0, known = 0, changed = 0;
            changed += ScanKnownTextUnderType(root, "UnityEngine.UI.Text, UnityEngine.UI", reason, log, ref total, ref nonEmpty, ref known);
            changed += ScanKnownTextUnderType(root, "TMPro.TMP_Text, Unity.TextMeshPro", reason, log, ref total, ref nonEmpty, ref known);
            log.LogInfo($"EnhancedSettingsPanel localizer [{reason}]: root={DebugPath(root)}, total={total}, nonEmpty={nonEmpty}, known={known}, changed={changed}");
        }

        private static int ScanKnownTextUnderType(GameObject root, string typeName, string reason, ManualLogSource log, ref int total, ref int nonEmpty, ref int known)
        {
            int changed = 0;
            try
            {
                if (root == null) return 0;
                Type type = SafeFindType(typeName);
                if (type == null) return 0;
                PropertyInfo prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead || !prop.CanWrite) return 0;
                foreach (var obj in root.GetComponentsInChildren(type, true))
                {
                    if (obj == null) continue;
                    total++;
                    string value = prop.GetValue(obj, null) as string;
                    if (string.IsNullOrEmpty(value)) continue;
                    nonEmpty++;
                    Component comp = obj as Component;
                    string path = comp != null ? DebugPath(comp.gameObject) : obj.ToString();
                    bool knownText = Translator.IsKnownEnglishText(value);
                    if (!knownText)
                    {
                        // Keep this scoped to the real QoL menu subtree. It covers exact dictionary entries and phrase rules.
                        string maybe = Translator.Translate(value);
                        if (maybe == value)
                        {
                            Translator.DumpEnhancedPanelMissingCandidate(value, reason + " | " + path);
                            continue;
                        }
                    }
                    known++;
                    string newValue = Translator.Translate(value);
                    if (newValue == value)
                    {
                        Translator.DumpEnhancedPanelMissingCandidate(value, reason + " | " + path);
                        continue;
                    }
                    prop.SetValue(obj, newValue, null);
                    changed++;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning($"EnhancedSettingsPanel localizer [{reason}] failed for {typeName}: {ex.Message}");
            }
            return changed;
        }

        public static void InvokeQoLEnhancedOpenMenu()
        {
            try
            {
                Type emc = SafeFindType("QoL_Unknown.EnhancedMenuController");
                if (emc == null)
                {
                    Plugin.Log?.LogWarning("KrokMP-style lifecycle shim cannot find QoL_Unknown.EnhancedMenuController.");
                    return;
                }
                MethodInfo ensure = AccessTools.Method(emc, "EnsureInitialized");
                try { ensure?.Invoke(null, null); } catch { }
                FieldInfo instField = AccessTools.Field(emc, "Instance");
                object instance = instField != null ? instField.GetValue(null) : null;
                if (instance == null)
                {
                    MethodInfo init = AccessTools.Method(emc, "Initialize");
                    try { init?.Invoke(null, new object[] { null }); } catch { }
                    instance = instField != null ? instField.GetValue(null) : null;
                }
                if (instance == null)
                {
                    Plugin.Log?.LogWarning("KrokMP-style lifecycle shim could not get EnhancedMenuController.Instance.");
                    return;
                }
                MethodInfo open = AccessTools.Method(emc, "OpenMenu");
                if (open == null)
                {
                    Plugin.Log?.LogWarning("KrokMP-style lifecycle shim cannot find EnhancedMenuController.OpenMenu.");
                    return;
                }
                open.Invoke(instance, null);
                Plugin.Log?.LogInfo("KrokMP-style lifecycle shim invoked EnhancedMenuController.OpenMenu().");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("KrokMP-style lifecycle shim InvokeQoLEnhancedOpenMenu failed: " + ex.Message);
            }
        }

        public static string DebugPath(GameObject go)
        {
            return GetPath(go);
        }
    }


    internal sealed class KrokMpStyleEnhancedSettingsButtonListener : MonoBehaviour
    {
        public void OnClicked()
        {
            try
            {
                Plugin.Log?.LogInfo("KrokMP-style lifecycle shim EnhancedSettingsButton onClick fired from " + RuntimeTextPatcher.DebugPath(gameObject));
                RuntimeTextPatcher.InvokeQoLEnhancedOpenMenu();
                Plugin.Instance?.RequestSourceBasedEnhancedMenuScan("krokmp-style-enhancedsettingsbutton-onclick");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("KrokMP-style lifecycle shim EnhancedSettingsButton listener failed: " + ex.Message);
            }
        }
    }

    internal sealed class SourceEnhancedSettingsButtonListener : MonoBehaviour
    {
        public void OnClicked()
        {
            try
            {
                Plugin.Log?.LogInfo("Source-based enhanced menu fix: EnhancedSettingsButton onClick fired from " + RuntimeTextPatcher.DebugPath(gameObject));
                Plugin.Instance?.RequestSourceBasedEnhancedMenuScan("source-enhancedsettingsbutton-onclick");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("Source-based enhanced settings button listener failed: " + ex.Message);
            }
        }
    }

    internal static class TinyJsonStringDictionary
    {
        public static Dictionary<string, string> Parse(string json)
        {
            int i = 0;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            SkipWs(json, ref i); Expect(json, ref i, '{');
            while (true)
            {
                SkipWs(json, ref i);
                if (Peek(json, i) == '}') { i++; break; }
                string key = ReadString(json, ref i);
                SkipWs(json, ref i); Expect(json, ref i, ':'); SkipWs(json, ref i);
                string value = ReadString(json, ref i);
                result[key] = value;
                SkipWs(json, ref i);
                char c = Peek(json, i);
                if (c == ',') { i++; continue; }
                if (c == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' at position " + i);
            }
            return result;
        }
        private static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';
        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        private static void Expect(string s, ref int i, char c) { if (Peek(s, i) != c) throw new FormatException($"Expected '{c}' at position {i}"); i++; }
        private static string ReadString(string s, ref int i)
        {
            Expect(s, ref i, '"');
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("Invalid escape at end of JSON string");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Invalid unicode escape");
                        string hex = s.Substring(i, 4);
                        sb.Append((char)Convert.ToInt32(hex, 16));
                        i += 4;
                        break;
                    default: throw new FormatException("Unsupported escape \\" + e + " at position " + i);
                }
            }
            throw new FormatException("Unterminated JSON string");
        }
    }
}
