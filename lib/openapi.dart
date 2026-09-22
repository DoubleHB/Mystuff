// OpenAPI / Swagger for the docs scan: find the spec the docs page points to (or one at the usual place), then
// read the auth schemes and a few GET endpoints out of it - the desktop's DocsScanner.ReadSpec. The desktop is
// handed a spec URL by its sources; none of the phone's five sources has one, so the phone looks for it.
import 'dart:convert';

import 'package:yaml/yaml.dart';

import 'docs_scanner.dart' show DocsScanResult, FoundItem;

const specKind = 'OpenAPI spec';

final _quotedRx = RegExp(r'''["']([^"'\s<>]{4,300})["']''');
final _specWordRx = RegExp(r'openapi|swagger|api-docs|\boas\b', caseSensitive: false);
final _uiAssetRx = RegExp(r'swagger-ui|redoc|\.(js|css|png|svg|html?)(\?|$)|/docs?/?$|/swagger/?$', caseSensitive: false);
final _specFileRx = RegExp(r'\.(json|ya?ml)(\?|$)|/api-docs(/|$|\?)|/openapi(/|$|\?)|/swagger(/v\d+)?/?$', caseSensitive: false);
final _yamlStartRx = RegExp(r'^\s*(openapi|swagger)\s*:', multiLine: true);

/// Where the usual spec files live, on the docs site and on the API host the example request uses.
List<String> guessSpecUrls(String pageUrl, {String? exampleUrl}) {
  const paths = ['/openapi.json', '/swagger.json', '/v3/api-docs', '/swagger/v1/swagger.json', '/openapi.yaml', '/api-docs'];
  final origins = <String>[];
  for (final u in [pageUrl, exampleUrl]) {
    final p = u == null ? null : Uri.tryParse(u.replaceAll('{key}', 'k'));
    if (p == null || p.host.isEmpty || !(p.scheme == 'http' || p.scheme == 'https')) continue;
    final origin = '${p.scheme}://${p.host}${p.hasPort ? ':${p.port}' : ''}';
    if (!origins.contains(origin)) origins.add(origin);
  }
  return [for (final o in origins) for (final p in paths) '$o$p'];
}

/// Spec links on the docs page: hrefs and the url a Swagger UI / Redoc script is given. Best first, at most three.
List<String> findSpecUrls(String html, String pageUrl) {
  final base = Uri.tryParse(pageUrl);
  if (base == null) return const [];
  final found = <String, int>{};
  for (final m in _quotedRx.allMatches(html)) {
    final s = m.group(1)!;
    if (!_specWordRx.hasMatch(s) || _uiAssetRx.hasMatch(s)) continue;
    // a full URL, a path, or a bare relative file name ("openapi.yaml") - not a word from the prose
    if (!(s.startsWith('http') || s.startsWith('/') || s.startsWith('.') || RegExp(r'\.(json|ya?ml)$', caseSensitive: false).hasMatch(s))) continue;
    if (!_specFileRx.hasMatch(s)) continue;
    Uri u;
    try {
      u = base.resolve(s);
    } on FormatException {
      continue;
    }
    if (!(u.scheme == 'http' || u.scheme == 'https') || u.host.isEmpty) continue;
    final url = u.removeFragment().toString();
    final lower = url.toLowerCase();
    final score = (lower.contains('.json') ? 3 : lower.contains('.yaml') || lower.contains('.yml') ? 2 : 1) + (lower.contains('openapi') ? 1 : 0);
    if ((found[url] ?? -1) < score) found[url] = score;
  }
  final list = found.entries.toList()..sort((a, b) => b.value.compareTo(a.value));
  return [for (final e in list.take(3)) e.key];
}

/// The spec as a map, from JSON or YAML text; null when the text is neither, or not a spec.
Map<String, dynamic>? parseSpec(String text) {
  final t = text.trimLeft();
  if (t.isEmpty) return null;
  dynamic j;
  if (t.startsWith('{')) {
    try {
      j = jsonDecode(t);
    } on FormatException {
      return null;
    }
  } else if (_yamlStartRx.hasMatch(t.length > 4000 ? t.substring(0, 4000) : t)) {
    try {
      j = _plain(loadYaml(t));
    } catch (_) {
      return null;
    }
  } else {
    return null;
  }
  if (j is! Map) return null;
  final m = <String, dynamic>{for (final e in j.entries) '${e.key}': e.value};
  return m['openapi'] is String || m['swagger'] is String || m['paths'] is Map ? m : null;
}

