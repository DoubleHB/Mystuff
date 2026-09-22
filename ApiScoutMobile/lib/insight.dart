import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart' show compute;
import 'package:http/http.dart' as http;

import 'knowledge.dart';
import 'models.dart';

/// What the provider's own page says about an API: a summary, its feature list, its main sections.
class ApiInfo {
  String source;
  String title = '';
  String summary = '';
  List<String> features = [];
  List<String> sections = [];
  String? error;
  ApiInfo(this.source);
  bool get isThin => summary.isEmpty && features.isEmpty;
}

// "More about this API" - the desktop app's ApiInsight, with the same two halves: benefits() is worked out from what
// ApiScout already knows; readInsight() reads the provider's docs page (or the README for an API that lives on GitHub).
// Nothing is invented: what the page does not say is not shown.

final _noiseRx = RegExp(r'<(script|style|noscript|svg|nav|footer|header|form|aside)\b.*?</\1>', caseSensitive: false, dotAll: true);
final _metaRx = RegExp(r'''<meta\b[^>]*?(?:name|property)\s*=\s*["'](?:og:|twitter:)?description["'][^>]*>''', caseSensitive: false);
final _contentRx = RegExp(r'''content\s*=\s*(?:"([^"]*)"|'([^']*)')''', caseSensitive: false);
final _titleRx = RegExp(r'<title\b[^>]*>(.*?)</title>', caseSensitive: false, dotAll: true);
final _headingRx = RegExp(r'<h([1-3])\b[^>]*>(.*?)</h\1>', caseSensitive: false, dotAll: true);
final _paragraphRx = RegExp(r'<p\b[^>]*>(.*?)</p>', caseSensitive: false, dotAll: true);
final _itemRx = RegExp(r'<li\b[^>]*>(.*?)</li>', caseSensitive: false, dotAll: true);
final _tagRx = RegExp(r'<[^>]+>');
final _featureHeadingRx =
    RegExp(r'\b(features?|what (you|it) can|capabilit|highlights|why |benefits|overview|what is|about|use cases|endpoints|resources|available data)\b', caseSensitive: false);
final _navWordsRx = RegExp(
    r'^(home|login|log in|sign ?up|sign in|pricing|blog|contact|docs?|documentation|menu|search|privacy|terms|cookies?|table of contents|contents|navigation|license|contributing|installation|install|changelog|support|faq|footer|share|follow us)\b',
    caseSensitive: false);
final _gitHubRx = RegExp(r'^https?://github\.com/([^/\s]+)/([^/#?\s]+)', caseSensitive: false);
final _legalRx =
    RegExp(r'cookie|all rights reserved|privacy policy|terms of (use|service)|©|subscribe to our|javascript (is|must be) (disabled|enabled)', caseSensitive: false);
final _entityRx = RegExp(r'&(#x[0-9a-fA-F]{1,6}|#\d{1,7}|[a-zA-Z]{2,8});');
final _spacesRx = RegExp(r'\s+');

const _entities = {
  'amp': '&', 'lt': '<', 'gt': '>', 'quot': '"', 'apos': "'", 'nbsp': ' ', 'mdash': '—', 'ndash': '–', 'hellip': '…', 'rsquo': '’', 'lsquo': '‘',
  'ldquo': '“', 'rdquo': '”', 'copy': '©', 'reg': '®', 'trade': '™', 'middot': '·', 'bull': '•', 'euro': '€', 'pound': '£', 'deg': '°', 'times': '×',
  'rarr': '→', 'larr': '←', 'laquo': '«', 'raquo': '»',
};

String htmlDecode(String s) => s.replaceAllMapped(_entityRx, (m) {
      final e = m.group(1)!;
      if (e.startsWith('#')) {
        final code = e[1] == 'x' || e[1] == 'X' ? int.tryParse(e.substring(2), radix: 16) : int.tryParse(e.substring(1));
        return code != null && code > 0 && code <= 0x10FFFF && (code < 0xD800 || code > 0xDFFF) ? String.fromCharCode(code) : m.group(0)!;
      }
      return _entities[e] ?? _entities[e.toLowerCase()] ?? m.group(0)!;
    });

