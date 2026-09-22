import 'dart:async';

import 'package:flutter/foundation.dart' show compute;

import 'insight.dart' show PageStatus, fetchPage, htmlDecode, isPublicWebUrl;
import 'models.dart';
import 'openapi.dart';

// "Scan docs for key info" - the desktop app's DocsScanner. Reads one API's public docs page (and the pricing page it
// links to) looking for: sign-up / get-a-key links, sample keys printed in the docs, example endpoints, and free-tier
// and rate-limit sentences. It only ever reads the provider's own published pages.
// The desktop reads an OpenAPI spec when a source gives one; none of the phone's five sources does, so the phone
// looks for a spec link on the docs page, or a spec at the usual place (openapi.dart).

class FoundItem {
  /// "Sign-up link", "Sample key", "Placeholder", "Example endpoint", "Free tier / limits", "Pricing page", "Pricing", "Note"
  final String kind;
  final String value;
  final String note;
  final String method;
  const FoundItem(this.kind, this.value, {this.note = '', this.method = ''});

  bool get isEndpoint => kind == 'Example endpoint';
  bool get isLink => value.toLowerCase().startsWith('http');

  Map<String, dynamic> toJson() => {'kind': kind, 'value': value, 'note': note, 'method': method};
  factory FoundItem.fromJson(Map<String, dynamic> j) =>
      FoundItem(j['kind'] as String? ?? '', j['value'] as String? ?? '', note: j['note'] as String? ?? '', method: j['method'] as String? ?? '');
}

class DocsScanResult {
  DateTime scannedAt;
  String pageUrl;
  String? error;
  List<FoundItem> items;
  DocsScanResult(this.pageUrl, {DateTime? scannedAt, this.error, List<FoundItem>? items})
      : scannedAt = scannedAt ?? DateTime.now(),
        items = items ?? [];

  Map<String, dynamic> toJson() => {'scannedAt': scannedAt.toIso8601String(), 'pageUrl': pageUrl, 'error': error, 'items': [for (final i in items) i.toJson()]};
  factory DocsScanResult.fromJson(Map<String, dynamic> j) => DocsScanResult(
        j['pageUrl'] as String? ?? '',
        scannedAt: DateTime.tryParse(j['scannedAt'] as String? ?? ''),
        error: j['error'] as String?,
        items: [for (final i in (j['items'] as List? ?? [])) if (i is Map) FoundItem.fromJson(i.cast<String, dynamic>())],
      );

  String get summary {
    int n(String kind) => items.where((i) => i.kind == kind).length;
    final parts = [
      if (n('Sign-up link') > 0) '${n('Sign-up link')} sign-up link(s)',
      if (n('Sample key') > 0) '${n('Sample key')} sample key(s)',
      if (n('Example endpoint') > 0) '${n('Example endpoint')} example endpoint(s)',
      if (n(specKind) > 0) 'an OpenAPI spec',
      if (n('Auth scheme') > 0) '${n('Auth scheme')} auth scheme(s)',
      if (n('Free tier / limits') + n('Pricing') > 0) '${n('Free tier / limits') + n('Pricing')} line(s) about what is free',
    ];
    return parts.isEmpty ? 'Nothing key-related found' : 'Found ${parts.join(', ')}';
  }
}

final _anchorRx = RegExp(r'''<a\b[^>]*?href\s*=\s*["']([^"'#][^"']*)["'][^>]*>(.*?)</a>''', caseSensitive: false, dotAll: true);
final _noiseRx = RegExp(r'<(script|style|noscript|svg)\b.*?</\1>', caseSensitive: false, dotAll: true);
final _tagRx = RegExp(r'<[^>]+>');
final _spacesRx = RegExp(r'\s+');
final _keyParamRx = RegExp(
    r'\b(api[_-]?key|apikey|app[_-]?id|appid|app[_-]?key|access[_-]?key|access[_-]?token|api[_-]?token|auth[_-]?token|wskey|token|key)\s*=\s*([A-Za-z0-9_\-\.]{1,80})',
    caseSensitive: false);
