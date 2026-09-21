// ApiScout Mobile self-check:  flutter test            (offline)
//                              flutter test --dart-define=LIVE=true   (also scans the real directories)
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:apiscout_mobile/insight.dart';
import 'package:apiscout_mobile/knowledge.dart';
import 'package:apiscout_mobile/models.dart';
import 'package:apiscout_mobile/sources.dart';
import 'package:apiscout_mobile/tester.dart';
import 'package:apiscout_mobile/user_data.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pointycastle/export.dart';

const live = bool.fromEnvironment('LIVE');

void main() {
  final knowledgeJson = File('assets/knowledge.json').readAsStringSync();
  final k = Knowledge.parse(knowledgeJson);

  test('the desktop app\'s knowledge file loads: every .NET pattern is a valid Dart pattern', () {
    expect(k.categoryRules.length, greaterThan(35));
    expect(k.keywordRules.length, greaterThan(30));
    expect(k.hints.length, greaterThan(60));
    expect(k.exportedFrom, startsWith('ApiScout '));
  });

  test('categories: source names fold into the canonical set, stems stay strict', () {
    String cat(String raw, [String name = 'X', String desc = '']) {
      final e = ApiEntry(name: name, url: 'https://x.example', rawCategory: raw, description: desc);
      k.categorise(e);
      return e.category;
    }

    expect(cat('Cryptocurrency'), 'Blockchain & Crypto');
    expect(cat('Finance'), 'Currency & Finance');
    expect(cat('Business'), 'Business'); // "bus" must not make it Transport
    expect(cat('Transportation'), 'Transport & Travel');
    expect(cat('Miscellaneous', 'Cat Facts', 'Daily cat facts'), 'Animals'); // says nothing, so the description decides
    expect(cat('', 'Open-Meteo', 'Global weather forecast API'), 'Weather');
    expect(cat('', 'Zzz', 'nothing to go on'), 'Other');
    expect(cat('Émissions', 'Zzz', ''), 'Émissions');
  });

  const readme = '''
# Public APIs
## Index
* [Animals](#animals)
### Animals
API | Description | Auth | HTTPS | CORS |
|:---|:---|:---|:---|:---|
| [Cat Facts](https://catfact.ninja/) | Daily cat facts | No | Yes | No |
| [**Dogs**](https://dog.ceo/dog-api/) | Based on the <b>Stanford</b> Dogs Dataset | `apiKey` | Yes | Yes |
| Broken row without a link | x | No | Yes | No |

### Finance
| API | Description | Auth | HTTPS | CORS |
|---|---|---|---|---|
| [Paid Thing](http://paid.example/docs) | Market data | `OAuth` | No | Unknown |
''';

  test('awesome-list tables: links, auth, https, cors; the index and broken rows are skipped', () {
    final list = parseMarkdownList(readme, 'public-apis');
    expect(list.map((e) => e.name), ['Cat Facts', 'Dogs', 'Paid Thing']);
    expect(list[0].auth, AuthKind.none);
    expect(list[0].cors, 'No');
    expect(list[1].auth, AuthKind.apiKey);
    expect(list[1].description, 'Based on the Stanford Dogs Dataset');
    expect(list[1].rawCategory, 'Animals');
    expect(list[2].auth, AuthKind.oauth);
    expect(list[2].https, false);
    expect(list[2].cors, 'Unknown');
  });

  test('JSON sources, and links that are not web pages', () {
    final marcel = parseMarcel('{"entries":[{"API":"Cat Facts","Description":"Cat facts every day of the year","Auth":"","HTTPS":true,"Cors":"no","Link":"https://www.catfact.ninja","Category":"Animals"}]}');
    expect(marcel.single.auth, AuthKind.none);
    final free = parseFreePublicApis('[{"title":"Null health","description":"d","documentation":"https://n.example","health":null},{"title":"Ftp","documentation":"ftp://f.example"}]');
    expect(free.first.health, isNull);
    final merged = mergeEntries([...parseMarkdownList(readme, 'public-apis'), ...marcel, ...free], k);
    expect(merged.map((e) => e.name), ['Cat Facts', 'Dogs', 'Null health', 'Paid Thing']); // ftp dropped, Cat Facts merged (www, trailing slash)
    final cat = merged.first;
    expect(cat.sources, ['public-apis', 'publicapis.dev']);
    expect(cat.category, 'Animals');
  });

  test('same docs URL = same API', () {
    expect(makeKey('https://www.Example.com/docs/', 'A'), makeKey('http://example.com/docs?utm=1', 'B'));
    expect(makeKey('https://rapidapi.com/#one', 'A'), isNot(makeKey('https://rapidapi.com/#two', 'A')));
    expect(makeKey('not a url', 'Name'), 'name:name');
  });

  test('key knowledge: demo keys, access levels, how-to', () {
    final nasa = ApiView(ApiEntry(name: 'NASA', url: 'https://api.nasa.gov', auth: AuthKind.apiKey), k);
    expect(nasa.demoKey, 'DEMO_KEY');
    expect(nasa.keyBadge, 'Demo key');
    expect(nasa.access, AccessLevel.fullFree);
    expect(nasa.example, contains('api_key=DEMO_KEY'));
    expect(ApiView(ApiEntry(name: 'JPL', url: 'https://ssd-api.jpl.nasa.gov/doc/cad.html', auth: AuthKind.none), k).hasDemoKey, isFalse);

    final open = ApiView(ApiEntry(name: 'Open', url: 'https://open.example', auth: AuthKind.none), k);
    expect([open.keyBadge, open.accessLabel], ['Open', 'Full free access']);
    final plain = ApiView(ApiEntry(name: 'Foo', url: 'https://foo.example/docs', auth: AuthKind.apiKey), k);
    expect([plain.keyBadge, plain.accessLabel], ['Key needed', 'Not stated']);
    expect(plain.howTo, contains('Create a free account'));
    expect(ApiView(ApiEntry(name: 'T', url: 'https://t.example', auth: AuthKind.apiKey, description: 'Has a free tier'), k).access, AccessLevel.freeTier);
    expect(ApiView(ApiEntry(name: 'P', url: 'https://p.example', auth: AuthKind.apiKey, pricing: 'paid'), k).access, AccessLevel.trialOnly);
  });

  test('tester helpers: headers, content type, pretty JSON, curl', () {
    String? bad;
    expect(parseHeaders('X-Api-Key: abc\n\nAccept: text/plain', (b) => bad = b), {'X-Api-Key': 'abc', 'Accept': 'text/plain'});
    expect(parseHeaders('no colon here', (b) => bad = b), isNull);
    expect(bad, 'no colon here');
    expect(guessContentType('{"a":1}'), 'application/json');
    expect(guessContentType('a=b&c=d'), 'application/x-www-form-urlencoded');
    expect(guessContentType('<x/>'), 'application/xml');
    expect(formatBody('{"a":[1,2]}'), ('{\n  "a": [\n    1,\n    2\n  ]\n}', true));
    expect(formatBody('<html>').$2, isFalse);
    expect(toCurl('POST', 'https://a.example/it\'s', 'X-K: v', '{"a":1}'),
        "curl -X POST 'https://a.example/it'\\''s' \\\n  -H 'X-K: v' \\\n  -H 'Content-Type: application/json' \\\n  --data-raw '{\"a\":1}'");
  });

  test('the background-isolate entry point builds a catalogue from downloaded texts', () {
    final result = buildCatalogue({
      'knowledge': knowledgeJson,
      'texts': {'public-apis': readme, 'marcelscruz': 'not json at all'},
      'notes': <String>[],
    });
    expect((result['entries'] as List).length, 3);
    expect((result['notes'] as List).cast<String>().where((n) => n.contains('failed')).length, 1);
  });

  test('{key}: filled in when sending, escaped in the URL only', () {
    expect(usesKeyPlaceholder('https://a.example/?k={KEY}'), isTrue);
    expect(usesKeyPlaceholder('https://a.example/?k=key'), isFalse);
    expect(fillKey('https://a.example/?k={key}&again={Key}', 'a+b/c', escape: true), 'https://a.example/?k=a%2Bb%2Fc&again=a%2Bb%2Fc');
    expect(fillKey('Authorization: Bearer {key}', 'a+b/c'), 'Authorization: Bearer a+b/c');
    expect(toCurl('GET', 'https://a.example/?k={key}', '', ''), contains('{key}')); // a copied command never carries the key
  });

  test('tags: parsed, tidied, counted', () {
    expect(parseTags(' weather, Side  Project ;#maps,, WEATHER\nnew'), ['weather', 'Side Project', 'maps', 'new']);
    expect(parseTags(List.generate(12, (i) => 't$i').join(',')).length, 8);
    expect(parseTags('x' * 50).single.length, 30);
    final u = UserData()..tags = {'a': ['Maps', 'x'], 'b': ['maps'], 'c': ['y']};
    expect(u.tagCounts.first, ('Maps', 2));
    expect(UserData.fromJson(jsonDecode(jsonEncode(u.toJson())) as Map<String, dynamic>).tags['a'], ['Maps', 'x']);
    expect(UserData.fromJson({'favourites': null, 'tags': {'a': 'not a list'}, 'notes': 5}).tags, isEmpty); // a damaged file must not stop the app
  });

  final desktopExport = File('test/fixtures/desktop-export.json').readAsStringSync();

  test('a real desktop export opens: clear part, and the keys with the passphrase', () {
    final file = BackupFile.read(desktopExport);
    expect(file.data.favourites, {'url:api.nasa.gov', 'url:catfact.ninja'});
    expect(file.data.tags['url:api.nasa.gov'], ['space', 'Side Project']);
    expect(file.data.notes['url:api.nasa.gov'], contains('“curly quotes” and ünïcödé'));
    expect(file.data.collections['Weather stuff'], ['url:open-meteo.com', 'url:api.nasa.gov']);
    expect([file.hasSecrets, file.secretCount, file.iterations], [true, 2, 310000]);
    expect(file.exportedAt, isNotNull);

    final watch = Stopwatch()..start();
    final keys = file.openKeys('correct horse 42');
    // ignore: avoid_print
    print('310,000 rounds of key stretching took ${watch.elapsedMilliseconds} ms here');
    expect(keys, {'url:api.nasa.gov': 'nasa-Key+with/odd=chars&42', 'url:openweathermap.org/api': '0123456789abcdef0123456789abcdef'});
    expect(() => file.openKeys('correct horse 43'), throwsA(isA<WrongPassphraseException>()));
    // the app's own key stretching against the library's, including a passphrase longer than one hash block
    for (final pass in ['p', 'ünï pass', 'x' * 64, 'y' * 65, 'z' * 200]) {
      final p = Uint8List.fromList(utf8.encode(pass)), salt = Uint8List.fromList(List.generate(16, (i) => i * 7));
      final reference = (PBKDF2KeyDerivator(HMac(SHA256Digest(), 64))..init(Pbkdf2Parameters(salt, 37, 32))).process(p);
      expect(pbkdf2Sha256(p, salt, 37), reference, reason: 'passphrase of ${p.length} bytes');
      expect(pbkdf2Sha256(p, salt, 1), (PBKDF2KeyDerivator(HMac(SHA256Digest(), 64))..init(Pbkdf2Parameters(salt, 1, 32))).process(p));
    }
    expect(() => BackupFile.read('{"App":"SomethingElse"}'), throwsA(isA<BackupFormatException>()));
    expect(() => BackupFile.read('not json'), throwsA(isA<BackupFormatException>()));
    expect(() => BackupFile.read(desktopExport.replaceFirst('310000', '2000000000')), throwsA(isA<BackupFormatException>()));
  });

  test('import merges and never overwrites; what the phone exports reads back', () {
    final mine = UserData()
      ..favourites = {'url:catfact.ninja'}
      ..tags = {'url:api.nasa.gov': ['SPACE', 'mine']}
      ..notes = {'url:api.nasa.gov': 'my own note'}
      ..collections = {'weather STUFF': ['url:wttr.in']};
    final s = mergeUserData(mine, BackupFile.read(desktopExport).data);
    expect([s.favourites, s.tagged, s.notes, s.keptLocal, s.collectionEntries], [1, 1, 0, 1, 2]);
    expect(mine.tags['url:api.nasa.gov'], ['SPACE', 'mine', 'Side Project']);
    expect(mine.notes['url:api.nasa.gov'], 'my own note');
    expect(mine.collections, {'weather STUFF': ['url:wttr.in', 'url:open-meteo.com', 'url:api.nasa.gov']});
    expect(mergeUserData(mine, BackupFile.read(desktopExport).data).toString(), startsWith('Imported 0 favourite(s)')); // a second import adds nothing

    final clear = BackupFile.write(mine, keys: {'k': 'secret-value-1'}); // no passphrase: the key must stay out
    expect(clear.contains('secret-value-1') || BackupFile.read(clear).hasSecrets, isFalse);
    final sealed = BackupFile.write(mine, keys: {'k': 'secret-value-1'}, passphrase: 'pass phrase', iterations: 1000);
    expect(sealed.contains('secret-value-1'), isFalse);
    final back = BackupFile.read(sealed);
    expect(back.openKeys('pass phrase'), {'k': 'secret-value-1'});
    expect(back.data.collections, mine.collections);
    if (Platform.environment['APISCOUT_PHONE_EXPORT'] case final out?) {
      // build-apk.ps1 hands this file to the desktop app's real import, to prove the other direction
      File(out).writeAsStringSync(BackupFile.write(mine, keys: {'url:api.nasa.gov': 'phone-Key+/=&1'}, passphrase: 'from the phone'));
    }
  });

  test('more about this API: benefits, HTML and README reading', () {
    final nasa = ApiView(ApiEntry(name: 'NASA', url: 'https://api.nasa.gov', auth: AuthKind.apiKey, https: true, cors: 'No', health: 97, sources: ['a', 'b', 'c']), k);
    final b = benefits(nasa);
    expect(b.first, contains('demo key (DEMO_KEY)'));
    expect(b, containsAll([contains('Completely free'), contains('HTTPS:'), contains('No CORS'), contains('Reliable: 97%'), contains('listed by 3')]));

    final info = ApiInfo('');
    readHtml('''<html><head><title>Cat Facts &amp; more</title><meta name="description" content="A free API that serves one random cat fact at a time &#8212; no key."></head>
<body><nav><ul><li>Home page of the whole site here</li></ul></nav><script>var li = "<li>not a real feature item</li>";</script>
<h1>Cat Facts</h1><p>short</p><p>Cat Facts gives you a random fact about cats every time you ask, in JSON, with no sign-up at all and no rate limit worth mentioning.</p>
<h2>Features</h2><ul><li>Random facts in plain JSON</li><li>Breeds list with country of origin</li><li>Login</li></ul>
<h2>Pricing</h2><p>We use cookies to improve your experience on this site and to show you the privacy policy every day.</p></body></html>''', info);
    expect(info.title, 'Cat Facts & more');
    expect(info.summary, startsWith('A free API that serves one random cat fact at a time — no key.'));
    expect(info.summary, contains('every time you ask'));
    expect(info.summary, isNot(contains('cookies')));
    expect(info.features, ['Random facts in plain JSON', 'Breeds list with country of origin']);
    expect(info.sections, ['Cat Facts', 'Features']);

    final md = ApiInfo('');
    readMarkdown('# Dog API\n\n![badge](x.png)\n\nThe internet\'s biggest collection of **open source** dog pictures, served as JSON by a [tiny API](https://x.example).\n\n'
        '## Features\n- Over 20,000 images of dogs\n- Breed and sub-breed lists\n- ok\n\n```\n- not a feature, this is inside a code block\n```\n## License\nMIT', md);
    expect(md.title, 'Dog API');
    expect(md.summary, contains('open source dog pictures, served as JSON by a tiny API.'));
    expect(md.features, ['Over 20,000 images of dogs', 'Breed and sub-breed lists']);
    expect(md.sections, ['Features']);
    expect(insightMarkdown(nasa, md), allOf(contains('### At a glance'), contains('- Over 20,000 images')));

    expect(htmlDecode('&lt;a&gt; &#x41;&#66; &unknown; &nbsp;|'), '<a> AB &unknown;  |');
    for (final bad in ['http://localhost/x', 'http://192.168.1.1/', 'http://10.0.0.2', 'http://[::1]/', 'ftp://a.example', 'http://printer/', 'http://nas.local/api']) {
      expect(isPublicWebUrl(bad), isFalse, reason: bad);
    }
    expect(isPublicWebUrl('https://api.nasa.gov/planetary'), isTrue);
  });

  test('LIVE: the five real directories scan into a few thousand categorised APIs', () async {
    HttpOverrides.global = null; // flutter test blocks real HTTP by default
    final texts = <String, String>{};
    for (final s in sources) {
      texts[s.id] = await fetchText(s.url);
    }
    final result = buildCatalogue({'knowledge': knowledgeJson, 'texts': texts, 'notes': <String>[]});
    final entries = [for (final e in result['entries'] as List) ApiEntry.fromJson((e as Map).cast<String, dynamic>())];
    final views = [for (final e in entries) ApiView(e, k)];
    // ignore: avoid_print
    print('${entries.length} APIs, ${entries.map((e) => e.category).toSet().length} categories, '
        '${views.where((v) => v.hasDemoKey).length} with a demo key, other: ${entries.where((e) => e.category == 'Other').length}');
    expect(entries.length, greaterThan(3000));
    expect(entries.where((e) => e.category == 'Other').length, lessThan(entries.length * 0.1));
    expect(views.where((v) => v.hasDemoKey).length, greaterThan(10));
    expect((result['notes'] as List).where((n) => (n as String).contains('failed')), isEmpty);

    final r = await sendTest('GET', 'https://www.thecocktaildb.com/api/json/v1/1/search.php?s=margarita', '', '');
    expect(r.ok && r.isJson, isTrue, reason: r.summary);
  }, skip: !live, timeout: const Timeout(Duration(minutes: 3)));
}
