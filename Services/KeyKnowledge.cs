using ApiScout.Models;

namespace ApiScout.Services;

/// <summary>What ApiScout knows about getting a key for a particular provider.</summary>
public sealed record KeyHint(
    string[] Hosts,
    string HowTo,
    string? SignupUrl = null,
    string? DemoKey = null,
    string? KeyUsage = null,
    string? Example = null,
    bool KeylessWorks = false);

/// <summary>
/// Publicly documented demo/test keys (each one is printed in the provider's own docs and was
/// checked against the live API on 2026-09-21) plus sign-up pointers for popular free APIs.
/// Nothing here is a private or leaked credential.
/// </summary>
public static class KeyKnowledge
{
    public static readonly KeyHint[] Hints =
    [
        // ---- providers that publish a shared demo key ----
        new(["api.nasa.gov"], "DEMO_KEY works straight away (30 requests/hour, 50/day per IP). Fill in the short form on api.nasa.gov for your own free key (1,000 requests/hour) - it arrives by email instantly.",
            "https://api.nasa.gov/#signUp", "DEMO_KEY", "Query parameter: api_key=DEMO_KEY", "https://api.nasa.gov/planetary/apod?api_key=DEMO_KEY"),
        new(["api.open.fec.gov", "api.data.gov", "regulations.gov", "collegescorecard.ed.gov"], "US government APIs behind api.data.gov share one key system. DEMO_KEY works for light testing; one free sign-up gives a key valid across all api.data.gov services.",
            "https://api.data.gov/signup/", "DEMO_KEY", "Query parameter api_key=DEMO_KEY or header X-Api-Key", "https://api.open.fec.gov/v1/candidates/?per_page=1&api_key=DEMO_KEY"),
        new(["alphavantage.co"], "The 'demo' key only answers the exact sample queries in the docs (e.g. IBM). Claim a free key with just an email address - 25 requests/day.",
            "https://www.alphavantage.co/support/#api-key", "demo", "Query parameter: apikey=demo", "https://www.alphavantage.co/query?function=TIME_SERIES_DAILY&symbol=IBM&apikey=demo"),
        new(["twelvedata.com"], "The 'demo' key works for the symbols used in the docs (AAPL, EUR/USD…). The free Basic plan gives 800 credits/day after sign-up.",
            "https://twelvedata.com/register", "demo", "Query parameter: apikey=demo", "https://api.twelvedata.com/time_series?symbol=AAPL&interval=1day&outputsize=5&apikey=demo"),
        new(["eodhd.com", "eodhistoricaldata.com"], "The 'demo' token works for a handful of tickers (AAPL.US, TSLA.US, VTI.US, AMZN.US, BTC-USD.CC, EURUSD.FOREX). Free sign-up gives 20 calls/day on any ticker.",
            "https://eodhd.com/register", "demo", "Query parameter: api_token=demo", "https://eodhd.com/api/eod/AAPL.US?api_token=demo&fmt=json"),
        new(["thesportsdb.com"], "The free shared key is 123 (the older key 3 still answers too). It goes in the URL path. Patreon supporters get a private key with livescores and higher limits.",
            "https://www.thesportsdb.com/pricing", "123", "Part of the path: /api/v1/json/123/…", "https://www.thesportsdb.com/api/v1/json/123/searchteams.php?t=Arsenal"),
        new(["theaudiodb.com"], "The free shared test key is 123 (the older key 2 still answers). It goes in the URL path.",
            "https://www.theaudiodb.com/pricing", "123", "Part of the path: /api/v1/json/123/…", "https://www.theaudiodb.com/api/v1/json/123/search.php?s=coldplay"),
        new(["thecocktaildb.com"], "The free test key is 1, in the URL path. Fine for development and education; supporters get a production key.",
            "https://www.thecocktaildb.com/api.php", "1", "Part of the path: /api/json/v1/1/…", "https://www.thecocktaildb.com/api/json/v1/1/search.php?s=margarita"),
        new(["themealdb.com"], "The free test key is 1, in the URL path. Fine for development and education; supporters get a production key.",
            "https://www.themealdb.com/api.php", "1", "Part of the path: /api/json/v1/1/…", "https://www.themealdb.com/api/json/v1/1/search.php?s=pie"),
        new(["ocr.space"], "The key 'helloworld' is a public test key (heavily rate limited). Register for a free key with 25,000 requests/month.",
            "https://ocr.space/ocrapi/freekey", "helloworld", "Query parameter or header: apikey=helloworld", "https://api.ocr.space/parse/imageurl?apikey=helloworld&url=https://dl.a9t9.com/ocr/solarcell.jpg"),
        new(["europeana.eu"], "The key 'api2demo' is Europeana's public demo key. Request your own free key from the Europeana account page.",
            "https://pro.europeana.eu/pages/get-api", "api2demo", "Query parameter: wskey=api2demo", "https://api.europeana.eu/record/v2/search.json?wskey=api2demo&query=vermeer&rows=3"),
        new(["reqres.in"], "ReqRes publishes a free shared key for its fake-data API. Send it as a header.",
            "https://reqres.in/signup", "reqres-free-v1", "Header: x-api-key: reqres-free-v1", "https://reqres.in/api/users/2"),

        // ---- key optional: works without one ----
        new(["coingecko.com"], "Works without a key (roughly 5-15 calls/minute). A free Demo account gives a key with a steadier 30 calls/minute.", "https://www.coingecko.com/en/api/pricing", KeyUsage: "Header x-cg-demo-api-key", Example: "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=gbp", KeylessWorks: true),
        new(["api.github.com", "docs.github.com", "developer.github.com"], "Works without a token at 60 requests/hour. A free personal access token lifts that to 5,000/hour.", "https://github.com/settings/tokens", KeyUsage: "Header Authorization: Bearer <token>", Example: "https://api.github.com/repos/dotnet/wpf", KeylessWorks: true),
        new(["exchangerate-api.com", "open.er-api.com"], "The open endpoint needs no key (daily rates). A free account adds a key with 1,500 requests/month.", "https://app.exchangerate-api.com/sign-up", Example: "https://open.er-api.com/v6/latest/GBP", KeylessWorks: true),
        new(["ipinfo.io"], "Works without a token for light use. A free account gives 50,000 lookups/month.", "https://ipinfo.io/signup", KeyUsage: "Query parameter token=…", Example: "https://ipinfo.io/8.8.8.8/json", KeylessWorks: true),
        new(["thecatapi.com", "thedogapi.com"], "Image search works without a key (10 results max). A free key unlocks votes, favourites and bigger pages.", "https://thecatapi.com/signup", KeyUsage: "Header x-api-key", Example: "https://api.thecatapi.com/v1/images/search", KeylessWorks: true),
        new(["api.tfl.gov.uk", "tfl.gov.uk"], "Works without a key at a low rate. Register on the TfL API portal for a free app_key (500 requests/minute).", "https://api-portal.tfl.gov.uk/signup", KeyUsage: "Query parameter app_key=…", Example: "https://api.tfl.gov.uk/Line/Mode/tube/Status", KeylessWorks: true),
        new(["open.fda.gov", "api.fda.gov"], "No key needed for 1,000 requests/day. A free api.data.gov key lifts it to 120,000/day.", "https://open.fda.gov/apis/authentication/", KeyUsage: "Query parameter api_key=…", Example: "https://api.fda.gov/drug/label.json?limit=1", KeylessWorks: true),

        // ---- free key after sign-up ----
        new(["omdbapi.com"], "Free key (1,000 requests/day): enter your email on the API key page, then click the activation link they send.", "https://www.omdbapi.com/apikey.aspx", KeyUsage: "Query parameter apikey=…"),
        new(["openweathermap.org"], "Create a free account; a default key appears under 'API keys' and activates within a couple of hours. Free plan: 1,000 calls/day.", "https://home.openweathermap.org/users/sign_up", KeyUsage: "Query parameter appid=…"),
        new(["weatherapi.com"], "Free plan after sign-up; the key is shown on your dashboard immediately.", "https://www.weatherapi.com/signup.aspx", KeyUsage: "Query parameter key=…"),
        new(["newsapi.org"], "Free Developer key after registering (100 requests/day, localhost/dev use only).", "https://newsapi.org/register", KeyUsage: "Header X-Api-Key or query apiKey=…"),
        new(["themoviedb.org"], "Create a free account, then request an API key under Settings → API (choose Developer). Approval is instant.", "https://www.themoviedb.org/settings/api", KeyUsage: "Header Authorization: Bearer <read token> or query api_key=…"),
        new(["giphy.com"], "Create an app in the GIPHY developer dashboard to get a free beta key.", "https://developers.giphy.com/dashboard/", KeyUsage: "Query parameter api_key=…"),
        new(["unsplash.com"], "Register as a developer and create an application; the Access Key is shown on the app page (50 requests/hour in demo mode).", "https://unsplash.com/oauth/applications", KeyUsage: "Header Authorization: Client-ID <access key>"),
        new(["pexels.com"], "Free key: sign in and request one - it is issued instantly.", "https://www.pexels.com/api/new/", KeyUsage: "Header Authorization: <key>"),
        new(["pixabay.com"], "Sign in to Pixabay - your key is then printed inside the API documentation page itself.", "https://pixabay.com/api/docs/", KeyUsage: "Query parameter key=…"),
        new(["coinmarketcap.com"], "Free Basic plan (10,000 credits/month) after sign-up; the key is on the developer dashboard.", "https://pro.coinmarketcap.com/signup", KeyUsage: "Header X-CMC_PRO_API_KEY"),
        new(["etherscan.io"], "Create a free account, then add a key under 'API Keys' (5 calls/second).", "https://etherscan.io/myapikey", KeyUsage: "Query parameter apikey=…"),
        new(["finnhub.io"], "Free key shown on the dashboard right after registering (60 calls/minute).", "https://finnhub.io/register", KeyUsage: "Query parameter token=…"),
        new(["polygon.io"], "Free Basic plan (5 calls/minute, end-of-day data) after sign-up.", "https://polygon.io/dashboard/signup", KeyUsage: "Query parameter apiKey=…"),
        new(["financialmodelingprep.com"], "Free plan (250 requests/day) after sign-up; the key is on your dashboard.", "https://site.financialmodelingprep.com/register", KeyUsage: "Query parameter apikey=…"),
        new(["stlouisfed.org"], "Create a free FRED account and request a key - issued instantly.", "https://fredaccount.stlouisfed.org/apikeys", KeyUsage: "Query parameter api_key=…"),
        new(["spoonacular.com"], "Free plan (150 points/day) - sign up and copy the key from your profile.", "https://spoonacular.com/food-api/console#Dashboard", KeyUsage: "Query parameter apiKey=…"),
        new(["edamam.com"], "Sign up for the free Developer plan to get an app_id and app_key.", "https://developer.edamam.com/edamam-recipe-api", KeyUsage: "Query parameters app_id=…&app_key=…"),
        new(["last.fm"], "Create an API account - the key is shown immediately.", "https://www.last.fm/api/account/create", KeyUsage: "Query parameter api_key=…"),
        new(["spotify.com"], "Create an app in the Spotify developer dashboard to get a client id and secret, then use the OAuth client-credentials flow.", "https://developer.spotify.com/dashboard"),
        new(["googleapis.com", "developers.google.com", "cloud.google.com"], "Create a project in Google Cloud Console, enable the API you want, then create an API key (or OAuth client) under Credentials. Most Google APIs have a free quota.", "https://console.cloud.google.com/apis/credentials", KeyUsage: "Query parameter key=…"),
        new(["twitch.tv"], "Register an application in the Twitch developer console for a client id and secret.", "https://dev.twitch.tv/console/apps"),
        new(["reddit.com"], "Create a 'script' app under Reddit preferences → apps to get a client id and secret.", "https://www.reddit.com/prefs/apps"),
        new(["rawg.io"], "Free key (20,000 requests/month) from the API page once signed in.", "https://rawg.io/apidocs", KeyUsage: "Query parameter key=…"),
        new(["football-data.org"], "Register for the free tier (12 competitions, 10 calls/minute); the token arrives by email.", "https://www.football-data.org/client/register", KeyUsage: "Header X-Auth-Token"),
        new(["azure.com", "azure.microsoft.com", "learn.microsoft.com"], "An Azure management or service API. Create a free Azure account (12 months of free services plus always-free allowances; a card is needed for identity checks), register an app in Microsoft Entra ID, and call the API with an OAuth bearer token. Usage beyond the free allowances is billed.",
            "https://azure.microsoft.com/free/", KeyUsage: "Header Authorization: Bearer <Entra ID token>"),
        new(["aws.amazon.com", "docs.aws.amazon.com"], "An Amazon Web Services API. Create an AWS account (Free Tier: 12 months plus always-free allowances; a card is required), create an IAM user or role, and sign requests with Signature V4 - in practice use the AWS SDK or CLI rather than raw HTTP. Usage beyond the Free Tier is billed.",
            "https://aws.amazon.com/free/", KeyUsage: "AWS Signature V4 (access key id + secret) - use the AWS SDK"),
        new(["rapidapi.com"], "This API is sold through RapidAPI. One free RapidAPI account gives a single key that works for every API on the marketplace - subscribe to this API's free (Basic) plan, then copy the key from the code snippet panel.", "https://rapidapi.com/auth/sign-up", KeyUsage: "Headers X-RapidAPI-Key and X-RapidAPI-Host"),
        new(["apilayer.com", "ipstack.com", "marketstack.com", "weatherstack.com", "numverify.com", "fixer.io", "aviationstack.com", "currencylayer.com", "mediastack.com", "positionstack.com"], "An APILayer product: sign up for the free plan and the access key is on your dashboard.", "https://apilayer.com/signup", KeyUsage: "Query parameter access_key=… (or header apikey)"),
        new(["abstractapi.com"], "Each Abstract API has its own free key - sign up, pick the API, copy the key.", "https://app.abstractapi.com/users/signup", KeyUsage: "Query parameter api_key=…"),
        new(["mapbox.com"], "A default public token is created with your free account.", "https://account.mapbox.com/access-tokens/", KeyUsage: "Query parameter access_token=…"),
        new(["tomtom.com"], "Register on the TomTom developer portal; a key is created with your first app (2,500 free requests/day).", "https://developer.tomtom.com/user/register", KeyUsage: "Query parameter key=…"),
        new(["opencagedata.com"], "Free trial key (2,500 requests/day) after sign-up.", "https://opencagedata.com/users/sign_up", KeyUsage: "Query parameter key=…"),
        new(["locationiq.com"], "Free plan (5,000 requests/day); the token is on your dashboard.", "https://my.locationiq.com/register", KeyUsage: "Query parameter key=…"),
        new(["openrouteservice.org"], "Free key (2,000 requests/day) from the dev dashboard.", "https://openrouteservice.org/dev/#/signup", KeyUsage: "Header Authorization or query api_key=…"),
        new(["huggingface.co"], "Create a free account and generate an access token under Settings.", "https://huggingface.co/settings/tokens", KeyUsage: "Header Authorization: Bearer <token>"),
        new(["deepl.com"], "DeepL API Free: 500,000 characters/month. Needs a card for identity checks but is not charged.", "https://www.deepl.com/pro-api", KeyUsage: "Header Authorization: DeepL-Auth-Key <key>"),
        new(["wordnik.com"], "Sign up, then request a key from the developer page (can take a day or two).", "https://developer.wordnik.com/", KeyUsage: "Query parameter api_key=…"),
        new(["dictionaryapi.com"], "Merriam-Webster: register for up to two free keys (1,000 queries/day each).", "https://dictionaryapi.com/register/index", KeyUsage: "Query parameter key=…"),
        new(["marvel.com"], "Create a Marvel developer account for a public and private key; requests are signed with an md5 hash of ts+private+public.", "https://developer.marvel.com/account"),
        new(["petfinder.com"], "Create an account and request an API key and secret (OAuth client credentials).", "https://www.petfinder.com/developers/"),
        new(["calendarific.com"], "Free plan (500 requests/month) after sign-up.", "https://calendarific.com/signup", KeyUsage: "Query parameter api_key=…"),
        new(["iqair.com", "airvisual.com"], "Create a free Community key in the IQAir dashboard.", "https://dashboard.iqair.com/", KeyUsage: "Query parameter key=…"),
        new(["openaq.org"], "Register for a free key (required since API v3).", "https://explore.openaq.org/register", KeyUsage: "Header X-API-Key"),
        new(["company-information.service.gov.uk", "companieshouse.gov.uk"], "Register on the Companies House developer hub, create an application, then add a REST key. Free.", "https://developer.company-information.service.gov.uk/", KeyUsage: "HTTP Basic auth - key as the username, blank password"),
        new(["metoffice.gov.uk"], "Register on Met Office DataHub and subscribe to the free Site Specific plan (360 calls/day).", "https://datahub.metoffice.gov.uk/", KeyUsage: "Header apikey"),
        new(["ticketmaster.com"], "Register on the developer portal; a Consumer Key is issued instantly (5,000 calls/day).", "https://developer-acct.ticketmaster.com/user/register", KeyUsage: "Query parameter apikey=…"),
        new(["yelp.com"], "Create an app in Yelp Developers to get an API key.", "https://www.yelp.com/developers/v3/manage_app", KeyUsage: "Header Authorization: Bearer <key>"),
        new(["virustotal.com"], "Join the VirusTotal community - your free public key is on your profile (4 lookups/minute).", "https://www.virustotal.com/gui/join-us", KeyUsage: "Header x-apikey"),
        new(["shodan.io"], "Register for a free account; the key is on your account page.", "https://account.shodan.io/register", KeyUsage: "Query parameter key=…"),
        new(["abuseipdb.com"], "Free plan (1,000 checks/day) after registering.", "https://www.abuseipdb.com/register", KeyUsage: "Header Key"),
        new(["haveibeenpwned.com"], "Breach lookups by account need a paid key; the Pwned Passwords range API is free and keyless.", "https://haveibeenpwned.com/API/Key", KeyUsage: "Header hibp-api-key", Example: "https://api.pwnedpasswords.com/range/21BD1"),
    ];

