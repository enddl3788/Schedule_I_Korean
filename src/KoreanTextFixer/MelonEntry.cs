#if MELON
using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppTMPro;
using MelonLoader;
using MelonLoader.Utils;
using XUnity.Common.Constants;

[assembly: MelonInfo(typeof(KoreanTextFixer.MelonEntry), "Korean Text Fixer", KoreanTextFixer.Translator.Version, "enddl3788")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPriority(-100)]

namespace KoreanTextFixer
{
    public class MelonEntry : MelonMod
    {
        private TextScanner _scanner;

        public override void OnInitializeMelon()
        {
            ModLog.Info = s => LoggerInstance.Msg(s);
            ModLog.Warn = s => LoggerInstance.Warning(s);
            ModLog.Error = s => LoggerInstance.Error(s);
            try
            {
                XuatTmpBridge.Apply();
                Translator.LoadDictionaries(Path.Combine(MelonEnvironment.GameRootDirectory, "AutoTranslator", "Translation", "ko", "Text"));
                _scanner = new TextScanner();
                ModLog.Info("KoreanTextFixer " + Translator.Version + " loaded. entries=" + Translator.Dict.Count);
            }
            catch (Exception e)
            {
                ModLog.Error("KoreanTextFixer init failed: " + e);
            }
        }

        public override void OnUpdate()
        {
            _scanner?.Tick();
        }
    }

    // MelonLoader는 게임 전용 네임스페이스에 "Il2Cpp" 접두사를 붙여 프록시를 생성한다(TMPro -> Il2CppTMPro).
    // XUnity.AutoTranslator는 "TMPro.TMP_Text" 같은 원래 이름으로만 찾기 때문에 TextMeshPro 타입이 null이 되고
    // TextMeshPro 훅이 통째로 빠진다. XUAT가 훅을 거는 OnApplicationLateStart보다 먼저, 비어 있는 항목을 채워 넣는다.
    internal static class XuatTmpBridge
    {
        public static void Apply()
        {
            try
            {
                Il2CppProxyAssemblies.Location = Path.Combine(MelonEnvironment.GameRootDirectory, "MelonLoader", "Il2CppAssemblies");
                RuntimeHelpers.RunClassConstructor(typeof(UnityTypes).TypeHandle);

                int fixedCount = 0;
                fixedCount += FillContainer("TMP_InputField", typeof(TMP_InputField), Il2CppClassPointerStore<TMP_InputField>.NativeClassPtr);
                fixedCount += FillContainer("TMP_Text", typeof(TMP_Text), Il2CppClassPointerStore<TMP_Text>.NativeClassPtr);
                fixedCount += FillContainer("TextMeshProUGUI", typeof(TextMeshProUGUI), Il2CppClassPointerStore<TextMeshProUGUI>.NativeClassPtr);
                fixedCount += FillContainer("TextMeshPro", typeof(TextMeshPro), Il2CppClassPointerStore<TextMeshPro>.NativeClassPtr);
                fixedCount += FillContainer("TMP_FontAsset", typeof(TMP_FontAsset), Il2CppClassPointerStore<TMP_FontAsset>.NativeClassPtr);
                fixedCount += FillContainer("TMP_Settings", typeof(TMP_Settings), Il2CppClassPointerStore<TMP_Settings>.NativeClassPtr);
                fixedCount += FillType("TextOverflowModes", typeof(TextOverflowModes));
                fixedCount += FillType("TextAlignmentOptions", typeof(TextAlignmentOptions));

                var check = typeof(UnityTypes).GetField("TMP_Text", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as TypeContainer;
                ModLog.Info("XUAT bridge: filled " + fixedCount + " TextMeshPro types, TMP_Text=" + (check?.ClrType?.FullName ?? "null"));
            }
            catch (Exception e)
            {
                ModLog.Warn("XUAT bridge failed (XUnity.AutoTranslator missing?): " + e);
            }
        }

        private static int FillContainer(string field, Type wrapper, IntPtr ptr)
        {
            var f = typeof(UnityTypes).GetField(field, BindingFlags.Public | BindingFlags.Static);
            if (f == null || f.GetValue(null) != null) return 0;
            var nativeType = ptr != IntPtr.Zero ? Il2CppType.TypeFromPointer(ptr) : Il2CppType.From(wrapper);
            SetStatic(f, new TypeContainer(nativeType, wrapper, ptr));
            return 1;
        }

        private static int FillType(string field, Type wrapper)
        {
            var f = typeof(UnityTypes).GetField(field, BindingFlags.Public | BindingFlags.Static);
            if (f == null || f.GetValue(null) != null) return 0;
            SetStatic(f, wrapper);
            return 1;
        }

        // static readonly 필드는 형식 초기화 후 리플렉션 SetValue가 막혀 있으므로 IL로 직접 기록한다.
        private static void SetStatic(FieldInfo f, object value)
        {
            var dm = new DynamicMethod("xuatbridge_set_" + f.Name, null, new[] { typeof(object) }, typeof(UnityTypes), true);
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, f.FieldType);
            il.Emit(OpCodes.Stsfld, f);
            il.Emit(OpCodes.Ret);
            ((Action<object>)dm.CreateDelegate(typeof(Action<object>)))(value);
        }
    }
}
#endif
