import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter/foundation.dart' show compute;
import 'package:http/http.dart' as http;

class TestResult {
  final bool ok;
  final bool isJson;
  final String summary;
  final String body;
  const TestResult(this.ok, this.isJson, this.summary, this.body);
}

const testMethods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'];
bool methodHasBody(String m) => m == 'POST' || m == 'PUT' || m == 'PATCH';

const _maxChars = 120000;

/// A reply is read up to this many bytes; the rest is dropped and the summary says so. Nothing in the app needs more
/// than it can show, and a 50 MB dump must not freeze the phone or run it out of memory.
const maxResponseBytes = 2 * 1024 * 1024;

/// Pretty-printing a big reply is slow enough to drop frames, so from this size it runs in an isolate.
const _formatInIsolateFrom = 150000;

/// [formatBody] for an isolate: a plain list travels, a record might not on every engine.
List<Object> formatBodyIsolate(String raw) {
  final (text, isJson) = formatBody(raw);
  return [text, isJson];
}

final _keyPlaceholderRx = RegExp(r'\{key\}', caseSensitive: false);

/// {key} stands for the user's own saved key. It is filled in only at the moment of sending:
/// the request as typed, and a copied cURL command, never carry the key itself.
bool usesKeyPlaceholder(String text) => _keyPlaceholderRx.hasMatch(text);
String fillKey(String text, String key, {bool escape = false}) => text.replaceAll(_keyPlaceholderRx, escape ? Uri.encodeComponent(key) : key);

/// The saved key, plain or percent-encoded, put back as {key}: for anything that leaves the app (a copy, a share,
/// generated code) and for what an API echoes back. A key typed out in full instead of as {key} is caught the same
/// way. Keys under six characters are left alone - they would match ordinary words.
String scrubKey(String text, String key) {
  if (key.length < 6 || text.isEmpty) return text;
  return text.replaceAll(key, '{key}').replaceAll(Uri.encodeComponent(key), '{key}');
}

/// Auth presets for Try it: one tap writes the usual header (or query parameter) with {key} standing for the saved key.
const authPresets = ['Bearer', 'X-Api-Key', '?api_key=', 'RapidAPI'];

/// [headers] with a "[name]: [value]" line: replacing the line of that name (whatever its case) or adding one.
String withHeaderLine(String headers, String name, String value) {
  final lines = [for (final l in headers.split('\n')) if (l.trim().isNotEmpty) l.trimRight()];
  final i = lines.indexWhere((l) => l.split(':').first.trim().toLowerCase() == name.toLowerCase());
  if (i >= 0) {
    lines[i] = '$name: $value';
  } else {
    lines.add('$name: $value');
  }
  return lines.join('\n');
}

/// [url] with "[name]=[value]" in its query: replacing that parameter if it is already there.
String withQueryParam(String url, String name, String value) {
  url = url.trim();
  final q = url.indexOf('?');
  if (q < 0) return '$url?$name=$value';
  final params = url.substring(q + 1).split('&').where((p) => p.isNotEmpty).toList();
  final i = params.indexWhere((p) => p.split('=').first.toLowerCase() == name.toLowerCase());
  if (i >= 0) {
    params[i] = '$name=$value';
  } else {
    params.add('$name=$value');
  }
  return '${url.substring(0, q)}?${params.join('&')}';
}

/// Zero or more "Name: value" lines. Returns the first malformed line through [bad].
Map<String, String>? parseHeaders(String text, void Function(String bad) bad) {
  final headers = <String, String>{};
  for (final raw in text.split('\n')) {
    final line = raw.trim();
    if (line.isEmpty) continue;
    final colon = line.indexOf(':');
    if (colon <= 0 || line.substring(colon + 1).trim().isEmpty) {
      bad(line);
      return null;
    }
    headers[line.substring(0, colon).trim()] = line.substring(colon + 1).trim();
  }
  return headers;
}

