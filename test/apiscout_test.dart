// ApiScout Mobile self-check:  flutter test            (offline)
//                              flutter test --dart-define=LIVE=true   (also scans the real directories)
import 'dart:io';

import 'package:apiscout_mobile/knowledge.dart';
import 'package:apiscout_mobile/models.dart';
import 'package:apiscout_mobile/sources.dart';
import 'package:apiscout_mobile/tester.dart';
import 'package:flutter_test/flutter_test.dart';

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
