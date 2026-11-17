using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;

namespace AutoUpdater.Localization
{
    /// <summary>
    /// 從主組件讀取語系資源的 ResourceManager。
    /// 預設的 ResourceManager 只會到附屬組件（例如 zh-TW\AutoUpdater.resources.dll）找語系資源；
    /// 本專案把語系資源以「基底名稱.語系.resources」內嵌在主組件，讓套件只需要一個 DLL。
    /// 找不到內嵌的語系時，交回預設行為（附屬組件，最後是中性語系，也就是英文）。
    /// </summary>
    internal sealed class EmbeddedCultureResourceManager : ResourceManager
    {
        private readonly Assembly _assembly;

        // 每個語系只讀一次；找不到的語系記為 null，避免重複查詢
        private readonly ConcurrentDictionary<string, ResourceSet> _embeddedSets = new ConcurrentDictionary<string, ResourceSet>();

        public EmbeddedCultureResourceManager(string baseName, Assembly assembly) : base(baseName, assembly)
        {
            _assembly = assembly;
        }

        protected override ResourceSet InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)
        {
            // 中性語系本來就在主組件，交給預設行為
            if (!culture.Equals(CultureInfo.InvariantCulture))
            {
                ResourceSet embedded = _embeddedSets.GetOrAdd(culture.Name, LoadEmbeddedSet);
                if (embedded != null)
                {
                    return embedded;
                }
            }
            return base.InternalGetResourceSet(culture, createIfNotExists, tryParents);
        }

        private ResourceSet LoadEmbeddedSet(string cultureName)
        {
            var stream = _assembly.GetManifestResourceStream($"{BaseName}.{cultureName}.resources");
            return stream == null ? null : new ResourceSet(stream);
        }

        /// <summary>
        /// 組件載入時，把自動產生的 LocalizationResources 改用本類別。
        /// LocalizationResources.Designer.cs 是 VS 依 resx 自動產生的，不能直接修改，
        /// 所以用反射設定它快取 ResourceManager 的私有欄位 resourceMan。
        /// </summary>
#pragma warning disable CA2255 // 函式庫需要在任何資源被讀取前完成替換，模組初始設定式是最早的時機
        [ModuleInitializer]
#pragma warning restore CA2255
        internal static void Install()
        {
            var field = typeof(LocalizationResources).GetField("resourceMan", BindingFlags.NonPublic | BindingFlags.Static);
            field?.SetValue(null, new EmbeddedCultureResourceManager(
                "AutoUpdater.Localization.LocalizationResources", typeof(LocalizationResources).Assembly));
        }
    }
}