String _text(String html) => htmlDecode(html.replaceAll(_tagRx, ' ')).replaceAll(_spacesRx, ' ').trim();
bool _looksLikeLegal(String t) => _legalRx.hasMatch(t);
bool _isFeature(String t) => t.length >= 18 && t.length <= 220 && ' '.allMatches(t).length >= 2 && !_navWordsRx.hasMatch(t) && !_looksLikeLegal(t);

bool _same(String a, String b) {
  if (a.isEmpty || b.isEmpty) return false;
  final al = a.toLowerCase(), bl = b.toLowerCase();
  return al.startsWith(bl.substring(0, bl.length < 40 ? bl.length : 40)) || bl.startsWith(al.substring(0, al.length < 40 ? al.length : 40));
}

List<String> _distinct(Iterable<String> items, int take, {bool ignoreCase = true}) {
  final seen = <String>{};
  final list = <String>[];
  for (final i in items) {
    if (seen.add(ignoreCase ? i.toLowerCase() : i)) list.add(i);
    if (list.length == take) break;
  }
  return list;
}

/// Plain facts turned into "what is in it for you" lines.
List<String> benefits(ApiView v) {
  final e = v.entry;
  final list = <String>[];
  if (v.hasDemoKey) {
    list.add('Try it right now: the provider publishes a demo key (${v.demoKey}) - no sign-up to see real data.');
  } else if (e.auth == AuthKind.none) {
    list.add('No key and no sign-up: you can call it straight away, from code or the browser.');
  } else if (v.keylessWorks) {
    list.add('Works without a key; a free key only lifts the limits.');
  } else if (e.auth == AuthKind.apiKey) {
    list.add(v.hint?.signupUrl != null ? 'Needs a free API key - the sign-up link is on the Keys card.' : 'Needs an API key - \'Scan docs for key info\' looks for the sign-up page.');
  } else if (e.auth == AuthKind.oauth) {
    list.add('Uses OAuth: more set-up (an app registration), but it can act on behalf of your users.');
  }
  list.add(switch (v.access) {
    AccessLevel.fullFree => 'Completely free: everything it offers, not just a sample.',
    AccessLevel.freeTier => 'Free tier: free to keep using within limits; paid plans only matter if you outgrow them.',
    AccessLevel.trialOnly => 'Careful: only a demo or trial is free - real use needs a paid plan.',
    AccessLevel.unknown => 'What is free is not stated by the directories - \'Scan docs for key info\' can usually find out.',
  });
  if (e.https == true) {
    list.add('HTTPS: requests and any key you send are encrypted in transit.');
  } else if (e.https == false) {
    list.add('No HTTPS: fine for public data, but do not send a secret key over it.');
  }
  if (e.cors == 'Yes') {
    list.add('CORS enabled: callable directly from JavaScript in a web page, no proxy server needed.');
  } else if (e.cors == 'No') {
    list.add('No CORS: call it from a server or an app - a browser page would need a proxy.');
  }
  if (e.health != null) {
    list.add(e.health! >= 90
        ? 'Reliable: ${e.health}% in freepublicapis.com\'s daily health tests.'
        : 'Health ${e.health}% in freepublicapis.com\'s daily tests - check it answers before building on it.');
  }
  if (v.example != null) list.add('ApiScout knows a request that works as it is - press Test this API.');
  if (e.sources.length >= 3) list.add('Well known: listed by ${e.sources.length} independent directories.');
  return list;
}

void readHtml(String html, ApiInfo info) {
  info.title = _text(_titleRx.firstMatch(html)?.group(1) ?? '');
  var meta = '';
  for (final m in _metaRx.allMatches(html)) {
    final c = _contentRx.firstMatch(m.group(0)!);
    if (c == null) continue;
    final t = _text(c.group(1) ?? c.group(2) ?? '');
    if (t.length >= 30 && t.length > meta.length) meta = t;
  }
  final body = html.replaceAll(_noiseRx, ' ');

  final paragraphs = _distinct(
      _paragraphRx.allMatches(body).map((m) => _text(m.group(1)!)).where((t) => t.length >= 60 && t.length <= 700 && t.contains(' ') && !_looksLikeLegal(t)), 3,
      ignoreCase: false);
  info.summary = [meta, ...paragraphs.where((p) => !_same(p, meta))].where((s) => s.isNotEmpty).take(3).join('\n\n');

  final headings = [
    for (final m in _headingRx.allMatches(body)) (m.start, _text(m.group(2)!))
  ].where((h) => h.$2.length >= 3 && h.$2.length <= 70 && !_navWordsRx.hasMatch(h.$2)).toList();
  info.sections = _distinct(headings.map((h) => h.$2), 12);

  // list items under a "Features"-like heading first, otherwise the first decent list items on the page
  final items = [for (final m in _itemRx.allMatches(body)) (m.start, _text(m.group(1)!))].where((i) => _isFeature(i.$2)).toList();
  final under = headings.where((h) => _featureHeadingRx.hasMatch(h.$2)).expand((h) => items.where((i) => i.$1 > h.$1 && i.$1 < h.$1 + 6000)).map((i) => i.$2);
  info.features = _distinct([...under, ...items.map((i) => i.$2)], 10);
}

