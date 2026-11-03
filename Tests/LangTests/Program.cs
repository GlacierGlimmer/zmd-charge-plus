using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using EndfieldChargePlus;

// Embedded .lang resource verification. Runs entirely against the compiled
// assembly: it never reads Localization/*.lang from disk, which also proves the
// published resources are self-contained.
int failures = 0;
void Check(bool condition, string message)
{
    if (condition) return;
    failures++;
    Console.WriteLine("FAIL " + message);
}

const string Prefix = "EndfieldChargePlus.lang.";
var assembly = typeof(LocalizationManager).Assembly;
var names = assembly.GetManifestResourceNames();

var langNames = names.Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".lang", StringComparison.Ordinal) && !n.EndsWith(".sha256", StringComparison.Ordinal)).ToList();
var shaNames = names.Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".lang.sha256", StringComparison.Ordinal)).ToList();
Check(langNames.Count == 21, $"expected core.lang + 20 locales, found {langNames.Count}");
Check(langNames.Any(n => n == Prefix + "core.lang"), "core.lang is embedded");
Check(shaNames.Count == langNames.Count, $"every .lang has a .sha256 sidecar ({shaNames.Count}/{langNames.Count})");

byte[] Read(string name)
{
    using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("missing " + name);
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return memory.ToArray();
}

foreach (string shaName in shaNames)
{
    string langName = shaName[..^".sha256".Length];
    if (!names.Contains(langName)) { failures++; Console.WriteLine("FAIL orphan hash " + shaName); continue; }
    string expected = Encoding.UTF8.GetString(Read(shaName)).Trim();
    string actual = Convert.ToHexString(SHA256.HashData(Read(langName))).ToLowerInvariant();
    Check(expected == actual, "SHA-256 mismatch for " + langName);
}

// core.lang header + key integrity.
var core = LocalizationManager.CoreLanguageFieldCount;
Check(core == 2535, $"core.lang field count is {core}");
Check(LocalizationManager.CoreLanguageVersion == "20261102", "core.lang resource date is " + LocalizationManager.CoreLanguageVersion);
Check(LocalizationManager.AvailableLanguageCodes.Count == 20, "discovered embedded locales: " + LocalizationManager.AvailableLanguageCodes.Count);
Check(LocalizationManager.AvailableLanguageCodes.Contains("en-US") && LocalizationManager.AvailableLanguageCodes.Contains("zh-CN"), "base languages are embedded");
Check(LocalizationManager.PlatformFieldSources.Count == 339, "every reviewed native field has an original-source alias");

// Base languages must be complete: no empty values, no English fallback for Chinese.
var english = LocalizationManager.ReadResource("en-US");
var chinese = LocalizationManager.ReadResource("zh-CN");
Check(english.Count == core, $"en-US covers every field ({english.Count})");
Check(chinese.Count == core, $"zh-CN covers every field ({chinese.Count})");
Check(english.Values.All(v => !string.IsNullOrWhiteSpace(v)), "en-US has no empty values");
Check(chinese.Values.All(v => !string.IsNullOrWhiteSpace(v)), "zh-CN has no empty values");

// Every embedded locale parses and exposes only non-empty translated values.
foreach (string code in LocalizationManager.AvailableLanguageCodes)
{
    var pack = LocalizationManager.ReadResource(code);
    Check(pack.Count > 0, "locale parses: " + code);
    Check(pack.Values.All(v => !string.IsNullOrWhiteSpace(v)), "locale has no empty values: " + code);
    foreach (var message in pack.Where(p => p.Key.Contains("Endfield Charge Plus", StringComparison.Ordinal)))
        Check(message.Value.Contains("Endfield Charge Plus", StringComparison.Ordinal), "product name is preserved: " + code + " " + message.Key);
    foreach (string titleKey in new[] { "windows.title", "linux.title", "macos.title", "android.title" })
        Check(pack[titleKey] == english[titleKey], "platform product title is preserved: " + code + " " + titleKey);
    Check(pack["Android EXTRA_STATUS code (CHARGING/DISCHARGING/FULL/NOT_CHARGING)."].Contains("CHARGING/DISCHARGING/FULL/NOT_CHARGING"), "technical enum names are preserved: " + code);
    var language = LocalizationManager.SupportedLanguages.Single(l=>l.Code==code).Language;
    foreach (var field in LocalizationManager.PlatformFieldSources)
        Check(LocalizationManager.TranslateEnglish(field.Key,language)==pack[field.Value], "native ID and original source resolve to the same translation: " + code + " " + field.Key);
}

