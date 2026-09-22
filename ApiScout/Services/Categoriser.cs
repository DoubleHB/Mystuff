using System.Globalization;
using System.Text.RegularExpressions;
using ApiScout.Models;

namespace ApiScout.Services;

/// <summary>
/// Every directory names its categories differently ("Cryptocurrency", "financial", "Crypto &amp; Web3"…).
/// This folds them into one canonical set, and classifies uncategorised APIs from their name and description.
/// </summary>
public static class Categoriser
{
    // Checked in order against the source's own category name.
    private const string E = "(?![\\p{L}])"; // strict word end for short stems

    private static readonly (Regex Rx, string Category)[] CategoryRules = Build(prefix: true,
        ("misc|other" + E + "|uncategor", "Other"),
        ("anime|manga", "Anime & Manga"),
        ("animal|pets?", "Animals"),
        ("malware|security|authenticat|authoriz|identity|privacy|captcha|fraud", "Security & Authentication"),
        ("art(?!i)|design|fonts?|colou?rs?", "Art & Design"),
        ("blockchain|crypto|web3|nft", "Blockchain & Crypto"),
        ("sports?|football|soccer|cricket|f1|racing", "Sports & Fitness"),
        ("social", "Social"),
        ("books?|literature|librar", "Books & Literature"),
        ("calendar|holidays?|time|dates?", "Calendar & Time"),
        ("cloud|storage|hosting|file sharing|backend|iot" + E, "Cloud & Storage"),
        ("continuous integration|development|developer|devops|tools?|programming|open ?source|source control|mcp|api", "Development Tools"),
        ("currency|exchange|financ|stocks?|bank|payments?|money|invest|tax", "Currency & Finance"),
        ("validation|data access|open data|analytics|databases?", "Data & Validation"),
        ("dictionar|language|translat|words?", "Dictionaries & Language"),
        ("documents?|productivity|office|pdf|notes?|collaboration|project management|forms?", "Documents & Productivity"),
        ("e-?mail", "Email"),
        ("environment|climate|energy|air quality|sustainab", "Environment"),
        ("events?|tickets?", "Events"),
        ("food|drink|recipes?|restaurants?|beer|wine|coffee", "Food & Drink"),
        ("games?|gaming|comics?|videogames?|esports?", "Games & Comics"),
        ("geo|maps?|location|places?|countr|ip address", "Geocoding & Maps"),
        ("government|civic|politic|legal|law|public sector", "Government & Open Data"),
        ("health|medic|fitness|covid|nutrition|drugs?", "Health"),
        ("jobs?|career|recruit|hr" + E, "Jobs"),
        ("machine learning|artificial|llm|vision|nlp|ai" + E + "|ml" + E, "Machine Learning & AI"),
        ("text", "Text Analysis"),
        ("movies?|video|film|stream|media|entertainment|tv" + E, "Movies, TV & Video"),
        ("music|audio|podcasts?|radio|lyrics", "Music"),
        ("news|rss|blog", "News"),
        ("personality|quotes?|jokes?|humou?r|trivia|random|fun" + E, "Quotes, Jokes & Fun"),
        ("phone|sms|messag|telephony|telecom|chat|notification|push|communicat", "Phone & Messaging"),
        ("photo|images?|pictures?|wallpaper", "Photography & Images"),
        ("science|math|space|astronom|chemistry|physics|education|university|school|academic", "Science, Math & Education"),
        ("shopping|e-?commerce|commerce|retail|marketplace|products?|advertising|marketing|seo", "Shopping & Marketing"),
        ("test data|mock|fake|placeholder|dummy", "Test Data"),
        ("tracking|logistics|shipping|parcel|delivery", "Tracking & Logistics"),
        ("transport|transit|travel|flights?|aviation|rail|train|hotel|bus" + E, "Transport & Travel"),
        ("url shorten|shorten|links?", "URL Shorteners"),
        ("vehicles?|cars?|automotive", "Vehicles"),
        ("weather|forecast", "Weather"),
        ("business|enterprise|crm|customer|sales|support|compan|real estate|property", "Business"));

