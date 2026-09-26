#if !MELON
using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace KoreanTextFixer
{
    [BepInPlugin("kr.schedule1.textfixer", "Korean Text Fixer", Translator.Version)]
    public class Plugin : BasePlugin
    {
        public override void Load()
        {
            ModLog.Info = s => base.Log.LogInfo(s);
            ModLog.Warn = s => base.Log.LogWarning(s);
            ModLog.Error = s => base.Log.LogError(s);
            try
            {
                Translator.LoadDictionaries(Path.Combine(Paths.GameRootPath, "BepInEx", "Translation", "ko", "Text"));
                ClassInjector.RegisterTypeInIl2Cpp<FixerBehaviour>();
                AddComponent<FixerBehaviour>();
                ModLog.Info("KoreanTextFixer " + Translator.Version + " loaded. entries=" + Translator.Dict.Count);
            }
            catch (Exception e)
            {
                ModLog.Error("KoreanTextFixer init failed: " + e);
            }
        }
    }

    public class FixerBehaviour : MonoBehaviour
    {
        public FixerBehaviour(IntPtr ptr) : base(ptr) { }

        private readonly TextScanner _scanner = new TextScanner();

        public void Update()
        {
            _scanner.Tick();
        }
    }
}
#endif
