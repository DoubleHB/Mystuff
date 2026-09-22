import 'dart:convert';

import 'package:http/http.dart' as http;

import 'knowledge.dart';
import 'models.dart';

/// The places API Scout looks - the same five keyless directories as the desktop app's default set.
class SourceInfo {
  final String id, name, description, url;
  const SourceInfo(this.id, this.name, this.description, this.url);
}

const sources = <SourceInfo>[
  SourceInfo('public-apis', 'public-apis (GitHub)', 'The big community list - about 1,400 APIs with auth, HTTPS and CORS columns',
      'https://raw.githubusercontent.com/public-apis/public-apis/master/README.md'),
  SourceInfo('public-api-lists', 'public-api-lists (GitHub)', 'Actively maintained fork with many newer APIs',
      'https://raw.githubusercontent.com/public-api-lists/public-api-lists/master/README.md'),
  SourceInfo('marcelscruz', 'publicapis.dev', 'JSON database behind publicapis.dev - about 1,700 APIs',
      'https://raw.githubusercontent.com/marcelscruz/public-apis/main/db/resources.json'),
  SourceInfo('freepublicapis', 'freepublicapis.com', 'About 650 keyless APIs, health-tested daily', 'https://www.freepublicapis.com/api/apis?limit=5000'),
  SourceInfo('n0shake', 'n0shake/Public-APIs (GitHub)', 'Older list of big-name platform APIs', 'https://raw.githubusercontent.com/n0shake/Public-APIs/master/README.md'),
];

const _userAgent = 'Mozilla/5.0 (Android) ApiScoutMobile/1.0';

Future<String> fetchText(String url, {Duration timeout = const Duration(seconds: 60)}) async {
  final resp = await http.get(Uri.parse(url), headers: {'User-Agent': _userAgent, 'Accept': 'text/html,application/json,text/plain,*/*'}).timeout(timeout);
  if (resp.statusCode >= 400) throw Exception('answered ${resp.statusCode}');
  return utf8.decode(resp.bodyBytes, allowMalformed: true);
}

// ---------------------------------------------------------------- "awesome list" READMEs (MarkdownListParser)

final _headingRx = RegExp(r'^\s{0,3}(#{1,5})\s+(.+?)\s*#*\s*$');
final _linkRx = RegExp(r'\[\*{0,2}\s*([^\]]+?)\s*\*{0,2}\]\(\s*<?(https?://[^)\s>]+)>?[^)]*\)');
final _separatorRx = RegExp(r'^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$');
final _htmlOrImageRx = RegExp(r'<[^>]+>|!\[[^\]]*\]\([^)]*\)');
final _inlineLinkRx = RegExp(r'\[([^\]]*)\]\([^)]*\)');
final _headingJunkRx = RegExp(r"[^\p{L}\p{N}\s&/,+'.()-]", unicode: true);
final _spaceRx = RegExp(r'\s+');

const _skipHeadings = [
  'sponsor', 'apilayer', 'content', 'index', 'license', 'contribut', 'resource', 'related', 'about', 'credit', 'learn more', 'table of',
  'unverified', 'unreachable', 'deprecated', 'dead link', 'graveyard',
];

String cleanText(String s) {
  s = s.replaceAll(_htmlOrImageRx, '');
  s = s.replaceAllMapped(_inlineLinkRx, (m) => m[1] ?? '');
  s = s.replaceAll('**', '').replaceAll('`', '').replaceAll('&amp;', '&');
  return s.replaceAll(_spaceRx, ' ').trim();
}

AuthKind parseAuth(String raw) {
  final a = raw.trim().replaceAll('`', '').toLowerCase();
  if (a.isEmpty || a == 'no' || a == 'none' || a == '-' || a == 'null') return AuthKind.none;
  if (a.contains('oauth')) return AuthKind.oauth;
  if (a.contains('key') || a.contains('token') || a.contains('mashape')) return AuthKind.apiKey;
  if (a == 'unknown' || a == '?') return AuthKind.unknown;
  return AuthKind.other;
}

List<String> _splitRow(String line) {
  var t = line.trim();
  if (t.startsWith('|')) t = t.substring(1);
  if (t.endsWith('|')) t = t.substring(0, t.length - 1);
  return [for (final c in t.split('|')) c.trim()];
}