String guessContentType(String body) {
  final b = body.trim();
  if (b.isEmpty || b.startsWith('{') || b.startsWith('[')) return 'application/json';
  if (b.startsWith('<')) return 'application/xml';
  return !b.contains('\n') && RegExp(r'^[^=&\s]+=[^&\s]*(&[^=&\s]+=[^&\s]*)*$').hasMatch(b) ? 'application/x-www-form-urlencoded' : 'text/plain';
}

/// Pretty-prints JSON; anything else comes back as it was. Capped for display.
(String, bool) formatBody(String raw) {
  var text = raw.trim();
  var isJson = false;
  if (text.startsWith('{') || text.startsWith('[') || text.startsWith('"')) {
    try {
      text = const JsonEncoder.withIndent('  ').convert(jsonDecode(text));
      isJson = true;
    } on FormatException {
      // cut-off or malformed: show as received
    }
  }
  if (text.length > _maxChars) text = '${text.substring(0, _maxChars)}\n\n… cut after $_maxChars characters';
  return (text, isJson);
}

String _size(int bytes) => bytes < 1024 ? '$bytes B' : bytes < 1048576 ? '${(bytes / 1024).toStringAsFixed(1)} KB' : '${(bytes / 1048576).toStringAsFixed(1)} MB';

/// Sends one request and returns the response formatted for reading.
/// Redirects are not followed automatically when the request carries headers or, with [carriesKey], the saved key
/// anywhere in it: a key must not travel to another host.
Future<TestResult> sendTest(String method, String url, String headerText, String body, {bool carriesKey = false}) async {
  final uri = Uri.tryParse(url.trim());
  if (uri == null || !(uri.scheme == 'http' || uri.scheme == 'https') || uri.host.isEmpty) return const TestResult(false, false, 'That is not a valid http(s) URL.', '');
  String? badLine;
  final headers = parseHeaders(headerText, (b) => badLine = b);
  if (headers == null) return TestResult(false, false, "This header line is not in 'Name: value' form: $badLine", '');

  final client = http.Client();
  final watch = Stopwatch()..start();
  try {
    final req = http.Request(method, uri)..followRedirects = headers.isEmpty && !carriesKey;
    req.headers['Accept'] = 'application/json, text/plain;q=0.8, */*;q=0.5';
    req.headers['User-Agent'] = 'APIFree/1.6 (Android)';
    req.headers.addAll(headers);
    if (methodHasBody(method)) {
      req.headers.putIfAbsent('Content-Type', () => '${guessContentType(body)}; charset=utf-8');
      req.body = body;
    }
    final resp = await client.send(req).timeout(const Duration(seconds: 30));
    // read the body in chunks, up to the cap: a huge reply is cut, not buffered whole
    final bytes = BytesBuilder(copy: false);
    var cut = false;
    await for (final chunk in resp.stream.timeout(const Duration(seconds: 30))) {
      if (bytes.length + chunk.length > maxResponseBytes) {
        bytes.add(chunk.sublist(0, (maxResponseBytes - bytes.length).toInt()));
        cut = true;
        break;
      }
      bytes.add(chunk);
    }
    watch.stop();
    final bodyBytes = bytes.takeBytes();

    final raw = utf8.decode(bodyBytes, allowMalformed: true);
    final String text;
    final bool isJson;
    if (raw.length >= _formatInIsolateFrom) {
      final r = await compute(formatBodyIsolate, raw);
      text = r[0] as String;
      isJson = r[1] as bool;
    } else {
      (text, isJson) = formatBody(raw);
    }
    final type = (resp.headers['content-type'] ?? 'unknown type').split(';').first;
    var summary = '${resp.statusCode} ${resp.reasonPhrase ?? ''} · ${watch.elapsedMilliseconds} ms · $type · ${_size(bodyBytes.length)}${cut ? ' (cut at ${_size(maxResponseBytes)})' : ''}';
    final code = resp.statusCode;
    if (code >= 300 && code < 400 && resp.headers['location'] != null) {
      summary += '\nThe API redirects to ${resp.headers['location']} - not followed, because your ${carriesKey ? 'key' : 'headers'} would go along. Test that URL directly if it is right.';
    } else if (!isJson && type.contains('html')) {
      summary += '\nThis is a web page, not JSON - probably the docs. Paste an endpoint URL from the docs and send again.';
    } else if (code == 401 || code == 403) {
      summary += '\nThe API wants a key. Add it to the URL or a header - {key} inserts the key saved under \'My key\'.';
    } else if (code == 405) {
      summary += '\nThis endpoint does not accept $method - check the docs for the right method.';
    } else if (code == 429) {
      final retry = resp.headers['retry-after'];
      summary += '\nRate limited${retry != null ? ' - try again in $retry seconds' : ''}. Shared demo keys run out quickly; your own key has its own allowance.';
    }
    return TestResult(code >= 200 && code < 300, isJson, summary, text);
  } on TimeoutException {
    return const TestResult(false, false, 'No answer within 30 seconds.', '');
  } catch (ex) {
    return TestResult(false, false, 'Request failed: $ex', '');
  } finally {
    client.close();
  }
}

