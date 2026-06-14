using BepInEx;
using BepInEx.Logging;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace QoLChinesePatchBootstrap
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class BootstrapPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "aaa.casualtiesunknown.qolchinesepatch.bootstrap";
        public const string PluginName = "QoL Unknown Chinese Patch Bootstrap";
        public const string PluginVersion = "0.10.14";

        private static ManualLogSource Log;
        private static Harmony _harmony;
        private static bool _bootstrapSettersPatched;
        private static bool _guiPatched;
        private static bool _probeCreated;
        private static bool _enhancedButtonWatcherCreated;
        private static bool _bootstrapAutoPrimeCompleted;
        private static bool _sceneHandlerRegistered;
        private static EnhancedSettingsButtonRescueWatcher _enhancedButtonWatcher;

        private void Awake()
        {
            Log = Logger;
            BootstrapTranslator.Load(Paths.PluginPath, Logger);
            try
            {
                if (_harmony == null) _harmony = new Harmony(PluginGuid + ".hiddendisabledtrigger");
                PatchBootstrapTextSetters(Logger);
                PatchGuiTextMethods(Logger);
                EnsureHiddenDisabledButtonTrigger(Logger);
                EnsureEnhancedSettingsButtonRescueWatcher(Logger);
                RegisterSceneReprimeHandler(Logger);
                Logger.LogInfo("Hidden disabled-button trigger loaded. It clones PreRunScript.instance/Button (11), hides it, starts an independent EnhancedSettingsButton rescue watcher, and primes EnhancedMenu translation once per menu lifecycle without double-toggling QoL buttons. KrokMP compatibility: no KROKMP_* object names are created and real KrokMP objects are not touched.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Hidden disabled-button trigger initialization failed: " + ex.Message);
            }
        }

        private static void EnsureHiddenDisabledButtonTrigger(ManualLogSource logger)
        {
            if (_probeCreated) return;
            try
            {
                GameObject go = new GameObject("QoLChinesePatch_HiddenDisabledButtonTrigger");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<HiddenDisabledButtonTrigger>();
                _probeCreated = true;
                logger.LogInfo("Hidden disabled-button trigger component created.");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Hidden disabled-button trigger creation failed: " + ex.Message);
            }
        }

        private static void EnsureEnhancedSettingsButtonRescueWatcher(ManualLogSource logger)
        {
            if (_enhancedButtonWatcherCreated) return;
            try
            {
                GameObject go = new GameObject("QoLChinesePatch_EnhancedSettingsButtonRescueWatcher");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _enhancedButtonWatcher = go.AddComponent<EnhancedSettingsButtonRescueWatcher>();
                _enhancedButtonWatcherCreated = true;
                logger.LogInfo("EnhancedSettingsButton rescue watcher component created.");
            }
            catch (Exception ex)
            {
                logger.LogWarning("EnhancedSettingsButton rescue watcher creation failed: " + ex.Message);
            }
        }

        private static void RegisterSceneReprimeHandler(ManualLogSource logger)
        {
            if (_sceneHandlerRegistered) return;
            try
            {
                SceneManager.sceneLoaded += OnSceneLoadedForReprime;
                _sceneHandlerRegistered = true;
                logger.LogInfo("Bootstrap scene re-prime handler registered. EnhancedMenu auto-prime can run again after scene/menu recreation.");
            }
            catch (Exception ex)
            {
                logger.LogWarning("Bootstrap scene re-prime handler registration failed: " + ex.Message);
            }
        }

        private static void OnSceneLoadedForReprime(Scene scene, LoadSceneMode mode)
        {
            try
            {
                _bootstrapAutoPrimeCompleted = false;
                if (_enhancedButtonWatcher != null)
                {
                    _enhancedButtonWatcher.Rearm("scene-loaded:" + scene.name);
                }
                Log?.LogInfo("Bootstrap scene re-prime armed for scene=" + scene.name + "; mode=" + mode);
            }
            catch (Exception ex)
            {
                Log?.LogWarning("Bootstrap scene re-prime failed: " + ex.Message);
            }
        }

        private sealed class EnhancedSettingsButtonRescueWatcher : MonoBehaviour
        {
            private int _attempts;
            private float _nextAttempt;
            private float _startedAt;
            private bool _completed;
            private bool _expiredLogged;
            private string _rearmReason = "startup";

            private void Awake()
            {
                _startedAt = Time.realtimeSinceStartup;
            }

            public void Rearm(string reason)
            {
                _attempts = 0;
                _nextAttempt = 0f;
                _startedAt = Time.realtimeSinceStartup;
                _completed = false;
                _expiredLogged = false;
                _rearmReason = reason ?? "unknown";
            }

            private void Update()
            {
                if (_completed) return;
                float now = Time.realtimeSinceStartup;
                if (now - _startedAt > 75f)
                {
                    if (!_expiredLogged)
                    {
                        _expiredLogged = true;
                        Log?.LogWarning("EnhancedSettingsButton rescue watcher expired after attempts=" + _attempts + "; reason=" + _rearmReason);
                    }
                    _completed = true;
                    return;
                }
                if (now < _nextAttempt) return;
                _nextAttempt = now + (_attempts < 20 ? 0.25f : 0.75f);
                _attempts++;

                try
                {
                    if (TryEnsureEnhancedSettingsButton("bootstrap-rescue-attempt-" + _attempts))
                    {
                        _completed = true;
                        Log?.LogInfo("EnhancedSettingsButton rescue watcher completed after attempts=" + _attempts + "; reason=" + _rearmReason);
                    }
                    else if (_attempts == 1 || _attempts == 20 || _attempts == 60)
                    {
                        Log?.LogInfo("EnhancedSettingsButton rescue watcher: waiting for Main Camera/Canvas/GammaPanel/Button; attempts=" + _attempts + "; reason=" + _rearmReason);
                    }
                }
                catch (Exception ex)
                {
                    if (_attempts <= 3) Log?.LogWarning("EnhancedSettingsButton rescue watcher failed: " + ex.Message);
                }
            }

            private static bool TryEnsureEnhancedSettingsButton(string reason)
            {
                GameObject existing = FindEnhancedSettingsButton();
                if (existing != null)
                {
                    AttachListener(existing, reason + ":existing", false);
                    return true;
                }

                GameObject source = GameObject.Find("Main Camera/Canvas/GammaPanel/Button");
                if (source == null) source = GameObject.Find("Canvas/GammaPanel/Button");
                if (source == null) return false;

                GameObject clone = UnityEngine.Object.Instantiate<GameObject>(source, source.transform.parent);
                clone.name = "EnhancedSettingsButton";
                try
                {
                    RectTransform rt = clone.GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition += new Vector2(0f, rt.rect.height + 10f);
                }
                catch { }

                try
                {
                    Button button = clone.GetComponent<Button>();
                    if (button != null) button.onClick = new Button.ButtonClickedEvent();
                }
                catch { }

                TrySetButtonText(clone, "OPTION\nMENU");
                AttachListener(clone, reason + ":created", true);
                BootstrapTextScanner.ScanAllKnownTextObjects("bootstrap-created-enhancedsettingsbutton", Log);
                Log?.LogInfo("EnhancedSettingsButton rescue watcher created button from " + GetPath(source) + " -> " + GetPath(clone));
                return true;
            }

            private static GameObject FindEnhancedSettingsButton()
            {
                GameObject go = GameObject.Find("Main Camera/Canvas/GammaPanel/EnhancedSettingsButton");
                if (go == null) go = GameObject.Find("Canvas/GammaPanel/EnhancedSettingsButton");
                if (go != null) return go;
                try
                {
                    foreach (Button b in Resources.FindObjectsOfTypeAll<Button>())
                    {
                        if (b != null && b.gameObject != null && b.gameObject.name == "EnhancedSettingsButton" && b.gameObject.scene.IsValid()) return b.gameObject;
                    }
                }
                catch { }
                return null;
            }

            private static void AttachListener(GameObject go, string reason, bool bootstrapCreatedButton)
            {
                if (go == null) return;
                Button button = go.GetComponent<Button>();
                if (button == null) return;

                // Critical parity with KrokMP: when QoL has already created EnhancedSettingsButton,
                // do NOT add another onClick listener. QoL's original listener already calls
                // EnhancedMenuController.OpenMenu(); adding ours causes a double toggle
                // (open -> close), which is exactly the "flash then disappear / no reaction" bug.
                if (bootstrapCreatedButton)
                {
                    var listener = go.GetComponent<EnhancedSettingsButtonBootstrapListener>();
                    if (listener == null)
                    {
                        listener = go.AddComponent<EnhancedSettingsButtonBootstrapListener>();
                        button.onClick.AddListener(listener.OnClicked);
                        Log?.LogInfo("EnhancedSettingsButton rescue watcher attached bootstrap click listener to created button " + GetPath(go) + " reason=" + reason);
                    }
                }
                else
                {
                    Log?.LogInfo("EnhancedSettingsButton rescue watcher found existing QoL button; leaving original onClick untouched to avoid double-toggle. reason=" + reason);
                }

                var autoPrime = go.GetComponent<EnhancedSettingsButtonBootstrapAutoPrime>();
                if (autoPrime == null)
                {
                    autoPrime = go.AddComponent<EnhancedSettingsButtonBootstrapAutoPrime>();
                }
                autoPrime.Arm(reason);
                Log?.LogInfo("EnhancedSettingsButton bootstrap auto-prime armed on " + GetPath(go) + " reason=" + reason);
            }

            private static void TrySetButtonText(GameObject go, string value)
            {
                try
                {
                    var tmps = go.GetComponentsInChildren<Component>(true);
                    foreach (var c in tmps)
                    {
                        if (c == null) continue;
                        Type t = c.GetType();
                        if (t.FullName != "TMPro.TextMeshProUGUI" && t.FullName != "TMPro.TMP_Text") continue;
                        PropertyInfo prop = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                        if (prop == null || !prop.CanWrite) continue;
                        prop.SetValue(c, BootstrapTranslator.Translate(value), null);
                        Component loc = c.GetComponent("UILocalizer");
                        if (loc != null) UnityEngine.Object.Destroy(loc);
                    }
                }
                catch { }
            }

            private static string GetPath(GameObject go)
            {
                if (go == null) return "<null>";
                try
                {
                    List<string> parts = new List<string>();
                    Transform t = go.transform;
                    while (t != null)
                    {
                        parts.Add(t.name);
                        t = t.parent;
                    }
                    parts.Reverse();
                    return string.Join("/", parts.ToArray());
                }
                catch { return go.name; }
            }
        }

        private sealed class EnhancedSettingsButtonBootstrapAutoPrime : MonoBehaviour
        {
            private float _startAt;
            private float _nextAttempt;
            private int _attempts;
            private bool _running;
            private string _reason;

            public void Arm(string reason)
            {
                _reason = reason ?? "unknown";
                _startAt = Time.realtimeSinceStartup;
                _nextAttempt = _startAt + 0.75f;
                _attempts = 0;
                _running = false;
            }

            private void Update()
            {
                if (_bootstrapAutoPrimeCompleted || _running) return;
                float now = Time.realtimeSinceStartup;
                if (now < _nextAttempt) return;
                _attempts++;
                _nextAttempt = now + (_attempts < 10 ? 0.5f : 1.0f);

                if (!IsQoLEnhancedControllerReady())
                {
                    if (_attempts == 1 || _attempts == 10 || _attempts == 30)
                        Log?.LogInfo("EnhancedSettingsButton bootstrap auto-prime waiting for QoL controller; attempts=" + _attempts + ", reason=" + _reason);
                    if (now - _startAt > 60f) _bootstrapAutoPrimeCompleted = true;
                    return;
                }

                _running = true;
                StartCoroutine(PrimeRoutine());
            }

            private System.Collections.IEnumerator PrimeRoutine()
            {
                Log?.LogInfo("EnhancedSettingsButton bootstrap auto-prime started; reason=" + _reason);
                object instance = null;
                GameObject menuBefore = null;
                bool wasActive = false;
                try
                {
                    instance = GetQoLEnhancedControllerInstance();
                    menuBefore = GetQoLEnhancedMenuInstance(instance);
                    wasActive = menuBefore != null && menuBefore.activeSelf;
                }
                catch { }

                InvokeQoLEnhancedOpenMenu();

                CanvasGroup primeCanvasGroup = null;
                float oldAlpha = 1f;
                bool oldInteractable = true;
                bool oldBlocksRaycasts = true;
                try
                {
                    object instAfterOpen = GetQoLEnhancedControllerInstance();
                    GameObject menuAfterOpen = GetQoLEnhancedMenuInstance(instAfterOpen);
                    if (!wasActive && menuAfterOpen != null)
                    {
                        primeCanvasGroup = menuAfterOpen.GetComponent<CanvasGroup>();
                        if (primeCanvasGroup == null) primeCanvasGroup = menuAfterOpen.AddComponent<CanvasGroup>();
                        oldAlpha = primeCanvasGroup.alpha;
                        oldInteractable = primeCanvasGroup.interactable;
                        oldBlocksRaycasts = primeCanvasGroup.blocksRaycasts;
                        primeCanvasGroup.alpha = 0f;
                        primeCanvasGroup.interactable = false;
                        primeCanvasGroup.blocksRaycasts = false;
                        Log?.LogInfo("EnhancedSettingsButton bootstrap auto-prime temporarily hid menu during priming to avoid visible flash.");
                    }
                }
                catch { }

                yield return null;
                yield return new WaitForEndOfFrame();

                int totalChanged = 0;
                for (int i = 0; i < 6; i++)
                {
                    totalChanged += BootstrapTextScanner.ScanAllKnownTextObjects("bootstrap-autoprime-known-" + i, Log);
                    yield return null;
                }

                try
                {
                    if (primeCanvasGroup != null)
                    {
                        primeCanvasGroup.alpha = oldAlpha;
                        primeCanvasGroup.interactable = oldInteractable;
                        primeCanvasGroup.blocksRaycasts = oldBlocksRaycasts;
                    }
                    object instNow = GetQoLEnhancedControllerInstance();
                    GameObject menuNow = GetQoLEnhancedMenuInstance(instNow);
                    if (!wasActive && menuNow != null && menuNow.activeSelf)
                    {
                        menuNow.SetActive(false);
                        TryInvokeQoLSyncMainMenuRunSettingsScreen(instNow, false);
                        Log?.LogInfo("EnhancedSettingsButton bootstrap auto-prime restored menu inactive after priming.");
                    }
                }
                catch { }

                _bootstrapAutoPrimeCompleted = true;
                Log?.LogInfo("EnhancedSettingsButton bootstrap auto-prime completed; totalChanged=" + totalChanged + "; reason=" + _reason);
            }
        }

        private sealed class EnhancedSettingsButtonBootstrapListener : MonoBehaviour
        {
            public void OnClicked()
            {
                try
                {
                    Log?.LogInfo("EnhancedSettingsButton bootstrap listener fired from " + gameObject.name);
                    InvokeQoLEnhancedOpenMenu();
                    BootstrapTextScanner.ScanAllKnownTextObjects("bootstrap-enhancedsettingsbutton-onclick", Log);
                }
                catch (Exception ex)
                {
                    Log?.LogWarning("EnhancedSettingsButton bootstrap listener failed: " + ex.Message);
                }
            }
        }

        private static void InvokeQoLEnhancedOpenMenu()
        {
            try
            {
                Type emc = FindTypeLoose("QoL_Unknown.EnhancedMenuController");
                if (emc == null)
                {
                    Log?.LogWarning("Bootstrap could not find QoL_Unknown.EnhancedMenuController.");
                    return;
                }
                MethodInfo ensure = emc.GetMethod("EnsureInitialized", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                try { ensure?.Invoke(null, null); } catch { }
                FieldInfo instField = emc.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object instance = instField != null ? instField.GetValue(null) : null;
                if (instance == null)
                {
                    MethodInfo init = emc.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    try { init?.Invoke(null, new object[] { null }); } catch { }
                    instance = instField != null ? instField.GetValue(null) : null;
                }
                MethodInfo open = emc.GetMethod("OpenMenu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (instance != null && open != null)
                {
                    open.Invoke(instance, null);
                    Log?.LogInfo("Bootstrap invoked EnhancedMenuController.OpenMenu().");
                }
                else
                {
                    Log?.LogWarning("Bootstrap could not resolve EnhancedMenuController instance/OpenMenu.");
                }
            }
            catch (Exception ex)
            {
                Log?.LogWarning("Bootstrap InvokeQoLEnhancedOpenMenu failed: " + ex.Message);
            }
        }

        private static bool IsQoLEnhancedControllerReady()
        {
            Type emc = FindTypeLoose("QoL_Unknown.EnhancedMenuController");
            if (emc == null) return false;
            FieldInfo instField = emc.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object instance = instField != null ? instField.GetValue(null) : null;
            return instance != null || emc.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null;
        }

        private static object GetQoLEnhancedControllerInstance()
        {
            Type emc = FindTypeLoose("QoL_Unknown.EnhancedMenuController");
            if (emc == null) return null;
            FieldInfo instField = emc.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return instField != null ? instField.GetValue(null) : null;
        }

        private static GameObject GetQoLEnhancedMenuInstance(object instance)
        {
            if (instance == null) return null;
            FieldInfo fi = instance.GetType().GetField("menuInstance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return fi != null ? fi.GetValue(instance) as GameObject : null;
        }

        private static void TryInvokeQoLSyncMainMenuRunSettingsScreen(object instance, bool visible)
        {
            try
            {
                if (instance == null) return;
                MethodInfo mi = instance.GetType().GetMethod("SyncMainMenuRunSettingsScreen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null) mi.Invoke(instance, new object[] { visible });
            }
            catch { }
        }

        private sealed class HiddenDisabledButtonTrigger : MonoBehaviour
        {
            private GameObject _mpLinkButton;
            private Sprite _mpIcon;
            private int _errorCounter;
            private int _attempts;
            private float _nextAttempt;
            private bool _mainMenuLiteDone;

            private void Awake()
            {
                try
                {
                    // We cannot bundle KrokMP's mp.png, so use a tiny generated sprite to keep Image.sprite non-null before the hidden trigger is neutralized.
                    Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    tex.SetPixel(0, 0, Color.red);
                    tex.Apply(false, true);
                    _mpIcon = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
                    Log?.LogInfo("Hidden disabled-button trigger Awake: generated dummy mp icon.");
                }
                catch (Exception ex)
                {
                    DoError(ex.ToString());
                }
            }

            private void Update()
            {
                if (_mpIcon == null) return;
                if (_mpLinkButton != null) return;
                if (Time.realtimeSinceStartup < _nextAttempt) return;
                _attempts++;
                _nextAttempt = Time.realtimeSinceStartup + (_attempts < 20 ? 0.25f : 1.0f);
                try
                {
                    DoTheNormalButton();
                    if (_mpLinkButton != null)
                    {
                        Log?.LogInfo("Hidden disabled-button trigger: hidden button clone created; disabling Update loop.");
                        enabled = false;
                    }
                    else if (_attempts == 1 || _attempts == 10 || _attempts == 30)
                    {
                        Log?.LogInfo($"Hidden disabled-button trigger: waiting for PreRunScript.instance/Button (11); attempts={_attempts}");
                    }
                }
                catch (Exception ex)
                {
                    DoError(ex.ToString());
                }
            }

            private void DoTheNormalButton()
            {
                if (_mpLinkButton != null) return;
                Component instance = GetPreRunScriptInstance();
                if (instance == null) return;

                Transform source = instance.transform.Find("Button (11)");
                if (source == null)
                {
                    if (_attempts == 1 || _attempts == 10 || _attempts == 30)
                        Log?.LogInfo("Hidden disabled-button trigger: PreRunScript.instance found, but Button (11) was not found.");
                    return;
                }

                _mpLinkButton = UnityEngine.Object.Instantiate<Transform>(source, instance.transform, false).gameObject;
                _mpLinkButton.name = "QoLCP_DISABLED_BUTTON_REENABLE_LITE";

                try
                {
                    RectTransform rt = _mpLinkButton.GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition += new Vector2(180f, 0f);
                    _mpLinkButton.transform.SetSiblingIndex(source.GetSiblingIndex());
                }
                catch (Exception ex) { DoError("rect/sibling setup " + ex); }

                try
                {
                    SetupTooltip(_mpLinkButton);
                }
                catch (Exception ex) { DoError("tooltip setup " + ex); }

                try
                {
                    Image img = _mpLinkButton.GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = _mpIcon;
                        img.color = Color.red;
                    }
                }
                catch (Exception ex) { DoError("image setup " + ex); }

                try
                {
                    Button button = _mpLinkButton.GetComponent<Button>();
                    if (button != null)
                    {
                        button.onClick.RemoveAllListeners();
                        try { button.onClick.SetPersistentListenerState(0, UnityEventCallState.Off); } catch { }
                        button.interactable = false;
                    }
                }
                catch (Exception ex) { DoError("button setup " + ex); }

                try
                {
                    ApplyHiddenTriggerState(_mpLinkButton);
                }
                catch (Exception ex) { DoError("hide trigger setup " + ex); }

                try
                {
                    EnsureKrokMpCompatibleMainMenuLiteArtifacts(instance, source);
                }
                catch (Exception ex) { DoError("mainmenu-lite setup " + ex); }

                Log?.LogInfo("Hidden disabled-button trigger: cloned Button (11), kept active, then hidden/offscreen; QoLCP mainmenu-lite compatibility artifacts attempted.");
            }

            private void EnsureKrokMpCompatibleMainMenuLiteArtifacts(Component instance, Transform sourceButton)
            {
                if (_mainMenuLiteDone || instance == null) return;
                _mainMenuLiteDone = true;

                // KrokMP compatibility rule:
                // - Do not create objects named KROKMP_* in this plugin.
                // - If the real KrokMP plugin is loaded or its menu objects already exist,
                //   skip our optional mainmenu-lite artifacts entirely. They were only a
                //   lifecycle compatibility crutch; the real stable path is EnhancedSettingsButton auto-prime.
                if (IsRealKrokMpPresentOrObjectsExist())
                {
                    Log?.LogInfo("Hidden disabled-button trigger: real KrokMP detected or KROKMP objects exist; skipping QoLCP mainmenu-lite artifacts for compatibility.");
                    return;
                }

                int created = 0;

                try
                {
                    Transform runSettings = instance.transform.Find("RunSettings");
                    if (runSettings != null)
                    {
                        GameObject panel = UnityEngine.Object.Instantiate<GameObject>(runSettings.gameObject, instance.transform, false);
                        panel.name = "QoLCP_MENU_LITE_HIDDEN";
                        TryRenameChild(panel.transform, "RunSettings", "QoLCP_RUNSETTINGS_LITE");
                        ApplyHiddenTriggerState(panel);
                        created++;

                        Transform title = panel.transform.Find("QoLCP_RUNSETTINGS_LITE");
                        if (title != null)
                        {
                            GameObject templateLabel = UnityEngine.Object.Instantiate<GameObject>(title.gameObject, instance.transform, false);
                            templateLabel.name = "QoLCP_TEMPLATE_LABEL_LITE_HIDDEN";
                            ApplyHiddenTriggerState(templateLabel);
                            created++;
                        }

                        Transform cancel = panel.transform.Find("Cancel");
                        if (cancel != null)
                        {
                            GameObject templateButton = UnityEngine.Object.Instantiate<GameObject>(cancel.gameObject, instance.transform, false);
                            templateButton.name = "QoLCP_TEMPLATE_BUTTON_LITE_HIDDEN";
                            ApplyHiddenTriggerState(templateButton);
                            created++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DoError("RunSettings-lite clone " + ex);
                }

                try
                {
                    if (sourceButton != null)
                    {
                        GameObject menuButton = UnityEngine.Object.Instantiate<Transform>(sourceButton, instance.transform, false).gameObject;
                        menuButton.name = "QoLCP_MENU_OPEN_BUTTON_LITE_HIDDEN";
                        TrySetAnyText(menuButton, "MULTIPLAYER");
                        ApplyHiddenTriggerState(menuButton);
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    DoError("main menu button-lite clone " + ex);
                }

                Log?.LogInfo("Hidden disabled-button trigger: created QoLCP mainmenu-lite hidden artifacts=" + created);
            }


            private static bool IsRealKrokMpPresentOrObjectsExist()
            {
                try
                {
                    // BepInEx plugin metadata check. Avoid hard reference to KrokMP assemblies.
                    foreach (var info in Chainloader.PluginInfos.Values)
                    {
                        string name = info?.Metadata?.Name ?? string.Empty;
                        string guid = info?.Metadata?.GUID ?? string.Empty;
                        if (name.IndexOf("Krokosha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("KrokMP", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            name.IndexOf("Krokosha_MP_CU", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            guid.IndexOf("Krokosha", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            guid.IndexOf("KrokMP", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                }
                catch { }

                try
                {
                    // Object-name check for real KrokMP UI. This plugin intentionally no longer
                    // creates KROKMP_* objects, so these names should only come from KrokMP itself.
                    string[] knownPaths = new string[]
                    {
                        "KROKMP_MENU",
                        "KROKMP_BUTTON_LINK",
                        "KROKMP_MENU_OPEN_BUTTON",
                        "KROKMP_BUTTON_REENABLE",
                        "KROKMP_MENU_LITE_HIDDEN" // legacy builds only
                    };
                    foreach (string path in knownPaths)
                    {
                        if (GameObject.Find(path) != null) return true;
                    }

                    foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null || t.gameObject == null) continue;
                        if (!t.gameObject.scene.IsValid()) continue;
                        string n = t.gameObject.name ?? string.Empty;
                        if (n.StartsWith("KROKMP_", StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
                catch { }

                return false;
            }

            private static void TryRenameChild(Transform parent, string oldName, string newName)
            {
                try
                {
                    if (parent == null) return;
                    Transform child = parent.Find(oldName);
                    if (child != null) child.name = newName;
                }
                catch { }
            }

            private static void TrySetAnyText(GameObject go, string value)
            {
                try
                {
                    if (go == null) return;
                    foreach (Component c in go.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null) continue;
                        Type t = c.GetType();
                        if (t.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0 &&
                            t.Name.IndexOf("TMP", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        PropertyInfo prop = t.GetProperty("text", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
                        {
                            prop.SetValue(c, value, null);
                            return;
                        }
                    }
                }
                catch { }
            }

            private static void ApplyHiddenTriggerState(GameObject go)
            {
                if (go == null) return;
                go.SetActive(true);

                CanvasGroup cg = go.GetComponent<CanvasGroup>();
                if (cg == null) cg = go.AddComponent<CanvasGroup>();
                cg.alpha = 0f;
                cg.interactable = false;
                cg.blocksRaycasts = false;

                foreach (Graphic g in go.GetComponentsInChildren<Graphic>(true))
                {
                    if (g == null) continue;
                    g.raycastTarget = false;
                    Color c = g.color;
                    c.a = 0f;
                    g.color = c;
                }

                foreach (Selectable s in go.GetComponentsInChildren<Selectable>(true))
                {
                    if (s == null) continue;
                    s.interactable = false;
                }

                RectTransform rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchoredPosition += new Vector2(10000f, 10000f);
                    rt.localScale = Vector3.one;
                }

                Canvas.ForceUpdateCanvases();
            }

            private static Component GetPreRunScriptInstance()
            {
                try
                {
                    Type preRun = FindTypeLoose("PreRunScript");
                    if (preRun == null) return null;

                    FieldInfo fi = preRun.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object value = null;
                    if (fi != null) value = fi.GetValue(null);
                    if (value == null)
                    {
                        PropertyInfo pi = preRun.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (pi != null && pi.GetIndexParameters().Length == 0) value = pi.GetValue(null, null);
                    }
                    return value as Component;
                }
                catch { return null; }
            }

            private static void SetupTooltip(GameObject go)
            {
                Type tooltipType = FindTypeLoose("UITooltip");
                if (tooltipType == null) return;
                Component tooltip = go.GetComponent(tooltipType);
                if (tooltip == null) return;

                string key = "mainmenu_mp_button_tooltip_lastresort_enable_mod";
                SetStringMember(tooltipType, tooltip, "localeName", "krokosha_coop_" + key);
                SetBoolMember(tooltipType, tooltip, "skipLocale", true);
                // Real KrokMP uses Lang.Get(key). For this hidden trigger, setting a stable non-empty tipName is enough to reproduce the field writes.
                SetStringMember(tooltipType, tooltip, "tipName", key);
            }

            private static void SetStringMember(Type t, object obj, string name, string value)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null && fi.FieldType == typeof(string)) { fi.SetValue(obj, value); return; }
                PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanWrite && pi.PropertyType == typeof(string)) pi.SetValue(obj, value, null);
            }

            private static void SetBoolMember(Type t, object obj, string name, bool value)
            {
                FieldInfo fi = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi != null && fi.FieldType == typeof(bool)) { fi.SetValue(obj, value); return; }
                PropertyInfo pi = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pi != null && pi.CanWrite && pi.PropertyType == typeof(bool)) pi.SetValue(obj, value, null);
            }

            private void DoError(string str)
            {
                if (_errorCounter >= 3) return;
                _errorCounter++;
                Log?.LogError("Hidden disabled-button trigger error: " + str);
            }
        }

        private static void PatchBootstrapTextSetters(ManualLogSource logger)
        {
            if (_bootstrapSettersPatched) return;
            try
            {
                if (_harmony == null) _harmony = new Harmony(PluginGuid + ".hiddendisabledtrigger");
                MethodInfo prefix = typeof(BootstrapPlugin).GetMethod(nameof(BootstrapTextSetterPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                int patched = 0;
                foreach (var item in new[]
                {
                    new { TypeName = "UnityEngine.UI.Text", AssemblyName = "UnityEngine.UI", PropName = "text" },
                    new { TypeName = "TMPro.TMP_Text", AssemblyName = "Unity.TextMeshPro", PropName = "text" }
                })
                {
                    Type t = FindTypeByFullName(item.TypeName, item.AssemblyName);
                    PropertyInfo prop = t?.GetProperty(item.PropName, BindingFlags.Public | BindingFlags.Instance);
                    MethodInfo setter = prop?.GetSetMethod();
                    if (setter == null) continue;
                    try { _harmony.Patch(setter, prefix: new HarmonyMethod(prefix)); patched++; }
                    catch (Exception ex) { logger.LogWarning($"Bootstrap text setter patch failed for {item.TypeName}.{item.PropName}: {ex.Message}"); }
                }
                _bootstrapSettersPatched = true;
                logger.LogInfo($"Bootstrap text setters patched: {patched}");
            }
            catch (Exception ex) { logger.LogWarning("Bootstrap text setter patch failed: " + ex.Message); }
        }

        private static void BootstrapTextSetterPrefix(ref string value)
        {
            try { if (!string.IsNullOrEmpty(value)) value = BootstrapTranslator.Translate(value); } catch { }
        }

        private static void PatchGuiTextMethods(ManualLogSource logger)
        {
            if (_guiPatched) return;
            try
            {
                if (_harmony == null) _harmony = new Harmony(PluginGuid + ".hiddendisabledtrigger");
                Type gui = typeof(GUI);
                MethodInfo stringPrefix = typeof(BootstrapPlugin).GetMethod(nameof(GuiStringPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo contentPrefix = typeof(BootstrapPlugin).GetMethod(nameof(GuiContentPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                int patched = 0;
                foreach (string name in new[] { "Label", "Box", "Button", "RepeatButton", "Toggle" })
                {
                    foreach (MethodInfo mi in gui.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (mi.Name != name || mi.ContainsGenericParameters) continue;
                        var ps = mi.GetParameters();
                        bool hasString = ps.Any(p => p.ParameterType == typeof(string) && p.Name == "text");
                        bool hasContent = ps.Any(p => p.ParameterType == typeof(GUIContent) && p.Name == "content");
                        if (!hasString && !hasContent) continue;
                        try { _harmony.Patch(mi, prefix: new HarmonyMethod(hasString ? stringPrefix : contentPrefix)); patched++; } catch { }
                    }
                }

                // QoL 1.0.4.5 main-menu module panel draws several labels through IMGUI/GUILayout.
                // The old patch only covered GUI.*, so exact JSON entries were loaded but never reached this panel.
                Type layout = typeof(GUILayout);
                foreach (string name in new[] { "Label", "Box", "Button", "RepeatButton", "Toggle" })
                {
                    foreach (MethodInfo mi in layout.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (mi.Name != name || mi.ContainsGenericParameters) continue;
                        var ps = mi.GetParameters();
                        bool hasString = ps.Any(p => p.ParameterType == typeof(string) && p.Name == "text");
                        bool hasContent = ps.Any(p => p.ParameterType == typeof(GUIContent) && p.Name == "content");
                        if (!hasString && !hasContent) continue;
                        try { _harmony.Patch(mi, prefix: new HarmonyMethod(hasString ? stringPrefix : contentPrefix)); patched++; } catch { }
                    }
                }
                _guiPatched = true;
                logger.LogInfo($"Bootstrap GUI/GUILayout text methods patched: {patched}");
            }
            catch (Exception ex) { logger.LogWarning("Bootstrap GUI text patch failed: " + ex.Message); }
        }

        private static void GuiStringPrefix(ref string text)
        {
            try { if (!string.IsNullOrEmpty(text)) text = BootstrapTranslator.Translate(text); } catch { }
        }

        private static void GuiContentPrefix(GUIContent content)
        {
            try { if (content != null && !string.IsNullOrEmpty(content.text)) content.text = BootstrapTranslator.Translate(content.text); } catch { }
        }

        private static Type FindTypeByFullName(string fullName, string assemblyName)
        {
            Type t = Type.GetType(fullName + ", " + assemblyName, false);
            if (t != null) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.IsNullOrEmpty(assemblyName) && !asm.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                    t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName, false); if (t != null) return t; } catch { }
            }
            return null;
        }

        private static Type FindTypeLoose(string name)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types; } catch { continue; }
                if (types == null) continue;
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    if (t.FullName == name || t.Name == name) return t;
                }
            }
            return null;
        }
    }

    internal static class BootstrapTranslator
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> PhraseMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static List<KeyValuePair<string, string>> PhraseRules = new List<KeyValuePair<string, string>>();
        private static readonly List<PlaceholderRule> PlaceholderRules = new List<PlaceholderRule>();
        public static int Count => Map.Count + PhraseMap.Count;

        public static bool ShouldSkipDynamicText(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string s = value.ToLowerInvariant();
            // v0.8.9 keeps intro prompts translatable through setter/GUI patches. The early setter patch translates them at assignment time,
            // which avoids the old scan-vs-localizer flicker better than leaving them untranslated.
            return false;
        }

        public static void Load(string pluginPath, ManualLogSource log)
        {
            Map.Clear(); PhraseMap.Clear(); Cache.Clear(); PlaceholderRules.Clear(); PhraseRules.Clear();
            string dir = Path.Combine(pluginPath, "QoLChinesePatch");
            Directory.CreateDirectory(dir);
            if (!Directory.Exists(dir)) return;
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
                    log.LogInfo($"Bootstrap loaded {entries.Count} {(isPhraseFile ? "phrase replacements" : "exact translations")} from {file}");
                }
                catch (Exception ex)
                {
                    log.LogWarning($"Bootstrap failed to load translation file {file}: {ex.Message}");
                }
            }
            foreach (var kv in Map)
            {
                var rule = PlaceholderRule.TryCreate(kv.Key, kv.Value);
                if (rule != null) PlaceholderRules.Add(rule);
            }
            PhraseRules = new List<KeyValuePair<string, string>>(PhraseMap);
            PhraseRules.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
            log.LogInfo($"Bootstrap translator ready. Translations={Count}; placeholder rules={PlaceholderRules.Count}; phrase rules={PhraseRules.Count}");
        }

        public static bool IsKnownEnglishText(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (ShouldSkipDynamicText(value)) return false;
            if (Map.ContainsKey(value)) return true;
            string trimmed = value.Trim();
            if (trimmed.Length != value.Length && Map.ContainsKey(trimmed)) return true;
            foreach (var rule in PlaceholderRules)
                if (rule.Matches(value)) return true;
            foreach (var kv in PhraseRules)
                if (!string.IsNullOrEmpty(kv.Key) && value.IndexOf(kv.Key, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        public static string Translate(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (ShouldSkipDynamicText(input)) return input;
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
                if (rule.TryTranslate(input, out var replaced)) return replaced;
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
                // Second-pass exact lookup: useful when phrase replacement produces a
                // canonical mixed key. This prevents partial localization in alerts.
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

    internal static class BootstrapTextScanner
    {
        public static int ScanAllKnownTextObjects(string reason, ManualLogSource log)
        {
            int total = 0, nonEmpty = 0, known = 0, changed = 0;
            changed += ScanType("UnityEngine.UI.Text", "UnityEngine.UI", reason, log, ref total, ref nonEmpty, ref known);
            changed += ScanType("TMPro.TMP_Text", "Unity.TextMeshPro", reason, log, ref total, ref nonEmpty, ref known);
            changed += ScanKnownStringHolders(reason, log, ref total, ref nonEmpty, ref known);
            log?.LogInfo($"Dual-DLL bootstrap force text scan [{reason}]: total={total}, nonEmpty={nonEmpty}, known={known}, changed={changed}");
            return changed;
        }

        private static int ScanKnownStringHolders(string reason, ManualLogSource log, ref int total, ref int nonEmpty, ref int known)
        {
            int changed = 0;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                if (types == null) continue;

                foreach (Type type in types)
                {
                    try
                    {
                        if (type == null) continue;
                        if (!typeof(UnityEngine.Object).IsAssignableFrom(type)) continue;
                        string n = type.Name ?? string.Empty;
                        if (n.IndexOf("Tooltip", StringComparison.OrdinalIgnoreCase) < 0 &&
                            n.IndexOf("Localizer", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        UnityEngine.Object[] objs;
                        try { objs = Resources.FindObjectsOfTypeAll(type); } catch { continue; }
                        if (objs == null) continue;
                        total += objs.Length;

                        foreach (var obj in objs)
                        {
                            try
                            {
                                if (obj == null) continue;
                                foreach (FieldInfo fi in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    try
                                    {
                                        if (fi == null || fi.FieldType != typeof(string) || fi.IsInitOnly) continue;
                                        string value = fi.GetValue(obj) as string;
                                        if (string.IsNullOrEmpty(value)) continue;
                                        nonEmpty++;
                                        if (!BootstrapTranslator.IsKnownEnglishText(value)) continue;
                                        known++;
                                        string newValue = BootstrapTranslator.Translate(value);
                                        if (newValue == value) continue;
                                        fi.SetValue(obj, newValue);
                                        changed++;
                                    }
                                    catch { }
                                }

                                foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    try
                                    {
                                        if (pi == null || pi.PropertyType != typeof(string) || !pi.CanRead || !pi.CanWrite) continue;
                                        if (pi.GetIndexParameters().Length != 0) continue;
                                        string value = pi.GetValue(obj, null) as string;
                                        if (string.IsNullOrEmpty(value)) continue;
                                        nonEmpty++;
                                        if (!BootstrapTranslator.IsKnownEnglishText(value)) continue;
                                        known++;
                                        string newValue = BootstrapTranslator.Translate(value);
                                        if (newValue == value) continue;
                                        pi.SetValue(obj, newValue, null);
                                        changed++;
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            return changed;
        }

        private static int ScanType(string fullName, string assemblyName, string reason, ManualLogSource log, ref int total, ref int nonEmpty, ref int known)
        {
            int changed = 0;
            try
            {
                Type type = FindType(fullName, assemblyName);
                if (type == null)
                {
                    log?.LogWarning($"Dual-DLL bootstrap scan [{reason}]: type not found: {fullName}, {assemblyName}");
                    return 0;
                }
                PropertyInfo prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                if (prop == null || !prop.CanRead || !prop.CanWrite)
                {
                    log?.LogWarning($"Dual-DLL bootstrap scan [{reason}]: text property unavailable on {fullName}");
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
                    if (!BootstrapTranslator.IsKnownEnglishText(value)) continue;
                    known++;
                    string newValue = BootstrapTranslator.Translate(value);
                    if (newValue == value) continue;
                    prop.SetValue(obj, newValue, null);
                    changed++;
                }
            }
            catch (Exception ex)
            {
                log?.LogWarning($"Dual-DLL bootstrap scan [{reason}] failed for {fullName}: {ex.Message}");
            }
            return changed;
        }

        private static Type FindType(string fullName, string assemblyName)
        {
            Type t = Type.GetType(fullName + ", " + assemblyName, false);
            if (t != null) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (!string.IsNullOrEmpty(assemblyName) && !asm.GetName().Name.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)) continue;
                    t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }

    internal static class TinyJsonStringDictionary
    {
        public static Dictionary<string, string> Parse(string json)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '{') return dict;
            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}') break;
                string key = ReadString(json, ref i);
                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':') break;
                i++;
                SkipWs(json, ref i);
                string value = ReadString(json, ref i);
                if (key != null) dict[key] = value ?? string.Empty;
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ',') { i++; continue; }
                if (i < json.Length && json[i] == '}') break;
            }
            return dict;
        }
        private static void SkipWs(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        private static string ReadString(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length || s[i] != '"') return null;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
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
                            if (i + 4 <= s.Length && int.TryParse(s.Substring(i, 4), System.Globalization.NumberStyles.HexNumber, null, out int code))
                            { sb.Append((char)code); i += 4; }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
