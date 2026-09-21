import 'dart:async';
import 'dart:convert';

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
/// Redirects are not followed automatically when the request carries headers: a key in a header must not travel to another host.
Future<TestResult> sendTest(String method, String url, String headerText, String body) async {
  final uri = Uri.tryParse(url.trim());
  if (uri == null || !(uri.scheme == 'http' || uri.scheme == 'https') || uri.host.isEmpty) return const TestResult(false, false, 'That is not a valid http(s) URL.', '');
  String? badLine;
  final headers = parseHeaders(headerText, (b) => badLine = b);
  if (headers == null) return TestResult(false, false, "This header line is not in 'Name: value' form: $badLine", '');

  final client = http.Client();
  final watch = Stopwatch()..start();
  try {
    final req = http.Request(method, uri)..followRedirects = headers.isEmpty;
    req.headers['Accept'] = 'application/json, text/plain;q=0.8, */*;q=0.5';
    req.headers['User-Agent'] = 'ApiScoutMobile/1.0';
    req.headers.addAll(headers);
    if (methodHasBody(method)) {
      req.headers.putIfAbsent('Content-Type', () => '${guessContentType(body)}; charset=utf-8');
      req.body = body;
    }
    final streamed = await client.send(req).timeout(const Duration(seconds: 30));
    final resp = await http.Response.fromStream(streamed).timeout(const Duration(seconds: 30));
    watch.stop();

    final (text, isJson) = formatBody(utf8.decode(resp.bodyBytes, allowMalformed: true));
    final type = (resp.headers['content-type'] ?? 'unknown type').split(';').first;
    var summary = '${resp.statusCode} ${resp.reasonPhrase ?? ''} · ${watch.elapsedMilliseconds} ms · $type · ${_size(resp.bodyBytes.length)}';
    final code = resp.statusCode;
    if (code >= 300 && code < 400 && resp.headers['location'] != null) {
      summary += '\nThe API redirects to ${resp.headers['location']} - not followed, because your headers would go along. Test that URL directly if it is right.';
    } else if (!isJson && type.contains('html')) {
      summary += '\nThis is a web page, not JSON - probably the docs. Paste an endpoint URL from the docs and send again.';
    } else if (code == 401 || code == 403) {
      summary += '\nThe API wants a key. Add it to the URL or a header.';
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

/// The request as a bash-style curl command.
String toCurl(String method, String url, String headerText, String body) {
  String q(String s) => "'${s.replaceAll("'", "'\\''")}'";
  final sb = StringBuffer('curl');
  if (method != 'GET') sb.write(' -X $method');
  sb.write(' ${q(url.trim())}');
  final headers = parseHeaders(headerText, (_) {}) ?? {};
  if (methodHasBody(method) && body.trim().isNotEmpty) headers.putIfAbsent('Content-Type', () => guessContentType(body));
  headers.forEach((name, value) => sb.write(' \\\n  -H ${q('$name: $value')}'));
  if (methodHasBody(method) && body.trim().isNotEmpty) sb.write(' \\\n  --data-raw ${q(body.trim())}');
  return sb.toString();
}