final _headerRx = RegExp(r'''\b(x-[a-z0-9-]*(?:key|token)[a-z0-9-]*|authorization)\s*:\s*([^\s"'<]{1,90}(?:\s[^\s"'<]{4,90})?)''', caseSensitive: false);
final _authSchemeRx = RegExp(r'^(bearer|basic|token|apikey|api-key|key|digest|oauth)$', caseSensitive: false);
final _signupRx = RegExp(
    r'sign\s?-?up|register|get\s+(an?\s+|your\s+)?(free\s+)?(api\s+)?(key|token|access)|api[\s_-]?keys?|dashboard|developer\s+portal|pricing|get\s+started|create\s+(an?\s+)?(account|app)|request\s+(a\s+)?key',
    caseSensitive: false);
final _freeTierRx = RegExp(
    r'free\s+(tier|plan|forever|account|api|key|to\s+use|for)|no\s+(api\s+)?(key|auth\w*|sign\s?-?up|registration)\s+(is\s+)?(required|needed|necessary)|without\s+(an?\s+)?(api\s+)?key|(requests?|calls?|queries|lookups)\s*(per|/|a|each)\s*(second|minute|hour|day|month)|rate\s?-?limit|demo\s+key|test\s+key|sandbox|free\s+trial|\d+\s*-?\s*day\s+trial',
    caseSensitive: false);
final _placeholderRx = RegExp(
    r'^(your|my|the)?[_\-\.]*(api)?[_\-\.]*(key|token|id|secret|apikey|appid|access|value|here|xxx+|x+|\.\.\.|key_here|token_here|string|text|true|false|null|undefined)+[_\-\.0-9]*$',
    caseSensitive: false);
final _urlRx = RegExp(r'''https?://[^\s"'<>\)\]\\|`]+''');
final _bareUrlRx = RegExp(r'''(?<![\w/.@:-])(?:[a-z0-9-]+\.)+[a-z]{2,}/[^\s"'<>\)\]\\|`]+''', caseSensitive: false);
final _methodPathRx = RegExp(r'''\b(GET|POST|PUT|PATCH|DELETE)\s+(/[A-Za-z0-9_\-/{}.:~%]+(?:\?[^\s"'<>|`]*)?)''');
final _methodBeforeRx = RegExp(r'\b(GET|POST|PUT|PATCH|DELETE)\s*$');
final _assetRx = RegExp(r'\.(js|css|png|jpe?g|gif|svg|ico|woff2?|ttf|map|pdf|zip|mp4|webp)(\?|$)', caseSensitive: false);
final _notApiHostRx = RegExp(
    r'(^|\.)(github\.com|githubusercontent\.com|twitter\.com|x\.com|facebook\.com|linkedin\.com|youtube\.com|youtu\.be|instagram\.com|medium\.com|discord\.(gg|com)|slack\.com|stackoverflow\.com|npmjs\.com|pypi\.org|nuget\.org|w3\.org|schema\.org|gravatar\.com|shields\.io|google-analytics\.com|googletagmanager\.com|gstatic\.com|cloudflare\.com|jsdelivr\.net|unpkg\.com|cdnjs\.com|apple\.com|play\.google\.com|wikipedia\.org|example\.(com|org))$|^(cdn|fonts|static|assets|img|images)\.',
    caseSensitive: false);
final _keyParamNameRx =
    RegExp(r'^(api[_-]?key|apikey|app[_-]?id|appid|app[_-]?key|access[_-]?key|access[_-]?token|api[_-]?token|auth[_-]?token|wskey|token|key)$', caseSensitive: false);
final _pricingLinkRx = RegExp(r'\bpricing\b|\bplans?\b|\bprices\b', caseSensitive: false);
final _pricingRx = RegExp(
    r'free\s+(plan|tier|forever|account|version|usage|quota|of\s+charge)|(\$|£|€)\s?0(\.00)?\b|\b0\s?(\$|£|€)|no\s+credit\s+card|free\b[^.|]{0,70}\b(requests?|calls?|credits?|queries|lookups)|\b(requests?|calls?|credits?|queries|lookups)\b[^.|]{0,40}\bfree\b|free\s+trial|\d+\s*-?\s*day\s+trial|always\s+free|free\s+for\s+(personal|non-?commercial|open\s?source|developers)',
    caseSensitive: false);
final _apiPathRx = RegExp(r'/api(/|$)|/v\d+(\.\d+)?(/|$)|/rest(/|$)|/json(/|$)', caseSensitive: false);
final _dataFileRx = RegExp(r'\.(json|xml|php)$', caseSensitive: false);
final _notEndpointPathRx =
    RegExp(r'/(docs?|documentation|guides?|blog|pricing|sign-?up|log-?in|register|terms|privacy|support|contact|about|faq|dashboard|account|reference)(/|$|\.)', caseSensitive: false);