final _fenceRx = RegExp(r'```.*?```', dotAll: true);
final _mdImageRx = RegExp(r'!\[[^\]]*\]\([^)]*\)');
final _mdLinkRx = RegExp(r'\[([^\]]+)\]\([^)]*\)');
final _numberedRx = RegExp(r'^\d+\.\s');
final _hashesRx = RegExp(r'^[# ]+');

void readMarkdown(String markdown, ApiInfo info) {
  final lines = markdown.replaceAll(_fenceRx, ' ').replaceAll('\r', '').split('\n');
  String clean(String s) =>
      _text(s.replaceAll(_mdImageRx, '').replaceAllMapped(_mdLinkRx, (m) => m.group(1)!).replaceAll('**', '').replaceAll('`', '').replaceAll('__', ''));

  info.title = clean(lines.firstWhere((l) => l.startsWith('# '), orElse: () => '')).replaceFirst(_hashesRx, '');
  info.sections = _distinct(
      lines
          .where((l) => l.startsWith('## ') || l.startsWith('### '))
          .map((l) => clean(l.replaceFirst(_hashesRx, '')))
          .where((t) => t.length >= 3 && t.length <= 70 && !_navWordsRx.hasMatch(t)),
      12,
      ignoreCase: false);

  final paragraphs = <String>[];
  final current = <String>[];
  for (final line in [...lines, '']) {
    final t = line.trim();
    final prose = t.isNotEmpty &&
        !t.startsWith('#') && !t.startsWith('|') && !t.startsWith('<') && !t.startsWith('[!') && !t.startsWith('- ') && !t.startsWith('* ') && !t.startsWith('>') &&
        !_numberedRx.hasMatch(t);
    if (prose) {
      current.add(t);
      continue;
    }
    if (current.isNotEmpty) {
      final p = clean(current.join(' '));
      if (p.length >= 60 && p.length <= 900 && !_looksLikeLegal(p)) paragraphs.add(p);
    }
    current.clear();
  }
  info.summary = paragraphs.take(3).join('\n\n');

  final bullets = <(int, String)>[];
  final featureLines = <int>[];
  for (var i = 0; i < lines.length; i++) {
    final t = lines[i].trim();
    if (t.startsWith('- ') || t.startsWith('* ')) {
      final b = clean(t.substring(2));
      if (_isFeature(b)) bullets.add((i, b));
    }
    if (lines[i].startsWith('#') && _featureHeadingRx.hasMatch(lines[i])) featureLines.add(i);
  }
  final under = featureLines.expand((h) => bullets.where((b) => b.$1 > h && b.$1 < h + 40)).map((b) => b.$2);
  info.features = _distinct([...under, ...bullets.map((b) => b.$2)], 10);
}