/// The headers a copied request carries: the ones typed, plus a content type for a body that has none.
Map<String, String> _copiedHeaders(String method, String headerText, String body) {
  final headers = parseHeaders(headerText, (_) {}) ?? {};
  if (methodHasBody(method) && body.trim().isNotEmpty) headers.putIfAbsent('Content-Type', () => guessContentType(body));
  return headers;
}

/// The request as JavaScript, for a browser console or Node 18+: `fetch` with the method, headers and body set out.
/// A JSON string literal is a JavaScript string literal, so the quoting is jsonEncode's.
String toFetch(String method, String url, String headerText, String body) {
  final headers = _copiedHeaders(method, headerText, body);
  final hasBody = methodHasBody(method) && body.trim().isNotEmpty;
  final sb = StringBuffer('const response = await fetch(${jsonEncode(url.trim())}');
  if (method != 'GET' || headers.isNotEmpty || hasBody) {
    sb.write(', {\n');
    if (method != 'GET') sb.write('  method: ${jsonEncode(method)},\n');
    if (headers.isNotEmpty) {
      sb.write('  headers: {\n');
      headers.forEach((name, value) => sb.write('    ${jsonEncode(name)}: ${jsonEncode(value)},\n'));
      sb.write('  },\n');
    }
    if (hasBody) sb.write('  body: ${jsonEncode(body.trim())},\n');
    sb.write('}');
  }
  sb.write(');\nconsole.log(response.status, await response.text());');
  return sb.toString();
}

/// The request as Python 3 with the `requests` package (`pip install requests`). Python reads JSON string literals too.
String toPython(String method, String url, String headerText, String body) {
  final headers = _copiedHeaders(method, headerText, body);
  final hasBody = methodHasBody(method) && body.trim().isNotEmpty;
  final sb = StringBuffer('import requests\n\nresponse = requests.${method.toLowerCase()}(\n    ${jsonEncode(url.trim())},\n');
  if (headers.isNotEmpty) {
    sb.write('    headers={\n');
    headers.forEach((name, value) => sb.write('        ${jsonEncode(name)}: ${jsonEncode(value)},\n'));
    sb.write('    },\n');
  }
  if (hasBody) sb.write('    data=${jsonEncode(body.trim())},\n');
  sb.write(')\nprint(response.status_code)\nprint(response.text)');
  return sb.toString();
}

/// The request as a bash-style curl command.
String toCurl(String method, String url, String headerText, String body) {
  String q(String s) => "'${s.replaceAll("'", "'\\''")}'";
  final sb = StringBuffer('curl');
  if (method != 'GET') sb.write(' -X $method');
  sb.write(' ${q(url.trim())}');
  _copiedHeaders(method, headerText, body).forEach((name, value) => sb.write(' \\\n  -H ${q('$name: $value')}'));
  if (methodHasBody(method) && body.trim().isNotEmpty) sb.write(' \\\n  --data-raw ${q(body.trim())}');
  return sb.toString();
}