final _trailingRx = RegExp(r'[.,;:!?)*&"]+$');
final _pipesRx = RegExp(r'(\s*\|\s*)+');

String _clean(String html) => htmlDecode(html.replaceAll(_tagRx, ' ')).replaceAll(_spacesRx, ' ').trim();
String _leftPath(Uri u) => '${u.scheme}://${u.authority}${u.path}';
String _trimSlash(String s) => s.replaceFirst(RegExp(r'/+$'), '');

// "api.foo.co.uk" and "www.foo.co.uk" are the same site; good enough without a public-suffix list
String _siteOf(String host) {
  final parts = host.toLowerCase().split('.');
  final keep = parts.length >= 3 && parts[parts.length - 2].length <= 3 && parts.last.length == 2 ? 3 : 2;
  return parts.skip(parts.length - keep < 0 ? 0 : parts.length - keep).join('.');
}

Uri? _resolve(Uri base, String href) {
  try {
    final u = base.resolve(href);
    return (u.scheme == 'http' || u.scheme == 'https') && u.host.isNotEmpty ? u : null;
  } on FormatException {
    return null;
  }
}

String _sentenceAround(String text, int index, int length) {
  const stops = '.!?|';
  var start = index;
  while (start > 0 && index - start < 160 && !stops.contains(text[start - 1])) {
    start--;
  }
  var end = index + length;
  while (end < text.length && end - index < 200 && !stops.contains(text[end])) {
    end++;
  }
  return text.substring(start, end + 1 > text.length ? text.length : end + 1).trim();
}

void _addKey(DocsScanResult result, Set<String> keys, String value, String note) {
  if (keys.length >= 8 || value.isEmpty || !keys.add(value)) return;
  final lower = value.toLowerCase();
  final placeholder = value.length < 3 && int.tryParse(value) == null ||
      _placeholderRx.hasMatch(value) ||
      lower.contains('your') ||
      lower.contains('xxx') ||
      '{<\$['.contains(value[0]);
  result.items.add(placeholder
      ? FoundItem('Placeholder', value, note: '$note - a stand-in; swap in your own key')
      : FoundItem('Sample key', value, note: '$note - may be a working demo key or just an example'));
}

void readDocsPage(String html, String pageUrl, DocsScanResult result) {
  final baseUri = Uri.tryParse(pageUrl);

  // 1. sign-up style links
  final seen = <String>{};
  for (final m in _anchorRx.allMatches(html)) {
    final href = htmlDecode(m.group(1)!.trim());
    final text = _clean(m.group(2)!);
    final h = href.toLowerCase();
    if (h.startsWith('javascript') || h.startsWith('mailto')) continue;
    if (!_signupRx.hasMatch(text) && !_signupRx.hasMatch(href.replaceAll(RegExp(r'[-_/]'), ' '))) continue;
    final abs = baseUri == null ? null : _resolve(baseUri, href);
    if (abs == null || !seen.add(_leftPath(abs).toLowerCase())) continue;
    result.items.add(FoundItem('Sign-up link', abs.toString(), note: text.isNotEmpty && text.length < 80 ? text : ''));
    if (seen.length >= 8) break;
  }

  final body = htmlDecode(html.replaceAll(_noiseRx, ' ').replaceAll(_tagRx, ' ')).replaceAll(_spacesRx, ' ');

  // 2. values shown next to key parameters in the docs' own examples
  final keys = <String>{};
  for (final m in _keyParamRx.allMatches(body)) {
    _addKey(result, keys, m.group(2)!.replaceFirst(RegExp(r'\.+$'), ''), 'Shown in the docs as ${m.group(1)}=…');
  }
  for (final m in _headerRx.allMatches(body)) {
    // a second word belongs to the value only after a scheme ("Authorization: Bearer abc"), not in "X-Api-Key: abc with every call"
    var value = m.group(2)!.trim();
    final space = value.indexOf(' ');
    if (space > 0 && !_authSchemeRx.hasMatch(value.substring(0, space))) value = value.substring(0, space);
    _addKey(result, keys, value, 'Shown in the docs as header ${m.group(1)}');
  }

  // 3. example endpoints, ready to try
  if (baseUri != null) _addEndpoints(htmlDecode(html), body, baseUri, result);

  // 4. free tier / rate limit sentences
  var notes = 0;
  final said = <String>{};
  for (final m in _freeTierRx.allMatches(body)) {
    final sentence = _sentenceAround(body, m.start, m.end - m.start);
    if (sentence.length < 25 || !said.add(sentence.substring(0, sentence.length < 60 ? sentence.length : 60).toLowerCase())) continue;
    result.items.add(FoundItem('Free tier / limits', sentence));
    if (++notes >= 6) break;
  }
}