/// name, description, auth, https, cors, open/trial
List<int> _mapColumns(List<String> header) {
  final cols = [0, -1, -1, -1, -1, -1];
  for (var c = 0; c < header.length; c++) {
    final name = header[c].trim().replaceAll('*', '').toLowerCase();
    if (name == 'api' || name == 'name' || name == 'title' || name.startsWith('api ')) {
      cols[0] = c;
    } else if (name.startsWith('desc')) {
      cols[1] = c;
    } else if (name.startsWith('auth')) {
      cols[2] = c;
    } else if (name == 'https') {
      cols[3] = c;
    } else if (name == 'cors') {
      cols[4] = c;
    } else if (name.contains('trial') || name.contains('pricing')) {
      cols[5] = c;
    }
  }
  if (cols[1] < 0 && header.length > 1) cols[1] = cols[0] == 0 ? 1 : 0;
  return cols;
}

List<ApiEntry> parseMarkdownList(String markdown, String sourceName) {
  final list = <ApiEntry>[];
  final lines = markdown.replaceAll('\r', '').split('\n');
  var heading = '';
  var skip = false;
  List<int>? cols;
  String cell(List<String> cells, int i) => i >= 0 && i < cells.length ? cleanText(cells[i]) : '';

  for (var i = 0; i < lines.length; i++) {
    final line = lines[i];
    final h = _headingRx.firstMatch(line);
    if (h != null) {
      heading = cleanText(h[2]!).replaceAll(_headingJunkRx, '').replaceAll(_spaceRx, ' ').trim();
      final lower = heading.toLowerCase();
      skip = _skipHeadings.any(lower.contains);
      cols = null;
      continue;
    }
    if (skip) continue;

    if (line.contains('|')) {
      // the header row is the one directly above a |---|---| separator
      if (i + 1 < lines.length && _separatorRx.hasMatch(lines[i + 1]) && !_separatorRx.hasMatch(line)) {
        cols = _mapColumns(_splitRow(line));
        i++;
        continue;
      }
      if (cols == null || _separatorRx.hasMatch(line)) continue;
      final cells = _splitRow(line);
      if (cols[0] >= cells.length) continue;
      final link = _linkRx.firstMatch(cells[cols[0]]);
      if (link == null) continue;

      final url = link[2]!.trim();
      final authRaw = cell(cells, cols[2]);
      final corsRaw = cell(cells, cols[4]).toLowerCase();
      final httpsRaw = cell(cells, cols[3]).toLowerCase();
      final flag = cols[5] >= 0 && cols[5] < cells.length ? cells[cols[5]] : '';
      final entry = ApiEntry(
        name: cleanText(link[1]!),
        url: url,
        description: cell(cells, cols[1]),
        rawCategory: heading,
        authRaw: authRaw,
        auth: cols[2] < 0 ? AuthKind.unknown : parseAuth(authRaw),
        cors: corsRaw.startsWith('yes') ? 'Yes' : corsRaw.startsWith('no') ? 'No' : corsRaw.isEmpty ? '' : 'Unknown',
        https: httpsRaw.startsWith('yes') ? true : httpsRaw.startsWith('no') ? false : url.startsWith('https') ? true : null,
        // the markers are an emoji and an image, so the raw cell is read
        pricing: flag.contains('💸') || flag.toLowerCase().contains('paid') ? 'paid' : flag.toLowerCase().contains('open source') ? 'open' : '',
        sources: [sourceName],
      );
      if (entry.name.isNotEmpty) list.add(entry);
    } else if (line.trim().isNotEmpty && cols != null) {
      cols = null; // left the table
    }
  }
  return list;
}

// ---------------------------------------------------------------- JSON sources

String _str(dynamic map, String name) => map is Map && map[name] is String ? map[name] as String : '';

List<ApiEntry> parseMarcel(String json) {
  final root = jsonDecode(json);
  final entries = root is List ? root : (root is Map && root['entries'] is List ? root['entries'] as List : const []);
  final list = <ApiEntry>[];
  for (final e in entries) {
    final url = _str(e, 'Link');
    if (url.isEmpty) continue;
    final auth = _str(e, 'Auth');
    final cors = _str(e, 'Cors');
    list.add(ApiEntry(
      name: _str(e, 'API'),
      description: _str(e, 'Description'),
      url: url,
      rawCategory: _str(e, 'Category'),
      authRaw: auth,
      auth: parseAuth(auth),
      cors: cors == 'yes' ? 'Yes' : cors == 'no' ? 'No' : cors.isEmpty ? '' : 'Unknown',
      https: e is Map && e['HTTPS'] is bool ? e['HTTPS'] as bool : url.startsWith('https'),
      sources: ['publicapis.dev'],
    ));
  }
  return list;
}