/// YAML nodes as plain maps and lists, with string keys.
dynamic _plain(dynamic v) {
  if (v is YamlMap) return <String, dynamic>{for (final e in v.entries) '${e.key}': _plain(e.value)};
  if (v is YamlList) return [for (final x in v) _plain(x)];
  if (v is Map) return <String, dynamic>{for (final e in v.entries) '${e.key}': _plain(e.value)};
  if (v is List) return [for (final x in v) _plain(x)];
  return v;
}

String _s(dynamic m, String name) => m is Map && m[name] is String ? m[name] as String : '';

/// What the spec says, as docs-scan items: the spec itself, GET endpoints without parameters, then the auth schemes
/// (OpenAPI 2 securityDefinitions / OpenAPI 3 components.securitySchemes).
void readSpec(Map<String, dynamic> root, String specUrl, DocsScanResult result) {
  final info = root['info'];
  final paths = root['paths'] is Map ? root['paths'] as Map : const {};
  final title = _s(info, 'title'), version = _s(info, 'version');
  final about = [
    if (title.isNotEmpty) '$title${version.isNotEmpty ? ' $version' : ''}',
    '${paths.length} path${paths.length == 1 ? '' : 's'}',
    'OpenAPI ${_s(root, 'openapi').isNotEmpty ? _s(root, 'openapi') : _s(root, 'swagger')}',
  ].join(' · ');
  result.items.add(FoundItem(specKind, specUrl, note: '$about - client code and request collections can be generated from it'));
  _addEndpoints(root, paths, result);

  dynamic schemes = root['securityDefinitions'];
  if (schemes is! Map && root['components'] is Map) schemes = (root['components'] as Map)['securitySchemes'];
  if (schemes is! Map || schemes.isEmpty) {
    result.items.add(const FoundItem('Auth scheme', 'None declared', note: 'The OpenAPI spec defines no security scheme - the API may be open'));
    return;
  }
  for (final e in schemes.entries) {
    final s = e.value;
    final type = _s(s, 'type'), authorize = _authorizeUrl(s);
    final value = switch (type.toLowerCase()) {
      'apikey' => 'API key in ${_s(s, 'in')}: ${_s(s, 'name')}',
      'oauth2' => 'OAuth 2${authorize.isNotEmpty ? ' - authorise at $authorize' : ''}',
      'http' => 'HTTP ${_s(s, 'scheme')} auth',
      'basic' => 'HTTP basic auth',
      'openidconnect' => 'OpenID Connect${_s(s, 'openIdConnectUrl').isNotEmpty ? ' - ${_s(s, 'openIdConnectUrl')}' : ''}',
      _ => type.isEmpty ? 'Unknown' : type,
    };
    result.items.add(FoundItem('Auth scheme', value, note: "From the OpenAPI spec ('${e.key}')"));
  }
}

/// OpenAPI 2 puts authorizationUrl on the scheme; OpenAPI 3 inside flows.
String _authorizeUrl(dynamic s) {
  final top = _s(s, 'authorizationUrl');
  if (top.isNotEmpty) return top;
  if (s is Map && s['flows'] is Map) {
    for (final f in (s['flows'] as Map).values) {
      final u = _s(f, 'authorizationUrl');
      if (u.isNotEmpty) return u;
    }
  }
  return '';
}

/// Base URL (OpenAPI 3 servers / OpenAPI 2 host + basePath) joined to the first few GET paths without parameters.
void _addEndpoints(Map<String, dynamic> root, Map paths, DocsScanResult result) {
  var baseUrl = '';
  if (root['servers'] is List && (root['servers'] as List).isNotEmpty) {
    baseUrl = _s((root['servers'] as List).first, 'url');
  } else if (_s(root, 'host').isNotEmpty) {
    final schemes = root['schemes'];
    final scheme = schemes is List ? (schemes.contains('https') ? 'https' : 'http') : 'https';
    baseUrl = '$scheme://${_s(root, 'host')}${_s(root, 'basePath')}';
  }
  if (!baseUrl.toLowerCase().startsWith('http') || baseUrl.contains('{')) return;
  baseUrl = baseUrl.replaceFirst(RegExp(r'/+$'), '');
  var n = 0;
  for (final p in paths.entries) {
    final path = '${p.key}';
    if (path.contains('{') || p.value is! Map || (p.value as Map)['get'] is! Map) continue;
    final what = _s((p.value as Map)['get'], 'summary');
    result.items.add(FoundItem('Example endpoint', '$baseUrl$path', method: 'GET', note: 'GET - from the OpenAPI spec${what.isNotEmpty ? ': $what' : ''}. Press Try to load it.'));
    if (++n >= 5) break;
  }
}