/// URLs in the docs that look like API calls (api. host, /api/ or /v1/ path, a query string, .json…) and
/// "GET /path" lines, turned into requests the Try it card can send. Placeholder keys become {key}.
void _addEndpoints(String html, String body, Uri baseUri, DocsScanResult result) {
  final site = _siteOf(baseUri.host);
  final best = <String, (int score, String method, int order)>{};
  final shown = <String, String>{}; // lower-case url -> url as first seen
  var order = 0;
  // the stripped text catches code samples; the raw html catches href="…" links to live examples
  // …and some docs print URLs without the scheme ("www.foo.com/api/x.php?s=1") - accepted on the docs' own site only
  for (final (text, rx, bare) in [(body, _urlRx, false), (html, _urlRx, false), (body, _bareUrlRx, true)]) {
    for (final m in rx.allMatches(text)) {
      final raw = (bare ? '${baseUri.scheme}://' : '') + m.group(0)!.replaceFirst(_trailingRx, '');
      final u = Uri.tryParse(raw);
      if (u == null || u.host.isEmpty || !(u.scheme == 'http' || u.scheme == 'https')) continue;
      final host = u.host.toLowerCase();
      if ((bare && _siteOf(host) != site) || (_notApiHostRx.hasMatch(host) && !host.startsWith('api.')) || _assetRx.hasMatch(u.path)) continue;
      final sameSite = _siteOf(host) == site;
      final score = (host.startsWith('api.') || host.contains('.api.') ? 3 : 0) +
          (_apiPathRx.hasMatch(u.path) ? 2 : 0) +
          (u.query.isNotEmpty ? 2 : 0) +
          (_dataFileRx.hasMatch(u.path) ? 2 : 0) +
          (sameSite ? 1 : 0);
      if (u.path.length <= 1 && u.query.isEmpty) continue;
      if (u.query.isEmpty && _notEndpointPathRx.hasMatch(u.path)) continue;
      if (score < 3 || !(sameSite || score >= 5)) continue;
      if (_trimSlash(_leftPath(u)) == _trimSlash(_leftPath(baseUri)) && u.query.isEmpty) continue;

      final url = keyPlaceholders(raw);
      // "GET https://…" in the docs tells us the method
      final before = text.substring(m.start - 12 < 0 ? 0 : m.start - 12, m.start);
      final method = _methodBeforeRx.firstMatch(before)?.group(1) ?? '';
      final id = url.toLowerCase();
      final have = best[id];
      if (have == null || have.$1 < score) {
        shown.putIfAbsent(id, () => url);
        best[id] = (score, method.isNotEmpty ? method : have?.$2 ?? '', have != null && have.$3 > 0 ? have.$3 : ++order);
      }
    }
  }

  final picked = best.entries.toList()..sort((a, b) => b.value.$1 != a.value.$1 ? b.value.$1.compareTo(a.value.$1) : a.value.$3.compareTo(b.value.$3));
  final top = picked.take(6).toList();
  for (final p in top) {
    final method = p.value.$2.isNotEmpty ? p.value.$2 : 'GET';
    result.items.add(FoundItem('Example endpoint', shown[p.key]!, method: method, note: '$method - from the docs. Press Try to load it.'));
  }

  // "GET /v1/things" lines: join them to the origin of the best full example, or to a guessed api. host
  final first = top.isEmpty ? null : Uri.tryParse(shown[top.first.key]!.replaceAll('{key}', 'k'));
  final guessed = first == null;
  final origin = first != null ? '${first.scheme}://${first.authority}' : '${baseUri.scheme}://${baseUri.host.toLowerCase().startsWith('api.') ? baseUri.host : 'api.$site'}';
  var paths = 0;
  final seenPaths = <String>{};
  for (final m in _methodPathRx.allMatches(body)) {
    final path = m.group(2)!.replaceFirst(RegExp(r'[.,;:]+$'), '');
    if (path.length < 3 || !RegExp(r'[A-Za-z]').hasMatch(path) || path.startsWith('//') || !seenPaths.add('${m.group(1)}$path'.toLowerCase())) continue;
    if (top.any((p) => p.key.contains(path.toLowerCase()))) continue;
    result.items.add(FoundItem('Example endpoint', '$origin$path',
        method: m.group(1)!,
        note: '${m.group(1)} - path from the docs${guessed ? '; the api. host is a guess - check it' : ''}${path.contains('{') ? '; fill in the {…} parts (Variables)' : ''}'));
    if (++paths >= 4) break;
  }
}

/// ?apikey=YOUR_KEY (or an empty / bracketed value) becomes ?apikey={key} so the saved key can be dropped in.
String keyPlaceholders(String url) {
  // worked out on the address as the docs wrote it: Dart's Uri would percent-encode the braces of a {placeholder}
  final hash = url.indexOf('#');
  if (hash >= 0) url = url.substring(0, hash);
  final q = url.indexOf('?');
  if (q < 0 || q == url.length - 1) return q < 0 ? url : url.substring(0, q);
  final left = url.substring(0, q);
  final parts = url.substring(q + 1).split('&').map((p) {
    final eq = p.indexOf('=');
    if (eq > 0 && _keyParamNameRx.hasMatch(p.substring(0, eq))) {
      String v;
      try {
        v = Uri.decodeComponent(p.substring(eq + 1));
      } catch (_) {
        v = p.substring(eq + 1); // not valid percent-encoding: take it as written
      }
      final lower = v.toLowerCase();
      if (v.isEmpty || _placeholderRx.hasMatch(v) || lower.contains('your') || lower.contains('xxx') || '{[<\$'.contains(v[0])) return '${p.substring(0, eq)}={key}';
    }
    return p;
  });
  return '$left?${parts.join('&')}';
}

/// A "Pricing" / "Plans" link on the docs page that stays on the provider's own site.
String? findPricingLink(String html, String pageUrl) {
  final baseUri = Uri.tryParse(pageUrl);
  if (baseUri == null || baseUri.host.isEmpty) return null;
  final site = _siteOf(baseUri.host);
  for (final m in _anchorRx.allMatches(html)) {
    final href = htmlDecode(m.group(1)!.trim());
    final text = _clean(m.group(2)!);
    if (text.length > 40 || !(_pricingLinkRx.hasMatch(text) || _pricingLinkRx.hasMatch(href.replaceAll(RegExp(r'[-_/]'), ' ')))) continue;
    final abs = _resolve(baseUri, href);
    if (abs == null || _siteOf(abs.host) != site) continue;
    if (_trimSlash(_leftPath(abs)) == _trimSlash(_leftPath(baseUri))) continue;
    return abs.removeFragment().toString();
  }
  return null;
}

/// What the pricing page says about free use.
void readPricing(String html, String pricingUrl, DocsScanResult result) {
  result.items.add(FoundItem('Pricing page', pricingUrl));
  final body = htmlDecode(html.replaceAll(_noiseRx, ' | ').replaceAll(_tagRx, ' | ')).replaceAll(_spacesRx, ' ');
  var found = 0;
  final said = <String>{};
  for (final m in _pricingRx.allMatches(body)) {
    final sentence = _sentenceAround(body, m.start, m.end - m.start).replaceAll(RegExp(r'^[| ]+|[| ]+$'), '').replaceAll(_pipesRx, ' · ');
    if (sentence.length < 12 || !said.add(sentence.substring(0, sentence.length < 50 ? sentence.length : 50).toLowerCase())) continue;
    result.items.add(FoundItem('Pricing', sentence, note: 'From the pricing page'));
    if (++found >= 6) break;
  }
  if (found == 0) {
    result.items.add(const FoundItem('Pricing', 'No free plan is mentioned on the pricing page', note: 'Only paid plans were found - or the page builds itself with JavaScript'));
  }
}

/// What a docs scan says: a free plan or stated limits = free tier; only trials or only paid plans = trial only.
AccessLevel accessFromDocs(DocsScanResult? scan) {
  if (scan == null) return AccessLevel.unknown;
  final pricing = [for (final i in scan.items) if (i.kind == 'Pricing') i.value];
  final limits = [for (final i in scan.items) if (i.kind == 'Free tier / limits') i.value];
  bool isTrial(String v) => v.toLowerCase().contains('trial');
  if (pricing.any((v) => v.startsWith('No free plan'))) {
    return limits.any((v) => !isTrial(v) && v.toLowerCase().contains('free')) ? AccessLevel.freeTier : AccessLevel.trialOnly;
  }
  final all = [...pricing, ...limits];
  if (all.isEmpty) return AccessLevel.unknown;
  return all.any((v) => !isTrial(v)) ? AccessLevel.freeTier : AccessLevel.trialOnly;
}

// ---- the scan itself: fetch on the main isolate, parse in another (Dart patterns have no time-out), on the first part of a long page

const _maxParseChars = 900000;

List<Map<String, dynamic>> _pageIsolate(Map<String, String> a) {
  final r = DocsScanResult(a['url']!);
  readDocsPage(a['html']!, a['url']!, r);
  final pricing = findPricingLink(a['html']!, a['url']!);
  return [for (final i in r.items) i.toJson(), if (pricing != null) {'pricingLink': pricing}, for (final s in findSpecUrls(a['html']!, a['url']!)) {'specLink': s}];
}

/// Empty when the text is not an OpenAPI / Swagger spec.
List<Map<String, dynamic>> _specIsolate(Map<String, String> a) {
  final spec = parseSpec(a['text']!);
  if (spec == null) return const [];
  final r = DocsScanResult(a['url']!);
  readSpec(spec, a['url']!, r);
  return [for (final i in r.items) i.toJson()];
}

List<Map<String, dynamic>> _pricingIsolate(Map<String, String> a) {
  final r = DocsScanResult(a['url']!);
  readPricing(a['html']!, a['url']!, r);
  return [for (final i in r.items) i.toJson()];
}

String _cap(String s) => s.length > _maxParseChars ? s.substring(0, _maxParseChars) : s;

Future<DocsScanResult> scanDocs(ApiEntry api, {String? exampleUrl}) async {
  final result = DocsScanResult(api.url);
  if (!isPublicWebUrl(api.url)) return result..error = 'Not read: the docs link is not a public web address.';
  String? pricingUrl;
  final specLinks = <String>[];
  try {
    final html = _cap(await fetchPage(api.url, 3000000));
    for (final j in await compute(_pageIsolate, {'html': html, 'url': api.url})) {
      if (j['pricingLink'] != null) {
        pricingUrl = j['pricingLink'] as String;
      } else if (j['specLink'] != null) {
        specLinks.add(j['specLink'] as String);
      } else {
        result.items.add(FoundItem.fromJson(j));
      }
    }
  } on PageStatus catch (s) {
    result.error = 'The docs page answered ${s.code} ${s.reason} - it may block automated readers. Open it in the browser instead.';
  } on TimeoutException {
    result.error = 'The docs page took too long to answer.';
  } catch (ex) {
    result.error = 'Could not reach the docs page: $ex';
  }

  // the spec: links the page gave first; otherwise the usual places on the docs site and the API host
  final candidates = specLinks.isNotEmpty ? specLinks : (result.error == null ? guessSpecUrls(api.url, exampleUrl: exampleUrl).take(4).toList() : const <String>[]);
  for (final u in candidates) {
    if (!isPublicWebUrl(u)) continue;
    try {
      final text = _cap(await fetchPage(u, 3000000));
      final items = [for (final j in await compute(_specIsolate, {'text': text, 'url': u})) FoundItem.fromJson(j)];
      if (items.isEmpty) continue;
      // the spec's endpoints join the page's, so the Docs tab shows one EXAMPLE ENDPOINT section
      var at = result.items.lastIndexWhere((i) => i.isEndpoint || i.kind == 'Sample key' || i.kind == 'Placeholder' || i.kind == 'Sign-up link') + 1;
      result.items.insertAll(at, items);
      break;
    } catch (_) {
      // a 404 at a guessed place, or a page that is not a spec: try the next
    }
  }

  if (pricingUrl != null && isPublicWebUrl(pricingUrl)) {
    try {
      final html = _cap(await fetchPage(pricingUrl, 2000000));
      for (final j in await compute(_pricingIsolate, {'html': html, 'url': pricingUrl})) {
        result.items.add(FoundItem.fromJson(j));
      }
    } catch (_) {
      // a pricing page that cannot be read is simply skipped - the docs page may still have said something
    }
  }

  if (result.items.isEmpty && result.error == null) {
    result.items.add(const FoundItem('Note', 'Nothing key-related found on this page',
        note: 'The page may build itself with JavaScript, or the key details live on another page. Open the docs in the browser.'));
  }
  return result;
}