List<ApiEntry> parseFreePublicApis(String json) {
  final root = jsonDecode(json);
  final list = <ApiEntry>[];
  for (final e in root is List ? root : const []) {
    var url = _str(e, 'documentation');
    if (url.isEmpty) url = _str(e, 'source');
    if (url.isEmpty) continue;
    final health = e is Map ? e['health'] : null;
    list.add(ApiEntry(
      name: _str(e, 'title'),
      description: _str(e, 'description'),
      url: url,
      auth: AuthKind.none, // the site only lists APIs usable without signing up
      authRaw: 'No (listed as keyless)',
      https: url.startsWith('https'),
      health: health is num ? health.round() : null,
      sources: ['freepublicapis.com'],
    ));
  }
  return list;
}

List<ApiEntry> parseSource(String id, String text) => switch (id) {
      'marcelscruz' => parseMarcel(text),
      'freepublicapis' => parseFreePublicApis(text),
      _ => parseMarkdownList(text, id),
    };

// ---------------------------------------------------------------- merge (Scanner.Merge / MakeKey)

/// Same docs URL = same API, ignoring scheme, www, query strings and trailing slashes.
String makeKey(String url, String name) {
  final u = Uri.tryParse(url);
  if (u == null || !u.hasScheme || u.host.isEmpty) return 'name:${name.toLowerCase()}';
  var host = u.host.toLowerCase();
  if (host.startsWith('www.')) host = host.substring(4);
  var path = u.path.toLowerCase();
  while (path.endsWith('/')) {
    path = path.substring(0, path.length - 1);
  }
  var key = host + path;
  // marketplaces and code hosts hold many APIs under one host, and some docs sites hold several under one page
  if (path.isEmpty || u.fragment.isNotEmpty) key += '#${u.fragment.toLowerCase()}';
  while (key.endsWith('#')) {
    key = key.substring(0, key.length - 1);
  }
  return key;
}

bool _isWebUrl(String url) {
  final u = Uri.tryParse(url);
  return u != null && (u.scheme == 'http' || u.scheme == 'https') && u.host.isNotEmpty;
}

List<ApiEntry> mergeEntries(Iterable<ApiEntry> entries, Knowledge knowledge) {
  final byKey = <String, ApiEntry>{};
  for (final e in entries) {
    if (e.name.isEmpty || !_isWebUrl(e.url)) continue;
    if (e.key.isEmpty) e.key = makeKey(e.url, e.name);
    final have = byKey[e.key];
    if (have == null) {
      byKey[e.key] = e;
      continue;
    }
    for (final s in e.sources) {
      if (!have.sources.contains(s)) have.sources.add(s);
    }
    if (have.auth == AuthKind.unknown && e.auth != AuthKind.unknown) {
      have.auth = e.auth;
      have.authRaw = e.authRaw;
    }
    if (have.description.length < 12 && e.description.length > have.description.length) have.description = e.description;
    if (have.rawCategory.isEmpty) have.rawCategory = e.rawCategory;
    if ((have.cors.isEmpty || have.cors == 'Unknown') && (e.cors == 'Yes' || e.cors == 'No')) have.cors = e.cors;
    if (have.pricing.isEmpty) have.pricing = e.pricing;
    have.https ??= e.https;
    have.health ??= e.health;
  }
  final list = byKey.values.toList();
  recategorise(list, knowledge);
  return list;
}

/// Categories from the current rules, then name order. Also run on a cached catalogue when the rules have changed
/// since it was scanned - no network needed, the raw source categories are kept in every entry.
void recategorise(List<ApiEntry> list, Knowledge knowledge) {
  for (final e in list) {
    knowledge.categorise(e);
  }
  knowledge.foldSmall(list);
  list.sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
}

/// Runs in a background isolate (see AppState.scan): parse every downloaded source, merge, categorise.
/// Everything crossing the isolate boundary is plain JSON-like data.
Map<String, dynamic> buildCatalogue(Map<String, dynamic> args) {
  final knowledge = Knowledge.parse(args['knowledge'] as String);
  final texts = (args['texts'] as Map).cast<String, String>();
  final notes = <String>[...(args['notes'] as List).cast<String>()];
  final all = <ApiEntry>[];
  // source order matters: earlier (better described) sources win ties
  for (final s in sources) {
    final text = texts[s.id];
    if (text == null) continue;
    try {
      final found = parseSource(s.id, text);
      notes.add('${s.name}: ${found.length} APIs');
      all.addAll(found);
    } catch (ex) {
      notes.add('${s.name}: failed - could not be read ($ex)');
    }
  }
  final merged = mergeEntries(all, knowledge);
  return {
    'entries': [for (final e in merged) e.toJson()],
    'notes': notes,
  };
}