    // Curated providers are freemium unless listed here (matched on the hint's first host)
    private static readonly HashSet<string> FullFreeHosts =
    [
        "api.nasa.gov", "api.open.fec.gov", "api.github.com", "api.tfl.gov.uk", "open.fda.gov", "themoviedb.org", "stlouisfed.org",
        "last.fm", "europeana.eu", "company-information.service.gov.uk", "openaq.org", "twitch.tv", "spotify.com", "marvel.com", "petfinder.com",
    ];
    private static readonly HashSet<string> TrialOnlyHosts = ["haveibeenpwned.com"];

    public static AccessLevel AccessFor(KeyHint hint) =>
        FullFreeHosts.Contains(hint.Hosts[0]) ? AccessLevel.FullFree :
        TrialOnlyHosts.Contains(hint.Hosts[0]) ? AccessLevel.TrialOnly : AccessLevel.FreeTier;

    public static KeyHint? Find(ApiEntry e)
    {
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant();
        foreach (var hint in Hints)
            foreach (var h in hint.Hosts)
                if (host == h || host.EndsWith("." + h, StringComparison.Ordinal)) return hint;
        return null;
    }

    public static string GenericHowTo(ApiEntry e) => e.Auth switch
    {
        AuthKind.None =>
            "No key needed - this API is listed as open. Call its endpoints directly.\n\n" +
            "Tip: send a descriptive User-Agent header and stay within any rate limit in the docs.",
        AuthKind.ApiKey when e.AuthRaw.Contains("mashape", StringComparison.OrdinalIgnoreCase) =>
            "This API is served through RapidAPI.\n\n1. Create a free RapidAPI account.\n2. Open the API's page and subscribe to its free (Basic) plan.\n3. Copy your key from the code snippet panel.\n4. Send it as the X-RapidAPI-Key header, with X-RapidAPI-Host.",
        AuthKind.ApiKey =>
            "This API needs an API key. The usual route:\n\n" +
            "1. Open the docs (button above) and look for 'Sign up', 'Get API key', 'Dashboard' or 'Pricing'.\n" +
            "2. Create a free account - pick the Free / Developer / Hobby plan.\n" +
            "3. Copy the key from your dashboard (sometimes it arrives by email).\n" +
            "4. Send it the way the docs say - normally a query parameter (?api_key=…) or a header (X-Api-Key / Authorization: Bearer …).\n\n" +
            "Press 'Scan docs for key info' to look for the sign-up link, free-tier limits and any sample key on the docs page.",
        AuthKind.OAuth =>
            "This API uses OAuth.\n\n" +
            "1. Open the docs and find the developer portal / 'Create an app' page.\n" +
            "2. Register an application to get a client id and client secret.\n" +
            "3. Server-to-server data: use the client-credentials flow to swap id + secret for a bearer token.\n" +
            "   Acting for a user: use the authorization-code flow with a redirect URL.\n" +
            "4. Send the token as Authorization: Bearer <token>; refresh it when it expires.",
        AuthKind.Other =>
            $"The directory lists authentication as '{e.AuthRaw}'.\n\n" +
            (e.AuthRaw.Contains("agent", StringComparison.OrdinalIgnoreCase)
                ? "No key: just send a descriptive User-Agent header (app name + contact) with every request."
                : "Check the docs for what to send. Press 'Scan docs for key info' for a quick look."),
        _ =>
            "The directory that listed this API does not say whether a key is needed.\n\n" +
            "Press 'Scan docs for key info' - ApiScout reads the docs page (and the OpenAPI spec when there is one) " +
            "and reports the auth scheme, sign-up links, free-tier notes and any sample key it finds.",
    };
}