    // Used when the source gave no usable category: score name + description by keyword hits.
    private static readonly (Regex Rx, string Category)[] KeywordRules = Build(prefix: false,
        ("weather|forecast|meteorolog|temperature|rainfall", "Weather"),
        ("bitcoin|crypto\\w*|blockchain|ethereum|nft|token prices?|defi", "Blockchain & Crypto"),
        ("stocks?|forex|currency|exchange rates?|financial|banks?|payments?|invoice|tax|market data", "Currency & Finance"),
        ("geocod\\w*|maps?|postcodes?|zip codes?|countries|country|cities|latitude|timezones?|ip address|geolocation|ip lookup", "Geocoding & Maps"),
        ("movies?|films?|tv shows?|series|youtube|videos?|streaming|actors?", "Movies, TV & Video"),
        ("music|songs?|lyrics|albums?|artists?|podcasts?|radio|spotify", "Music"),
        ("anime|manga", "Anime & Manga"),
        ("cats?|dogs?|animals?|birds?|pets?|species|fish|wildlife", "Animals"),
        ("games?|gaming|pok[eé]mon|minecraft|steam|chess|comics?|marvel|dungeons", "Games & Comics"),
        ("jokes?|quotes?|trivia|facts?|memes?|random|insults?|advice|fortune", "Quotes, Jokes & Fun"),
        ("recipes?|food|meals?|cocktails?|beers?|brewer\\w*|coffee|nutrition|restaurants?", "Food & Drink"),
        ("books?|poems?|poetry|bible|quran|literature|library|authors?", "Books & Literature"),
        ("dictionary|translat\\w*|words?|synonyms?|languages?|grammar|spelling", "Dictionaries & Language"),
        ("news|headlines|articles?|rss", "News"),
        ("football|soccer|nba|nfl|cricket|formula 1|f1|sports?|olympic\\w*|tennis|baseball|hockey", "Sports & Fitness"),
        ("nasa|space|astronom\\w*|planets?|science|math\\w*|numbers?|physics|chemistry|universit\\w*|research|papers?", "Science, Math & Education"),
        ("government|census|parliament|congress|legislation|open data|public data|elections?", "Government & Open Data"),
        ("health|medical|covid|disease|drugs?|hospital|fitness|fda", "Health"),
        ("images?|photos?|pictures?|avatars?|placeholder images?|wallpapers?", "Photography & Images"),
        ("art|museum|design|colou?rs?|fonts?|icons?", "Art & Design"),
        ("machine learning|ai|llm|sentiment|ocr|face detection|neural|gpt|text-to-speech", "Machine Learning & AI"),
        ("fake data|mock|dummy|lorem|test data|placeholder", "Test Data"),
        ("emails?|smtp|mailbox|disposable", "Email"),
        ("sms|phone numbers?|whatsapp|telegram|messaging|notifications?", "Phone & Messaging"),
        ("flights?|airports?|trains?|bus|transit|transport|travel|hotels?|airlines?", "Transport & Travel"),
        ("cars?|vehicles?|vin", "Vehicles"),
        ("jobs?|careers?|vacancies", "Jobs"),
        ("holidays?|calendar|dates?|time", "Calendar & Time"),
        ("air quality|carbon|emissions|climate|energy|pollution|earthquakes?|recycling", "Environment"),
        ("security|malware|phishing|password|breach\\w*|vulnerabilit\\w*|threat|virus", "Security & Authentication"),
        ("shorten\\w*|short urls?|short links?", "URL Shorteners"),
        ("twitter|facebook|instagram|reddit|social|discord|mastodon", "Social"),
        ("shopping|products?|prices?|e-?commerce|store|barcode", "Shopping & Marketing"),
        ("parcels?|shipping|tracking|logistics", "Tracking & Logistics"),
        ("github|json|developer|code|user ?agent|qr codes?|http|webhooks?|regex|screenshot|pdf|ip|dns|domain|whois|uuid|hash", "Development Tools"),
        ("events?|tickets?|concerts?", "Events"));

    private static readonly HashSet<string> Canonical = [.. CategoryRules.Select(r => r.Category)];

    /// <summary>The finished patterns, for the knowledge file the Android app is built with (tools: --export-knowledge).</summary>
    internal static (IEnumerable<(string Pattern, string Category)> ByCategory, IEnumerable<(string Pattern, string Category)> ByKeyword) Export() =>
        (CategoryRules.Select(r => (r.Rx.ToString(), r.Category)), KeywordRules.Select(r => (r.Rx.ToString(), r.Category)));

    /// <summary>Source categories that only hold a handful of APIs are folded into the canonical set.</summary>
    public static void FoldSmall(List<ApiEntry> entries, int minimum = 8)
    {
        foreach (var g in entries.GroupBy(e => e.Category).Where(g => !Canonical.Contains(g.Key) && g.Count() < minimum).ToList())
            foreach (var e in g)
            {
                var raw = e.RawCategory;
                e.RawCategory = "";
                Apply(e);
                e.RawCategory = raw;
            }
    }

    public static void Apply(ApiEntry e)
    {
        var raw = e.RawCategory.Trim();
        if (raw.Length > 0)
        {
            foreach (var (rx, cat) in CategoryRules)
                if (rx.IsMatch(raw))
                {
                    if (cat != "Other") { e.Category = cat; return; }
                    raw = ""; // "Miscellaneous" says nothing - let the description decide
                    break;
                }
        }

        var text = e.Name + " " + e.Description;
        string best = "";
        int bestScore = 0;
        foreach (var (rx, cat) in KeywordRules)
        {
            var score = rx.Matches(text).Count;
            if (score > bestScore) { best = cat; bestScore = score; }
        }
        if (bestScore > 0) { e.Category = best; return; }

        e.Category = raw.Length is > 0 and <= 40
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(raw.Replace('_', ' ').ToLowerInvariant())
            : "Other";
    }

    // prefix: stems such as "financ" match "finance"/"financial"; otherwise whole words only
    private static (Regex, string)[] Build(bool prefix, params (string Pattern, string Category)[] rules) =>
        [.. rules.Select(r => (new Regex(@"(?<![\p{L}])(?:" + r.Pattern + ")" + (prefix ? "" : E), RegexOptions.IgnoreCase | RegexOptions.Compiled), r.Category))];
}