// Date header parsing and platform-specific title fields.
Check(LocalizationManager.TryParseResourceHeader("#20261030$2535", out var parsedDate, out var parsedCount) && parsedDate == "20261030" && parsedCount == 2535, "date header parses");
Check(!LocalizationManager.TryParseResourceHeader("#20261340$1", out _, out _), "invalid calendar date is rejected");
Check(!LocalizationManager.TryParseResourceHeader("#2026102$1", out _, out _), "short date is rejected");
Check(!LocalizationManager.TryParseResourceHeader("#0.1.2$1", out _, out _), "legacy product-version header is rejected by the runtime format");
Check(LocalizationManager.PlatformTitle == "Endfield Charge Plus", "Windows platform title comes from windows.title: " + LocalizationManager.PlatformTitle);
Check(LocalizationManager.Field("linux.title") == "Endfield Charge Plus For Linux", "linux.title is available in the shared pack");
Check(LocalizationManager.Field("macos.title") == "Endfield Charge Plus For MacOS", "macos.title is available in the shared pack");
Check(LocalizationManager.Field("android.title") == "Endfield Charge Plus for Android", "android.title is available in the shared pack");

// An older/shorter language file maps only its known prefix; the tail falls back.
var prefixKeys = new[] { "one", "two", "three", "four" };
var prefixPack = LocalizationManager.BuildPackFromValues("ja-JP", prefixKeys, new[] { "一", "二" });
Check(prefixPack.Count == 2 && prefixPack["one"] == "一" && !prefixPack.ContainsKey("three"), "old shorter resource keeps a valid prefix");

// Language list is derived from the embedded resources, not a hard-coded array.
Check(LocalizationManager.SupportedLanguages.Count == 20, "language list matches embedded resources: " + LocalizationManager.SupportedLanguages.Count);
Check(LocalizationManager.SupportedLanguages.Any(l => l.Code == "ja-JP"), "Japanese is selectable");
Check(LocalizationManager.SupportedLanguages.First(l => l.Code == "ar-SA").RightToLeft, "RTL metadata is preserved");
Check(LocalizationManager.SupportedLanguages.All(l => l.DisplayName == l.NativeName && l.Region.Length == 0), "language choices show native language names without country restrictions");
Check(LocalizationManager.SupportedLanguages.First(l => l.Code == "en-US").DisplayName == "English" && LocalizationManager.SupportedLanguages.First(l => l.Code == "zh-TW").DisplayName == "繁體中文" && LocalizationManager.SupportedLanguages.First(l => l.Code == "pt-BR").DisplayName == "Português", "English, Traditional Chinese and Portuguese have global labels");
foreach (var (tag, expected) in new[] { ("en-GB", "en-US"), ("en-AU", "en-US"), ("en-IN", "en-US"), ("en-ZA", "en-US"), ("zh-HK", "zh-TW"), ("zh-MO", "zh-TW"), ("zh-Hant", "zh-TW"), ("es-MX", "es-ES"), ("es-AR", "es-ES"), ("pt-PT", "pt-BR"), ("pt-AO", "pt-BR"), ("fr-CA", "fr-FR"), ("ar-EG", "ar-SA"), ("bn-IN", "bn-BD") })
    Check(LocalizationManager.NormalizePreference(tag) == expected, "global language pack serves regional system settings: " + tag);

// Removed languages must never select a missing resource or crash.
Check(LocalizationManager.PreferenceFor(LocalizationManager.ResolvePreference("Auto", "zh-Hant-HK")) == "zh-TW", "Hong Kong system locale uses retained Traditional Chinese");
Check(LocalizationManager.ResolvePreference("Auto", "nb-NO") == AppLanguage.English, "removed Norwegian locale falls back to English");
Check(LocalizationManager.ResolvePreference("Auto", "iw-IL") == AppLanguage.English, "removed Hebrew alias falls back to English");
Check(LocalizationManager.NormalizePreference("bo-CN") == "Auto", "removed saved language returns to Auto");
Check(LocalizationManager.NormalizePreference("de-AT") == "de-DE", "removed region uses retained language");
var expectedCodes = new[] { "zh-CN", "en-US", "zh-TW", "ja-JP", "ko-KR", "de-DE", "fr-FR", "es-ES", "pt-BR", "ru-RU", "ar-SA", "hi-IN", "bn-BD", "ur-PK", "id-ID", "vi-VN", "th-TH", "tr-TR", "it-IT", "fa-IR" };
Check(expectedCodes.OrderBy(c => c).SequenceEqual(LocalizationManager.AvailableLanguageCodes.OrderBy(c => c)), "compiled resource set is exactly the original 20 languages");