/// http(s), a real host name, and not this phone or the local network: a catalogue entry must not make the app poke around a LAN.
bool isPublicWebUrl(String url) {
  final u = Uri.tryParse(url);
  if (u == null || !(u.scheme == 'http' || u.scheme == 'https') || u.host.isEmpty) return false;
  final host = u.host.toLowerCase();
  if (host == 'localhost' || host.endsWith('.localhost') || host.endsWith('.local') || host.endsWith('.internal') || !host.contains('.') && !host.contains(':')) return false;
  final ip = InternetAddress.tryParse(host);
  if (ip == null) return true;
  if (ip.isLoopback || ip.isLinkLocal) return false;
  final b = ip.rawAddress;
  if (ip.type == InternetAddressType.IPv4) {
    return !(b[0] == 10 || b[0] == 0 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
  }
  return !((b[0] & 0xfe) == 0xfc || b.every((x) => x == 0)); // unique-local, unspecified
}

const _userAgent = 'Mozilla/5.0 (Android) ApiScoutMobile/1.1';

Future<String> fetchPage(String url, int maxBytes) async {
  final client = http.Client();
  try {
    final req = http.Request('GET', Uri.parse(url));
    req.headers['User-Agent'] = _userAgent;
    req.headers['Accept'] = 'text/html,text/markdown,text/plain,*/*';
    final resp = await client.send(req).timeout(const Duration(seconds: 20));
    if (resp.statusCode >= 400) throw PageStatus(resp.statusCode, resp.reasonPhrase ?? '');
    final bytes = <int>[];
    await for (final chunk in resp.stream.timeout(const Duration(seconds: 20))) {
      bytes.addAll(chunk);
      if (bytes.length > maxBytes) break; // the start of a huge page is enough to describe it
    }
    return utf8.decode(bytes, allowMalformed: true);
  } finally {
    client.close();
  }
}

class PageStatus implements Exception {
  final int code;
  final String reason;
  PageStatus(this.code, this.reason);
}

/// Dart patterns have no time-out, so a tangled page is read off the UI thread, and only its first part.
const _maxParseChars = 900000;

Map<String, dynamic> parseInsightIsolate(Map<String, dynamic> args) {
  final info = ApiInfo('');
  final text = args['text'] as String;
  (args['markdown'] as bool) ? readMarkdown(text, info) : readHtml(text, info);
  return {'title': info.title, 'summary': info.summary, 'features': info.features, 'sections': info.sections};
}

Future<void> _parse(String text, ApiInfo info, {required bool markdown}) async {
  if (text.length > _maxParseChars) text = text.substring(0, _maxParseChars);
  final r = await compute(parseInsightIsolate, {'text': text, 'markdown': markdown});
  info.title = r['title'] as String;
  info.summary = r['summary'] as String;
  info.features = (r['features'] as List).cast<String>();
  info.sections = (r['sections'] as List).cast<String>();
}

Future<ApiInfo> readInsight(ApiEntry api) async {
  final info = ApiInfo(api.url);
  if (!isPublicWebUrl(api.url)) return info..error = 'The docs link is not a public web address.';
  try {
    // an API that lives on GitHub describes itself in its README
    final gh = _gitHubRx.firstMatch(api.url);
    if (gh != null && !const ['orgs', 'topics', 'features'].contains(gh.group(1))) {
      try {
        info.source = 'https://raw.githubusercontent.com/${gh.group(1)}/${gh.group(2)}/HEAD/README.md';
        await _parse(await fetchPage(info.source, 1500000), info, markdown: true);
        if (!info.isThin) return info;
      } on PageStatus {
        // no README there: read the page instead
      }
      info.source = api.url;
    }
    await _parse(await fetchPage(api.url, 2500000), info, markdown: false);
    if (info.isThin) info.error = 'The page says little in plain HTML - it probably builds itself with JavaScript. Open it in the browser.';
  } on PageStatus catch (s) {
    info.error = 'The page answered ${s.code} ${s.reason} - it may block automated readers. Open it in the browser.';
  } on TimeoutException {
    info.error = 'The page took too long to answer.';
  } catch (ex) {
    info.error = 'Could not reach the page: $ex';
  }
  return info;
}

String insightMarkdown(ApiView v, ApiInfo? info) {
  final sb = StringBuffer('## ${v.name}\n\n${v.entry.description}\n\n[${v.url}](${v.url})\n\n### At a glance\n\n');
  for (final b in benefits(v)) {
    sb.write('- $b\n');
  }
  if (info != null && !info.isThin) {
    if (info.summary.isNotEmpty) sb.write('\n### In the provider\'s words\n\n${info.summary}\n');
    if (info.features.isNotEmpty) {
      sb.write('\n### Features\n\n');
      for (final f in info.features) {
        sb.write('- $f\n');
      }
    }
    if (info.sections.isNotEmpty) sb.write('\n### The docs cover\n\n${info.sections.join(' · ')}\n');
    sb.write('\n_Read from ${info.source}_\n');
  }
  return sb.toString();
}