foreach (var (tag, expected) in new[] { ("zh-Hans-HK", "zh-CN"), ("zh-Hans-MO", "zh-CN"), ("zh-Hant-CN", "zh-TW"), ("fr-CA", "fr-FR"), ("de-AT", "de-DE"), ("pt-PT", "pt-BR"), ("in-ID", "id-ID") })
    Check(LocalizationManager.NormalizePreference(tag) == expected, "system script and region fallback: " + tag);

// Exercise every shipped language against real runtime notification formatting.
foreach (var locale in LocalizationManager.SupportedLanguages)
{
    LocalizationManager.SetLanguage(locale.Language);
    Check(LocalizationManager.ReadResource(locale.Code).Count == core, "every field is translated: " + locale.Code);
    var result = new EndfieldChargePlus.Settings.SettingsWindow.UpdateCheckResult("0.1.2", "9.8.7", EndfieldChargePlus.Settings.SettingsWindow.UpdateStatusKind.UpdateAvailable, true, true);
    string title = result.NotificationTitle, body = result.NotificationMessage;
    Check(title.Length > 0 && body.Contains("v9.8.7") && body.Split("v9.8.7").Length == 2 && !body.Contains("{0}"), "notification keeps the version exactly once: " + locale.Code);
    if (locale.Code != "en-US")
        Check(title != "Endfield Charge Plus update available" && !body.StartsWith("Found "), "notification is localized: " + locale.Code);
    foreach (string key in new[] { "Settings", "About", "HUD Content", "No shortcut configured.", "Multiple HUD instances", "Open releases" })
        Check(LocalizationManager.ReadResource(locale.Code).ContainsKey(key), "visible message has a resource: " + locale.Code + " " + key);
}
LocalizationManager.SetLanguage(AppLanguage.English);

// Switching and fallback behaviour.
LocalizationManager.SetLanguage(AppLanguage.Japanese);
Check(LocalizationManager.TranslateEnglish("Settings") == "設定" || LocalizationManager.TranslateEnglish("Settings") != "Settings", "Japanese translation is returned for Settings");
string french = LocalizationManager.TranslateEnglish("Display & Position", AppLanguage.French);
Check(!string.IsNullOrWhiteSpace(french), "French lookup returns text");
LocalizationManager.SetLanguage(AppLanguage.English);
Check(LocalizationManager.TranslateEnglish("ECP_UNREGISTERED_TEST_MESSAGE") == "ECP_UNREGISTERED_TEST_MESSAGE", "unknown text falls back to English");
LocalizationManager.SetLanguage(AppLanguage.SimplifiedChinese);
Check(LocalizationManager.Text("知道了", "Done") == "知道了", "Chinese source text is preserved");
LocalizationManager.SetLanguage(AppLanguage.English);
Check(LocalizationManager.Text("知道了", "Done") == "Done", "English source text is preserved");

// A partial locale falls back per-field without inventing translations.
var partial = LocalizationManager.AvailableLanguageCodes.FirstOrDefault(c => LocalizationManager.ReadResource(c).Count < core);
// Complete shipped packs need no real missing field; the synthetic prefix test above verifies partial-pack mapping.
if (partial is not null)
{
    var pack = LocalizationManager.ReadResource(partial);
    string? missingKey = english.Keys.FirstOrDefault(key => !pack.ContainsKey(key));
    Check(missingKey is not null, "partial locale has missing fields (" + partial + ")");
    if (missingKey is not null)
        Check(LocalizationManager.TranslateEnglish(missingKey, ErrorLanguage(partial)) == missingKey, "missing field falls back to English: " + partial);
}

// Escape round-trip through a known multi-line message.
var ruleText = english.Keys.FirstOrDefault(k => k.Contains('\n'));
Check(ruleText is not null, "multi-line core keys are escaped on disk and restored at runtime");
if (ruleText is not null)
{
    Check(english.ContainsKey(ruleText) && english[ruleText] == ruleText, "multi-line key round-trips through core.lang escaping");
    Check(LocalizationManager.AvailableLanguageCodes.Any(code => LocalizationManager.ReadResource(code).ContainsKey(ruleText)), "multi-line key is map-readable in at least one locale");
}

Console.WriteLine(failures == 0 ? "PASS embedded .lang resources" : $"{failures} failure(s)");
return failures == 0 ? 0 : 1;

static AppLanguage ErrorLanguage(string code)
{
    var info = LocalizationManager.SupportedLanguages.First(l => l.Code == code);
    return info.Language;
}
